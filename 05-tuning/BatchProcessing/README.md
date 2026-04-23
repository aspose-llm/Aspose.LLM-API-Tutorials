# Batch Processing

Classify, extract from, or summarise N pieces of input through a single
loaded model. The pattern beats spawning a new process per item by a factor
of "model load time ÷ per-item time" — typically 10-100×.

## What it demonstrates

- **One `AsposeLLMApi` instance** that serves many prompts.
- **Fresh session per item** via `StartNewChatAsync()` so prior outputs
  don't contaminate subsequent classifications.
- **Per-item + total timing** so you can see where the cost goes.
- Low-temperature system prompt shaped for structured one-word output.

## Why separate sessions

If you loop over inputs using only `SendMessageAsync` (no explicit session),
the facade keeps feeding them into the same conversation. By item #5 the
model has seen your system prompt and four prior classifications — and it
starts *pattern-matching its own replies* instead of the new input. Worse, a
long batch fills the context window and triggers `CacheCleanupStrategy`,
which throws out arbitrary older messages. Fresh sessions keep every item
independent.

The cost of a fresh session is one token-prefill of the system prompt. The
SDK's KV cache makes that cheap.

## Run it

```bash
cd 05-tuning/BatchProcessing
dotnet run
```

Sample output:

```
Classifying 8 sentences, one session each.

── Results ──
  [positive]  ( 310 ms)  The support team solved my issue in under ten minutes.
  [negative]  ( 290 ms)  I cannot believe how broken the new release is.
  [neutral ]  ( 280 ms)  The meeting ended on time. Nothing else to report.
  [negative]  ( 305 ms)  My favourite bakery is closing next month. I'm gutted.
  [neutral ]  ( 275 ms)  The update adds dark mode and a few keyboard shortcuts.
  [positive]  ( 298 ms)  Every single task I gave the assistant was flawless.
  [neutral ]  ( 282 ms)  Battery life is about what the spec sheet says.
  [negative]  ( 317 ms)  Three hours on hold, then a disconnect — appalling.

── Throughput ──
  Items:        8
  Total time:   14.6s (includes ~1× model load)
  Per item avg: 294 ms
  Throughput:   0.55 items/s
```

Numbers vary by CPU / GPU, model size, and prompt length. The first item
always takes longer because it absorbs the model load and one-time warmup
cost. Subsequent items land in the steady-state range.

## Scaling notes

- **The Engine is not thread-safe.** Calling `SendMessageToSessionAsync`
  concurrently from multiple threads on one `AsposeLLMApi` will race on
  native state. Keep batch loops sequential within a process.
- **Horizontal scaling** means multiple processes. The facade enforces one
  `AsposeLLMApi` per *process*, so parallelism is "spawn N workers, each
  handling 1/N of the work".
- **Long batches** (hundreds of items): watch memory. Every session, even
  a dead one, holds some KV until the next cleanup. Call
  `api.ForceCacheCleanup(CacheCleanupStrategy.KeepSystemPromptOnly)` every
  ~50 items for a server-style workload.

## Things to try

- Replace the sentiment task with extractive QA: "Extract the company name
  and date from this sentence as JSON: `{company, date}`."
- Raise `MaxTokens` and collect longer outputs (summaries, rewrites) — watch
  the per-item latency scale roughly linearly with output length.
- Swap `SmallModelPreset` for `Qwen25Preset` and compare throughput.
  0.5B is fast and cheap; 7B is slower but dramatically more accurate.
- Time the single-session variant (remove `StartNewChatAsync` per item) and
  measure how much faster it is — then check accuracy. You'll see the
  accuracy-vs-speed tradeoff first-hand.
