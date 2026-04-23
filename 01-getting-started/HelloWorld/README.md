# Hello World

The shortest meaningful program you can write with `Aspose.LLM`: apply a
licence, build a preset, create an `AsposeLLMApi`, send one message, print the
reply. Forty lines including comments.

## What you'll see

- The SDK downloads the Qwen 2 0.5B GGUF model on first run (~400 MB).
- It also downloads a matching native llama.cpp binary for your OS/arch.
- It answers a single question.

## Run it

```bash
# From the tutorials repo root:
cd 01-getting-started/HelloWorld

# Make sure a licence is available — one of:
#   export ASPOSE_LLM_LICENSE=/absolute/path/to/License.lic
#   # or drop License.lic into this folder

dotnet run
```

Expected output (abridged):

```
==========================================
  Aspose.LLM — Hello World
==========================================

[licence] applied from /.../License.lic
Model: QuantFactory/Qwen2-0.5B-Instruct-GGUF/Qwen2-0.5B-Instruct.Q4_K_M.gguf
Cache: /var/folders/.../aspose-llm-tutorials-models
You > In one sentence: what is the capital of France?
Bot < The capital of France is Paris.
```

## Files

- [`Program.cs`](Program.cs) — the example, top-level statements.
- [`HelloWorld.csproj`](HelloWorld.csproj) — references `Aspose.LLM` and the
  shared `Aspose.LLM.Tutorials.Common` project.

## What to try next

- Edit `Program.cs` and change the question — try `"Translate 'good morning'
  into Japanese."` or `"Write a haiku about autumn."`.
- Swap the small preset for the SDK's default Qwen 2.5 7B by replacing
  `new SmallModelPreset()` with `new Aspose.LLM.Abstractions.Parameters.Presets.Qwen25Preset()`.
  Expect a much larger first-run download (~4.5 GB).
- Continue to [`QuickWins`](../QuickWins/) to see three short demos in one program.
