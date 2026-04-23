# Handle Cancellation

Four scenarios running the same long prompt — three cancel it at different
points, one lets it complete — showing how to wire `CancellationToken`
through `SendMessageAsync` and what happens when cancellation fires.

## What it demonstrates

- **Timeout cancellation** via `CancellationTokenSource.CancelAfter`.
- **User cancellation** via explicit `cts.Cancel()` from another task.
- **Linked cancellation** via
  `CancellationTokenSource.CreateLinkedTokenSource` — first source to
  fire wins.
- The exception shape: `OperationCanceledException` thrown from
  `SendMessageAsync`; **no partial reply is returned** — the method
  either completes fully or throws.
- Measured elapsed time at cancel — typically within 10-50 ms of the
  token firing, because the SDK checks cooperatively on every token.

## Run it

```bash
cd 07-how-to/HandleCancellation
dotnet run
```

Typical output:

```
── Scenario 1 — 800 ms timeout ──
  cancelled after   812 ms (OperationCanceledException thrown, no partial reply returned)

── Scenario 2 — user cancel after ~400 ms ──
  cancelled after   418 ms (OperationCanceledException thrown, no partial reply returned)

── Scenario 3 — linked (deadline + external, earliest wins) ──
  cancelled after   615 ms (OperationCanceledException thrown, no partial reply returned)

── Scenario 4 — no cancellation (full reply, for comparison) ──
  completed in  4,230 ms
  reply chars: 1,540
  sample:      'Prefill is the initial stage of inference where the model processes the entire prompt at once…'
```

Numbers vary. The key pattern: scenario 1 cancelled near 800 ms, scenario 2
near 400 ms, scenario 3 near 600 ms (external beat the 3-second deadline),
and scenario 4 ran to completion in ~4 s.

## Partial output?

**There isn't one.** `SendMessageAsync` returns the whole reply as a single
string. When cancellation fires, the engine aborts and the method throws;
there's no intermediate string of half-generated tokens to inspect. If
your UI needs to show "I got 400 tokens before the user cancelled",
you'd need token streaming — which is not in the current facade surface.

If streaming is important for your use case, file a feature request;
alternatively, split your prompt into phases (ingestion, then incremental
Q&A) and cancel at a phase boundary.

## The ~10-50 ms cancel latency

The SDK inspects the `CancellationToken` inside the native generation
loop once per token. Token generation on Qwen 2 0.5B CPU is roughly
20-30 ms per token, so a cancel fires after at most that many
milliseconds of delay. On GPU that window shrinks to the low single
digits. Effectively cooperative cancellation, not pre-emptive.

## Common pitfalls

- **Passing `CancellationToken.None`** when you meant to pass a real
  token. The operation will never cancel. Always wire
  `HttpContext.RequestAborted` in ASP.NET, or a real `CancellationTokenSource`
  from your caller.
- **Not `using`-ing the CTS.** `CancellationTokenSource` is `IDisposable`
  and holds a timer if you called `CancelAfter`; leaking it leaks the
  timer. Always `using var cts = ...` or dispose in `finally`.
- **Catching `Exception` instead of `OperationCanceledException`.** The
  latter is the specific exception the SDK throws on cancel; it derives
  from `Exception` but also from `TaskCanceledException`, so you can
  catch it as either.
- **Relying on cancellation to "trim" long outputs.** Use `MaxTokens`
  for that — it's a hard generation-side stop and always works. Cancel
  is for responding to external signals, not for shaping output length.

## Things to try

- Reduce `MaxTokens` to 50 and watch scenario 4 run in a tenth of the
  time. Cancellation windows shrink accordingly.
- Wire the program's Ctrl+C handler (as [`SimpleChat`](../../02-text-chat/SimpleChat/)
  does) to fire a real CTS and cancel scenarios on demand rather than
  by timer.
- Add a fifth scenario with a token that's already cancelled when
  passed in (`cts.Cancel(); await api.SendMessageAsync(..., cts.Token);`).
  Expected behaviour: immediate throw without any generation at all.
