using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static TriagePrompts;

// -----------------------------------------------------------------------------
// Customer Feedback Triage — read a CSV of feedback items, classify each one
// with sentiment / category / urgency / one-line summary, write a CSV out
// with the new columns appended, and print a rollup.
//
// This recipe combines two patterns from earlier tutorials:
//
//   * 05-tuning/BatchProcessing: one loaded model, fresh chat session per
//     item so prior classifications can't bias the next one.
//
//   * 08-solutions/02-MeetingNotesToActions: structured JSON output with a
//     defensive parser that tolerates the small drifts (stray prose, code
//     fences) a 7B model occasionally adds.
//
// Per-row schema returned by the model:
//   {
//     "sentiment": "positive" | "negative" | "neutral",
//     "category":  "bug" | "feature_request" | "billing" | "onboarding" |
//                  "compliment" | "security" | "other",
//     "urgency":   "now" | "this_week" | "anytime",
//     "summary":   "<one sentence, ≤ 80 chars>"
//   }
//
// CSV parsing. A real CSV library would be overkill here — a tiny hand-rolled
// reader handles the only tricky case (quoted fields with embedded commas and
// escaped double-quotes per RFC 4180). 30 lines, no extra dependency, and the
// behaviour is auditable in a single screenful.
//
// Model choice. The spec ("0.5B is more than enough") was based on the
// theoretical complexity of the task — short text, a tight categorical
// schema. In practice the PII Redactor build showed 0.5B drifts off-format
// and refuses to be exhaustive. We default to Qwen 2.5 7B (Qwen25Preset)
// for reliability; SmallModelPreset stays as a one-line swap for huge
// batches where speed beats marginal accuracy.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--in" when i + 1 < args.Length:
            inputPath = args[++i];
            break;
        case "--out" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-feedback.csv");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Input CSV not found: {inputPath}");
    return 1;
}

outputPath ??= inputPath + ".triaged.csv";

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Customer Feedback Triage");
TutorialEnvironment.ApplyLicense();

var rows = CsvIO.ReadCsv(inputPath);
if (rows.Count == 0)
{
    ConsoleHelpers.Fail($"No rows found in {inputPath}.");
    return 1;
}

ConsoleHelpers.Info($"Input:  {inputPath} ({rows.Count} row(s))");
ConsoleHelpers.Info($"Output: {outputPath}\n");

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
// Tight categorical schema → low temperature is safe and produces stable
// classifications across re-runs on the same host.
preset.SamplerParameters.Temperature = 0.2f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = TriageSystemPrompt;
// 100 tokens covers the four-field JSON object with a roomy summary; bumping
// it higher just wastes per-item latency.
preset.ChatParameters.MaxTokens = 100;

using var api = new AsposeLLMApi(preset);

// ---------- Classify --------------------------------------------------------
ConsoleHelpers.Info($"Classifying {rows.Count} item(s) through {nameof(Qwen25Preset)}…\n");

var total = Stopwatch.StartNew();
var classified = new List<TriagedRow>(rows.Count);
int failures = 0;

