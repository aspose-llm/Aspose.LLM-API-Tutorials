using System.ComponentModel;
using System.Diagnostics;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using Microsoft.Extensions.Logging;

const int TextContextSize = 4096;
const int ImageContextSize = 8192;
const int TextReservedTokens = 800;
const int ImageMaxBytes = 50 * 1024 * 1024;
const int CleanupCadence = 3;
const int VramPressureDeltaMiB = 1024;
const long PrivatePressureDeltaBytes = 1024L * 1024L * 1024L;

BatchMode mode;
try
{
    mode = ParseMode(args);
}
catch (ArgumentException ex)
{
    if (string.Equals(ex.Message, "help", StringComparison.Ordinal))
    {
        PrintUsage();
        return 0;
    }

    ConsoleHelpers.Fail(ex.Message);
    PrintUsage();
    return 1;
}

ConsoleHelpers.Banner("Aspose.LLM — Directory Batch Processing");
TutorialEnvironment.ApplyLicense();

var preset = BuildPreset(mode);
var contextSize = preset.ContextParameters.ContextSize
    ?? (mode == BatchMode.Image ? ImageContextSize : TextContextSize);
var files = GetSampleFiles(mode);

if (files.Any(path => !File.Exists(path)))
{
    var missing = files.First(path => !File.Exists(path));
    ConsoleHelpers.Fail($"Bundled asset is missing: {missing}");
    return 1;
}

ConsoleHelpers.Info(
    $"Mode:            {mode}\n" +
    $"Sample items:    {files.Length}\n" +
    $"Context size:    {contextSize} tokens\n" +
    $"Cleanup cadence: {CleanupCadence} items\n" +
    $"Cache:           {preset.EngineParameters.ModelCachePath}");
ConsoleHelpers.Info(
    "Phases:          preflight guard -> post-item snapshot -> " +
    "emergency cleanup on failure/context overflow -> periodic cleanup -> " +
    "pressure cleanup before the next session -> final cleanup");

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Warning);
});
var logger = loggerFactory.CreateLogger<AsposeLLMApi>();
using var api = new AsposeLLMApi(preset, logger);

var baseline = await CaptureSnapshotAsync("Baseline after API construction");
PrintSnapshot(baseline, baseline);
var previousSnapshot = baseline;

var total = Stopwatch.StartNew();
var results = new List<BatchResult>(files.Length);

for (var index = 0; index < files.Length; index++)
{
    var filePath = files[index];
    ConsoleHelpers.Section($"Item {index + 1}/{files.Length} — {Path.GetFileName(filePath)}");

    var result = mode == BatchMode.Image
        ? await ProcessImageAsync(api, filePath)
        : await ProcessTextAsync(api, filePath, contextSize);

    results.Add(result);
    PrintResult(result);

    var postItemSnapshot = await CaptureSnapshotAsync($"Post-item snapshot after item {index + 1}");
    PrintSnapshot(postItemSnapshot, baseline);

    if (result.RequiresEmergencyCleanup)
    {
        previousSnapshot = await RunCleanupPhaseAsync(
            api,
            "Emergency cleanup",
            "The previous item failed or overflowed the fixed context budget. Clean up before opening the next session.",
            baseline);
    }
    else if ((index + 1) % CleanupCadence == 0)
    {
        previousSnapshot = await RunCleanupPhaseAsync(
            api,
            $"Periodic cleanup after {index + 1} items",
            "This fixed cleanup phase prunes stale session cache on schedule.",
            baseline);
    }
    else if (ShouldRunPressureCleanup(postItemSnapshot, previousSnapshot, baseline, out var reason))
    {
        previousSnapshot = await RunCleanupPhaseAsync(
            api,
            "Pressure cleanup between sessions",
            reason,
            baseline);
    }
    else
    {
        previousSnapshot = postItemSnapshot;
    }
}

_ = await RunCleanupPhaseAsync(
    api,
    "Final cleanup",
    "The batch is complete. This shows what remains resident after all session cache has been pruned.",
    baseline);

total.Stop();
PrintSummary(results, total.Elapsed);
return results.Any(r => r.Status == BatchItemStatus.Failed) ? 1 : 0;

static PresetCoreBase BuildPreset(BatchMode mode)
{
    if (mode == BatchMode.Image)
    {
        var preset = new Gemma3VisionPreset();
        preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
        preset.EngineParameters.EnableDebugLogging = false;
        preset.ContextParameters.ContextSize = ImageContextSize;
        preset.ChatParameters.MaxTokens = 120;
        preset.ChatParameters.SystemPrompt =
            "You are a concise batch vision assistant. Describe each image in 1-2 concrete sentences. " +
            "Mention visible objects, colours, and any readable text.";
        return preset;
    }

    var presetText = new Aspose.LLM.Tutorials.Common.SmallModelPreset();
    presetText.ContextParameters.ContextSize = TextContextSize;
    presetText.ChatParameters.MaxTokens = 180;
    presetText.ChatParameters.SystemPrompt =
        "You summarize one independent document at a time. Reply in 2-3 concise sentences and keep only facts explicit in the document.";
    presetText.SamplerParameters.Temperature = 0.2f;
    return presetText;
}

