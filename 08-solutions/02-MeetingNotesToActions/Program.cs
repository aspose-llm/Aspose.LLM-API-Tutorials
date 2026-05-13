using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Prompts;

// -----------------------------------------------------------------------------
// Meeting Notes → Action Items — extract explicit, owned commitments from a
// speaker-prefixed meeting transcript.
//
// The model reads the whole meeting in one shot (Qwen 2.5 7B, 32k context),
// classifies each utterance as either DISCUSSION or COMMITMENT, and emits a
// JSON array of action items with owner, task restatement, due date, a
// source quote, and a confidence rating. Downstream tooling (Asana, Linear,
// Jira) ingests the JSON directly.
//
// Key design choices, with lessons from the PII Redactor build:
//
//   * Long context. Transcripts routinely exceed 5k tokens. ContextSize is
//     bumped to 32 768 — Qwen 2.5's full trained window. Prefill on CPU
//     is slow; Metal/CUDA are comfortable.
//
//   * Temperature 0.1 + fixed seed for near-deterministic output. The
//     downstream parsing is pure code, so back-to-back runs produce
//     byte-identical JSON on the same host.
//
//   * JSON, not line-based. Each action item has five distinct fields and
//     the content varies (no two owners, tasks, or quotes are alike), so
//     the "repetition loop on identical prefixes" failure mode that bit us
//     in PII Redactor doesn't apply. JSON is the natural shape for
//     downstream ingestion.
//
//   * Date anchor. The model receives today's date so relative phrases like
//     "by Friday" or "next Tuesday" can be resolved to ISO dates.
//
//   * Verification gate. Every action item must have a `source_quote` that
//     appears verbatim in the transcript. Invented commitments are dropped.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
DateOnly meetingDate = DateOnly.FromDateTime(DateTime.Today);
string? outputPath = null;
bool haveExplicitDate = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--date" when i + 1 < args.Length:
            if (!DateOnly.TryParseExact(args[++i], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out meetingDate))
            {
                ConsoleHelpers.Fail($"--date must be YYYY-MM-DD (got '{args[i]}').");
                return 1;
            }
            haveExplicitDate = true;
            break;
        case "--out" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-transcript.txt");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Transcript file not found: {inputPath}");
    return 1;
}

outputPath ??= inputPath + ".actions.json";

