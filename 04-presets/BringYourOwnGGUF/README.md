# Bring Your Own GGUF

Load a local `.gguf` file instead of a preset-bundled HuggingFace repo.
Useful when you've got a fine-tune, a custom quantisation, or an offline
mirror.

## What it demonstrates

- Building a `PresetCoreBase` **from scratch** — no subclass, no inheritance,
  every field set explicitly so you can see what each knob does.
- `ModelSourceParameters.ModelFilePath` taking priority over
  `AsposeModelId` / `HuggingFaceRepoId`.
- Sensible CPU-only defaults for context, inference, chat, and sampler.

## Where does the GGUF come from?

Anywhere you like. A few easy options:

```bash
# HuggingFace CLI
pip install -U huggingface_hub
huggingface-cli download bartowski/Phi-3.5-mini-instruct-GGUF \
    Phi-3.5-mini-instruct-Q4_K_M.gguf --local-dir ~/models

# Direct download (curl)
curl -L -o ~/models/model.gguf \
    https://huggingface.co/.../resolve/main/model.gguf

# Or use the copy another tutorial already cached for you:
ls "$TMPDIR/aspose-llm-tutorials-models/"
```

## Run it

```bash
cd 04-presets/BringYourOwnGGUF

# Option A: point at your own file
dotnet run -- /absolute/path/to/your-model.gguf

# Option B: run nothing-fancy and let the tutorial find a cached .gguf
# (works if you ran HelloWorld first — Qwen 2 0.5B is in the cache)
dotnet run
```

Example output when the tutorial auto-discovers the Qwen 2 0.5B from the
cache:

```
=======================================
  Aspose.LLM — Bring Your Own GGUF
=======================================

[licence] applied from /.../License.lic
Using GGUF: /.../aspose-llm-tutorials-models/Qwen2-0.5B-Instruct.Q4_K_M.gguf
Size: 397 MB

You > Briefly introduce yourself and say what you are good at.
Bot < I'm a small language model trained to be helpful and concise. I'm good at
      answering straightforward questions quickly.

You > Name one concrete weakness you have, in one sentence.
Bot < I can struggle with multi-step reasoning when prompts get long.

Done — preset built from scratch, your own GGUF loaded successfully.
```

## The important lines

| Line                                                           | What it does                                                   |
| -------------------------------------------------------------- | -------------------------------------------------------------- |
| `preset.BaseModelSourceParameters.ModelFilePath = ggufPath`    | Wins over any HuggingFace / AsposeId — the local file rules.   |
| `preset.EngineParameters.ModelCachePath = Path.GetDirectoryName(ggufPath)` | Keeps any side-effects (auxiliary files) next to your GGUF.    |
| `preset.BaseModelInferenceParameters.GpuLayers = 0`            | CPU-only. Set to `-1` to offload everything to GPU if you have one. |
| `preset.ContextParameters.ContextSize = 4096`                  | Modest, safe for any model; raise to whatever the GGUF supports. |
| `preset.ChatParameters.SystemPrompt = "..."`                   | Chat template comes from GGUF metadata; only the system prompt is yours to set. |

## Caveats

- **Context size.** Each GGUF has a maximum trained context baked into its
  metadata. Setting `ContextSize` higher than that gives nonsense outputs
  eventually. Check the model card.
- **Chat template.** The SDK reads the chat template from the GGUF's
  metadata. If the file lacks one (custom conversions sometimes drop it), you
  get raw-completion behaviour — the model sees your system prompt and
  user messages as plain text, no role markers. Re-export with the correct
  template or pick a different quantisation.
- **Quantisation quality.** Q4_K_M is a reasonable default; Q4_0 is faster
  but lossier; Q8_0 is higher quality at double the RAM. F16 is the
  half-precision baseline, roughly 2× the size of Q8_0.

## Things to try

- Download three quantisations of the same model (Q4_0, Q4_K_M, Q8_0), point
  the tutorial at each, and compare reply quality and latency.
- Try a reasoning model like Qwen3: raise `MaxTokens` to 1024+ because
  the `<think>...</think>` block alone eats 300-500 tokens before the actual
  answer.
- Disable `UseMemoryMapping` (`false`) and see memory usage climb — good
  for benchmarking real cost vs. mmap's lazy loading.
