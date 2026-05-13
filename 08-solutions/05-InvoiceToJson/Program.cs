using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Prompts;

// -----------------------------------------------------------------------------
// Invoice / Receipt → Structured JSON — turn OCR-style receipt or invoice text
// into one strictly-typed JSON object that downstream expense systems can
// ingest without further post-processing.
//
// The model reads the whole document in one shot (Qwen 2.5 7B), classifies
// the layout into vendor / metadata / line-items / totals / payment blocks,
// and emits a single `{...}` object matching the schema in the system prompt.
// We then locate that object in the reply, parse it with System.Text.Json,
// and round-trip it through a strictly-typed record graph so the final file
// has clean shape regardless of any extra prose the model added.
//
// Key design choices:
//
//   * One JSON object, not an array. The schema is heterogeneous (vendor block,
//     totals, line items each carry different fields), so the temperature-0
//     repetition trap that bit us in PII Redactor — where homogeneous list
//     output spirals on identical prefixes — does NOT apply here. We can run
//     pure-deterministic extraction and ship byte-identical JSON between runs.
//
//   * Balanced-brace walker. The model occasionally wraps the JSON in a
//     `Here is the parsed receipt:` preamble or a markdown fence. `FindJsonObject`
//     scans for the first `{`, then walks forward respecting string literals
//     and escapes until the matching `}` closes the object. That recovers the
//     payload without forcing the model to obey strict no-prose rules.
//
//   * Schema-deserialise, not raw element. Once the JSON is located, we
//     deserialise into typed records and re-serialise. That normalises field
//     ordering, drops fields the model added that are not in the schema, and
//     fills missing optional fields with null. The output file's shape is
//     fixed by our records — not by the model's free-form whim.
//
//   * Currency hint. `--currency-hint USD` (etc.) prepends a one-line nudge
//     above the receipt text. The model honours it for ambiguous symbols
//     like `$` (which can mean USD/CAD/AUD/MXN/HKD).
//
//   * Numbers as numbers. The schema uses `double?` for every monetary field.
//     System.Text.Json refuses to coerce `"1250.00"` (string) into `double` —
//     so if the model returns a quoted number, deserialisation fails loudly
//     and the raw reply is dumped for inspection. That's the correct
//     behaviour: silent string-to-double coercion would mask drift from the
//     contract.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
string? currencyHint = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--currency-hint" when i + 1 < args.Length:
            currencyHint = args[++i].Trim().ToUpperInvariant();
            if (currencyHint.Length != 3 || !currencyHint.All(char.IsLetter))
            {
                ConsoleHelpers.Fail($"--currency-hint must be a 3-letter ISO 4217 code (got '{currencyHint}').");
                return 1;
            }
            break;
        case "--out" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-receipt.txt");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Receipt file not found: {inputPath}");
    return 1;
}

outputPath ??= inputPath + ".parsed.json";

var receiptText = await File.ReadAllTextAsync(inputPath);

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Invoice / Receipt → JSON");
TutorialEnvironment.ApplyLicense();

ConsoleHelpers.Info($"Receipt: {inputPath} ({receiptText.Length:N0} chars)");
ConsoleHelpers.Info($"Hint:    {currencyHint ?? "(none)"}\n");

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
// Temperature strictly 0 is safe here because the JSON shape is heterogeneous
// (vendor block, totals, line_items each have different fields) — there is no
// uniform-prefix repetition trap of the kind that hit PII Redactor with
// line-based output. If the parser starts seeing truncation issues on
// pathological inputs, raise to 0.1.
preset.SamplerParameters.Temperature = 0.0f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = ExtractionSystemPrompt;
preset.ChatParameters.MaxTokens = 1024;

using var api = new AsposeLLMApi(preset);

var hintLine = currencyHint is null
    ? string.Empty
    : $"Currency hint: amounts should be reported as {currencyHint} unless the receipt explicitly states a different currency.\n\n";

var userMessage =
    $"{hintLine}" +
    $"Receipt / invoice text follows between the ---- markers.\n" +
    $"----\n{receiptText}\n----";

var reply = await api.SendMessageAsync(userMessage);

// ---------- Parse the JSON object ------------------------------------------
ParsedDocument? parsed;
string rawJson;
try
{
    rawJson = FindJsonObject(reply)
              ?? throw new FormatException("no JSON object found in model output");
    parsed = JsonSerializer.Deserialize<ParsedDocument>(rawJson, JsonOptions.Parse);
    if (parsed is null)
        throw new FormatException("JSON deserialised to null");
}
catch (Exception ex)
{
    ConsoleHelpers.Fail($"Failed to parse model output: {ex.Message}");
    Console.WriteLine("\n---- raw model output ----");
    Console.WriteLine(reply);
    return 2;
}

