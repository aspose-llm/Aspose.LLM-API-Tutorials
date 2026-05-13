using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Constants;

// -----------------------------------------------------------------------------
// PII Redactor — scrub personally-identifiable information from text using a
// local LLM, with a deterministic and auditable pipeline.
//
// Design note. Rather than ask the model to emit the redacted text directly
// (which invites whitespace reflows, invented attribution, and flaky diffs),
// we ask it for a JSON list of PII SPANS it found. Replacement happens in
// plain C# — string.Replace over the original input. That gives us three
// wins:
//   * Determinism. Same input + same style → byte-identical output, because
//     the only stochastic step (LLM entity detection) runs with temperature 0
//     and a fixed seed, and the replacement step is pure.
//   * Audit trail. The JSON list is the audit report — no extra diffing.
//   * Stable style. Mapping an entity value to a stable ID (`<PERSON_1>`) is a
//     trivial post-process, not something the model has to reason about
//     across the whole document.
//
// Three replacement styles (choose via --style):
//   - labeled  — default. Each hit becomes `<REDACTED_NAME>`, `<REDACTED_EMAIL>`, etc.
//   - stable   — each distinct value gets a stable id: `<PERSON_1>`, `<EMAIL_2>`.
//                The same person stays `<PERSON_1>` every time they're mentioned.
//   - generic  — every hit becomes `████`. Full obliteration for maximum safety.
//
// First-line-filter disclaimer: this is a pragmatic tool, not a compliance
// oracle. See the README.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string style = "labeled";
HashSet<string>? categoryFilter = null;
string? inputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--style" when i + 1 < args.Length:
            style = args[++i].ToLowerInvariant();
            break;
        case "--categories" when i + 1 < args.Length:
            categoryFilter = new HashSet<string>(
                args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToUpperInvariant()));
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

if (style is not ("labeled" or "stable" or "generic"))
{
    ConsoleHelpers.Fail($"Unknown --style '{style}'. Use labeled | stable | generic.");
    return 1;
}

// Default to the bundled sample next to the executable.
inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-ticket.txt");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Input file not found: {inputPath}");
    return 1;
}

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — PII Redactor");
TutorialEnvironment.ApplyLicense();

var inputText = await File.ReadAllTextAsync(inputPath);
ConsoleHelpers.Info($"Input:  {inputPath} ({inputText.Length:N0} chars)");
ConsoleHelpers.Info($"Style:  {style}");
ConsoleHelpers.Info($"Filter: {(categoryFilter is null ? "all categories" : string.Join(", ", categoryFilter))}\n");

// Model choice. Empirically, Qwen 2 0.5B caps out around 65-70% recall on
// this exhaustive-extraction task — it keeps stopping early after the first
// few obvious spans. Qwen 2.5 7B (Qwen25Preset) scans all the way through
// and near-perfect recall is the norm. Both presets use the same tutorial
// model cache, so there's no extra download once either has been used in
// any prior tutorial.
//
// Near-deterministic sampler: tiny temperature + fixed seed produces
// reproducible output on a given host; the replacement step downstream is
// pure, so the redacted file is byte-identical on re-run.
var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.SamplerParameters.Temperature = 0.1f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = PiiSystemPrompt;
preset.ChatParameters.MaxTokens = Math.Clamp(inputText.Length / 2, 512, 2048);

using var api = new AsposeLLMApi(preset);
var reply = await api.SendMessageAsync(inputText);

// ---------- Parse entities --------------------------------------------------
List<Entity> entities;
try
{
    entities = ExtractEntities(reply, inputText);
}
catch (Exception ex)
{
    ConsoleHelpers.Fail($"Failed to parse model output: {ex.Message}");
    Console.WriteLine("\n---- raw model output ----");
    Console.WriteLine(reply);
    return 2;
}

// Apply category filter if the user asked for a subset.
if (categoryFilter is not null)
    entities = entities.Where(e => categoryFilter.Contains(e.Type)).ToList();

ConsoleHelpers.Info($"Model identified {entities.Count} PII span(s).");

// ---------- Apply redaction (pure, deterministic) --------------------------
var redacted = ApplyRedaction(inputText, entities, style, out var audit);

