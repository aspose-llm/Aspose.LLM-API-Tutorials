using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using Microsoft.Extensions.Logging;

// RAM/VRAM usage grows with ContextSize because the KV cache scales with the
// number of tokens the session can hold. A very rough rule of thumb for a
// 7B-class model is that each extra 1K tokens may cost on the order of
// 50-100 MiB of RAM/VRAM, depending on the model, KV-cache quantization, and
// backend. Keep the context only as large as the task really needs.
const int ImageContextSize = 8192;

ConsoleHelpers.Banner("Aspose.LLM — Directory Batch Processing");
TutorialEnvironment.ApplyLicense();

var preset = BuildPreset();
var files = GetSampleFiles();

foreach (var file in files)
{
    if (!File.Exists(file))
    {
        ConsoleHelpers.Fail($"Bundled asset is missing: {file}");
        return 1;
    }
}

ConsoleHelpers.Info(
    $"Images:       {files.Length}\n" +
    $"Context size: {ImageContextSize}\n" +
    $"Cache:        {preset.EngineParameters.ModelCachePath}");
ConsoleHelpers.Info("Pattern: one image -> one session -> one cleanup");

using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
using var api = new AsposeLLMApi(preset, loggerFactory.CreateLogger<AsposeLLMApi>());
var baseline = await Telemetry.CaptureAsync("baseline");
Telemetry.Print(baseline, baseline);

var sessionId = await api.StartNewChatAsync();

foreach (var file in files)
{
    ConsoleHelpers.Section(Path.GetFileName(file));

    var imageBytes = await File.ReadAllBytesAsync(file);
    var reply = await api.SendMessageToSessionAsync(
        sessionId,
        "Describe this image in 1-2 short sentences.",
        media: new[] { imageBytes });

    Console.WriteLine(reply.Trim());
    Telemetry.Print(await Telemetry.CaptureAsync("after reply"), baseline);

    // By default, the SDK performs automatic cleanup when the active session
    // reaches context pressure. If you need to release resources for the
    // current session explicitly, you can also call:
    // api.ForceCacheCleanup(CacheCleanupStrategy.KeepSystemPromptOnly);
    // Console.WriteLine("cache cleaned");
    // Telemetry.Print(await Telemetry.CaptureAsync("after cleanup"), baseline);
}

return 0;

static Gemma3VisionPreset BuildPreset()
{
    var preset = new Gemma3VisionPreset();
    preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
    preset.EngineParameters.EnableDebugLogging = false;
    preset.ContextParameters.ContextSize = ImageContextSize;
    preset.ChatParameters.MaxTokens = 120;
    preset.ChatParameters.SystemPrompt =
        "You describe one image at a time in simple language.";
    return preset;
}

static string[] GetSampleFiles()
{
    var root = Path.Combine(AppContext.BaseDirectory, "assets", "images");
    return
    [
        Path.Combine(root, "route-map.png"),
        Path.Combine(root, "shapes-note.png"),
        Path.Combine(root, "status-board.png"),
        Path.Combine(root, "warehouse-labels.png"),
    ];
}
