using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static ReviewPrompt;

// -----------------------------------------------------------------------------
// Code Review Companion — feed a unified-format git diff to the model and get
// back a list of concerns a careful human reviewer would want to investigate
// (logic, security, concurrency, performance, testing, readability, error
// handling). Pre-pass for PR review, NOT a replacement for human eyes.
//
// Output shape: JSON array of `{severity, file, line, category, message,
// snippet}` records. Same defensive-parsing playbook as 02-MeetingNotesToActions.
//
// Concerns are grouped by severity for the colour-coded console summary;
// the full list also lands in `<input>.review.json` (or whatever
// `--out` specifies).
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
string? langHint = null;
string? severityFilter = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--in" when i + 1 < args.Length:    inputPath = args[++i]; break;
        case "--lang" when i + 1 < args.Length:  langHint = args[++i].ToLowerInvariant(); break;
        case "--only" when i + 1 < args.Length:  severityFilter = args[++i].ToLowerInvariant(); break;
        case "--out" when i + 1 < args.Length:   outputPath = args[++i]; break;
        default: if (inputPath is null) inputPath = args[i]; break;
    }
}

// Resolve diff source: explicit path > non-empty stdin > bundled sample.
// Console.IsInputRedirected is true in any non-interactive shell even when
// stdin is /dev/null, so we also confirm the redirect carries actual bytes.
string diff = "";
string diffLabel = "";
if (inputPath is not null)
{
    if (!File.Exists(inputPath)) { ConsoleHelpers.Fail($"Diff file not found: {inputPath}"); return 1; }
    diff = await File.ReadAllTextAsync(inputPath);
    diffLabel = inputPath;
}
else if (Console.IsInputRedirected)
{
    var stdinText = await Console.In.ReadToEndAsync();
    if (!string.IsNullOrWhiteSpace(stdinText)) { diff = stdinText; diffLabel = "stdin"; }
}

if (string.IsNullOrWhiteSpace(diff))
{
    inputPath = Path.Combine(AppContext.BaseDirectory, "assets", "sample.patch");
    diff = await File.ReadAllTextAsync(inputPath);
    diffLabel = inputPath;
}

outputPath ??= (inputPath ?? Path.Combine(AppContext.BaseDirectory, "review")) + ".review.json";

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Code Review Companion");
TutorialEnvironment.ApplyLicense();
ConsoleHelpers.Info($"Reviewing: {diffLabel} ({diff.Length:N0} chars)");
if (langHint is not null) ConsoleHelpers.Info($"Language hint: {langHint}");
if (severityFilter is not null) ConsoleHelpers.Info($"Filter: {severityFilter}-severity only");
Console.WriteLine();

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.ContextParameters.ContextSize = 16384;
preset.SamplerParameters.Temperature = 0.2f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = SystemPrompt;
preset.ChatParameters.MaxTokens = 2048;

using var api = new AsposeLLMApi(preset);

var userMessage =
    (langHint is not null ? $"Language hint: {langHint}\n\n" : "") +
    $"Review the following diff and emit a JSON array of concerns " +
    $"(see system prompt for schema). Diff:\n\n{diff}";

var reply = await api.SendMessageAsync(userMessage);

// ---------- Parse + filter --------------------------------------------------
List<Concern> concerns;
try { concerns = ExtractConcerns(reply); }
catch (Exception ex)
{
    ConsoleHelpers.Fail($"Failed to parse model output: {ex.Message}");
    Console.WriteLine("\n---- raw model output ----");
    Console.WriteLine(reply);
    return 2;
}

if (severityFilter is not null)
    concerns = concerns.Where(c => string.Equals(c.Severity, severityFilter, StringComparison.OrdinalIgnoreCase)).ToList();

// Sort: high → medium → low, then by file/line.
concerns = concerns
    .OrderBy(c => SeverityRank(c.Severity))
    .ThenBy(c => c.File ?? "")
    .ThenBy(c => c.Line ?? 0)
    .ToList();

// ---------- Output ----------------------------------------------------------
var outJson = JsonSerializer.Serialize(concerns, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});
await File.WriteAllTextAsync(outputPath, outJson);

ConsoleHelpers.Section($"Concerns ({concerns.Count})");
if (concerns.Count == 0)
{
    Console.WriteLine("  (no concerns flagged — diff looks clean to the model)");
}
else
{
    foreach (var c in concerns) PrintConcern(c);
}

