# Long-Running Conversation

Scripted 12-turn chat run twice with different `CacheCleanupStrategy`
settings. Shows which strategy preserves which part of history when the
context window fills up.

## What it demonstrates

- **`ContextParameters.ContextSize` is finite.** When chat history exceeds
  it, the SDK drops older messages according to the configured strategy —
  your code doesn't do the dropping, the engine does, silently.
- **Two concrete strategies side-by-side:**
  - `RemoveOldestMessages` — FIFO. New turns push the oldest out.
  - `KeepSystemPromptAndFirstUserMessage` — pins both the system prompt
    and the user's very first turn. Good for "remember the task description
    you were given, even if the conversation gets long."
- **A quiz at the end of each part** proves which strategy preserved the
  early fact and which dropped it.

## How the demo forces the issue

`SmallModelPreset` defaults to `ContextSize = 4096`, large enough that the
demo's 12 turns would fit. The tutorial overrides it to **512** and raises
`MaxTokens` to 160 so each reply genuinely consumes budget — the buffer
overflows around turn 4-5 and cleanup applies for the rest.

Production apps, of course, use the largest context their hardware can
afford. This tutorial is specifically about what to do *when they can't*.

## Run it

```bash
cd 02-text-chat/LongRunningConversation
dotnet run
```

Real output from a local run (content varies; the *shape* doesn't):

```
── Part A — RemoveOldestMessages (FIFO) ──
You > [t01] Please remember: I own a small bookstore on Volkov Street and its mascot ...
Bot < I remember that you own a bookstore on Volkov Street. Hemingway, your mascot, is a one-eared orange cat.
... (11 filler turns covering sea water, bicycles, photosynthesis, ...) ...
You > [quiz] Earlier I told you what street my bookstore is on and what my cat is called ...
Bot < I previously informed you that my bookstore is located on the street "The Grove."

── Part B — KeepSystemPromptAndFirstUserMessage (pin the beginning) ──
You > [t01] Please remember: I own a small bookstore on Volkov Street and its mascot ...
Bot < Hemingway is a one-eared orange cat at the Volkov Street bookstore.
... (11 filler turns) ...
You > [quiz] ...
Bot < The street I told you was Volkov Street. My cat, Hemingway, is named after the street.
```

**Part A hallucinates** "The Grove" — the FIFO strategy dropped turn 1 from
the KV cache, and the model confidently invents a plausible-looking street
name. **Part B recalls** Volkov Street and Hemingway correctly — the
`KeepSystemPromptAndFirstUserMessage` strategy pinned turn 1 through every
cleanup cycle.

That's the whole lesson.

## Picking a strategy

| Strategy                                    | Good for                                                       |
| ------------------------------------------- | -------------------------------------------------------------- |
| `RemoveOldestMessages`                      | General chat where only recent context matters.                |
| `KeepSystemPromptAndFirstUserMessage`       | Task-oriented chats where the initial brief must stay anchored.|
| `KeepSystemPromptAndLastUserMessage`        | Rare — mostly a corner-case choice.                            |
| `KeepSystemPromptAndHalf`                   | When you want to preserve a bit of both ends.                  |
| `KeepSystemPromptOnly`                      | Nearly always wrong (drops all user context). Use `ForceCacheCleanup` for this effect, not as the auto strategy. |

## Practical advice

- **Raise `ContextSize` first.** Qwen 2 0.5B supports 32k; Qwen 2.5 / 3
  support 128k. Only reach for cleanup strategies when you've maxed out
  what the model and hardware can hold.
- **Manual pinning.** If your important context is in turn 3 (not turn 1),
  consider *re-sending it* at turn N instead of relying on `First`/`Last`
  pinning. Cleanup fires on the KV cache, not on your reasoning.
- **Persist long chats.** For sessions that will legitimately run for days,
  pair this with [`SessionPersistence`](../SessionPersistence/) and flush
  state to disk between idle periods.
- **Watch for silent drops.** When `EnableDebugLogging = true`, the
  SDK emits log lines on cleanup. A server that never sees those lines
  hasn't hit the limit yet; when it does, dashboards should surface it.

## Things to try

- Set `ContextSize = 4096` and see the quiz pass on *both* parts — the
  buffer never overflows, so cleanup never fires.
- Replace the filler prompts with longer ones (a paragraph each) to fill
  the window in 3-4 turns instead of 8-10.
- Call `api.ForceCacheCleanup(CacheCleanupStrategy.KeepSystemPromptAndFirstUserMessage)`
  right before the quiz in Part A. You'll find it's too late — the early
  fact was evicted back at turn 9. Cleanup is *reactive*, not retroactive.