// If the user didn't pass --date, try to parse "Date: YYYY-MM-DD" from the
// first 5 lines of the transcript — a common convention in meeting notes.
var transcript = await File.ReadAllTextAsync(inputPath);
if (!haveExplicitDate)
{
    foreach (var line in transcript.Split('\n').Take(5))
    {
        var idx = line.IndexOf("Date:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) continue;
        var trailing = line.Substring(idx + 5).Trim();
        if (trailing.Length >= 10 &&
            DateOnly.TryParseExact(trailing[..10], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            meetingDate = parsed;
            break;
        }
    }
}

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Meeting Notes → Action Items");
TutorialEnvironment.ApplyLicense();

ConsoleHelpers.Info($"Transcript: {inputPath} ({transcript.Length:N0} chars)");
ConsoleHelpers.Info($"Anchor date: {meetingDate:yyyy-MM-dd} ({meetingDate.DayOfWeek})\n");

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
// Full Qwen 2.5 window. A two-hour meeting transcribed by Otter.ai sits
// around 20-30k tokens; 32k is ample headroom.
preset.ContextParameters.ContextSize = 32768;
preset.SamplerParameters.Temperature = 0.1f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = ExtractionSystemPrompt;
preset.ChatParameters.MaxTokens = 2048;

using var api = new AsposeLLMApi(preset);

// Anchor the model with an explicit date table — small / mid-size models
// consistently miscalculate day-of-week offsets ("next Tuesday" on a
// Wednesday reliably goes wrong). Pre-computing the next 14 days and
// handing the model a lookup table sidesteps that entire failure mode.
var datesTable = new System.Text.StringBuilder();
datesTable.AppendLine($"Date anchor: today is {meetingDate:yyyy-MM-dd} ({meetingDate.DayOfWeek}).");
datesTable.AppendLine("Next 14 days — USE THESE for any relative date in the transcript:");
for (int i = 0; i <= 14; i++)
{
    var d = meetingDate.AddDays(i);
    var label = i switch { 0 => "today", 1 => "tomorrow", _ => $"+{i}d" };
    datesTable.AppendLine($"  {d:yyyy-MM-dd}  {d.DayOfWeek,-9}  ({label})");
}

var userMessage =
    $"{datesTable}\n" +
    $"Transcript follows between the ---- markers.\n" +
    $"----\n{transcript}\n----";

var reply = await api.SendMessageAsync(userMessage);

// ---------- Parse action items ---------------------------------------------
List<ActionItem> items;
try
{
    items = ExtractItems(reply, transcript);
}
catch (Exception ex)
{
    ConsoleHelpers.Fail($"Failed to parse model output: {ex.Message}");
    Console.WriteLine("\n---- raw model output ----");
    Console.WriteLine(reply);
    return 2;
}

// ---------- Output ----------------------------------------------------------
var outJson = JsonSerializer.Serialize(items, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});
await File.WriteAllTextAsync(outputPath, outJson);

ConsoleHelpers.Section("Action items");
if (items.Count == 0)
{
    Console.WriteLine("  (none extracted)");
}
else
{
    for (int i = 0; i < items.Count; i++)
    {
        var a = items[i];
        var conf = (a.Confidence ?? "medium").PadRight(6);
        var due = a.Due ?? "—".PadRight(10);
        var owner = a.Owner ?? "(unassigned)";
        Console.WriteLine($"  {i + 1,2}. [{conf}] {due}  {owner}");
        Console.WriteLine($"      → {a.Task}");
        if (!string.IsNullOrEmpty(a.SourceQuote))
        {
            var q = a.SourceQuote.Replace("\n", " ");
            if (q.Length > 88) q = q[..85] + "…";
            ConsoleHelpers.Info($"      \"{q}\"");
        }
        Console.WriteLine();
    }
}

ConsoleHelpers.Info($"Wrote {items.Count} action item(s) to {outputPath}");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static List<ActionItem> ExtractItems(string reply, string transcript)
{
    var jsonSpan = FindJsonArray(reply)
                   ?? throw new FormatException("no JSON array found in model output");

    using var doc = JsonDocument.Parse(jsonSpan);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
        throw new FormatException("expected a JSON array at the root");

    var result = new List<ActionItem>();
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        if (el.ValueKind != JsonValueKind.Object) continue;

        var owner = GetStringOrNull(el, "owner");
        var task = GetStringOrNull(el, "task");
        var due = GetStringOrNull(el, "due");
        var confidence = GetStringOrNull(el, "confidence");
        var sourceQuote = GetStringOrNull(el, "source_quote");

        // Minimum bar for an action item: a task description. Owner can be
        // null (some commitments are collective, though rare); everything
        // else is null-tolerant.
        if (string.IsNullOrWhiteSpace(task)) continue;

        // Hallucination gate: source quote must appear verbatim in the
        // transcript. Model sometimes paraphrases — drop those.
        if (!string.IsNullOrWhiteSpace(sourceQuote) &&
            !transcript.Contains(sourceQuote, StringComparison.Ordinal))
            sourceQuote = null;

        // Normalise confidence vocabulary.
        confidence = confidence?.ToLowerInvariant() switch
        {
            "high" or "medium" or "low" => confidence.ToLowerInvariant(),
            _ => "medium"
        };

        result.Add(new ActionItem(owner, task!, due, confidence, sourceQuote));
    }
    return result;
}

