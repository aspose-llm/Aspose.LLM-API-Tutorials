using System.Diagnostics;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Low-memory tuning — knobs that shrink the runtime footprint.
//
// The memory footprint of a loaded LLM comes from three places:
//   1. The model weights. Fixed by the GGUF file; mmap keeps it paged out of
//      RSS until actually touched, but the virtual mapping is still full size.
//   2. The KV cache. Scales linearly with ContextSize × n_layers × hidden_dim,
//      and can be halved by switching from F16 to Q8_0 cache dtype.
//   3. Compute buffers. Small; scale with NBatch. Rarely the bottleneck.
//
// This tutorial runs the same model under two configs back-to-back, each in
// its own `using` scope (the facade is a process singleton, so we dispose
// the first engine before building the second), and compares process RSS.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Low Memory");

TutorialEnvironment.ApplyLicense();

Console.WriteLine();

// --- Baseline: SmallModelPreset defaults -----------------------------------
var baseline = await MeasureAsync("Baseline  (ctx=4096, KV=F16)", preset =>
{
    // SmallModelPreset defaults: ContextSize 4096, KV cache at F16.
});

// --- Low-memory config -----------------------------------------------------
var tight = await MeasureAsync("Low-mem   (ctx=1024, KV=Q8_0)", preset =>
{
    // Three levers, smallest first:
    // (a) smaller ContextSize — linearly shrinks the KV cache.
    preset.ContextParameters.ContextSize = 1024;
    preset.ContextParameters.NBatch = 256;
    preset.ContextParameters.NUbatch = 128;

    // (b) quantise the KV cache from F16 to Q8_0 — roughly half the KV RAM
    //     at a small quality cost. Q4_0 is an even harsher option if the
    //     model tolerates it.
    preset.ContextParameters.TypeK = GgmlType.Q8_0;
    preset.ContextParameters.TypeV = GgmlType.Q8_0;

    // (c) leave mmap on (the default). mmap lets the OS page weights out of
    //     RSS under memory pressure; turning it off forces the whole GGUF
    //     into resident memory.
    preset.BaseModelInferenceParameters.UseMemoryMapping = true;
    preset.BaseModelInferenceParameters.UseMemoryLocking = false;
});

// --- Summary ----------------------------------------------------------------
ConsoleHelpers.Section("Summary");
Console.WriteLine($"  Baseline RSS after load: {baseline / 1_000_000,7:N0} MB");
Console.WriteLine($"  Low-mem  RSS after load: {tight    / 1_000_000,7:N0} MB");
Console.WriteLine($"  Delta:                   {(baseline - tight) / 1_000_000,7:N0} MB  ({(baseline - tight) * 100.0 / baseline:F1}% smaller)");

ConsoleHelpers.Info(
    "\nNumbers will vary by platform. On macOS with mmap, RSS measures *resident* " +
    "pages — the virtual mapping is the same for both configs, but the " +
    "low-mem run touches fewer of them. On Windows/Linux you may see a " +
    "larger absolute delta because of how those kernels account for " +
    "file-backed memory.");

return;

// ---------------------------------------------------------------------------
static async Task<long> MeasureAsync(string label, Action<SmallModelPreset> configure)
{
    ConsoleHelpers.Section(label);

    var preset = new SmallModelPreset();
    configure(preset);

    using var api = new AsposeLLMApi(preset);

    // Force the model to actually load by issuing one tiny query — otherwise
    // we're timing just the C# object graph, not the native weights.
    var sw = Stopwatch.StartNew();
    var reply = await api.SendMessageAsync("Reply with exactly: OK");
    sw.Stop();

    // Encourage the GC to release anything transient so the RSS reading
    // reflects the model itself, not debris from the generation.
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var proc = Process.GetCurrentProcess();
    proc.Refresh();
    var rss = proc.WorkingSet64;

    Console.WriteLine($"  First-reply time: {sw.ElapsedMilliseconds,5:N0} ms");
    Console.WriteLine($"  Reply sample:     '{reply.Trim().Substring(0, Math.Min(40, reply.Trim().Length))}'");
    Console.WriteLine($"  Process RSS:      {rss / 1_000_000,5:N0} MB");

    return rss;
}