// Honour the currency hint if the model omitted the field.
if (string.IsNullOrWhiteSpace(parsed.Currency) && currencyHint is not null)
    parsed = parsed with { Currency = currencyHint };

// ---------- Output ----------------------------------------------------------
var outJson = JsonSerializer.Serialize(parsed, JsonOptions.Write);
await File.WriteAllTextAsync(outputPath, outJson);

ConsoleHelpers.Section("Parsed document");
var vendorName = parsed.Vendor?.Name ?? "(unknown vendor)";
Console.WriteLine($"  Parsed {parsed.DocumentType ?? "document"} from {vendorName}:");
Console.WriteLine($"    Date:         {parsed.Date ?? "—"}");
Console.WriteLine($"    Document:     {parsed.DocumentNumber ?? "—"} ({parsed.DocumentType ?? "—"})");
Console.WriteLine($"    Line items:   {parsed.LineItems?.Count ?? 0}");
Console.WriteLine($"    Subtotal:     {FormatMoney(parsed.Subtotal, parsed.Currency)}");
if (parsed.TaxRate is not null || parsed.Tax is not null)
{
    var ratePct = parsed.TaxRate is null ? "" : $" ({parsed.TaxRate * 100:0.#}%)";
    Console.WriteLine($"    Tax{ratePct,-8}  {FormatMoney(parsed.Tax, parsed.Currency)}");
}
if (parsed.Tip is { } tip && tip > 0)
    Console.WriteLine($"    Tip:          {FormatMoney(parsed.Tip, parsed.Currency)}");
Console.WriteLine($"    Total:        {FormatMoney(parsed.Total, parsed.Currency)}");
Console.WriteLine($"    Payment:      {parsed.PaymentMethod ?? "—"}");

if (parsed.LineItems is { Count: > 0 })
{
    ConsoleHelpers.Section("Line items");
    for (int i = 0; i < parsed.LineItems.Count; i++)
    {
        var li = parsed.LineItems[i];
        var qty = li.Quantity is null ? "?" : li.Quantity.Value.ToString(CultureInfo.InvariantCulture);
        var unit = FormatMoney(li.UnitPrice, parsed.Currency);
        var total = FormatMoney(li.Total, parsed.Currency);
        Console.WriteLine($"  {i + 1,2}. {qty,3} × {li.Description ?? "(no description)"}");
        Console.WriteLine($"      {unit} each → {total}");
    }
}

Console.WriteLine();
ConsoleHelpers.Info($"Wrote parsed JSON to {outputPath}");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string FormatMoney(double? value, string? currency)
{
    if (value is null) return "—";
    var amount = value.Value.ToString("0.00", CultureInfo.InvariantCulture);
    return currency is null ? amount : $"{amount} {currency}";
}

// Find the first balanced `{...}` chunk in the model's reply. Walks forward
// from the first `{` and tracks brace depth, taking string literals and
// escape sequences into account. Returns null if no balanced object exists.
//
// This sidesteps the strictest no-prose rule from the system prompt: even
// when the model adds `Here is the parsed receipt:` or wraps the answer in
// ```json fences, the JSON itself is recoverable.
static string? FindJsonObject(string text)
{
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

// ---------------------------------------------------------------------------
// JsonSerializer options — read forgiving, write strict.
// ---------------------------------------------------------------------------

internal static class JsonOptions
{
    // Forgiving parse: case-insensitive property names (the model occasionally
    // capitalises a field), tolerate quoted numbers as a fallback, skip
    // comments, allow trailing commas.
    public static readonly JsonSerializerOptions Parse = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Strict write: indented, never drop optional fields, relaxed escaping
    // so non-ASCII vendor names (Cyrillic, CJK, accented Latin) round-trip
    // as-is rather than escaping to \uXXXX.
    public static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

// ---------------------------------------------------------------------------
// Schema records — one knob to change shape, one place to add fields.
// ---------------------------------------------------------------------------

internal record Vendor(
    [property: JsonPropertyName("name")]    string? Name,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("tax_id")]  string? TaxId);

internal record LineItem(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("quantity")]    double? Quantity,
    [property: JsonPropertyName("unit_price")]  double? UnitPrice,
    [property: JsonPropertyName("total")]       double? Total);

internal record ParsedDocument(
    [property: JsonPropertyName("vendor")]          Vendor? Vendor,
    [property: JsonPropertyName("document_type")]   string? DocumentType,
    [property: JsonPropertyName("document_number")] string? DocumentNumber,
    [property: JsonPropertyName("date")]            string? Date,
    [property: JsonPropertyName("currency")]        string? Currency,
    [property: JsonPropertyName("line_items")]      List<LineItem>? LineItems,
    [property: JsonPropertyName("subtotal")]        double? Subtotal,
    [property: JsonPropertyName("tax")]             double? Tax,
    [property: JsonPropertyName("tax_rate")]        double? TaxRate,
    [property: JsonPropertyName("tip")]             double? Tip,
    [property: JsonPropertyName("total")]           double? Total,
    [property: JsonPropertyName("payment_method")]  string? PaymentMethod);

