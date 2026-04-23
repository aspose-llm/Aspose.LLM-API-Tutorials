# Aspose.LLM API Tutorials

Hands-on, runnable examples that walk you through the Aspose.LLM .NET SDK, from
a one-line "hello world" to multi-session chat, persistence, and tuning.

Each tutorial lives in its own folder and is a self-contained console project
you can run with `dotnet run`. READMEs inside every folder explain what the
example demonstrates, how to run it, and what output to expect.

---

## Before you start

### 1. .NET 8 SDK

All tutorials target `net8.0`. Install the .NET 8 SDK from
<https://dotnet.microsoft.com/download>.

### 2. An Aspose.LLM licence

Calls such as `SendMessageAsync` throw `"Not licensed for this method"` without
a valid licence. Drop a `License.lic` file into one of these locations:

- The tutorial's working directory (the folder you run `dotnet run` from).
- Anywhere, then point the `ASPOSE_LLM_LICENSE` environment variable at it:
  `export ASPOSE_LLM_LICENSE=/path/to/License.lic`

Every tutorial uses the shared `TutorialEnvironment.ApplyLicense()` helper,
which looks in both places and prints a clear error if neither is set.

If you do not have a licence yet, request a temporary one at
<https://purchase.aspose.com/temporary-license>.

### 3. Disk space for models

The SDK downloads model files from Hugging Face the first time a preset is
used. Sizes:

| Preset                        | Model                     | Size    |
| ----------------------------- | ------------------------- | ------- |
| `SmallModelPreset` (shared)   | Qwen 2 0.5B Q4_K_M        | ~400 MB |
| `Qwen25Preset` (SDK default)  | Qwen 2.5 7B Instruct Q4_K_M | ~4.5 GB |
| `Qwen3Preset`                 | Qwen 3 8B Q4_K_M          | ~4.9 GB |

Downloads are cached to the OS temp directory by default; override with
`ASPOSE_LLM_MODEL_CACHE=/some/path`.

The pilot tutorials all use `SmallModelPreset` so the first run completes in
minutes, not hours, on a typical network.

### 4. (Optional) Hugging Face token

Public repos work without credentials, but Hugging Face rate-limits unauthenticated
downloads. If you hit a 401/429, set `HF_TOKEN=<your token>`.

---

## Tutorial map

### 01 — Getting started

| Folder                                          | What it shows                                                |
| ----------------------------------------------- | ------------------------------------------------------------ |
| [`01-getting-started/HelloWorld`](01-getting-started/HelloWorld/)     | Smallest possible end-to-end: licence → preset → one reply.  |
| [`01-getting-started/QuickWins`](01-getting-started/QuickWins/)       | Three 30-second demos: one-shot Q&A, swap system prompt, use the default Qwen2.5 preset. |

### 02 — Text chat

| Folder                                                                   | What it shows                                                |
| ------------------------------------------------------------------------ | ------------------------------------------------------------ |
| [`02-text-chat/SimpleChat`](02-text-chat/SimpleChat/)                                 | Interactive REPL. Type, hit enter, read the reply, repeat.   |
| [`02-text-chat/MultiTurnChat`](02-text-chat/MultiTurnChat/)                           | The model remembers facts you gave it earlier in the session. |
| [`02-text-chat/SessionPersistence`](02-text-chat/SessionPersistence/)                 | Save a session to disk, restart the process, reload, continue. |
| [`02-text-chat/MultipleConcurrentSessions`](02-text-chat/MultipleConcurrentSessions/) | One API instance, three independent chat personalities in parallel. |

### 03 — Multimodal

| Folder                                              | What it shows                                                |
| --------------------------------------------------- | ------------------------------------------------------------ |
| [`03-multimodal/VisionQA`](03-multimodal/VisionQA/) | Ask a vision model about an image. Uses `Gemma3VisionPreset` (auto-downloads a ~3 GB model + projector). |

### 04 — Presets and prompts

| Folder                                                              | What it shows                                                |
| ------------------------------------------------------------------- | ------------------------------------------------------------ |
| [`04-presets/BringYourOwnGGUF`](04-presets/BringYourOwnGGUF/)       | Build a preset from scratch and point it at any `.gguf` file on disk. |
| [`04-presets/SystemPromptRecipes`](04-presets/SystemPromptRecipes/) | Same question, five different system prompts — side-by-side behaviour tasting. |

More use cases (long context, deployment, tuning, DI) will be added as the
pilot lands.

---

## Running a tutorial

```bash
cd 01-getting-started/HelloWorld
dotnet run
```

First run will: download the GGUF model, download the native llama.cpp binary
for your platform, load both, then reply. Expect several minutes on a cold
cache. Subsequent runs reuse both and start in seconds.

---

## Package source

The tutorials reference the official `Aspose.LLM` NuGet package via a pinned
`<AsposeLLMVersion>` property in `Directory.Build.props`. If your environment
needs an internal feed, add it to `nuget.config` at the repo root.

To build against a local checkout of the SDK instead (useful for SDK
developers), replace the `PackageReference` in a project with a
`ProjectReference` pointing at
`../../../aspose.ai.llm/Aspose.LLM/Aspose.LLM.csproj`.

---

## Shared helpers

Everything under `_shared/Aspose.LLM.Tutorials.Common/` is cross-cutting
infrastructure (licence loading, a small-model preset, console formatting).
It is a library, not a tutorial — reading it is optional, but it keeps each
`Program.cs` focused on the actual API surface being demonstrated.