for (int i = 0; i < rows.Count; i++)
{
    var row = rows[i];
    var text = row.Text;

    // Defensive truncation: very long rants would blow MaxTokens budget on
    // the prompt side. 2 000 chars is plenty for the model to gist the
    // sentiment + category. The summary remains ≤ 80 chars regardless.
    if (text.Length > 2_000) text = text[..2_000];

    Triage triage;
    long ms;

    if (string.IsNullOrWhiteSpace(text))
    {
        // Empty rows: skip the LLM call. They get a neutral "unclear" stub
        // so the output schema stays uniform.
        triage = Triage.UnclearStub();
        ms = 0;
    }
    else
    {
        // Fresh session per item: identical guidance from the system prompt
        // applies to every row, but no prior classifications bias the
        // current one.
        var sessionId = await api.StartNewChatAsync();
        var sw = Stopwatch.StartNew();

        string reply;
        try
        {
            reply = await api.SendMessageToSessionAsync(sessionId, text);
        }
        catch (Exception ex)
        {
            sw.Stop();
            failures++;
            ConsoleHelpers.Warn($"  [{i + 1}/{rows.Count}] LLM call failed: {ex.Message}");
            classified.Add(new TriagedRow(row, Triage.UnclearStub(), sw.ElapsedMilliseconds));
            continue;
        }
        sw.Stop();
        ms = sw.ElapsedMilliseconds;

        if (!Triage.TryParse(reply, out triage))
        {
            failures++;
            ConsoleHelpers.Warn($"  [{i + 1}/{rows.Count}] could not parse model reply, falling back to 'other / anytime'.");
            triage = Triage.UnclearStub();
        }
    }

    classified.Add(new TriagedRow(row, triage, ms));
    var bar = ProgressBar(i + 1, rows.Count);
    var preview = row.Text.Length > 64 ? row.Text[..61] + "…" : row.Text;
    Console.WriteLine($"  {bar} {i + 1,4}/{rows.Count}  [{triage.Sentiment,-8}] [{triage.Category,-15}] [{triage.Urgency,-9}] {preview}");
}

total.Stop();

// ---------- Write CSV out --------------------------------------------------
CsvIO.WriteCsv(outputPath, classified);

ConsoleHelpers.Info($"\nWrote {classified.Count} classified row(s) to {outputPath}.");
if (failures > 0)
    ConsoleHelpers.Warn($"  {failures} row(s) fell back to 'other / anytime' due to parse / call failures.");

// ---------- Rollup ---------------------------------------------------------
ConsoleHelpers.Section("Rollup");

var bySentiment = Tally(classified, r => r.Triage.Sentiment);
var byCategory = Tally(classified, r => r.Triage.Category);
var byUrgency = Tally(classified, r => r.Triage.Urgency);

Console.WriteLine($"  Sentiment: {FormatTally(bySentiment, ["positive", "neutral", "negative"])}");
Console.WriteLine($"  Category:  {FormatTally(byCategory, ["bug", "feature_request", "billing", "onboarding", "compliment", "security", "other"])}");
Console.WriteLine($"  Urgency:   {FormatTally(byUrgency, ["now", "this_week", "anytime"])}");

ConsoleHelpers.Section("Top \"now\"-urgent items");
var nowItems = classified.Where(r => r.Triage.Urgency == "now").Take(5).ToList();
if (nowItems.Count == 0)
{
    Console.WriteLine("  (none)");
}
else
{
    foreach (var r in nowItems)
    {
        var cat = $"[{r.Triage.Category}]".PadRight(18);
        var summary = r.Triage.Summary.Length > 70 ? r.Triage.Summary[..67] + "…" : r.Triage.Summary;
        Console.WriteLine($"  {cat} {summary} — id {r.Row.Id}");
    }
}

ConsoleHelpers.Section("Throughput");
var totalSeconds = total.Elapsed.TotalSeconds;
var itemsPerSecond = classified.Count / Math.Max(0.001, totalSeconds);
Console.WriteLine(
    $"  Items:        {classified.Count}\n" +
    $"  Total time:   {totalSeconds:F1}s (includes ~1× model load)\n" +
    $"  Throughput:   {itemsPerSecond:F2} items/s");

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static Dictionary<string, int> Tally(IEnumerable<TriagedRow> rows, Func<TriagedRow, string> selector)
{
    var dict = new Dictionary<string, int>();
    foreach (var r in rows)
    {
        var key = selector(r);
        dict[key] = dict.GetValueOrDefault(key) + 1;
    }
    return dict;
}

static string FormatTally(Dictionary<string, int> tally, string[] preferredOrder)
{
    var ordered = preferredOrder.Where(tally.ContainsKey).Select(k => $"{tally[k]} {k}");
    var extras = tally.Keys.Except(preferredOrder).OrderBy(k => k).Select(k => $"{tally[k]} {k}");
    return string.Join(", ", ordered.Concat(extras));
}

