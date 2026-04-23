using System.Diagnostics;
using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// CPU-only deployment — explicit thread tuning.
//
// On a CPU-only host (no GPU, or one you deliberately don't want to use),
// the single biggest performance knob is how many CPU threads the native
// engine runs in parallel. llama.cpp distinguishes two pools:
//   * NThreads        — threads for token-by-token generation (hot path).
//   * NThreadsBatch   — threads for batch prefill (cold path, ingesting
//                       the prompt before generation starts).
//
// This tutorial sweeps NThreads across 1, 2, 4, and ProcessorCount, timing
// the same generation each time. Typical finding on a modern laptop: more
// threads helps up to the number of performance cores, then levels off or
// even regresses because of context-switching overhead.
//
// Each run uses its own AsposeLLMApi instance (the facade is a process
// singleton), so we dispose+recreate between sweeps.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — CPU-Only Deployment");

TutorialEnvironment.ApplyLicense();

var procCount = Environment.ProcessorCount;
var threadCounts = new[] { 1, 2, 4, procCount }
    .Distinct()
    .Where(n => n >= 1 && n <= procCount)
    .OrderBy(n => n)
    .ToArray();

ConsoleHelpers.Info(
    $"System reports {procCount} logical processors. " +
    $"Sweeping NThreads ∈ {{{string.Join(", ", threadCounts)}}}.\n");

const string Question =
    "Briefly explain what 'prefill' and 'decode' mean in LLM inference, in 3-4 sentences.";

var results = new List<(int Threads, long FirstTokenMs, long TotalMs, int ReplyChars)>();

foreach (var threads in threadCounts)
{
    ConsoleHelpers.Section($"NThreads = {threads}");

    var preset = new SmallModelPreset();

    // Explicitly disable GPU offload — we're measuring CPU. SmallModelPreset
    // already defaults to GpuLayers=0, but be defensive.
    preset.BaseModelInferenceParameters.GpuLayers = 0;

    // The two thread-pool knobs. For a classifier/short-answer workload,
    // NThreadsBatch matters less (short prompts); for long-context
    // prefill it dominates.
    preset.ContextParameters.NThreads = threads;
    preset.ContextParameters.NThreadsBatch = threads;

    // Disable flash-attention etc. to keep the comparison clean.
    preset.ContextParameters.FlashAttention = false;
    preset.ContextParameters.OffloadKqv = false;

    preset.ChatParameters.MaxTokens = 120;
    preset.ChatParameters.SystemPrompt =
        "Answer concisely and technically.";

    using var api = new AsposeLLMApi(preset);

    var sw = Stopwatch.StartNew();
    var reply = await api.SendMessageAsync(Question);
    sw.Stop();

    // We don't get first-token time from the facade today — the string is
    // returned only when complete. Record total time and let the README
    // explain why the breakdown isn't available.
    results.Add((threads, 0, sw.ElapsedMilliseconds, reply.Length));

    ConsoleHelpers.Info(
        $"  Total time:  {sw.ElapsedMilliseconds,5:N0} ms\n" +
        $"  Reply chars: {reply.Length}\n" +
        $"  Sample:      '{reply.Trim().Substring(0, Math.Min(80, reply.Trim().Length))}…'");
}

// --- Summary ---------------------------------------------------------------
ConsoleHelpers.Section("Summary — threads vs time");
Console.WriteLine();
Console.WriteLine("  Threads │ Total time │ Speedup vs 1-thread");
Console.WriteLine("  ────────┼────────────┼────────────────────");

var baseline = results.First().TotalMs;
foreach (var (t, _, total, _) in results)
{
    var speedup = baseline / (double)total;
    Console.WriteLine($"   {t,5}  │ {total,7:N0} ms │ {speedup,6:F2}×");
}

ConsoleHelpers.Info(
    "\nExpect near-linear speedup up to the number of performance cores, " +
    "then diminishing returns or mild regression past that. If your Mac " +
    "has efficiency + performance cores, don't count the efficiency cores " +
    "into your NThreads upper bound.");
