using System.Diagnostics;
using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Batch processing — one loaded model, many independent prompts.
//
// The expensive steps in using an LLM are (a) loading the native binary,
// (b) mapping the GGUF into memory, and (c) warming up the first KV batch.
// Once those are paid, *each additional prompt is cheap*. This tutorial runs
// a small sentiment-classification batch to show the shape of that pattern.
//
// Each item gets its OWN session, so prior items never contaminate the next
// classification. Sessions are created and then forgotten — the SDK reclaims
// their KV cache on the next cleanup cycle.
//
// The same pattern scales to hundreds of items; for thousands, consider
// saving sessions to disk (see 02-text-chat/SessionPersistence) or running
// multiple processes on separate GPUs. The Engine itself is NOT thread-safe,
// so within one process everything is serial.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Batch Processing");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
preset.ChatParameters.SystemPrompt =
    "You are a precise sentiment classifier. For each sentence you see, reply with exactly one word: positive, negative, or neutral. No punctuation, no explanation.";
preset.ChatParameters.MaxTokens = 8;              // one word is plenty
preset.SamplerParameters.Temperature = 0.1f;      // deterministic-ish

using var api = new AsposeLLMApi(preset);

var items = new[]
{
    "The support team solved my issue in under ten minutes.",
    "I cannot believe how broken the new release is.",
    "The meeting ended on time. Nothing else to report.",
    "My favourite bakery is closing next month. I'm gutted.",
    "The update adds dark mode and a few keyboard shortcuts.",
    "Every single task I gave the assistant was flawless.",
    "Battery life is about what the spec sheet says.",
    "Three hours on hold, then a disconnect — appalling.",
};

ConsoleHelpers.Info($"Classifying {items.Length} sentences, one session each.\n");

var total = Stopwatch.StartNew();
var results = new List<(string Text, string Label, long Ms)>();

foreach (var sentence in items)
{
    // Fresh session per item so the prior label doesn't bias the next call
    // through chat history. This is idiomatic for classification/extraction
    // workloads — the model should treat each input in isolation.
    var sessionId = await api.StartNewChatAsync();

    var sw = Stopwatch.StartNew();
    var label = (await api.SendMessageToSessionAsync(sessionId, sentence))
        .Trim()
        .ToLowerInvariant();
    sw.Stop();

    results.Add((sentence, label, sw.ElapsedMilliseconds));
}

total.Stop();

// --- Present results ---------------------------------------------------------
ConsoleHelpers.Section("Results");
foreach (var (text, label, ms) in results)
{
    var colour = label switch
    {
        "positive" => ConsoleColor.Green,
        "negative" => ConsoleColor.Red,
        "neutral" => ConsoleColor.DarkGray,
        _ => ConsoleColor.Yellow,     // model went off-script
    };
    var prev = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = colour;
        Console.Write($"  [{label,-8}]");
    }
    finally { Console.ForegroundColor = prev; }

    Console.Write($"  ({ms,4} ms)  ");
    Console.WriteLine(text.Length > 70 ? text[..70] + "…" : text);
}

ConsoleHelpers.Section("Throughput");
var avgMs = results.Count == 0 ? 0 : results.Sum(r => r.Ms) / results.Count;
Console.WriteLine(
    $"  Items:        {results.Count}\n" +
    $"  Total time:   {total.Elapsed.TotalSeconds:F1}s (includes ~1× model load)\n" +
    $"  Per item avg: {avgMs} ms\n" +
    $"  Throughput:   {results.Count / Math.Max(0.001, total.Elapsed.TotalSeconds):F2} items/s");

ConsoleHelpers.Info("\nAll items processed with a single loaded model.");