static string? GetStringOrNull(JsonElement el, string field)
{
    if (!el.TryGetProperty(field, out var prop)) return null;
    if (prop.ValueKind == JsonValueKind.Null) return null;
    if (prop.ValueKind != JsonValueKind.String) return null;
    var s = prop.GetString();
    return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

static string? FindJsonArray(string text)
{
    int start = text.IndexOf('[');
    if (start < 0) return null;

    int depth = 0;
    bool inString = false;
    bool escape = false;
    for (int i = start; i < text.Length; i++)
    {
        char c = text[i];
        if (escape) { escape = false; continue; }
        if (inString)
        {
            if (c == '\\') escape = true;
            else if (c == '"') inString = false;
        }
        else
        {
            if (c == '"') inString = true;
            else if (c == '[') depth++;
            else if (c == ']') { depth--; if (depth == 0) return text.Substring(start, i - start + 1); }
        }
    }
    return null;
}

// ---------------------------------------------------------------------------
// Types + constants
// ---------------------------------------------------------------------------

internal record ActionItem(
    [property: JsonPropertyName("owner")]        string? Owner,
    [property: JsonPropertyName("task")]         string Task,
    [property: JsonPropertyName("due")]          string? Due,
    [property: JsonPropertyName("confidence")]   string? Confidence,
    [property: JsonPropertyName("source_quote")] string? SourceQuote);

internal static class Prompts
{
    public const string ExtractionSystemPrompt = """
        You are a meeting-minutes extractor. Read the transcript the user
        pastes and return a STRICT JSON array where each element is one
        action item explicitly committed to during the meeting.

        Element schema:
        {
          "owner":        "<full name of the committer or null>",
          "task":         "<imperative restatement of what they promised>",
          "due":          "<ISO date YYYY-MM-DD or null>",
          "confidence":   "high" | "medium" | "low",
          "source_quote": "<verbatim span from the transcript>"
        }

        Rules:
        1. ONLY include commitments. Discussion, opinions, "maybe we
           should", and proposals that were explicitly deferred or rejected
           are NOT action items. Read the surrounding turns to detect
           deferrals: if the chair says "let's park it" or "revisit later",
           that thread did not become an action item.
        2. owner = the person who took the commitment on, identified by
           speaker label. If a participant says "I will X", attribute to
           whoever spoke that line. Never invent an owner.
        3. task = imperative mood, concise. Expand pronouns so the task
           reads standalone ("Draft the wire protocol spec", not "Draft
           it").
        4. due = ISO YYYY-MM-DD copied from the date table the user
           provides at the top of the message. Do NOT compute dates
           yourself — look up the weekday or relative label in the table
           and use the ISO value next to it. If no date was stated or
           derivable, use null.
        5. confidence rubric:
             high   — explicit commitment AND explicit (or derivable) date.
             medium — explicit commitment, fuzzy date ("sometime next
                      week", "soon").
             low    — implied commitment without a clear owner-date pair.
        6. source_quote MUST be a verbatim span from the transcript —
           character-for-character, including punctuation and capitalisation.
           Do NOT paraphrase, compress, or rewrite. If the nearest
           verbatim span is awkward, quote it anyway — the caller needs
           an auditable excerpt.
        7. ONE utterance CAN produce MULTIPLE action items. If a speaker
           says "prototype by 15 May, production by 1 July", that's TWO
           separate action items (both can share the same source_quote).
        8. Return STRICT JSON. No prose before or after, no markdown
           fences, no trailing commas.
        9. If there are no action items, return [].

        Example
        -------
        With anchor date 2026-04-01 (Wednesday) and transcript fragment:

            Amir Kovacs: I can have the spec drafted by March 31st.
            Mira Tanaka: Great. Priya — prototype by when?
            Priya Sharma: 15 May for the prototype, July 1 for production.

        Expected output:
            [
              {
                "owner": "Amir Kovacs",
                "task": "Draft the spec",
                "due": "2026-03-31",
                "confidence": "high",
                "source_quote": "I can have the spec drafted by March 31st."
              },
              {
                "owner": "Priya Sharma",
                "task": "Deliver the prototype",
                "due": "2026-05-15",
                "confidence": "high",
                "source_quote": "15 May for the prototype, July 1 for production."
              },
              {
                "owner": "Priya Sharma",
                "task": "Roll production out",
                "due": "2026-07-01",
                "confidence": "high",
                "source_quote": "15 May for the prototype, July 1 for production."
              }
            ]

        Notice how a single utterance containing two commitments produced
        two separate action items, both quoting the same source span.
        """;
}
