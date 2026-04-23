using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// ASP.NET Core integration — serve the LLM as an HTTP chat backend.
//
// Architecture:
//   - Register a singleton AsposeLLMApi built from a preset. The facade
//     enforces one-instance-per-process, which aligns naturally with a
//     web host (one process, one loaded model).
//   - Inject the facade into minimal-API endpoint handlers.
//   - Serialise concurrent requests with SemaphoreSlim(1,1). The underlying
//     native engine can't generate for two contexts in parallel, and the
//     facade delegates to it without its own locking — so we gate at the
//     endpoint.
//
// Why the facade and not the low-level Engine + AddLlamaServices? The public
// Aspose.LLM NuGet exposes only the facade surface today. If/when the SDK
// also surfaces Aspose.LLM.Core types through NuGet you could switch to
// builder.Services.AddLlamaServices(preset) and inject Engine directly.
// -----------------------------------------------------------------------------

TutorialEnvironment.ApplyLicense();

var builder = WebApplication.CreateBuilder(args);

var preset = new SmallModelPreset();
preset.ChatParameters.MaxTokens = 200;
preset.ChatParameters.SystemPrompt =
    "You are a friendly HTTP chat assistant. Keep replies under three sentences.";

// One AsposeLLMApi per process; ASP.NET Core's DI container holds it.
builder.Services.AddSingleton(new AsposeLLMApi(preset));

// One gate to serialise every call that touches the native engine.
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));

var app = builder.Build();

// Dispose the native engine when the host shuts down.
app.Lifetime.ApplicationStopping.Register(() =>
    app.Services.GetRequiredService<AsposeLLMApi>().Dispose());

// Landing page — curl-ready cheatsheet, so hitting / in a browser shows
// what to do instead of returning 404.
app.MapGet("/", () => Results.Text(@"Aspose.LLM chat server is up.

Try:

  # Start a new session
  curl -X POST http://localhost:5080/sessions

  # Send a message (pass the sessionId from the /sessions response)
  curl -X POST http://localhost:5080/chat \
       -H 'Content-Type: application/json' \
       -d '{""sessionId"":""<id>"",""message"":""Hi, who are you?""}'
", "text/plain"));

// POST /sessions → { sessionId }
app.MapPost("/sessions", async (AsposeLLMApi api, SemaphoreSlim gate, CancellationToken ct) =>
{
    await gate.WaitAsync(ct);
    try
    {
        var sessionId = await api.StartNewChatAsync();
        return Results.Ok(new { sessionId });
    }
    finally { gate.Release(); }
});

// POST /chat → { sessionId, reply }. sessionId is optional — one-shot Q&A
// stays one HTTP call if you omit it.
app.MapPost("/chat", async (
    ChatRequest req,
    AsposeLLMApi api,
    SemaphoreSlim gate,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });

    await gate.WaitAsync(ct);
    try
    {
        var sessionId = req.SessionId;
        if (string.IsNullOrEmpty(sessionId))
            sessionId = await api.StartNewChatAsync();

        var reply = await api.SendMessageToSessionAsync(
            sessionId,
            req.Message,
            media: null,
            cancellationToken: ct);

        return Results.Ok(new { sessionId, reply });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = $"unknown sessionId '{req.SessionId}'" });
    }
    catch (OperationCanceledException)
    {
        // Client went away mid-generation. 499 is the common "client closed
        // request" code in nginx-style reverse proxy stacks.
        return Results.StatusCode(499);
    }
    finally { gate.Release(); }
});

ConsoleHelpers.Banner("Aspose.LLM — ASP.NET Core chat server");
ConsoleHelpers.Info("Listening on http://localhost:5080 — model-loading begins on first /chat call.");
ConsoleHelpers.Info("Press Ctrl+C to stop.\n");

app.Run("http://localhost:5080");

// Request DTO. `record` with positional params lets System.Text.Json
// deserialise straight from the request body.
public sealed record ChatRequest(string? SessionId, string Message);