static string[] GetSampleFiles(BatchMode mode)
{
    var root = mode == BatchMode.Image
        ? Path.Combine(AppContext.BaseDirectory, "assets", "images")
        : Path.Combine(AppContext.BaseDirectory, "assets", "text");

    return mode == BatchMode.Image
        ? new[]
        {
            Path.Combine(root, "route-map.png"),
            Path.Combine(root, "shapes-note.png"),
            Path.Combine(root, "status-board.png"),
            Path.Combine(root, "warehouse-labels.png"),
        }
        : new[]
        {
            Path.Combine(root, "incident-summary.txt"),
            Path.Combine(root, "ops-shift.log"),
            Path.Combine(root, "project-brief.txt"),
            Path.Combine(root, "release-notes.md"),
        };
}

static async Task<BatchResult> ProcessImageAsync(AsposeLLMApi api, string filePath)
{
    var fileInfo = new FileInfo(filePath);
    var bytes = await File.ReadAllBytesAsync(filePath);

    if (fileInfo.Length > ImageMaxBytes)
    {
        return BatchResult.ContextRejected(
            filePath,
            fileInfo.Length,
            256,
            0,
            "Image is larger than the SDK media limit (50 MB). Resize or compress it before starting a session.");
    }

    try
    {
        var sessionId = await api.StartNewChatAsync();
        var sw = Stopwatch.StartNew();
        const string prompt =
            "Describe this image in 1-2 concrete sentences. Mention visible objects, colours, and any readable text.";
        var reply = (await api.SendMessageToSessionAsync(sessionId, prompt, media: new[] { bytes })).Trim();
        sw.Stop();

        return BatchResult.Processed(filePath, fileInfo.Length, 256, sw.ElapsedMilliseconds, reply);
    }
    catch (Exception ex) when (LooksLikeContextOverflow(ex))
    {
        return BatchResult.ContextRejected(
            filePath,
            fileInfo.Length,
            256,
            0,
            $"Runtime context overflow: {SanitizeException(ex)} Reduce the prompt or the per-image workload.",
            requiresEmergencyCleanup: true);
    }
    catch (Exception ex)
    {
        return BatchResult.Failed(
            filePath,
            fileInfo.Length,
            256,
            0,
            SanitizeException(ex),
            requiresEmergencyCleanup: true);
    }
}

static async Task<BatchResult> ProcessTextAsync(AsposeLLMApi api, string filePath, int contextSize)
{
    var fileInfo = new FileInfo(filePath);
    var content = (await File.ReadAllTextAsync(filePath)).Trim();

    if (string.IsNullOrWhiteSpace(content))
        return BatchResult.Skipped(filePath, fileInfo.Length, 0, 0, "Bundled file is empty.");

    var estimatedTokens = EstimateTokens(content.Length);
    var maxDocumentTokens = contextSize - TextReservedTokens;
    if (estimatedTokens > maxDocumentTokens)
    {
        return BatchResult.ContextRejected(
            filePath,
            fileInfo.Length,
            estimatedTokens,
            0,
            $"Estimated {estimatedTokens} tokens exceeds the safe budget of {maxDocumentTokens} tokens. Chunk the document or raise ContextSize intentionally.");
    }

    try
    {
        var sessionId = await api.StartNewChatAsync();
        var sw = Stopwatch.StartNew();
        var prompt =
            "Summarize the following document in 2-3 sentences. Focus on the explicit main idea, key facts, and any action items.\n\n" +
            $"Document:\n{content}";
        var reply = (await api.SendMessageToSessionAsync(sessionId, prompt)).Trim();
        sw.Stop();

        return BatchResult.Processed(filePath, fileInfo.Length, estimatedTokens, sw.ElapsedMilliseconds, reply);
    }
    catch (Exception ex) when (LooksLikeContextOverflow(ex))
    {
        return BatchResult.ContextRejected(
            filePath,
            fileInfo.Length,
            estimatedTokens,
            0,
            $"Runtime context overflow: {SanitizeException(ex)} Chunk the document, shorten the prompt, or increase the fixed context.",
            requiresEmergencyCleanup: true);
    }
    catch (Exception ex)
    {
        return BatchResult.Failed(
            filePath,
            fileInfo.Length,
            estimatedTokens,
            0,
            SanitizeException(ex),
            requiresEmergencyCleanup: true);
    }
}

