using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Prompts;

// -----------------------------------------------------------------------------
// Contract Clause Analyzer — scan a software-services contract and flag the
// clauses that warrant a human legal review (auto-renewal, unlimited
// liability, exclusivity, IP assignment, indemnification carve-outs, etc.).
//
// This is *not* a legal-advice tool. It is a pre-scan that lets a lawyer
// spend their finite time on the clauses that matter, instead of grinding
// through the same boilerplate on every contract. The README's disclaimer
// is the only legally meaningful copy in this whole project.
//
// Why a local model.  Contracts contain confidential terms — pricing,
// customer names, liability caps. Cloud APIs are off-limits in many
// jurisdictions, particularly under European / Russian data-residency
// regulations. The Aspose.LLM SDK runs the model in-process, no data
// leaves the machine.
//
// Design choices, copying the pattern that worked for tutorial 02:
//
//   * Long context. Contracts routinely exceed 5k tokens. ContextSize is
//     bumped to 16 384 — most contracts fit in a single shot. Multi-
//     document chunking (MSA + SOW + DPA) is a stretch goal.
//
//   * Temperature 0.1 + fixed seed for near-deterministic output. Same
//     contract on the same host produces byte-identical JSON across runs.
//
//   * JSON, not line-based. Each flagged clause has six distinct fields
//     (clause_type, severity, excerpt, location, rationale,
//     suggested_counter); per-entry text is unique, so the repetition
//     trap that bites 0.5B models on homogeneous lists doesn't apply
//     here, and JSON is the natural shape for downstream tooling.
//
//   * Controlled vocabulary. clause_type is restricted to eleven values
//     so downstream tooling can rely on stable categories. Anything
//     outside the set is dropped after parsing.
//
//   * Hallucination gate. The `excerpt` field must be a verbatim substring
//     of the contract — paraphrased excerpts are silently dropped. This
//     is the same audit-trail discipline used in tutorials 02 and 04.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
string? outputPath = null;
HashSet<string>? focus = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--focus" when i + 1 < args.Length:
            focus = new HashSet<string>(
                args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => NormaliseClauseType(s)));
            break;
        case "--out" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-contract.txt");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Contract file not found: {inputPath}");
    return 1;
}

outputPath ??= inputPath + ".report.json";

// Validate any --focus values against the controlled vocabulary up front.
if (focus is not null)
{
    var unknown = focus.Where(f => !KnownClauseTypes.Contains(f)).ToList();
    if (unknown.Count > 0)
    {
        ConsoleHelpers.Fail(
            $"Unknown --focus value(s): {string.Join(", ", unknown)}.\n" +
            $"Allowed: {string.Join(", ", KnownClauseTypes.OrderBy(s => s))}");
        return 1;
    }
}

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Contract Clause Analyzer");
TutorialEnvironment.ApplyLicense();

var contract = await File.ReadAllTextAsync(inputPath);
ConsoleHelpers.Info($"Contract: {inputPath} ({contract.Length:N0} chars)");
ConsoleHelpers.Info($"Focus:    {(focus is null ? "all clause types" : string.Join(", ", focus))}\n");

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
// Most software-services contracts fit comfortably in 16k. Multi-document
// packages (MSA + SOW + DPA pasted together) start bumping into this and
// would benefit from chunking — left as a stretch goal.
preset.ContextParameters.ContextSize = 16384;
preset.SamplerParameters.Temperature = 0.1f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = AnalysisSystemPrompt;
preset.ChatParameters.MaxTokens = 2048;

using var api = new AsposeLLMApi(preset);

var focusLine = focus is null
    ? "Scan ALL eleven clause types from the controlled vocabulary."
    : "Scan ONLY these clause types: " + string.Join(", ", focus.OrderBy(s => s)) +
      ". Skip everything else.";

var userMessage =
    $"{focusLine}\n" +
    $"Contract follows between the ---- markers.\n" +
    $"----\n{contract}\n----";

var reply = await api.SendMessageAsync(userMessage);

// ---------- Parse flagged clauses -------------------------------------------
List<Clause> clauses;
try
{
    clauses = ExtractClauses(reply, contract, focus);
}
catch (Exception ex)
{
    ConsoleHelpers.Fail($"Failed to parse model output: {ex.Message}");
    Console.WriteLine("\n---- raw model output ----");
    Console.WriteLine(reply);
    return 2;
}

// ---------- Output ----------------------------------------------------------
var outJson = JsonSerializer.Serialize(clauses, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});
await File.WriteAllTextAsync(outputPath, outJson);