ConsoleHelpers.Info($"Wrote full report to {outputPath}");
ConsoleHelpers.Info("This is a PRE-PASS, not a replacement for human review. Spot-check before merging.");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static List<Concern> ExtractConcerns(string reply)
{
    var jsonSpan = FindJsonArray(reply)
                   ?? throw new FormatException("no JSON array found in model output");

    using var doc = JsonDocument.Parse(jsonSpan);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
        throw new FormatException("expected a JSON array at the root");

    var result = new List<Concern>();
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        if (el.ValueKind != JsonValueKind.Object) continue;

        var severity = GetStringOrNull(el, "severity")?.ToLowerInvariant();
        var file = GetStringOrNull(el, "file");
        var line = GetIntOrNull(el, "line");
        var category = GetStringOrNull(el, "category")?.ToLowerInvariant();
        var message = GetStringOrNull(el, "message");
        var snippet = GetStringOrNull(el, "snippet");

        // Drop entries with no message or invalid severity vocabulary.
        if (string.IsNullOrWhiteSpace(message)) continue;
        if (severity is not ("high" or "medium" or "low")) severity = "medium";
        if (!ValidCategories.Contains(category ?? "")) category = "other";

        result.Add(new Concern(severity!, file, line, category!, message!, snippet));
    }
    return result;
}

static string? GetStringOrNull(JsonElement el, string field) =>
    el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String
        ? p.GetString() is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null
        : null;

static int? GetIntOrNull(JsonElement el, string field)
{
    if (!el.TryGetProperty(field, out var p)) return null;
    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
    if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
    return null;
}

static string? FindJsonArray(string text)
{
    int start = text.IndexOf('[');
    if (start < 0) return null;
    int depth = 0; bool inString = false; bool escape = false;
    for (int i = start; i < text.Length; i++)
    {
        char c = text[i];
        if (escape) { escape = false; continue; }
        if (inString) { if (c == '\\') escape = true; else if (c == '"') inString = false; }
        else
        {
            if (c == '"') inString = true;
            else if (c == '[') depth++;
            else if (c == ']') { depth--; if (depth == 0) return text.Substring(start, i - start + 1); }
        }
    }
    return null;
}

static int SeverityRank(string s) => s switch { "high" => 0, "medium" => 1, _ => 2 };

static void PrintConcern(Concern c)
{
    var prev = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = c.Severity switch
        {
            "high"   => ConsoleColor.Red,
            "medium" => ConsoleColor.Yellow,
            _        => ConsoleColor.DarkGray
        };
        var loc = c.File is null ? "" : c.Line is null ? c.File : $"{c.File}:{c.Line}";
        Console.Write($"  [{c.Severity.ToUpper(),-6}] ");
        Console.ResetColor();
        Console.WriteLine($"{loc}  ({c.Category})");
        Console.WriteLine($"      {c.Message}");
        if (!string.IsNullOrWhiteSpace(c.Snippet))
        {
            var sn = c.Snippet.Replace("\n", " ").Trim();
            if (sn.Length > 88) sn = sn[..85] + "…";
            ConsoleHelpers.Info($"      | {sn}");
        }
        Console.WriteLine();
    }
    finally { Console.ForegroundColor = prev; }
}

// ---------------------------------------------------------------------------
// Types + constants
// ---------------------------------------------------------------------------

internal record Concern(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("file")]     string? File,
    [property: JsonPropertyName("line")]     int? Line,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("message")]  string Message,
    [property: JsonPropertyName("snippet")]  string? Snippet);

internal static class ReviewPrompt
{
    public static readonly HashSet<string> ValidCategories = new()
    {
        "logic", "security", "concurrency", "performance",
        "testing", "readability", "error_handling", "other"
    };

    public const string SystemPrompt = """
        You are a code-review assistant. The user supplies a unified-format
        git diff. You return a JSON array of concerns a careful human
        reviewer would want to investigate.

        Schema:
        {
          "severity":  "high" | "medium" | "low",
          "file":      "<file path from the diff>",
          "line":      <line number in the new version, integer>,
          "category":  "logic" | "security" | "concurrency" | "performance" |
                       "testing" | "readability" | "error_handling" | "other",
          "message":   "<one or two sentences describing the concern>",
          "snippet":   "<the offending line(s) verbatim from the diff>"
        }

        Severity rubric:
        - high   — almost certainly wrong / dangerous (race conditions, SQL
                   injection, missing await on critical path, resource
                   leaks in hot paths, auth bypass).
        - medium — suspicious / brittle (swallowed exceptions, timing-unsafe
                   comparisons, deep nesting, potential null deref, missing
                   edge-case handling).
        - low    — style / missing tests / dead code / unclear naming.

        Rules:
        1. Flag ONLY things in the diff. Do not comment on unchanged code
           that happens to be in the context window.
        2. Every entry MUST have a message; line and snippet should match
           the actual offending line.
        3. Be terse — one concern per line, not three rolled into one.
        4. Don't suggest style rewrites the linter handles (semicolons,
           blank lines). Focus on "did the author think about X?" issues.
        5. If the diff looks clean, return [] — an empty array, not a
           concern list with `severity: "none"`.
        6. Return STRICT JSON. No prose before or after, no markdown code
           fences, no trailing commas.
        """;
}
