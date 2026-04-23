using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Abstractions.Parameters;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Bring Your Own GGUF.
//
// Built-in presets (Qwen25Preset, Gemma3VisionPreset, ...) point at specific
// HuggingFace repos. Often you want to run *your* GGUF — maybe a model you
// quantised yourself, a fine-tune, or a download from somewhere else. This
// tutorial shows the full workflow:
//   1. Resolve a local .gguf file path.
//   2. Build a preset from scratch (no subclassing the built-in ones).
//   3. Load it into AsposeLLMApi just like any other preset.
//
// Resolving the file: pass the path as the first argument, or drop any
// .gguf into the shared tutorial cache directory and the program will
// pick the first one it finds. After running HelloWorld once, Qwen 2 0.5B
// is already cached, so this tutorial works without extra setup.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Bring Your Own GGUF");

TutorialEnvironment.ApplyLicense();

var ggufPath = args.Length > 0 ? args[0] : DiscoverCachedGGUF();
if (ggufPath is null || !File.Exists(ggufPath))
{
    ConsoleHelpers.Fail("No .gguf file found.");
    ConsoleHelpers.Info(
        "Either pass a path on the command line:\n" +
        "    dotnet run -- /path/to/your-model.gguf\n" +
        "or run the HelloWorld tutorial first (that caches Qwen 2 0.5B for reuse).");
    return 1;
}

ConsoleHelpers.Info($"Using GGUF: {ggufPath}");
ConsoleHelpers.Info($"Size: {new FileInfo(ggufPath).Length / 1_000_000:N0} MB\n");

// Build a preset from scratch — no built-in preset type, just PresetCoreBase
// with fields filled in. Any field you don't touch keeps the class default.
var preset = new PresetCoreBase();

// 1. Tell the SDK where the model lives. `ModelFilePath` has top priority
//    in ModelSourceParameters — it wins over AsposeModelId and HuggingFace.
preset.BaseModelSourceParameters.ModelFilePath = ggufPath;

// 2. Engine and context: conservative, CPU-safe defaults so this works on
//    any laptop. Tune for your own model by raising ContextSize up to
//    whatever the GGUF's metadata allows.
preset.EngineParameters.ModelCachePath = Path.GetDirectoryName(ggufPath);
preset.EngineParameters.EnableDebugLogging = false;
preset.ContextParameters.ContextSize = 4096;
preset.ContextParameters.NBatch = 512;
preset.ContextParameters.NUbatch = 256;
preset.ContextParameters.NThreads = Math.Max(1, Environment.ProcessorCount / 2);
preset.ContextParameters.NThreadsBatch = Math.Max(1, Environment.ProcessorCount / 2);

// 3. Inference: CPU by default. Set GpuLayers to -1 (offload everything)
//    or to a specific integer if you have a supported GPU.
preset.BaseModelInferenceParameters.GpuLayers = 0;
preset.BaseModelInferenceParameters.UseMemoryMapping = true;
preset.BaseModelInferenceParameters.SplitMode = LlamaSplitMode.LLAMA_SPLIT_MODE_LAYER;

// 4. Chat defaults. The chat template baked into the GGUF metadata is
//    applied automatically — you don't normally override it here. Just
//    set the system prompt, response budget, and cache strategy.
preset.ChatParameters.SystemPrompt =
    "You are a concise assistant. Introduce yourself briefly when asked.";
preset.ChatParameters.MaxTokens = 200;
preset.ChatParameters.CacheCleanupStrategy = CacheCleanupStrategy.RemoveOldestMessages;

// 5. Sampler — start low-variance so output is predictable, then experiment.
preset.SamplerParameters.Temperature = 0.5f;
preset.SamplerParameters.TopP = 0.9f;
preset.SamplerParameters.TopK = 40;
preset.SamplerParameters.RepetitionPenalty = 1.1f;

using var api = new AsposeLLMApi(preset);

const string q1 = "Briefly introduce yourself and say what you are good at.";
ConsoleHelpers.UserLine(q1);
ConsoleHelpers.AssistantLine(await api.SendMessageAsync(q1));

const string q2 = "Name one concrete weakness you have, in one sentence.";
ConsoleHelpers.UserLine(q2);
ConsoleHelpers.AssistantLine(await api.SendMessageAsync(q2));

ConsoleHelpers.Info("\nDone — preset built from scratch, your own GGUF loaded successfully.");
return 0;

static string? DiscoverCachedGGUF()
{
    var cache = TutorialEnvironment.ResolveModelCachePath();
    // Pick the largest .gguf — heuristically that's the base model, not an
    // mmproj projector (which are smaller and meaningless alone).
    return Directory.Exists(cache)
        ? Directory.EnumerateFiles(cache, "*.gguf", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => new FileInfo(p).Length)
            .FirstOrDefault()
        : null;
}
