# Logging Integration

Wire `Microsoft.Extensions.Logging.ILogger` into `AsposeLLMApi` so you can
see SDK events in your console or log file — and understand exactly which
layer each log line comes from.

## What it demonstrates

- **The facade takes an optional `ILogger`** via its constructor:
  `new AsposeLLMApi(preset, logger)`.
- **`Microsoft.Extensions.Logging.Console`** — the simplest real logger.
- **A minimal custom `ILoggerProvider`** (~25 lines) that writes to a
  file — demonstrates how trivial it is to route logs anywhere.
- **`EngineParameters.EnableDebugLogging`** — the knob that turns the
  managed layer from near-silent to verbose.

## Two logging channels

The SDK emits log messages from **two different places**, and they behave
differently:

| Channel               | Goes to                                  | Controlled by                       |
| --------------------- | ---------------------------------------- | ----------------------------------- |
| Managed (SDK C#)      | The `ILogger` you pass to the ctor       | `EnableDebugLogging` + log filter   |
| Native (llama.cpp)    | stdout by default                        | llama.cpp's own callback mechanism  |

**This tutorial covers the first channel only.** The native llama.cpp
lines you see during model load (`llama_kv_cache: layer N: dev = CPU`,
etc.) come from the second channel and aren't currently routed through
the managed `ILogger` by the public facade surface.

## Run it

```bash
cd 07-how-to/LoggingIntegration
dotnet run
```

Sample output (abridged):

```
── Flavour 1 — no logger (default) ──
  reply: 'ready'
  (no SDK log lines visible — the managed layer is silent.)

── Flavour 2 — Microsoft.Extensions.Logging.Console ──
info: Aspose.LLM.AsposeLLMApi[0]
      Initialising engine with preset Aspose.LLM.Tutorials.Common.SmallModelPreset
info: Aspose.LLM.AsposeLLMApi[0]
      Starting new chat session abcd-ef01-...
dbug: Aspose.LLM.AsposeLLMApi[0]
      Sending message; 42 chars
  reply: 'ready'
  SDK managed events were printed above with timestamps/levels.

── Flavour 3 — custom file logger ──
  wrote 612 bytes to /.../bin/Debug/net8.0/sdk.log
  first 5 lines of that file:
    14:02:17.230 [Information] Aspose.LLM.AsposeLLMApi: Initialising engine...
    14:02:17.312 [Information] Aspose.LLM.AsposeLLMApi: Starting new chat session...
    ...
```

## Wiring into a real host

### ASP.NET Core

```csharp
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AsposeLLMApi>>();
    return new AsposeLLMApi(preset, logger);
});
```

`ILogger<AsposeLLMApi>` resolves via the host's existing logging stack —
whatever you've configured (console, Serilog sink to Seq, Application
Insights, etc.) picks up the SDK's messages automatically.

### Console app with Serilog

```csharp
using var factory = LoggerFactory.Create(b =>
    b.AddSerilog(new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.File("logs/sdk-.log", rollingInterval: RollingInterval.Day)
        .CreateLogger()));
var logger = factory.CreateLogger<AsposeLLMApi>();
```

The SDK doesn't know what's on the other end of `ILogger`; Serilog, NLog,
and ZLogger all ship adapter packages that plug into `LoggerFactory`.

## What's logged at each level

Approximate taxonomy — the SDK doesn't document these strictly but the
pattern is standard .NET:

- `Trace` / `Debug` — per-token, per-cache-entry, per-binary-lookup. Only
  useful when diagnosing specific issues.
- `Information` — session lifecycle, model load start/end, cache cleanup
  firing.
- `Warning` — recoverable anomalies (e.g. cache miss, fallback to a
  different acceleration).
- `Error` — exceptions that escape to the caller.

With `EnableDebugLogging = false` (the default), the SDK filters below
`Information` internally even if your `ILogger` would accept them. Flip
the flag to see the detailed levels.

## The minimal file logger — why ship it in the tutorial

Every log-sink tutorial shows Serilog or NLog, but those add dependencies
the user may not want. The 25-line `FileLoggerProvider` at the bottom of
[`Program.cs`](Program.cs) proves three things:

1. `ILoggerProvider` is just one method (`CreateLogger`).
2. `ILogger` is just one method that matters (`Log<TState>`).
3. Wiring a custom sink into the Microsoft.Extensions.Logging stack is a
   2-minute job, no frameworks required.

Copy the class into your own project and modify the `Log` method to route
wherever you need — syslog, a message queue, Datadog's HTTP endpoint,
etc.

## Things to try

- Flip `EnableDebugLogging = true` in Flavour 1 and see that the
  managed-side verbosity increases regardless of whether a logger is
  attached — the flag affects the *filter*, not the output destination.
- Add `b.AddFilter("Aspose.LLM", LogLevel.Warning)` to the console
  flavour to suppress everything below warning from the SDK's namespace
  while keeping other sources at Debug.
- Replace the console formatter with `json`
  (`o => o.FormatterName = "json"`) and pipe output to `jq` for
  structured-log filtering.