static async Task<MemorySnapshot> RunCleanupPhaseAsync(
    AsposeLLMApi api,
    string title,
    string explanation,
    MemorySnapshot baseline)
{
    ConsoleHelpers.Section(title);
    ConsoleHelpers.Info(explanation);
    api.ForceCacheCleanup(CacheCleanupStrategy.KeepSystemPromptOnly);
    var snapshot = await CaptureSnapshotAsync($"{title} snapshot");
    PrintSnapshot(snapshot, baseline);
    return snapshot;
}

static async Task<MemorySnapshot> CaptureSnapshotAsync(string label)
{
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var vramUsedMiB = await TryReadNvidiaVramUsageAsync();
    return new MemorySnapshot(
        label,
        GC.GetTotalMemory(false),
        process.WorkingSet64,
        process.PrivateMemorySize64,
        vramUsedMiB);
}

static async Task<int?> TryReadNvidiaVramUsageAsync()
{
    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.used --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        if (!process.Start())
            return null;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            return null;

        var totalUsed = 0;
        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(line.Trim(), out var value))
                totalUsed += value;
        }

        return totalUsed >= 0 ? totalUsed : null;
    }
    catch (Win32Exception)
    {
        return null;
    }
    catch (InvalidOperationException)
    {
        return null;
    }
}

static void PrintSnapshot(MemorySnapshot snapshot, MemorySnapshot baseline)
{
    Console.WriteLine(
        $"  {snapshot.Label}\n" +
        $"    Managed heap: {FormatMiB(snapshot.ManagedBytes)} ({FormatDelta(snapshot.ManagedBytes - baseline.ManagedBytes)})\n" +
        $"    Working set:  {FormatMiB(snapshot.WorkingSetBytes)} ({FormatDelta(snapshot.WorkingSetBytes - baseline.WorkingSetBytes)})\n" +
        $"    Private mem:  {FormatMiB(snapshot.PrivateBytes)} ({FormatDelta(snapshot.PrivateBytes - baseline.PrivateBytes)})");

    if (snapshot.NvidiaVramUsedMiB is int usedMiB)
        Console.WriteLine($"    VRAM used:    {usedMiB} MiB");
    else
        Console.WriteLine("    VRAM used:    unavailable (nvidia-smi not found)");
}

static bool ShouldRunPressureCleanup(
    MemorySnapshot current,
    MemorySnapshot previous,
    MemorySnapshot baseline,
    out string reason)
{
    if (current.NvidiaVramUsedMiB is int currentVram && baseline.NvidiaVramUsedMiB is int baselineVram)
    {
        var deltaFromBaseline = currentVram - baselineVram;
        if (deltaFromBaseline >= VramPressureDeltaMiB)
        {
            reason = $"VRAM is up by {deltaFromBaseline} MiB from the batch baseline. Force cleanup before the next session.";
            return true;
        }
    }

    var privateDelta = current.PrivateBytes - baseline.PrivateBytes;
    if (privateDelta >= PrivatePressureDeltaBytes)
    {
        reason = $"Private memory is up by {FormatMiB(privateDelta)} from the batch baseline. Force cleanup before the next session.";
        return true;
    }

    if (current.NvidiaVramUsedMiB is int currentSessionVram && previous.NvidiaVramUsedMiB is int previousSessionVram)
    {
        var deltaFromPrevious = currentSessionVram - previousSessionVram;
        if (deltaFromPrevious >= VramPressureDeltaMiB)
        {
            reason = $"VRAM grew by {deltaFromPrevious} MiB since the previous session snapshot. Force cleanup before opening another session.";
            return true;
        }
    }

    reason = string.Empty;
    return false;
}

static void PrintResult(BatchResult result)
{
    var previous = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = result.Status switch
        {
            BatchItemStatus.Processed => ConsoleColor.Green,
            BatchItemStatus.ContextRejected => ConsoleColor.DarkYellow,
            BatchItemStatus.Skipped => ConsoleColor.DarkYellow,
            BatchItemStatus.Failed => ConsoleColor.Red,
            _ => previous,
        };
        Console.Write($"  [{result.Status,-9}]");
    }
    finally
    {
        Console.ForegroundColor = previous;
    }

    var tokenInfo = result.EstimatedTokens is int tokens ? $" · ~{tokens} tok" : string.Empty;
    Console.WriteLine($"  {Path.GetFileName(result.FilePath)} · {result.SourceBytes:N0} bytes{tokenInfo} · {result.ElapsedMs} ms");
    Console.WriteLine($"    {result.Detail}");
}