// ---------------------------------------------------------------------------
// System prompt
// ---------------------------------------------------------------------------

internal static class Prompts
{
    public const string ExtractionSystemPrompt = """
        You are an invoice / receipt data extractor. The user pastes the text
        of a receipt or invoice — often produced by OCR, so it may contain
        line misalignment, stray pipe characters, l-vs-1 confusions, or other
        scanning artefacts. You emit ONE strict JSON object matching the
        schema below — no markdown fences, no commentary, no explanation.

        Schema:
        {
          "vendor": {
            "name":    string|null,
            "address": string|null,
            "tax_id":  string|null
          },
          "document_type":   "receipt" | "invoice" | "unknown",
          "document_number": string|null,
          "date":            "YYYY-MM-DD"|null,
          "currency":        "USD"|"EUR"|"GBP"|"RUB"|"JPY"|... (ISO 4217 3-letter code),
          "line_items": [
            {
              "description": string,
              "quantity":    number,
              "unit_price":  number,
              "total":       number
            }
          ],
          "subtotal":       number|null,
          "tax":            number|null,
          "tax_rate":       number|null,
          "tip":            number|null,
          "total":          number|null,
          "payment_method": "cash" | "card" | "bank_transfer" | "other" | null
        }

        Rules:
        1. Numbers must be JSON numbers (1250.00 — NOT "1,250.00", NOT "$1250").
           Strip currency symbols, thousands separators, and trailing notes
           ("each", "incl.") before placing the value in the JSON.
        2. Locale-aware decimals: European "1.250,50" means one thousand two
           hundred fifty point five; US "1,250.50" is the same number. Both
           normalise to the JSON number 1250.50.
        3. tax_rate is a fraction in [0.0, 1.0]. If the receipt shows
           "VAT 20%", set tax_rate to 0.20. If a percentage is stated AND an
           absolute tax amount is shown, populate BOTH tax and tax_rate.
        4. document_type: "receipt" = consumer-facing point-of-sale slip;
           "invoice" = B2B document with itemised VAT and (often) a due date.
           Use "unknown" when nothing in the input distinguishes them.
        5. date in ISO format YYYY-MM-DD. If the receipt prints "14/03/2026"
           or "March 14, 2026", normalise to "2026-03-14". Use null when no
           date is present.
        6. currency is the ISO 4217 3-letter code. If the receipt shows "$"
           and no other context, default to "USD" — UNLESS a currency hint
           was provided in the user message, in which case use that hint.
        7. Subtotal-row guard: if the receipt has an explicit "Subtotal" row,
           do NOT also include it as a line_item. line_items are only the
           individual goods or services purchased.
        8. Discounts and "buy 2 get 1 free" promotions appear as line_items
           with a negative `total`. Keep the description verbatim.
        9. If a field is missing from the input, return null. NEVER guess —
           a null is always preferable to a confabulated value.
        10. Return STRICT JSON only. No markdown fences, no preamble, no
            "Here is the parsed receipt:" prose. Start the response with `{`
            and end with `}`.

        Example
        -------
        Input fragment:

            COFFEE SHOP — Receipt R-2026-0478
            Date: 2026-03-14
            Flat white      2 x 280.00   560.00
            Almond pastry   1 x 180.00   180.00
            Subtotal                     740.00
            VAT 20% (incl.)              123.33
            TOTAL                        740.00 RUB
            Paid by card

        Expected output (formatted here for clarity — emit on as few lines
        as you wish, as long as it is valid JSON):
        {
          "vendor": { "name": "COFFEE SHOP", "address": null, "tax_id": null },
          "document_type": "receipt",
          "document_number": "R-2026-0478",
          "date": "2026-03-14",
          "currency": "RUB",
          "line_items": [
            { "description": "Flat white",    "quantity": 2, "unit_price": 280.00, "total": 560.00 },
            { "description": "Almond pastry", "quantity": 1, "unit_price": 180.00, "total": 180.00 }
          ],
          "subtotal": 740.00,
          "tax": 123.33,
          "tax_rate": 0.20,
          "tip": null,
          "total": 740.00,
          "payment_method": "card"
        }

        Note that the Subtotal row is reflected in the `subtotal` field —
        NOT repeated as a third line_item — and the VAT 20% entry populates
        both `tax` (the absolute amount) and `tax_rate` (the fraction 0.20).
        """;
}