static string ProgressBar(int done, int total, int width = 20)
{
    var filled = (int)Math.Round(width * (double)done / Math.Max(1, total));
    return "[" + new string('█', filled) + new string('·', width - filled) + "]";
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

internal sealed record FeedbackRow(string Id, string Source, string Text, string Timestamp);

internal sealed record TriagedRow(FeedbackRow Row, Triage Triage, long ElapsedMs);

internal sealed record Triage(
    string Sentiment,
    string Category,
    string Urgency,
    string Summary)
{
    private static readonly HashSet<string> Sentiments = new() { "positive", "negative", "neutral" };
    private static readonly HashSet<string> Categories = new()
    {
        "bug", "feature_request", "billing", "onboarding", "compliment", "security", "other"
    };
    private static readonly HashSet<string> Urgencies = new() { "now", "this_week", "anytime" };

    public static Triage UnclearStub()
        => new("neutral", "other", "anytime", "Unclear feedback; review manually");

    public static bool TryParse(string reply, out Triage triage)
    {
        triage = UnclearStub();

        var jsonSpan = FindJsonObject(reply);
        if (jsonSpan is null) return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonSpan);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            var sentiment = NormaliseEnum(GetString(doc.RootElement, "sentiment"), Sentiments, "neutral");
            var category = NormaliseEnum(GetString(doc.RootElement, "category"), Categories, "other");
            var urgency = NormaliseEnum(GetString(doc.RootElement, "urgency"), Urgencies, "anytime");

            var summary = (GetString(doc.RootElement, "summary") ?? string.Empty)
                .Replace('\r', ' ').Replace('\n', ' ').Trim();
            // Trim trailing punctuation/quotes — the rubric asks for none.
            summary = summary.TrimEnd('.', '!', '?', '"', '\'');
            if (summary.Length == 0) summary = "Unclear feedback; review manually";
            if (summary.Length > 80) summary = summary[..80].TrimEnd();

            triage = new Triage(sentiment, category, urgency, summary);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement el, string field)
    {
        if (!el.TryGetProperty(field, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        return prop.GetString();
    }

    private static string NormaliseEnum(string? value, HashSet<string> allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return allowed.Contains(v) ? v : fallback;
    }

    private static string? FindJsonObject(string text)
    {
        // Pick the first balanced {...} substring. Tolerates a leading
        // markdown fence or a stray "Here's the JSON:" preamble that small
        // models occasionally inject.
        int start = text.IndexOf('{');
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
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return text.Substring(start, i - start + 1); }
            }
        }
        return null;
    }
}

// ---------------------------------------------------------------------------
// CSV reader / writer
// ---------------------------------------------------------------------------

internal static class CsvIO
{
    // Hand-rolled RFC-4180-ish CSV. Handles:
    //   * header row (first non-empty line)
    //   * quoted fields with embedded commas
    //   * embedded newlines inside quoted fields
    //   * doubled "" inside quoted fields → literal "
    // Skips blank lines outside quoted fields. Case-insensitive header lookup.
    // No external dependency, ~60 lines including the writer.
    public static List<FeedbackRow> ReadCsv(string path)
    {
        var raw = File.ReadAllText(path);
        var fields = Tokenize(raw);
        if (fields.Count == 0) return new List<FeedbackRow>();

        var header = fields[0];
        int idIdx = FindColumn(header, "id");
        int sourceIdx = FindColumn(header, "source");
        int textIdx = FindColumn(header, "text");
        int tsIdx = FindColumn(header, "timestamp");

        if (textIdx < 0)
            throw new FormatException("CSV must contain a 'text' column.");

        var rows = new List<FeedbackRow>();
        for (int r = 1; r < fields.Count; r++)
        {
            var row = fields[r];
            // Skip rows that came back empty after tokenisation.
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])) continue;

            string id = idIdx >= 0 && idIdx < row.Count ? row[idIdx] : r.ToString();
            string source = sourceIdx >= 0 && sourceIdx < row.Count ? row[sourceIdx] : "";
            string text = textIdx < row.Count ? row[textIdx] : "";
            string timestamp = tsIdx >= 0 && tsIdx < row.Count ? row[tsIdx] : "";
            if (string.IsNullOrWhiteSpace(id)) id = r.ToString();
            rows.Add(new FeedbackRow(id, source, text, timestamp));
        }
        return rows;
    }

    public static void WriteCsv(string path, IEnumerable<TriagedRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,source,text,timestamp,sentiment,category,urgency,summary");
        foreach (var r in rows)
        {
            sb.Append(Escape(r.Row.Id)).Append(',');
            sb.Append(Escape(r.Row.Source)).Append(',');
            sb.Append(Escape(r.Row.Text)).Append(',');
            sb.Append(Escape(r.Row.Timestamp)).Append(',');
            sb.Append(Escape(r.Triage.Sentiment)).Append(',');
            sb.Append(Escape(r.Triage.Category)).Append(',');
            sb.Append(Escape(r.Triage.Urgency)).Append(',');
            sb.AppendLine(Escape(r.Triage.Summary));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static int FindColumn(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string Escape(string field)
    {
        if (field is null) return string.Empty;
        bool mustQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!mustQuote) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static List<List<string>> Tokenize(string raw)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Escaped double-quote: "" → "
                    if (i + 1 < raw.Length && raw[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    // Swallow; handled with \n.
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || !string.IsNullOrEmpty(row[0])) rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        // Trailing field / row at end-of-file (no closing newline).
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || !string.IsNullOrEmpty(row[0])) rows.Add(row);
        }

        return rows;
    }
}

