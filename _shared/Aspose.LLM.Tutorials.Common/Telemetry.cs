using System.ComponentModel;
using System.Diagnostics;

namespace Aspose.LLM.Tutorials.Common;

public static class Telemetry
{
    public static async Task<MemorySnapshot> CaptureAsync(string label)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();

        return new MemorySnapshot(
            label,
            GC.GetTotalMemory(false),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            await TryReadNvidiaVramUsageAsync());
    }

    public static void Print(MemorySnapshot snapshot, MemorySnapshot baseline)
    {
        Console.WriteLine(
            $"{snapshot.Label}\n" +
            $"  RAM heap:    {FormatMiB(snapshot.ManagedBytes)} ({FormatDelta(snapshot.ManagedBytes - baseline.ManagedBytes)})\n" +
            $"  RAM working: {FormatMiB(snapshot.WorkingSetBytes)} ({FormatDelta(snapshot.WorkingSetBytes - baseline.WorkingSetBytes)})\n" +
            $"  RAM private: {FormatMiB(snapshot.PrivateBytes)} ({FormatDelta(snapshot.PrivateBytes - baseline.PrivateBytes)})");

        if (snapshot.NvidiaVramUsedMiB is int usedMiB)
            Console.WriteLine($"  VRAM used:   {usedMiB} MiB");
        else
            Console.WriteLine("  VRAM used:   unavailable");
    }

    private static async Task<int?> TryReadNvidiaVramUsageAsync()
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

    private static string FormatMiB(long bytes) => $"{bytes / 1024d / 1024d:F1} MiB";

    private static string FormatDelta(long bytes)
    {
        var sign = bytes >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(bytes) / 1024d / 1024d:F1} MiB";
    }
}

public sealed record MemorySnapshot(
    string Label,
    long ManagedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    int? NvidiaVramUsedMiB);
