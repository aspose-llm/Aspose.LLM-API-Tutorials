using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Abstractions.Parameters;
using Aspose.LLM.Abstractions.Parameters.Presets;

namespace Aspose.LLM.Tutorials.Common;

/// <summary>
/// A light preset tuned for fast first-run experiences: Qwen 2 0.5B instruct
/// (Q4_K_M, ~400 MB). CPU-only by default so the tutorials run on any laptop.
///
/// Override anything you like after construction — for example set
/// <c>GpuLayers = -1</c> if you want to push the whole model onto GPU, or
/// raise <c>ContextSize</c> if you need more room for history.
/// </summary>
public sealed class SmallModelPreset : PresetCoreBase
{
    public SmallModelPreset()
    {
        var cpuHalf = Math.Max(1, Environment.ProcessorCount / 2);

        // Small, freely-available GGUF the SDK can auto-download from Hugging Face
        // the first time the preset is loaded. 400 MB download.
        BaseModelSourceParameters.HuggingFaceRepoId = "QuantFactory/Qwen2-0.5B-Instruct-GGUF";
        BaseModelSourceParameters.HuggingFaceFileName = "Qwen2-0.5B-Instruct.Q4_K_M.gguf";

        EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
        EngineParameters.EnableDebugLogging = false;

        // Keep the footprint modest. 4k context is enough for every pilot
        // tutorial; bump it yourself for longer conversations.
        ContextParameters.ContextSize = 4096;
        ContextParameters.NBatch = 512;
        ContextParameters.NUbatch = 256;
        ContextParameters.NThreads = cpuHalf;
        ContextParameters.NThreadsBatch = cpuHalf;

        // CPU-safe: zero layers on GPU. If you have a capable GPU, set
        // preset.BaseModelInferenceParameters.GpuLayers = -1 for full offload.
        BaseModelInferenceParameters.GpuLayers = 0;
        BaseModelInferenceParameters.UseMemoryMapping = true;
        BaseModelInferenceParameters.UseMemoryLocking = false;
        BaseModelInferenceParameters.SplitMode = LlamaSplitMode.LLAMA_SPLIT_MODE_LAYER;

        ChatParameters.MaxTokens = 256;
        ChatParameters.SystemPrompt = "You are a concise, helpful assistant. Reply in a single short paragraph unless asked otherwise.";
        ChatParameters.CacheCleanupStrategy = CacheCleanupStrategy.KeepSystemPromptOnly;

        // Low-variance sampler: the tutorials want predictable output that
        // still reads naturally. Override these per-tutorial for creativity.
        SamplerParameters.Temperature = 0.6f;
        SamplerParameters.TopP = 0.9f;
        SamplerParameters.TopK = 40;
        SamplerParameters.MinP = 0.05f;
        SamplerParameters.RepetitionPenalty = 1.1f;
    }
}
