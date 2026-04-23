# Long Context

Paste a document into a session once, then ask several questions against it.
Demonstrates how to raise the context window beyond a preset's default and
when that actually buys you something.

## What it demonstrates

- Increasing `ContextParameters.ContextSize` from 4 096 (the `SmallModelPreset`
  default) to 16 384 to fit a ~2 000-token document plus Q&A overhead.
- Ingesting the document in one turn and asking multiple follow-up questions
  in the same session — the KV cache is built once and reused per question.
- Three question *flavours* of increasing difficulty:
  - Q1: pointed fact retrieval ("what is the deadline for X").
  - Q2: cross-reference between two distant sections.
  - Q3: broad summary of a list.

## Context budgeting

Every GGUF has a **maximum trained context** baked into its metadata. Asking
for more produces degenerate output silently. For the models the tutorials
use:

| Preset                 | Model                  | Trained context |
| ---------------------- | ---------------------- | --------------- |
| `SmallModelPreset`     | Qwen 2 0.5B            | 32 768          |
| `Qwen25Preset`         | Qwen 2.5 7B Instruct   | 131 072         |
| `Qwen3Preset`          | Qwen 3 8B              | 131 072         |
| `Gemma3VisionPreset`   | Gemma 3 Vision Latex   | 8 192           |

Going beyond trained context requires RoPE extension (YaRN) — available on
`ContextParameters` (`YarnExtFactor`, `YarnOrigCtx`, `YarnBetaFast`,
`YarnBetaSlow`). That's a separate tutorial; for this one we stay within the
model's trained window.

## Compute cost

Prefill cost grows roughly quadratically with context length on CPU. KV cache
memory grows linearly: Qwen 2 0.5B at 16 k context uses ~200 MB of KV cache
at F16, half that at Q8 via `ContextParameters.KvType`.

A rough rule of thumb for CPU inference:

| Context  | Prefill time (0.5B, 8-core CPU) | Memory budget |
| -------- | ------------------------------- | ------------- |
| 4 k      | ~0.5-1 s                        | ~50 MB KV     |
| 16 k     | ~4-8 s                          | ~200 MB KV    |
| 32 k     | ~15-25 s                        | ~400 MB KV    |
| 128 k+   | uncomfortable on CPU            | ~1.6 GB KV    |

With GPU offload (`GpuLayers = -1`) everything above scales roughly 5-20×
better.

## Run it

```bash
cd 05-tuning/LongContext
dotnet run
```

Sample output with `SmallModelPreset` (Qwen 2 0.5B is small, so answer
quality varies — it usually lands Q1 and Q2 and partially Q3):

```
Context window: 16384 tokens · document: 6,142 chars (~1,535 tokens).

[document ingested; model ack: OK]

── Q1 — pointed fact (needle-in-haystack) ──
You > According to the memo, what is the deadline for the cache invalidation ...
Bot < "D-17: cache invalidation strategy ... Owner: Priya Sharma. Deadline:
      2026-05-15 for the prototype, 2026-07-01 for production rollout."
      The prototype is due 2026-05-15, production by 2026-07-01; Priya Sharma owns it.

── Q2 — cross-reference two distant sections ──
You > What is the budget ceiling for Halyard, and what share of it has the infra ...
Bot < The ceiling is USD 1,400,000; Infrastructure has committed USD 520,000, about 37%.

── Q3 — broad summary ──
You > List the three biggest risks called out in the memo, each in one sentence.
Bot < 1. FoundationDB licence costs may push the project over budget by ~USD 95k.
      2. Rust-talent pipeline is thin; timeline slippage is plausible.
      3. Legacy Redis will briefly run near 80% of its commit bandwidth during dual-write.
```

## Practical advice

- **Keep the system prompt short.** Every token of system prompt is prefixed
  to every message; a 500-token system prompt in a 16 k window is 3% of your
  budget, re-paid on every turn.
- **Pointed > broad.** Small models ace "what's X" and fumble "summarise
  everything". Decompose broad asks into targeted ones and stitch the
  answers yourself.
- **Monitor KV pressure.** If you see `CacheCleanupStrategy` firing in
  logs, you're losing history — either raise `ContextSize`, switch to
  `Qwen25Preset`, or move older turns to a separate summary session.
- **One-shot ingestion pattern.** Paste the document as the first user
  message. Subsequent turns are pure Q&A — the SDK reuses the cached KV
  and doesn't re-tokenise the document.

## Things to try

- Change `ContextSize` to 4 096 and re-run — Q2 will fail because the
  document gets truncated mid-ingestion by `CacheCleanupStrategy`.
- Paste ten copies of the document (`Document.Text` concatenated) to push
  past 16 k and watch the behaviour. With `CacheCleanupStrategy.RemoveOldestMessages`,
  early sections get dropped first; Q1 will silently fail if it was asking
  about them.
- Swap to `Qwen25Preset` (~5 GB model) and watch Q3 go from "partial list"
  to "complete, properly-structured summary".
