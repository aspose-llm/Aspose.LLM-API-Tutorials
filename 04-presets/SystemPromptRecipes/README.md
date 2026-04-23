# System Prompt Recipes

Five different system prompts against the same question — a side-by-side
tasting menu that shows how much you can change output behaviour without
touching the model, the sampler, or the temperature.

## What it demonstrates

- Mutating `ChatParameters.SystemPrompt` per session.
- `StartNewChatAsync(preset)` — passing a preset *per session* to override
  the facade's default preset for just that conversation.
- Low temperature (`0.4`) to keep recipe differences from being swamped by
  sampling randomness.

## The five recipes

| Recipe            | What it enforces                                                 |
| ----------------- | ---------------------------------------------------------------- |
| Terse             | One sentence, no preamble, no caveats.                           |
| Structured        | Fixed three-line format with labelled fields.                    |
| Teacher           | Explain as if to a 10-year-old, one paragraph, one analogy.      |
| Devil's advocate  | Every claim gets an immediate pushback sentence.                 |
| Translator        | French only, two sentences, never English.                       |

## Run it

```bash
cd 04-presets/SystemPromptRecipes
dotnet run
```

Representative output from a real run with the default `SmallModelPreset`
(Qwen 2 0.5B):

```
── Recipe — Terse ──
Bot < A computer retains information by storing it in its RAM, which is
      continuously updated as the computer is turned off.

── Recipe — Structured ──
Bot < A computer turns off when it is turned off.

── Recipe — Teacher ──
Bot < A computer remembers things when you turn it off by storing information
      in its RAM. When you turn it off, the RAM reads the stored data and uses
      that information to start the next time the computer is turned on. So
      it's like turning off the lights in your house, which helps you find
      the light bulbs when you need them again.

── Recipe — Devil's advocate ──
Bot < A computer's ability to remember things when turned off is dependent on
      various factors, including the type of data stored and the specific
      operating system. Without direct access to the computer or its hardware,
      it's challenging to provide a specific explanation.

── Recipe — Translator ──
Bot < A computer remembers things by storing information in its memory, which
      can be turned off by the user.
```

Notice:

- **Terse mostly works** (one sentence) but the content is factually wrong
  (RAM does not retain info when powered off). Small models follow *format*
  better than they reason about *facts*.
- **Structured collapses** — Qwen 2 0.5B drops the three-line schema entirely.
- **Teacher and Devil's-advocate land half the recipe** (teacher gets an
  analogy; advocate doesn't push back on its own claim).
- **Translator ignored French** entirely.

This is the **headline caveat**: recipes work reliably only on models that
understand complex formatting and multi-step instructions. Swap the preset
to `Qwen25Preset` or `Qwen3Preset` (~5 GB) and adherence jumps from roughly
40% to 95%. Consider this tutorial a demonstration of the *mechanism*, not
of the model's ceiling.

## Practical pointers

- **Keep it declarative.** "Always reply in format X" works better than
  "Try not to break format X". Models follow rules phrased as positive
  constraints more reliably than negative ones.
- **Small models miss edge cases.** Qwen 2 0.5B will *mostly* honour the
  structured format, but occasionally add a preamble or forget the Caveat
  line. Switch to a 7B+ preset (`Qwen25Preset`, `Qwen3Preset`) and the
  adherence goes from ~80% to ~99%.
- **History still matters.** System prompts shape *every* turn. Changing the
  system prompt mid-conversation isn't a supported operation — start a
  new session if you want to switch recipes.
- **Unexpected interactions.** A terse system prompt can make the model
  refuse to answer borderline questions by treating the terseness rule as
  "when unsure, say nothing". Test your prompts on both clear and ambiguous
  questions.

## Things to try

- Add a sixth recipe that forces a persona (e.g. "You are Feynman, answer in
  his style") and compare to the Teacher recipe.
- Use `CacheCleanupStrategy.KeepSystemPromptOnly` (in `SmallModelPreset`,
  already the default) vs `RemoveOldestMessages`. The system prompt is the
  cornerstone of keep-alive behaviour.
- Feed each recipe the same follow-up ("Explain it differently.") and see
  how each persona deviates — Teacher invents a new analogy, Terse just
  restates.