static void PrintSummary(IEnumerable<BatchResult> results, TimeSpan totalElapsed)
{
    var list = results.ToList();
    var processed = list.Count(r => r.Status == BatchItemStatus.Processed);
    var contextRejected = list.Count(r => r.Status == BatchItemStatus.ContextRejected);
    var skipped = list.Count(r => r.Status == BatchItemStatus.Skipped);
    var failed = list.Count(r => r.Status == BatchItemStatus.Failed);
    var avgMs = processed == 0 ? 0 : list.Where(r => r.Status == BatchItemStatus.Processed).Average(r => r.ElapsedMs);

    ConsoleHelpers.Section("Throughput");
    Console.WriteLine(
        $"  Processed:    {processed}\n" +
        $"  Context skip: {contextRejected}\n" +
        $"  Skipped:      {skipped}\n" +
        $"  Failed:       {failed}\n" +
        $"  Total time:   {totalElapsed.TotalSeconds:F1}s\n" +
        $"  Avg/item:     {avgMs:F0} ms\n" +
        $"  Throughput:   {processed / Math.Max(0.001, totalElapsed.TotalSeconds):F2} items/s");

    ConsoleHelpers.Info(
        "\nThe sample uses one fresh session per asset file. Forced cleanup prunes session cache, but model weights stay resident until the API is disposed.");
}

static int EstimateTokens(int charCount) => (charCount + 3) / 4;

static string FormatMiB(long bytes) => $"{bytes / 1024d / 1024d:F1} MiB";

static string FormatDelta(long bytes)
{
    var sign = bytes >= 0 ? "+" : "-";
    return $"{sign}{Math.Abs(bytes) / 1024d / 1024d:F1} MiB";
}

static BatchMode ParseMode(string[] args)
{
    if (args.Length == 0)
        return BatchMode.Image;

    if (args.Length == 2 && (args[0] == "--mode" || args[0] == "-m"))
        return ParseModeValue(args[1]);

    if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
        throw new ArgumentException("help");

    throw new ArgumentException("Use no arguments for image mode, or `--mode text` for text mode.");
}

static BatchMode ParseModeValue(string value) => value.ToLowerInvariant() switch
{
    "image" => BatchMode.Image,
    "text" => BatchMode.Text,
    _ => throw new ArgumentException($"Unsupported mode '{value}'. Use `image` or `text`."),
};

static void PrintUsage()
{
    Console.WriteLine(
        "Usage:\n" +
        "  dotnet run\n" +
        "  dotnet run -- --mode text");
}

static bool LooksLikeContextOverflow(Exception ex)
{
    var text = ex.ToString();
    return text.Contains("context", StringComparison.OrdinalIgnoreCase)
        || text.Contains("n_ctx", StringComparison.OrdinalIgnoreCase)
        || text.Contains("too many token", StringComparison.OrdinalIgnoreCase)
        || text.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase)
        || text.Contains("exceed", StringComparison.OrdinalIgnoreCase);
}

static string SanitizeException(Exception ex)
    => ex.GetBaseException().Message.Replace(Environment.NewLine, " ");

enum BatchMode
{
    Image,
    Text,
}

enum BatchItemStatus
{
    Processed,
    ContextRejected,
    Skipped,
    Failed,
}

sealed record MemorySnapshot(
    string Label,
    long ManagedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    int? NvidiaVramUsedMiB);

sealed record BatchResult(
    string FilePath,
    long SourceBytes,
    int? EstimatedTokens,
    long ElapsedMs,
    BatchItemStatus Status,
    string Detail,
    bool RequiresEmergencyCleanup)
{
    public static BatchResult Processed(string filePath, long sourceBytes, int? estimatedTokens, long elapsedMs, string detail)
        => new(filePath, sourceBytes, estimatedTokens, elapsedMs, BatchItemStatus.Processed, detail, false);

    public static BatchResult ContextRejected(
        string filePath,
        long sourceBytes,
        int? estimatedTokens,
        long elapsedMs,
        string detail,
        bool requiresEmergencyCleanup = false)
        => new(filePath, sourceBytes, estimatedTokens, elapsedMs, BatchItemStatus.ContextRejected, detail, requiresEmergencyCleanup);

    public static BatchResult Skipped(string filePath, long sourceBytes, int? estimatedTokens, long elapsedMs, string detail)
        => new(filePath, sourceBytes, estimatedTokens, elapsedMs, BatchItemStatus.Skipped, detail, false);

    public static BatchResult Failed(
        string filePath,
        long sourceBytes,
        int? estimatedTokens,
        long elapsedMs,
        string detail,
        bool requiresEmergencyCleanup = false)
        => new(filePath, sourceBytes, estimatedTokens, elapsedMs, BatchItemStatus.Failed, detail, requiresEmergencyCleanup);
}