ConsoleHelpers.Section("Clauses flagged for review");
if (clauses.Count == 0)
{
    Console.WriteLine("  (none flagged)");
}
else
{
    // Sort by severity (high → medium → low), then preserve source order.
    var ordered = clauses
        .Select((c, idx) => (c, idx))
        .OrderBy(t => SeverityRank(t.c.Severity))
        .ThenBy(t => t.idx)
        .Select(t => t.c)
        .ToList();

    foreach (var c in ordered)
    {
        var sev = SeverityTag(c.Severity);
        var typ = c.ClauseType.PadRight(28);
        var loc = (c.Location ?? "—").PadRight(14);
        var ex = c.Excerpt.Replace("\n", " ");
        if (ex.Length > 64) ex = ex[..61] + "…";
        Console.WriteLine($"  {sev}  {typ} {loc} \"{ex}\"");
    }

    Console.WriteLine();
    foreach (var c in ordered)
    {
        Console.WriteLine($"  [{c.Severity.ToUpperInvariant()}] {c.ClauseType} — {c.Location ?? "(no section)"}");
        Console.WriteLine($"    rationale:  {c.Rationale}");
        if (!string.IsNullOrWhiteSpace(c.SuggestedCounter))
            Console.WriteLine($"    counter:    {c.SuggestedCounter}");
        Console.WriteLine();
    }
}

