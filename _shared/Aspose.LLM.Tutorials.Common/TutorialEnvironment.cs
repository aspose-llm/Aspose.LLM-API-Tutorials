using Aspose.LLM;

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
    /// Falls back to <c>%TEMP%/aspose-llm-tutorials-models</c> so multiple
    /// tutorials share one cache and do not re-download the same file.
    /// </summary>
    public static string ResolveModelCachePath()
    {
        var envPath = Environment.GetEnvironmentVariable(ModelCacheEnvVar);
        var path = !string.IsNullOrWhiteSpace(envPath)
            ? envPath
            : Path.Combine(Path.GetTempPath(), "aspose-llm-tutorials-models");
        Directory.CreateDirectory(path);
        return path;
    }

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
}
