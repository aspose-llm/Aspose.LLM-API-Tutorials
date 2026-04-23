# Multiple Concurrent Sessions

One engine, many conversations. The sample spins up three sessions with
different system prompts (chef, software engineer, mathematician) off a single
`AsposeLLMApi` and proves each maintains its own history independently.

## What it demonstrates

- Starting multiple sessions with `StartNewChatAsync(preset)` — each gets
  a unique `sessionId`.
- Addressing a specific session with
  `SendMessageToSessionAsync(sessionId, ...)` — the facade no longer tracks an
  "implicit current session" when you do this; you route explicitly.
- Per-session system prompts despite one shared loaded model.
- Per-session history: Round 2's follow-up "why did you say that?" only makes
  sense because each session remembers its own Round 1 answer.

## Concurrent ≠ parallel

Three sessions exist at the same time, but the native llama.cpp engine
generates tokens serially. If you call `SendMessageToSessionAsync` for two
sessions simultaneously from two threads, the second call blocks until the
first finishes. That's expected: this pattern is about **multiplexing many
stateful conversations over one loaded model**, not about squeezing two
generations onto the same GPU.

If you need parallel generation, you'd run multiple processes (each with its
own `AsposeLLMApi`) on separate GPUs.

## Run it

```bash
cd 02-text-chat/MultipleConcurrentSessions
dotnet run
```

Example output (content will vary):

```
chef = 4a7b...
coder = 9c12...
math = e8f5...

── Round 1 — same question, three personas ──
You > [to chef] In one word: what is the best time of day to think hard?
Bot < Early.
You > [to coder] In one word: what is the best time of day to think hard?
Bot < Morning.
You > [to math] In one word: what is the best time of day to think hard?
Bot < Morning.

── Round 2 — follow-up, proves each session has its own memory ──
You > [to chef] Why did you say that? Give one sentence of reasoning ...
Bot < Because a cold early-morning kitchen is the only time I get quiet before ...
You > [to coder] Why did you say that? ...
Bot < Because my brain has the fewest Slack interruptions before 9am ...
You > [to math] Why did you say that? ...
Bot < Because caffeine elevates working-memory bandwidth ...
```

Notice how Round 2 answers vary in style and reasoning — that's three separate
histories at work.

## Scaling considerations

- **Memory.** Each active session keeps its history in the context window and
  consumes some KV cache. With `ContextSize = 4096` and a handful of
  sessions you'll be fine; scale to hundreds and consider
  [`SessionPersistence`](../SessionPersistence/) to page cold ones to disk.
- **Cleanup.** There's no `CloseSession` today — drop the `sessionId`
  reference and the SDK lets the next cache-cleanup cycle reclaim its KV.
  The next section of the tutorials (coming soon) will cover
  `ForceCacheCleanup` strategies for long-lived servers.

## Things to try

- Start 10+ sessions with varied personas, cycle through them asking the same
  question. See how response times evolve as KV cache fills.
- Change a session's persona by starting a new one (the API has no "re-system
  the existing session" operation — the convention is to start fresh).
- Save one of the sessions mid-run with `SaveChatSession(chefId, …)`, restart
  the program, load it, and continue — combines this pattern with
  [`SessionPersistence`](../SessionPersistence/).