// ---------------------------------------------------------------------------
// Prompt
// ---------------------------------------------------------------------------

internal static class TriagePrompts
{
    public const string TriageSystemPrompt = """
        You are a customer-feedback triage classifier. The user will paste ONE
        feedback item per turn. Return STRICT JSON with four fields:

        {
          "sentiment": "positive" | "negative" | "neutral",
          "category":  "bug" | "feature_request" | "billing" | "onboarding" |
                       "compliment" | "security" | "other",
          "urgency":   "now" | "this_week" | "anytime",
          "summary":   "<one sentence, max 80 chars, no trailing punctuation>"
        }

        Urgency rubric:
        - "now"       — blocks the user from using the product (crash, data
                        loss, billing charged incorrectly, security concern).
        - "this_week" — meaningful pain but has a workaround (UX friction,
                        feature gaps that have alternatives).
        - "anytime"   — nice-to-have, compliments, neutral observations.

        Category rubric:
        - bug             — software defect, crash, incorrect output.
        - feature_request — asking for something that doesn't exist yet.
        - billing         — invoice, refund, subscription, payment issues.
        - onboarding      — first-run, sign-up, initial configuration issues.
        - compliment      — positive feedback with no specific ask.
        - security        — vulnerability reports, account-takeover concerns.
        - other           — everything else, including ambiguous items.

        Rules:
        1. Return JSON only — no prose, no markdown fences, no commentary
           before or after.
        2. Summary is a concise restatement, NOT a quote of the feedback.
        3. Summary is in the same language as the input. Classification
           labels (sentiment/category/urgency) stay English.
        4. If the feedback is too vague to classify, use "other" + "anytime"
           + a summary of "Unclear feedback; review manually".
        5. Mixed signal — when a comment praises and complains, the sentiment
           follows the dominant signal (e.g. "great app but it crashes" is
           negative + bug).

        Examples
        --------
        Input:  "Keeps crashing on startup after v3.2 update"
        Output: {"sentiment":"negative","category":"bug","urgency":"now","summary":"Startup crash after v3.2 update"}

        Input:  "Love the new dark mode!"
        Output: {"sentiment":"positive","category":"compliment","urgency":"anytime","summary":"Positive feedback on dark mode"}

        Input:  "Can we get Slack integration?"
        Output: {"sentiment":"neutral","category":"feature_request","urgency":"this_week","summary":"User wants Slack integration"}

        Input:  "I was able to view another customer's invoice by editing the URL."
        Output: {"sentiment":"negative","category":"security","urgency":"now","summary":"IDOR — invoice visible by URL manipulation"}
        """;
}