ConsoleHelpers.Info($"Wrote {clauses.Count} clause(s) to {outputPath}");
ConsoleHelpers.Warn(
    "Reminder: this is an informational pre-scan, NOT legal advice. " +
    "All flagged clauses require review by qualified counsel before action.");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static bool ContainsIgnoringWhitespace(string haystack, string needle)
{
    static string Collapse(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    return Collapse(haystack).Contains(Collapse(needle), StringComparison.Ordinal);
}

static List<Clause> ExtractClauses(string reply, string contract, HashSet<string>? focus)
{
    var jsonSpan = FindJsonArray(reply)
                   ?? throw new FormatException("no JSON array found in model output");

    using var doc = JsonDocument.Parse(jsonSpan);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
        throw new FormatException("expected a JSON array at the root");

    var result = new List<Clause>();
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        if (el.ValueKind != JsonValueKind.Object) continue;

        var clauseType = NormaliseClauseType(GetStringOrNull(el, "clause_type") ?? "");
        var severity = GetStringOrNull(el, "severity")?.ToLowerInvariant();
        var excerpt = GetStringOrNull(el, "excerpt");
        var location = GetStringOrNull(el, "location");
        var rationale = GetStringOrNull(el, "rationale");
        var suggestedCounter = GetStringOrNull(el, "suggested_counter");

        // Controlled vocabulary check. Anything outside the eleven approved
        // categories is dropped — downstream tooling cannot consume free-form
        // categories, so it's better to discard than to leak.
        if (!KnownClauseTypes.Contains(clauseType)) continue;

        // If the user supplied --focus, drop entries the model added outside
        // the requested set. Belt-and-braces — the system prompt already
        // restricts the model, but we don't trust it not to drift.
        if (focus is not null && !focus.Contains(clauseType)) continue;

        // An entry without an excerpt is unauditable — drop it.
        if (string.IsNullOrWhiteSpace(excerpt)) continue;

        // Hallucination gate: the excerpt MUST appear in the contract.
        // The model normalises line breaks ("foo\n  bar" → "foo bar"), so a
        // strict-substring match fails on multi-line clauses. Compare on a
        // whitespace-collapsed form on both sides.
        if (!ContainsIgnoringWhitespace(contract, excerpt)) continue;

        // Normalise severity vocabulary. Anything weird collapses to medium —
        // safer default than dropping the whole entry.
        severity = severity switch
        {
            "high" or "medium" or "low" => severity,
            _ => "medium"
        };

        result.Add(new Clause(
            clauseType,
            severity,
            excerpt!,
            location,
            rationale ?? "",
            suggestedCounter));
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

static string NormaliseClauseType(string raw) =>
    raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

static int SeverityRank(string severity) => severity switch
{
    "high" => 0,
    "medium" => 1,
    "low" => 2,
    _ => 3
};

static string SeverityTag(string severity) => severity switch
{
    "high" => "[HIGH]  ",
    "medium" => "[MED]   ",
    "low" => "[LOW]   ",
    _ => "[?]     "
};

// ---------------------------------------------------------------------------
// Types + constants
// ---------------------------------------------------------------------------

internal record Clause(
    [property: JsonPropertyName("clause_type")]       string ClauseType,
    [property: JsonPropertyName("severity")]          string Severity,
    [property: JsonPropertyName("excerpt")]           string Excerpt,
    [property: JsonPropertyName("location")]          string? Location,
    [property: JsonPropertyName("rationale")]         string Rationale,
    [property: JsonPropertyName("suggested_counter")] string? SuggestedCounter);

internal static class Prompts
{
    // The eleven-value controlled vocabulary, exactly as specified in the
    // upstream spec. Downstream tooling (CLM systems, risk dashboards)
    // depends on this stable set.
    public static readonly HashSet<string> KnownClauseTypes = new()
    {
        "auto_renewal",
        "unlimited_liability",
        "exclusivity",
        "assignment",
        "indemnification",
        "ip_assignment",
        "non_compete",
        "termination_for_convenience",
        "governing_law",
        "dispute_resolution",
        "confidentiality"
    };

    public const string AnalysisSystemPrompt = """
        You are a contract-risk scanner. The user will paste contract
        text. You emit a STRICT JSON array flagging clauses that warrant
        human legal review. Every output is INFORMATIONAL — never legal
        advice.

        Element schema:
        {
          "clause_type":       "<one of the eleven values below>",
          "severity":          "high" | "medium" | "low",
          "excerpt":           "<verbatim 1-3 sentence span from the contract>",
          "location":          "<Section X.Y or Article N or null>",
          "rationale":         "<one factual sentence on why a human should review>",
          "suggested_counter": "<one factual sentence on a typical pushback or null>"
        }

        Controlled clause_type vocabulary (USE ONLY THESE eleven values):
          auto_renewal, unlimited_liability, exclusivity, assignment,
          indemnification, ip_assignment, non_compete,
          termination_for_convenience, governing_law, dispute_resolution,
          confidentiality.

        If a clause is risky but does not fit one of these eleven, SKIP it.
        Downstream tooling needs a stable category set.

        Severity rubric:
          high   — unbounded liability ("without cap or limitation"),
                   long auto-renewal (>= 12 month renewal terms with
                   long notice windows), broad exclusivity, IP
                   assignment of pre-existing or third-party-licensed work,
                   indemnification without a cap, governing law in an
                   uncommon jurisdiction (e.g. offshore tax havens) where
                   enforcement is uncertain.
          medium — confidentiality terms longer than 5 years,
                   non-compete >= 24 months or with broad geography,
                   dispute resolution in inconvenient forum or requiring
                   each party to bear its own costs.
          low    — stylistic concerns, boilerplate that is standard but
                   sometimes mis-negotiated, plain assignment clauses
                   with normal carve-outs.

        Do NOT flag standard boilerplate that legal teams routinely
        accept (e.g. "Confidentiality survives termination" with a
        reasonable duration, ordinary mutual indemnification with a
        liability cap, governing law in the parties' home jurisdictions).
        Quality over quantity — better to flag five real issues than
        twenty harmless ones.

        Rules:
        1. `excerpt` MUST be a verbatim span copied character-for-character
           from the contract. Do NOT paraphrase, compress, or rewrite.
           If the relevant span is awkward, copy it anyway — this is the
           audit trail.
        2. `location` should capture the section number when the contract
           uses one ("Section 12.3", "Article 8", "Clause 14.2", "§17.4").
           If the contract has no numbering at this point, set null.
        3. `rationale` is one factual sentence. NOT legal advice. NOT a
           value judgement. State *why* a human should look — e.g.
           "120-day notice window combined with 24-month auto-renewal
           creates rollover risk if calendar tracking lapses."
        4. `suggested_counter` is one short sentence describing a typical
           negotiating position. Mark it informational. If you can't
           think of a natural counter, set null — do not invent one.
        5. Each distinct clause appears at most once. If two paragraphs
           cover the same clause type with similar content, flag the
           more severe one.
        6. Return STRICT JSON. No prose before or after, no markdown
           fences, no trailing commas.
        7. If you find no matching clauses, return [].

        Example
        -------
        With the user message asking to scan all clause types, and the
        contract containing this fragment:

            Section 8.2 Indemnification. Provider shall indemnify Client
            against all damages arising from Provider's breach, without
            cap or limitation of any kind.

        Expected element in the output array:
            {
              "clause_type": "unlimited_liability",
              "severity": "high",
              "excerpt": "Provider shall indemnify Client against all damages arising from Provider's breach, without cap or limitation of any kind.",
              "location": "Section 8.2",
              "rationale": "Indemnification without a liability cap exposes Provider to unbounded financial risk.",
              "suggested_counter": "Cap indemnification at 12 months of fees paid, or at the amount of insurance coverage."
            }

        Notice that `excerpt` is the verbatim sentence from the contract,
        `location` matches the heading exactly, and both `rationale` and
        `suggested_counter` are factual, terse, and free of legal advice.
        """;
}
