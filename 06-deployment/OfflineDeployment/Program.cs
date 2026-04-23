using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Offline deployment — how to run the SDK on a machine with no internet.
//
// The SDK downloads two things the first time it's used:
//   1. The GGUF model file (from HuggingFace per the preset's URL).
//   2. The native llama.cpp runtime binary (from the GitHub release tagged
//      by BinaryManagerParameters.ReleaseTag).
//
// Both are cached locally and reused on subsequent runs. For an air-gapped
// machine you pre-populate the caches on an internet-connected host, copy
// the directories over, then configure paths explicitly so the SDK never
// tries to reach the network. This tutorial shows that configuration.
//
// Detection: before running, we search the tutorial cache dir for a .gguf.
// If nothing is found, we stop and tell you to run HelloWorld first so the
// cache gets populated with Qwen 2 0.5B (the model the Small preset uses).
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Offline Deployment");

TutorialEnvironment.ApplyLicense();

var modelCacheDir = TutorialEnvironment.ResolveModelCachePath();
var modelFile = FindCachedModel(modelCacheDir);
if (modelFile is null)
{
    ConsoleHelpers.Fail("No cached GGUF found at " + modelCacheDir);
    ConsoleHelpers.Info(
        "Run 01-getting-started/HelloWorld once (with network) to populate the cache, " +
        "then return here and rerun this tutorial.");
    return 1;
}

ConsoleHelpers.Info($"Model cache:  {modelCacheDir}");
ConsoleHelpers.Info($"Model file:   {modelFile} ({new FileInfo(modelFile).Length / 1_000_000:N0} MB)");

// The binary cache defaults to %LocalAppData%/Aspose.LLM/runtimes. Whatever
// path the BinaryManager used the first time is where the DLLs live.
var defaultBinaryPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Aspose.LLM",
    "runtimes");
ConsoleHelpers.Info($"Binary cache: {defaultBinaryPath} " +
    $"({(Directory.Exists(defaultBinaryPath) ? "exists" : "missing — binary download will still be attempted")})");
Console.WriteLine();

// Build a preset that points at BOTH local files. The crucial line is
// BaseModelSourceParameters.ModelFilePath — when it's set, ModelSourceParameters'
// priority rules make the SDK skip HuggingFaceRepoId lookup entirely. No
// network call for the model.
var preset = new SmallModelPreset();

// Wipe the HF config so even if the priority rules ever changed, there's
// nothing for the SDK to fall back to.
preset.BaseModelSourceParameters.HuggingFaceRepoId = null;
preset.BaseModelSourceParameters.HuggingFaceFileName = null;
preset.BaseModelSourceParameters.AsposeModelId = null;
preset.BaseModelSourceParameters.ModelFilePath = modelFile;

// Pin the binary cache path. The BinaryManager will reuse files in here
// without contacting GitHub — provided the exact tag's artefacts are
// already present.
preset.BinaryManagerParameters.BinaryPath = defaultBinaryPath;
preset.EngineParameters.ModelCachePath = modelCacheDir;

preset.ChatParameters.MaxTokens = 120;
preset.ChatParameters.SystemPrompt =
    "You are an air-gapped assistant. Respond concisely.";

ConsoleHelpers.Info("Loading model with explicit local paths (no network lookups expected)...\n");

using var api = new AsposeLLMApi(preset);

const string q = "Introduce yourself in one sentence.";
ConsoleHelpers.UserLine(q);
ConsoleHelpers.AssistantLine(await api.SendMessageAsync(q));

ConsoleHelpers.Info(
    "\nIf this ran on an air-gapped machine it would behave identically — " +
    "the SDK touched only files already on disk.");

return 0;

static string? FindCachedModel(string cacheDir)
{
    if (!Directory.Exists(cacheDir)) return null;

    // Prefer the largest non-mmproj .gguf — base models are bigger than
    // projectors, and projectors aren't usable on their own.
    return Directory.EnumerateFiles(cacheDir, "*.gguf", SearchOption.AllDirectories)
        .Where(p => !Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p => new FileInfo(p).Length)
        .FirstOrDefault();
}
