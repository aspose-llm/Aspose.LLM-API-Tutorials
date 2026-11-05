using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters;
using Aspose.LLM.Abstractions.Parameters.Presets;

namespace Aspose.LLM.Tutorials.Common;

/// <summary>
/// Bootstraps the two things every tutorial needs before calling the SDK:
/// the Aspose licence (mandatory — SendMessageAsync throws without it) and
/// a writable cache directory for downloaded GGUF models.
/// </summary>
public static class TutorialEnvironment
{
    public const string LicenseEnvVar = "ASPOSE_LLM_LICENSE";
    public const string ModelCacheEnvVar = "ASPOSE_LLM_MODEL_CACHE";
    public const string DefaultLicenseFileName = "License.lic";
    private const string DefaultModelCacheSubdir = "Aspose.LLM\\models";

    /// <summary>
    /// Loads the Aspose.LLM licence from either the <c>ASPOSE_LLM_LICENSE</c>
    /// environment variable or a <c>License.lic</c> file next to the running
    /// executable / in the working directory.
    /// </summary>
    /// <exception cref="FileNotFoundException">No licence found in any known location.</exception>
    public static void ApplyLicense()
    {
        var path = ResolveLicensePath()
            ?? throw new FileNotFoundException(
                $"Aspose.LLM licence not found. Set the {LicenseEnvVar} environment variable " +
                $"to an absolute path, or place '{DefaultLicenseFileName}' in the working directory. " +
                $"Request a temporary licence at https://purchase.aspose.com/temporary-license.");

        var license = new License();
        license.SetLicense(path);
        Console.WriteLine($"[licence] applied from {path}");
    }

    /// <summary>
    /// Returns the directory where presets cache downloaded GGUF model files.
    /// Falls back to <c>%LocalAppData%/Aspose.LLM/models</c> so the whole
    /// solution shares one stable cache outside the temp directory.
    /// </summary>
    public static string ResolveModelCachePath()
    {
        var envPath = Environment.GetEnvironmentVariable(ModelCacheEnvVar);
        var path = !string.IsNullOrWhiteSpace(envPath)
            ? envPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aspose.LLM",
                "models");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void PreferManagedCache(PresetCoreBase preset)
    {
        preset.EngineParameters.ModelCachePath = ResolveModelCachePath();
    }

    public static Qwen25Preset CreateQwen25Preset()
    {
        var preset = new Qwen25Preset();
        PreferManagedCache(preset);
        PreferLocalModelFile(
            preset.BaseModelSourceParameters,
            ResolveQwen25ModelFilePath());
        preset.EngineParameters.EnableDebugLogging = false;
        return preset;
    }

    public static Gemma3VisionPreset CreateGemma3VisionPreset()
    {
        var preset = new Gemma3VisionPreset();
        PreferManagedCache(preset);
        PreferLocalModelFile(
            preset.BaseModelSourceParameters,
            ResolveGemma3VisionModelFilePath());
        PreferLocalModelFile(
            preset.MmprojSourceParameters,
            ResolveGemma3VisionProjectorPath());
        preset.EngineParameters.EnableDebugLogging = false;
        return preset;
    }

    public static void PreferLocalModelFile(ModelSourceParameters source, string? localFilePath)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
            return;

        source.ModelFilePath = localFilePath;
        source.AsposeModelId = null;
        source.HuggingFaceRepoId = null;
        source.HuggingFaceFileName = null;
    }

    public static string? ResolveSmallModelFilePath() =>
        ResolveManagedCacheFile(
            targetRelativePath: Path.Combine(
                "Qwen_Qwen2-0.5B-Instruct-GGUF",
                "qwen2-0_5b-instruct-q4_k_m.gguf"),
            candidateRelativePaths:
            [
                Path.Combine("Qwen_Qwen2-0.5B-Instruct-GGUF", "qwen2-0_5b-instruct-q4_k_m.gguf"),
                Path.Combine("Qwen_Qwen2-0.5B-Instruct-GGUF", "Qwen2-0.5B-Instruct.Q4_K_M.gguf"),
                Path.Combine("QuantFactory_Qwen2-0.5B-Instruct-GGUF", "Qwen2-0.5B-Instruct.Q4_K_M.gguf"),
            ]);

    public static string? ResolveQwen25ModelFilePath() =>
        ResolveManagedCacheFile(
            targetRelativePath: Path.Combine(
                "bartowski_Qwen2.5-7B-Instruct-GGUF",
                "Qwen2.5-7B-Instruct-Q4_K_M.gguf"),
            candidateRelativePaths:
            [
                Path.Combine("bartowski_Qwen2.5-7B-Instruct-GGUF", "Qwen2.5-7B-Instruct-Q4_K_M.gguf"),
            ]);

    public static string? ResolveGemma3VisionModelFilePath() =>
        ResolveManagedCacheFile(
            targetRelativePath: Path.Combine(
                "mradermacher_Gemma-3-Vision-Latex-GGUF",
                "Gemma-3-Vision-Latex.Q4_K_M.gguf"),
            candidateRelativePaths:
            [
                Path.Combine("mradermacher_Gemma-3-Vision-Latex-GGUF", "Gemma-3-Vision-Latex.Q4_K_M.gguf"),
            ]);

    public static string? ResolveGemma3VisionProjectorPath() =>
        ResolveManagedCacheFile(
            targetRelativePath: Path.Combine(
                "mradermacher_Gemma-3-Vision-Latex-GGUF",
                "Gemma-3-Vision-Latex.mmproj-f16.gguf"),
            candidateRelativePaths:
            [
                Path.Combine("mradermacher_Gemma-3-Vision-Latex-GGUF", "Gemma-3-Vision-Latex.mmproj-f16.gguf"),
            ]);

    private static string? ResolveLicensePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(LicenseEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        // Probe a few conventional locations so the tutorials "just run" when
        // the user drops License.lic anywhere sensible.
        var probes = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), DefaultLicenseFileName),
            Path.Combine(AppContext.BaseDirectory, DefaultLicenseFileName),
        };
        return probes.FirstOrDefault(File.Exists);
    }

    private static string? ResolveManagedCacheFile(
        string targetRelativePath,
        IEnumerable<string> candidateRelativePaths)
    {
        var primaryCache = ResolveModelCachePath();
        var targetPath = Path.Combine(primaryCache, targetRelativePath);
        if (File.Exists(targetPath))
            return targetPath;

        foreach (var root in EnumerateKnownModelCacheRoots())
        {
            foreach (var relativePath in candidateRelativePaths)
            {
                var candidate = Path.Combine(root, relativePath);
                if (!File.Exists(candidate))
                    continue;

                if (PathsEqual(root, primaryCache))
                    return candidate;

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (!File.Exists(targetPath))
                    File.Copy(candidate, targetPath);
                return targetPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateKnownModelCacheRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var primaryCache = ResolveModelCachePath();
        if (seen.Add(primaryCache))
            yield return primaryCache;

        var localAppDataCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DefaultModelCacheSubdir);
        if (Directory.Exists(localAppDataCache) && seen.Add(localAppDataCache))
            yield return localAppDataCache;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            var candidate = Path.Combine(drive.RootDirectory.FullName, DefaultModelCacheSubdir);
            if (Directory.Exists(candidate) && seen.Add(candidate))
                yield return candidate;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
