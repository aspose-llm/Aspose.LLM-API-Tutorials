# Multi-Turn Chat

Proves that a chat session is stateful: facts the user provides in earlier
turns are available to the model on later turns, without you re-sending them.
Also shows how to *pre-seed* history when a conversation comes from elsewhere
(database, log replay, previous process).

## What it demonstrates

- **Part A — accumulate.** Four scripted user turns in one session. Turns 1-3
  establish facts; turn 4 asks the model to recall them. A correct recall
  confirms history is flowing into each call.
- **Part B — pre-seed.** A fresh session whose `ChatParameters.History` is
  filled by the caller with three prior `ChatMessage` entries. The first
  question the user asks is effectively the *fourth* turn of a conversation
  that never actually happened against the SDK.

## Run it

```bash
cd 02-text-chat/MultiTurnChat
dotnet run
```

Example output (content will vary):

```
── Part A — accumulate history across turns ──
You > My name is Alex.
Bot < Nice to meet you, Alex!
You > I live in Novosibirsk.
Bot < Novosibirsk — the Siberian science hub. Noted.
You > My favourite programming language is C#.
Bot < Got it — C# it is.
You > Recall the three facts I just told you, as a numbered list.
Bot < 1. Your name is Alex.
      2. You live in Novosibirsk.
      3. Your favourite programming language is C#.

── Part B — pre-seed history via ChatParameters ──
You > Given what I said earlier about my project, suggest one concrete next step.
Bot < For modeling schema drift over time, try ...
```

## How it works — the important lines

- `api.StartNewChatAsync()` — mint a new `sessionId`. Everything sent to that
  id is considered part of one conversation.
- `api.SendMessageToSessionAsync(sessionId, text)` — append a user turn,
  generate the assistant turn, return the text. The SDK does **not** ask you
  to hand back the history yourself.
- `ChatParameters.History = new List<ChatMessage> { ... }` — pre-seed the
  conversation. Use `ChatMessage.CreateUserMessage` / `CreateAssistantMessage`
  / `CreateSystemMessage` as typed builders.

## Context budget

`SmallModelPreset` is configured with `ContextSize = 4096` and
`CacheCleanupStrategy = KeepSystemPromptOnly`. Once the conversation exceeds
~4k tokens the SDK starts dropping history according to the strategy. Switch
to `CacheCleanupStrategy.RemoveOldestMessages` for a more intuitive "forgets
the oldest" behaviour — see [`SessionPersistence`](../SessionPersistence/) for
saving state *before* it gets evicted.

## Things to try

- Insert a deliberate contradiction in turn 2 ("my name is Bob") and see which
  name the model keeps in turn 4.
- Remove the system prompt entirely (`preset.ChatParameters.SystemPrompt = ""`)
  and watch recall degrade — system prompts anchor the "remember what the
  user says" behaviour.
- In Part B, replace the seeded assistant turn with a wrong answer and see
  whether the model corrects itself or builds on the error.
