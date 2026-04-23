using System.Diagnostics;
using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Handling cancellation — stop a generation before it finishes.
//
// Every method on AsposeLLMApi that starts inference takes an optional
// CancellationToken. When the token is cancelled, the native engine aborts
// the ongoing generation and the method throws OperationCanceledException.
//
// Three common cancellation sources:
//   1. Time-based — "don't let this run longer than N seconds".
//   2. User-initiated — Ctrl+C, a UI button, a client disconnect in a
//      web server (HttpContext.RequestAborted).
//   3. Linked — combine both, so whichever fires first wins.
//
// This tutorial runs all three head-to-head against a prompt designed to
// produce a long reply (MaxTokens = 1024), then reports what happened.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Handle Cancellation");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
preset.ChatParameters.MaxTokens = 1024;        // give the model room to ramble
preset.ChatParameters.SystemPrompt =
    "You are a detailed assistant. When asked a question, give a thorough multi-paragraph answer.";

using var api = new AsposeLLMApi(preset);

const string LongPrompt =
    "Explain the difference between prefill and decode phases in LLM inference. " +
    "Walk me through what happens at each phase, why they have different " +
    "compute characteristics, and how batching interacts with both. " +
    "Take your time and be thorough.";

// --- Scenario 1: timeout via CancelAfter ------------------------------------
await RunScenario(
    "Scenario 1 — 800 ms timeout",
    async () =>
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(800));
        return await api.SendMessageAsync(LongPrompt, cancellationToken: cts.Token);
    });

// --- Scenario 2: user cancellation (simulated) ------------------------------
await RunScenario(
    "Scenario 2 — user cancel after ~400 ms",
    async () =>
    {
        using var cts = new CancellationTokenSource();
        // Kick off a "UI thread" that cancels explicitly (not via a deadline).
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            cts.Cancel();
        });
        return await api.SendMessageAsync(LongPrompt, cancellationToken: cts.Token);
    });

// --- Scenario 3: linked tokens (whichever fires first wins) -----------------
await RunScenario(
    "Scenario 3 — linked (deadline + external, earliest wins)",
    async () =>
    {
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var external = new CancellationTokenSource();

        using var linked = CancellationTokenSource
            .CreateLinkedTokenSource(deadline.Token, external.Token);

        // Fire the external cancel earlier than the deadline — it should win.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            external.Cancel();
        });

        try
        {
            return await api.SendMessageAsync(LongPrompt, cancellationToken: linked.Token);
        }
        finally
        {
            deadline.Dispose();
            external.Dispose();
        }
    });

// --- Scenario 4: the happy path (no cancellation, for contrast) ------------
await RunScenario(
    "Scenario 4 — no cancellation (full reply, for comparison)",
    () => api.SendMessageAsync(LongPrompt));

ConsoleHelpers.Info(
    "\nTakeaway: cancellation is the same token-based pattern every .NET " +
    "async API uses. The SDK wraps the native loop in cooperative checks, " +
    "so the exception surfaces within one token of the cancel firing — " +
    "usually tens of milliseconds, not seconds.");

return;

// ---------------------------------------------------------------------------
static async Task RunScenario(string label, Func<Task<string>> body)
{
    ConsoleHelpers.Section(label);

    var sw = Stopwatch.StartNew();
    try
    {
        var reply = await body();
        sw.Stop();
        var snippet = reply.Trim();
        if (snippet.Length > 100) snippet = snippet.Substring(0, 100) + "…";
        Console.WriteLine($"  completed in {sw.ElapsedMilliseconds,5:N0} ms");
        Console.WriteLine($"  reply chars: {reply.Length}");
        Console.WriteLine($"  sample:      '{snippet}'");
    }
    catch (OperationCanceledException)
    {
        sw.Stop();
        ConsoleHelpers.Warn($"  cancelled after {sw.ElapsedMilliseconds:N0} ms " +
                            "(OperationCanceledException thrown, no partial reply returned)");
    }
    catch (Exception ex)
    {
        sw.Stop();
        ConsoleHelpers.Fail($"  unexpected {ex.GetType().Name} after {sw.ElapsedMilliseconds:N0} ms: {ex.Message}");
    }
}
