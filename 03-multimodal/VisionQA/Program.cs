using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Vision Q&A — ask a multimodal model about an image.
//
// The flow is almost identical to a text-only chat, with two differences:
//   1. We use a vision-capable preset (Gemma3VisionPreset) that auto-downloads
//      both a base model AND a vision projector (mmproj) from HuggingFace.
//   2. SendMessageAsync receives the image as a byte[] via the `media` param —
//      the SDK sniffs the format (PNG/JPEG/WebP/...) from magic bytes, no
//      explicit MIME type is needed.
//
// First-run download: ~3.2 GB (2.6 GB model + 600 MB mmproj). Go get coffee.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Vision Q&A");

TutorialEnvironment.ApplyLicense();

// Either accept a path from the command line, or fall back to the bundled
// sample (a 480x320 PNG with a red circle, blue triangle, and green square).
var imagePath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "assets", "three-shapes.png");

if (!File.Exists(imagePath))
{
    ConsoleHelpers.Fail($"Image not found: {imagePath}");
    ConsoleHelpers.Info("Usage: dotnet run -- /path/to/image.png");
    return 1;
}

var imageBytes = await File.ReadAllBytesAsync(imagePath);
ConsoleHelpers.Info($"Image: {imagePath} ({imageBytes.Length:N0} bytes)");

// Gemma 3 Vision Latex — the same preset used by the SDK's own smoke tests.
// It wires up both HuggingFace URLs (base model + mmproj) so the first run
// downloads everything automatically.
var preset = TutorialEnvironment.CreateGemma3VisionPreset();
preset.ChatParameters.MaxTokens = 150;
preset.ChatParameters.SystemPrompt =
    "You are a helpful vision assistant. Describe images concretely and concisely.";

ConsoleHelpers.Info(
    $"Model: {DescribeSource(preset.BaseModelSourceParameters)}");
ConsoleHelpers.Info(
    $"Projector: {DescribeSource(preset.MmprojSourceParameters)}");
ConsoleHelpers.Info($"Cache: {preset.EngineParameters.ModelCachePath}");

using var api = new AsposeLLMApi(preset);

const string question =
    "Describe this image in 2-3 sentences. Name the shapes and their colours, and read any text you can see.";
ConsoleHelpers.UserLine(question);

// Pass raw PNG bytes via the `media` enumerable. The SDK auto-detects the
// format from magic bytes — JPEG/PNG/WebP/BMP/GIF are all supported.
var reply = await api.SendMessageAsync(question, media: new[] { imageBytes });
ConsoleHelpers.AssistantLine(reply);

return 0;

static string DescribeSource(dynamic source) =>
    !string.IsNullOrWhiteSpace((string?)source.ModelFilePath)
        ? source.ModelFilePath
        : $"{source.HuggingFaceRepoId}/{source.HuggingFaceFileName}";
