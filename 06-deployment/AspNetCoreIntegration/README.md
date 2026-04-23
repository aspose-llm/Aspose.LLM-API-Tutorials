# ASP.NET Core Integration

A minimal-API HTTP chat server that runs the Aspose.LLM SDK behind two
endpoints. ~100 lines of code, zero ceremony, ready for curl/Postman.

## What it demonstrates

- **Registering `AsposeLLMApi` as a DI singleton** — the facade's one-per-
  process constraint fits a web host perfectly, and the container handles
  construction and disposal for you.
- **Injecting the facade** straight into minimal-API endpoint handlers.
- **Serialising concurrent requests** via a `SemaphoreSlim(1, 1)`. The
  underlying native engine generates one token stream at a time; ASP.NET
  Core happily multiplexes requests, so the gate is essential.
- **Cancellation wiring** — `HttpContext.RequestAborted` flows into
  `api.SendMessageToSessionAsync(..., cancellationToken: ct)`; a client
  that hangs up cancels the generation.
- **Graceful shutdown** — `IHostApplicationLifetime.ApplicationStopping`
  disposes the facade so the native engine unloads cleanly.

## Endpoints

| Method | Path         | Body                                      | Response                               |
| ------ | ------------ | ----------------------------------------- | -------------------------------------- |
| GET    | `/`          | —                                         | `text/plain` cheatsheet                |
| POST   | `/sessions`  | —                                         | `{"sessionId":"..."}`                  |
| POST   | `/chat`      | `{"sessionId":"...","message":"..."}`      | `{"sessionId":"...","reply":"..."}`    |

`/chat` accepts an optional `sessionId`. If absent, the server mints one on
the fly and returns it in the response — so a one-shot Q&A stays one HTTP
call. Pass the returned id back on subsequent requests to keep the
conversation going.

## Run it

```bash
cd 06-deployment/AspNetCoreIntegration
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

You'll see:

```
==========================================
  Aspose.LLM — ASP.NET Core chat server
==========================================
[licence] applied from /.../License.lic
Listening on http://localhost:5080 — model-loading begins on first /chat call.
Press Ctrl+C to stop.
```

The server is up but **no model is loaded yet** — loading happens lazily on
the first `/chat` call, so the first request pays the cold-start cost and
every call after it is fast.

## Try it from another terminal

```bash
# one-shot: no sessionId, one call
curl -X POST http://localhost:5080/chat \
     -H 'Content-Type: application/json' \
     -d '{"message":"In one sentence: what is the capital of France?"}'

# multi-turn: start a session, then two follow-ups on it
SID=$(curl -sX POST http://localhost:5080/sessions | jq -r .sessionId)

curl -X POST http://localhost:5080/chat \
     -H 'Content-Type: application/json' \
     -d "{\"sessionId\":\"$SID\",\"message\":\"My name is Alex.\"}"

curl -X POST http://localhost:5080/chat \
     -H 'Content-Type: application/json' \
     -d "{\"sessionId\":\"$SID\",\"message\":\"What did I tell you my name was?\"}"
```

## Why the facade and not `AddLlamaServices`

The SDK ships a DI extension method `IServiceCollection.AddLlamaServices(preset)`
that registers the low-level `Engine` + its dependencies. On paper it's the
more idiomatic ASP.NET Core surface. In practice the **public Aspose.LLM
NuGet package** (v26.3.0) only exposes the `AsposeLLMApi` facade — the
`Aspose.LLM.Core` namespace containing `Engine` and `AddLlamaServices` isn't
surfaced through NuGet. So this tutorial uses the facade as a DI-managed
singleton, which produces an equivalent result for a server that runs one
model per process.

If/when the SDK surfaces `Aspose.LLM.Core` through NuGet, you'd replace the
two `AddSingleton` calls with:

```csharp
builder.Services.AddLlamaServices(preset);
// ...and inject `Engine` into the endpoint handlers instead of AsposeLLMApi.
```

Everything else (the gate, cancellation, shutdown) stays the same.

## Production considerations

This tutorial is intentionally minimal. For production:

- **Authentication.** Add `app.UseAuthentication()` + `app.UseAuthorization()`
  and put an `[Authorize]` attribute on `/chat`. Never expose a raw chat
  endpoint.
- **Rate limiting.** Add `builder.Services.AddRateLimiter(...)`. A loaded
  LLM is a shared expensive resource; one chatty client can starve the rest.
- **Session storage.** Today the Engine holds session state in memory; it
  disappears on restart. Pair with
  [`SessionPersistence`](../../02-text-chat/SessionPersistence/) and
  persist sessions to disk or Redis between turns for durability.
- **Streaming.** The current endpoint returns the full reply in one response.
  For streamed tokens, you'd want a different SDK surface — the current
  facade/engine returns whole strings, not token streams. File a feature
  request if you need this.
- **Multiple replicas.** Because Engine is not thread-safe and one process
  can only load one model, scale horizontally — N processes on N CPUs/GPUs
  behind a load balancer that's either sticky-by-sessionId or seeds sessions
  from a shared store.

## Things to try

- Run the server with `ASPNETCORE_URLS=http://+:80` and hit it from another
  machine on the LAN.
- Add `Results.Stream(...)` around a fake tokenised output and watch a
  browser-side `fetch` reader receive the chunks.
- Swap the preset for `Qwen25Preset` and time a single `/chat` call after
  the warmup — typically ~1-2 s/token on CPU, far faster on GPU.
- Add a `/sessions/{id}` DELETE endpoint using
  `ForceCacheCleanup(CacheCleanupStrategy.KeepSystemPromptOnly)` to prune
  long-idle conversations.