// ---------- Write outputs ---------------------------------------------------
var outText = inputPath + ".redacted.txt";
var outAudit = inputPath + ".redactions.json";

await File.WriteAllTextAsync(outText, redacted);
var auditJson = JsonSerializer.Serialize(
    new { style, input = inputPath, redactions = audit },
    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
await File.WriteAllTextAsync(outAudit, auditJson);

// ---------- Console summary -------------------------------------------------
ConsoleHelpers.Section("Summary");
Console.WriteLine($"  Input chars:    {inputText.Length:N0}");
Console.WriteLine($"  Output chars:   {redacted.Length:N0}");
Console.WriteLine($"  Redactions:     {audit.Count}");
foreach (var g in audit.GroupBy(a => a.Type).OrderByDescending(g => g.Count()))
    Console.WriteLine($"    {g.Count()}× {g.Key}");
Console.WriteLine();
Console.WriteLine($"  Wrote redacted text to {outText}");
Console.WriteLine($"  Wrote audit trail   to {outAudit}");

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static List<Entity> ExtractEntities(string reply, string originalInput)
{
    // Line-based format produced by the model:
    //   NAME|John Smith
    //   EMAIL|john.smith@example.com
    //   END
    //
    // This format sidesteps a failure mode common in 0.5B LLMs: when asked
    // for a JSON array, the model's argmax-at-temperature-0 keeps emitting
    // the same `"type":` / `"value":` prefix and spirals into a
    // fixed-point repetition. A TYPE|VALUE grammar varies at the very
    // first token of each line, which makes the repetition trap much
    // harder to enter.
    //
    // Defensive parser: ignore blank lines, stop at END, drop rows whose
    // type isn't in our seven-category vocabulary, drop rows whose value
    // isn't a verbatim substring of the input (hallucination gate).
    var result = new List<Entity>();
    var seen = new HashSet<(string, string)>();

    foreach (var rawLine in reply.Split('\n'))
    {
        var line = rawLine.Trim();
        if (line.Length == 0) continue;
        if (line.Equals("END", StringComparison.OrdinalIgnoreCase)) break;

        // Strip markdown fences / commentary prefixes that small models
        // occasionally add on the first line.
        if (line.StartsWith("```")) continue;

        var sep = line.IndexOf('|');
        if (sep <= 0 || sep == line.Length - 1) continue;

        var type = line[..sep].Trim().ToUpperInvariant();
        var value = line[(sep + 1)..].Trim();

        if (!KnownTypes.Contains(type)) continue;
        if (string.IsNullOrWhiteSpace(value)) continue;
        if (!originalInput.Contains(value, StringComparison.Ordinal)) continue;
        if (!seen.Add((type, value))) continue;

        result.Add(new Entity(type, value));
    }
    return result;
}

static string ApplyRedaction(
    string text,
    List<Entity> entities,
    string style,
    out List<Redaction> audit)
{
    // Assign stable IDs per (type, value). Same value → same ID throughout.
    var stableIds = new Dictionary<(string type, string value), int>();
    var counters = new Dictionary<string, int>();
    foreach (var e in entities.OrderBy(e => text.IndexOf(e.Value, StringComparison.Ordinal)))
    {
        if (stableIds.ContainsKey((e.Type, e.Value))) continue;
        counters.TryGetValue(e.Type, out var n);
        stableIds[(e.Type, e.Value)] = ++n;
        counters[e.Type] = n;
    }

    // Replace longest spans first so "John Smith" is not partially replaced
    // by an earlier pass on "John".
    var uniqueByValue = entities
        .DistinctBy(e => e.Value)
        .OrderByDescending(e => e.Value.Length)
        .ToList();

    audit = new List<Redaction>();
    var result = text;
    foreach (var e in uniqueByValue)
    {
        var replacement = GetReplacement(e, style, stableIds[(e.Type, e.Value)]);
        // Count occurrences before replacing so the audit records the true
        // impact (a single entity can appear several times).
        int hits = CountOccurrences(result, e.Value);
        if (hits == 0) continue;  // a longer replacement already subsumed it
        result = result.Replace(e.Value, replacement);
        audit.Add(new Redaction(e.Type, e.Value, replacement, hits));
    }
    return result;
}

static string GetReplacement(Entity e, string style, int stableId) => style switch
{
    "labeled" => $"<REDACTED_{e.Type}>",
    "stable"  => $"<{StableLabel(e.Type)}_{stableId}>",
    "generic" => "████",
    _         => throw new ArgumentOutOfRangeException(nameof(style))
};

static string StableLabel(string type) => type switch
{
    "NAME"        => "PERSON",
    "EMAIL"       => "EMAIL",
    "PHONE"       => "PHONE",
    "ADDRESS"     => "ADDRESS",
    "CREDIT_CARD" => "CARD",
    "ID_NUMBER"   => "ID",
    "URL"         => "URL",
    _             => "REDACTED"
};

static int CountOccurrences(string haystack, string needle)
{
    if (string.IsNullOrEmpty(needle)) return 0;
    int count = 0, idx = 0;
    while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
    {
        count++;
        idx += needle.Length;
    }
    return count;
}

// ---------------------------------------------------------------------------
// Types + constants
// ---------------------------------------------------------------------------

internal record Entity(string Type, string Value);

internal record Redaction(
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("value")]       string Value,
    [property: JsonPropertyName("replacement")] string Replacement,
    [property: JsonPropertyName("occurrences")] int Occurrences);

