using Aspose.LLM;
using Aspose.LLM.Abstractions.Acceleration;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// GPU deployment — NVIDIA CUDA.
//
// On a Windows or Linux host with a CUDA-capable NVIDIA GPU, the SDK can
// offload some or all of the model weights to VRAM for dramatic speed-ups.
// This tutorial shows the four knobs you reach for:
//
//   - BinaryManagerParameters.PreferredAcceleration = CUDA
//       Forces the SDK to download the CUDA-flavoured native binary
//       instead of auto-detecting. Useful when auto-detect guesses wrong
//       (e.g. a CUDA-less driver installed alongside Vulkan).
//
//   - BaseModelInferenceParameters.GpuLayers
//       How many transformer layers go on GPU. Use -1 to offload every
//       layer (fastest, requires enough VRAM for weights + context) or a
//       specific count to split work between CPU and GPU for models that
//       won't fit entirely.
//
//   - BaseModelInferenceParameters.MainGpu
//       Index of the primary GPU (0-based). Relevant on multi-GPU hosts.
//
//   - BaseModelInferenceParameters.TensorSplit  (multi-GPU only)
//       Proportions for splitting the model across GPUs, e.g. new[] { 0.6f,
//       0.4f } for a 60/40 split on a 24 GB + 16 GB pair.
//
// IMPORTANT: this tutorial compiles and configures correctly on any host,
// but it only *runs* meaningfully on a machine with a CUDA-capable NVIDIA
// GPU and the appropriate driver. On macOS or a CPU-only box the SDK will
// fail over to CPU and the "CUDA" part of the demo is visible only in
// the printed configuration.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — GPU Deployment (CUDA)");

TutorialEnvironment.ApplyLicense();

// Use a 7B+ preset here — there's no point in GPU-offloading a 0.5B model
// (it already fits in every cache). Qwen 2.5 7B is a sensible default.
// If you'd rather stay on the small preset, swap it in.
var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.ChatParameters.MaxTokens = 150;
preset.ChatParameters.SystemPrompt =
    "You are a concise assistant. Keep replies to two or three sentences.";

// --- CUDA configuration ----------------------------------------------------

// 1. Tell BinaryManager to fetch the CUDA build. If your system has a
//    specific CUDA toolkit version you need pinned, the SDK's CudaVersion
//    enum documents the supported targets — leave it at default for
//    auto-detection.
preset.BinaryManagerParameters.PreferredAcceleration = AccelerationType.CUDA;

// 2. Offload EVERY transformer layer to GPU. For a 7B Q4_K_M model that's
//    ~5 GB of VRAM for weights, plus KV cache (~500 MB at 4k context in
//    F16). A 6 GB card can do it; an 8 GB card is comfortable.
//
//    If you have less VRAM, set a positive number like 24 to offload 24
//    of the model's N layers and keep the remainder on CPU. Experiment
//    downward until it stops OOM-ing.
preset.BaseModelInferenceParameters.GpuLayers = -1;

// 3. Primary GPU index. 0 is the first CUDA device. Set to 1, 2, ...
//    for secondary cards.
preset.BaseModelInferenceParameters.MainGpu = 0;

// 4. Multi-GPU split (comment in if you have 2+ GPUs).
// preset.BaseModelInferenceParameters.SplitMode = LlamaSplitMode.LLAMA_SPLIT_MODE_LAYER;
// preset.BaseModelInferenceParameters.TensorSplit = new[] { 0.6f, 0.4f };

// 5. Flash Attention and KQV-offload pay off dramatically on CUDA.
preset.ContextParameters.FlashAttention = true;
preset.ContextParameters.OffloadKqv = true;

// --- Print what we're about to do -----------------------------------------
ConsoleHelpers.Info("Configuration:");
Console.WriteLine($"  PreferredAcceleration: {preset.BinaryManagerParameters.PreferredAcceleration}");
Console.WriteLine($"  GpuLayers:             {preset.BaseModelInferenceParameters.GpuLayers}  (-1 = all)");
Console.WriteLine($"  MainGpu:               {preset.BaseModelInferenceParameters.MainGpu}");
Console.WriteLine($"  FlashAttention:        {preset.ContextParameters.FlashAttention}");
Console.WriteLine($"  OffloadKqv:            {preset.ContextParameters.OffloadKqv}");
Console.WriteLine($"  Model:                 {preset.BaseModelSourceParameters.HuggingFaceRepoId}/" +
                  $"{preset.BaseModelSourceParameters.HuggingFaceFileName}");
Console.WriteLine();

ConsoleHelpers.Info(
    "If a CUDA GPU is available, the SDK loads the CUDA native runtime and " +
    "offloads every layer. If not, it falls back to CPU silently — read the " +
    "load-time log lines to see which path was taken.\n");

using var api = new AsposeLLMApi(preset);

const string q = "On a CUDA-offloaded run, what is the single biggest contributor to first-token latency?";
ConsoleHelpers.UserLine(q);
ConsoleHelpers.AssistantLine(await api.SendMessageAsync(q));

ConsoleHelpers.Info(
    "\nCheck the llama.cpp load-time messages above for confirmation of " +
    "GPU usage (look for 'CUDA', 'offloading N layers to GPU', and " +
    "per-device VRAM accounting).");
