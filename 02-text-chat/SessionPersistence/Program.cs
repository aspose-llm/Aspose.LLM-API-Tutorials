using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Session persistence — save a conversation, throw everything away, load it
// back in a fresh engine, continue as if nothing happened.
//
// The flow mimics a real app: first run establishes a fact and saves to disk;
// a later run (simulated here by disposing & re-creating the AsposeLLMApi)
// reloads and confirms the fact is still there.
//
// Note: only one AsposeLLMApi may exist per process. We wrap each phase in its
// own `using` block so the first instance is fully disposed before the second
// is constructed. This is the same constraint a real server that recycles
// sessions between requests would respect.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Session Persistence");

TutorialEnvironment.ApplyLicense();

// Put the saved session next to the binary. Real apps would pick a durable
// location (appdata / user profile); /tmp is fine for a demo.
var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
Directory.CreateDirectory(sessionsDir);
var sessionFile = Path.Combine(sessionsDir, "persistence-demo.session");

// --- Phase 1: establish a fact and save ------------------------------------
ConsoleHelpers.Section("Phase 1 — write a fact, save, dispose");

string originalSessionId;
{
    using var api = new AsposeLLMApi(NewPreset());
    originalSessionId = await api.StartNewChatAsync();

    const string tellFact =
        "Please remember this for later: my apartment key hook is shaped like a brass anchor and lives next to the bookshelf.";
    ConsoleHelpers.UserLine(tellFact);
    ConsoleHelpers.AssistantLine(
        await api.SendMessageToSessionAsync(originalSessionId, tellFact));

    api.SaveChatSession(originalSessionId, sessionFile);
    ConsoleHelpers.Info($"[saved session {originalSessionId} → {sessionFile}]");
}   // <- dispose: native engine unloaded, singleton slot freed.

if (!File.Exists(sessionFile) || new FileInfo(sessionFile).Length == 0)
{
    ConsoleHelpers.Fail($"Expected saved file at {sessionFile}, but it is missing or empty.");
    return 1;
}
ConsoleHelpers.Info($"[on-disk size: {new FileInfo(sessionFile).Length:N0} bytes]");

// --- Phase 2: load the saved session and continue --------------------------
ConsoleHelpers.Section("Phase 2 — fresh engine, load, recall");

{
    using var api = new AsposeLLMApi(NewPreset());

    var reloadedId = await api.LoadChatSession(sessionFile);
    ConsoleHelpers.Info(
        $"[loaded session as {reloadedId} (was {originalSessionId} before save)]");

    const string question =
        "What did I tell you about the object next to my bookshelf? Be specific about its shape and material.";
    ConsoleHelpers.UserLine(question);
    ConsoleHelpers.AssistantLine(
        await api.SendMessageToSessionAsync(reloadedId, question));
}

// Clean up so repeated runs don't leave files lying around. Real apps would
// keep the file — the point of persistence is that it outlives the process.
try { File.Delete(sessionFile); } catch { /* best-effort */ }

ConsoleHelpers.Info("\nSession persistence demo complete.");
return 0;

// Local helper — a fresh preset each phase, so we're not accidentally sharing
// state through mutated fields.
static SmallModelPreset NewPreset()
{
    var p = new SmallModelPreset();
    p.ChatParameters.SystemPrompt =
        "You are a precise assistant that remembers concrete facts the user tells you and recalls them verbatim when asked.";
    p.ChatParameters.MaxTokens = 200;
    return p;
}