internal static class Constants
{
    public static readonly HashSet<string> KnownTypes = new()
    {
        "NAME", "EMAIL", "PHONE", "ADDRESS", "CREDIT_CARD", "ID_NUMBER", "URL"
    };

    public const string PiiSystemPrompt = """
        You are a PII detection engine. Your job is to EXHAUSTIVELY scan
        the user's text and list every personally-identifiable span you
        can find. Be thorough: scan every paragraph, every parenthetical,
        every quoted fragment. Do not stop after the first few hits.

        Output format — one span per line:

            TYPE|VALUE

        After the last span, emit a single line containing the word END.
        If you find no spans, output only END.

        Allowed TYPE values (use ONLY these seven, exactly as written):
        - NAME         full names or given names of real people in context.
                       NOT brand names, product names, cities, countries.
        - EMAIL        email addresses.
        - PHONE        phone numbers in any format.
        - ADDRESS      street addresses. Not bare city / country names.
        - CREDIT_CARD  card numbers or "ending NNNN" partial references.
        - ID_NUMBER    customer IDs, employee IDs, national IDs, passport
                       numbers. NOT: order, invoice, ticket, or version
                       numbers — those are business identifiers.
        - URL          URLs that expose identity (social profiles, personal
                       sites). NOT: public documentation URLs.

        Rules:
        1. VALUE must be copied character-for-character from the input.
           Do not paraphrase, normalise, or trim.
        2. Each distinct VALUE appears AT MOST ONCE in your output.
        3. No commentary, no markdown fences, no preamble. Start directly
           with the first TYPE|VALUE line (or END if nothing was found).
        4. Redact the LARGEST enclosing span. An email containing a name
           is ONE EMAIL entity, not a NAME + URL pair.
        5. When unsure, skip. False negatives are recoverable; false
           positives destroy information.
        6. Parenthetical mentions and internal-note sections count. If a
           person's name appears at the end in "(assigned to X)" form,
           X is still a NAME to capture.

        Example
        -------
        Input text:
            Case #C-87 — reported by Sarah Young (sarah.young@acme.co,
            office phone +1 (415) 555-0188). Address: 77 Market St., SF.
            Customer id 4410-221. Card on file ending in 1212. Backup
            contact: dmitry.ivanov@acme.co. Assigned to Oleg Kuznetsov.

        Expected output:
            NAME|Sarah Young
            EMAIL|sarah.young@acme.co
            PHONE|+1 (415) 555-0188
            ADDRESS|77 Market St., SF
            ID_NUMBER|4410-221
            CREDIT_CARD|ending in 1212
            EMAIL|dmitry.ivanov@acme.co
            NAME|Oleg Kuznetsov
            END

        The example catches eight spans from five lines. Aim for at least
        that level of thoroughness on the user's text. Note that "Oleg
        Kuznetsov" is captured even though he appears in the final
        assignment line — parenthetical and trailing mentions count.
        """;
}

