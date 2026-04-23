using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;
using Microsoft.Extensions.Logging;

// -----------------------------------------------------------------------------
// Logging integration — pipe the SDK's ILogger into your host's logging stack.
//
// AsposeLLMApi's constructor accepts an optional ILogger. When provided, the
// SDK's managed layer writes structured messages to it — model load events,
// session lifecycle, cache cleanup, errors. Native llama.cpp log lines are
// separate (they go to stdout by default) and are NOT routed through this
// logger in the current facade.
//
// Three flavours demonstrated:
//   1. No logger (baseline) — default, silent managed side.
//   2. Console logger via LoggerFactory — visible SDK events.
//   3. File logger via a simple custom provider — route to disk.
//
// The log volume depends on preset.EngineParameters.EnableDebugLogging:
//   - false (default): only warnings/errors and major lifecycle events.
//   - true:  plus per-operation debug lines. Noisy but illuminating.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Logging Integration");

TutorialEnvironment.ApplyLicense();

// --- Flavour 1: no logger ---------------------------------------------------
ConsoleHelpers.Section("Flavour 1 — no logger (default)");
{
    var preset = NewPreset(debug: false);
    using var api = new AsposeLLMApi(preset);
    var reply = await api.SendMessageAsync("Say 'ready' and nothing else.");
    Console.WriteLine($"  reply: '{reply.Trim()}'");
    Console.WriteLine("  (no SDK log lines visible — the managed layer is silent.)");
}

// --- Flavour 2: console logger ---------------------------------------------
ConsoleHelpers.Section("Flavour 2 — Microsoft.Extensions.Logging.Console");
{
    using var factory = LoggerFactory.Create(b =>
    {
        b.AddConsole(o => o.FormatterName = "simple");
        b.SetMinimumLevel(LogLevel.Debug);
    });
    var logger = factory.CreateLogger<AsposeLLMApi>();

    var preset = NewPreset(debug: true);
    using var api = new AsposeLLMApi(preset, logger);
    var reply = await api.SendMessageAsync("Say 'ready' and nothing else.");
    Console.WriteLine($"\n  reply: '{reply.Trim()}'");
    Console.WriteLine("  SDK managed events were printed above with timestamps/levels.");
}

// --- Flavour 3: file logger (custom minimal provider) ----------------------
ConsoleHelpers.Section("Flavour 3 — custom file logger");
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "sdk.log");
    File.Delete(logPath);  // clean slate per run

    using var factory = LoggerFactory.Create(b =>
    {
        b.AddProvider(new FileLoggerProvider(logPath));
        b.SetMinimumLevel(LogLevel.Information);
    });
    var logger = factory.CreateLogger<AsposeLLMApi>();

    var preset = NewPreset(debug: false);
    using var api = new AsposeLLMApi(preset, logger);
    _ = await api.SendMessageAsync("Say 'ready' and nothing else.");

    Console.WriteLine($"  wrote {new FileInfo(logPath).Length:N0} bytes to {logPath}");
    Console.WriteLine("  first 5 lines of that file:");
    foreach (var line in File.ReadLines(logPath).Take(5))
        Console.WriteLine("    " + line);
}

ConsoleHelpers.Info(
    "\nFor production wiring: use Serilog / NLog / ZLogger via their " +
    "Microsoft.Extensions.Logging adapter packages. The SDK doesn't care " +
    "which logging framework you use — it only sees the ILogger interface.");

return;

// ---------------------------------------------------------------------------
static SmallModelPreset NewPreset(bool debug)
{
    var p = new SmallModelPreset();
    p.EngineParameters.EnableDebugLogging = debug;
    p.ChatParameters.MaxTokens = 16;
    return p;
}

// --- Minimal file logger provider ------------------------------------------
// About 25 lines, no external dependency. Demonstrates how trivial it is to
// route the SDK's managed ILogger calls anywhere you want.
file sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, path, _gate);

    public void Dispose() { }

    private sealed class FileLogger(string category, string path, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel,-11}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += $" -- {exception.GetType().Name}: {exception.Message}";
            lock (gate) File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
