# Quick Wins

Three tiny scenarios, one program, one loaded model. Proves the three things
beginners want to see first: a one-shot reply, a session that remembers, and a
persona swap.

## What it demonstrates

| # | Win                       | SDK surface used                                        |
| - | ------------------------- | ------------------------------------------------------- |
| 1 | One-shot Q&A              | `AsposeLLMApi.SendMessageAsync`                         |
| 2 | Multi-turn memory         | `StartNewChatAsync` + `SendMessageToSessionAsync`       |
| 3 | Swap system prompt        | Preset with a different `ChatParameters.SystemPrompt`   |

The same `AsposeLLMApi` instance is reused for all three — loading the native
engine and the GGUF model is expensive, so we pay that cost once.

## Run it

```bash
cd 01-getting-started/QuickWins
dotnet run
```

Expected output shape (content will vary — Qwen 2 0.5B is a small model):

```
── Win 1 — one-shot question ──
You > Name three primary colours. ...
Bot < Red, blue, yellow.

── Win 2 — multi-turn memory ──
You > Remember this: my favourite colour is cerulean blue.
Bot < Got it — I'll remember ...
You > Earlier I told you my favourite colour. What was it? One word.
Bot < Cerulean.

── Win 3 — swap the system prompt ──
You > How should I start my day to be productive?
Bot < Arr, hoist the mainsail at dawn ...
```

> ⚠️ The default `SmallModelPreset` loads Qwen 2 0.5B — it's tiny and cheap
> to download, but it follows complex persona prompts unreliably. Expect Win 3
> to produce a *sometimes-pirate-y* answer, not a consistent one. Swap in a
> larger model (e.g. `Qwen25Preset` or `Qwen3Preset`) and the persona becomes
> rock-solid at the cost of a 5+ GB download.

## Things to tweak

- **Win 2**: comment out `StartNewChatAsync` and watch what happens when Win 1's
  history contaminates the memory test.
- **Win 3**: change the system prompt to any persona — "terse Unix greybeard",
  "patient teacher", "Shakespearean herald".
- **Sampler**: raise `preset.SamplerParameters.Temperature` before Win 3 to
  about `0.9f` and see replies get more playful.

## Next

Once you're comfortable here, move on to
[`02-text-chat/SimpleChat`](../../02-text-chat/SimpleChat/) for an interactive
REPL.
