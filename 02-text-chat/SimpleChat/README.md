# Simple Chat

An interactive console REPL. Useful as a scratchpad to get a feel for how the
model behaves and as a template for any app that just needs a chat loop.

## What it demonstrates

- Constructing a single `AsposeLLMApi` and reusing it across many turns.
- Letting the facade manage the "current session" implicitly — after the first
  `SendMessageAsync`, subsequent calls continue in the same conversation.
- Handling `Ctrl+C`: first press cancels the in-flight generation via
  `CancellationToken`, second press exits the process.
- Two in-REPL commands — `/new` starts a fresh session, `/quit` exits.

## Run it

```bash
cd 02-text-chat/SimpleChat
dotnet run
```

Example transcript:

```
Type a message and press Enter. /new resets, /quit exits.

You > What's a good weekend project for a .NET developer?
Bot < Build a small Blazor app that tracks your coffee intake — ...
You > Suggest one using only the console.
Bot < How about a markdown-based journal CLI ...
You > /new
[new session 7a2c-...-e1]
You > Hi — do you remember what we were just discussing?
Bot < We haven't discussed anything yet — this seems to be the start of ...
You > /quit
Goodbye.
```

Note how `/new` wipes the model's short-term memory: the next turn reports "no
prior context", because a fresh session starts with an empty history.

## Cancelling a long reply

If the model starts producing something you don't want, press `Ctrl+C` once.
The `CancellationToken` passed to `SendMessageAsync` unwinds the generation and
the REPL prompt returns. Press `Ctrl+C` a second time to quit outright.

## Files

- [`Program.cs`](Program.cs) — ~70 lines including comments.
- [`SimpleChat.csproj`](SimpleChat.csproj)

## Next

- [`MultiTurnChat`](../MultiTurnChat/) — same idea, but scripted, so you can see
  precisely what the model remembers across turns.
- [`SessionPersistence`](../SessionPersistence/) — save the whole conversation
  to disk and pick it up in a later process.
