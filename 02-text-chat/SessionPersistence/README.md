# Session Persistence

Save an in-progress conversation to disk, tear the SDK down, start it back up
in a fresh process, and pick up exactly where you left off.

## What it demonstrates

- `AsposeLLMApi.SaveChatSession(sessionId, filePath)` — serialise a session.
- `AsposeLLMApi.LoadChatSession(filePath)` — deserialise into the currently
  live engine; returns a fresh `sessionId` you use for subsequent messages.
- Respecting the singleton constraint: only one `AsposeLLMApi` instance per
  process at a time. The sample disposes the first instance before
  constructing the second.

## Why this matters

Chat sessions get expensive as they grow — both the token history and (with
`OffloadKqv`) the KV cache consume memory. Persisting a session lets you:

- Shut down between user sessions and restart without losing context.
- Move a conversation between processes — a worker that ingests facts during
  the day, a different process that answers questions at night.
- Snapshot a "known-good" starting point (e.g. after an expensive system-prompt
  warm-up) and spawn many conversations from it.

## Run it

```bash
cd 02-text-chat/SessionPersistence
dotnet run
```

Example output (content will vary):

```
── Phase 1 — write a fact, save, dispose ──
You > Please remember this for later: my apartment key hook is shaped like a brass anchor ...
Bot < Got it — brass anchor-shaped key hook next to the bookshelf. I'll remember.
[saved session 4d2f...b1 → /.../sessions/persistence-demo.session]
[on-disk size: 12,834 bytes]

── Phase 2 — fresh engine, load, recall ──
[loaded session as 4d2f...b1 (was 4d2f...b1 before save)]
You > What did I tell you about the object next to my bookshelf? ...
Bot < You mentioned a key hook shaped like a brass anchor sitting next to the bookshelf.

Session persistence demo complete.
```

`LoadChatSession` returns the **same** `sessionId` that was stored in the file
— the id is part of the serialised payload, not something the new engine
re-mints. Still, always use the id handed back by `LoadChatSession` rather
than hold on to the one you had before disposing the previous `AsposeLLMApi`;
treating it as an opaque handle keeps your code robust if the serialisation
rules change in a future SDK version.

## Caveats

- **Format is SDK-internal.** Don't write parsers against the file. Upgrades
  may change the layout; if you need long-term durability, roll your own
  application-level serialization of `ChatMessage` lists.
- **Model must match.** The session embeds assumptions about tokenizer and
  chat template. Loading a Qwen-2.5 session into a Llama-3.2 engine will
  produce incoherent output or outright errors.
- **KV cache is not the text.** The save captures the logical history, not
  the raw KV tensors — on load, the engine re-tokenises and re-fills cache.
  That's slower than a plain text append but it makes the file portable
  across machines and GPU/CPU configurations.

## Things to try

- Save the session, close the program, restart it and run only Phase 2
  (comment out Phase 1). The fact should still be recalled from the existing
  file.
- Change the preset between save and load (e.g. different `SystemPrompt`) and
  see what stays vs. what re-initialises.
- Make Phase 1 a 10-turn conversation, save it, and measure file size against
  a single-turn session.
