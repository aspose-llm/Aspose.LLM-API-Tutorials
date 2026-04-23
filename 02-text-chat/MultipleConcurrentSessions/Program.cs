using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Multiple concurrent sessions off one AsposeLLMApi instance.
//
// The facade enforces one *engine* per process, but one engine can own many
// independent *sessions*. Each session has its own chat history, its own
// cumulative context, and — demonstrated here — its own system prompt
// supplied at StartNewChatAsync time.
//
// IMPORTANT distinction: concurrent ≠ parallel. The native llama.cpp engine
// serialises token generation. Three sessions live at the same time, but
// at any given moment only one of them is generating. This pattern is what
// a chatbot backend uses to multiplex many users over one engine: cheap
// context switches between stateful conversations.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Multiple Concurrent Sessions");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
preset.ChatParameters.MaxTokens = 100;
using var api = new AsposeLLMApi(preset);

// Start three sessions, each with its own persona. The per-session ChatParameters
// is passed to StartNewChatAsync as part of a preset copy; everything else
// (context size, sampler) falls back to the default preset.
var chefId = await StartPersona(api,
    "You are a professional chef. Answer every question as a working restaurant chef would. Keep answers to one or two sentences.");
var coderId = await StartPersona(api,
    "You are a senior software engineer. Answer every question from a programming perspective. Keep answers to one or two sentences.");
var mathId = await StartPersona(api,
    "You are a pure mathematician. Answer every question with mathematical precision. Keep answers to one or two sentences.");

ConsoleHelpers.Info($"chef = {chefId}\ncoder = {coderId}\nmath = {mathId}\n");

// --- Round 1: the same question to each session ---------------------------
ConsoleHelpers.Section("Round 1 — same question, three personas");

const string q1 = "In one word: what is the best time of day to think hard?";
foreach (var (label, id) in new[] { ("chef", chefId), ("coder", coderId), ("math", mathId) })
{
    ConsoleHelpers.UserLine($"[to {label}] {q1}");
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(id, q1));
}

// --- Round 2: a follow-up only makes sense if history is per-session -------
ConsoleHelpers.Section("Round 2 — follow-up, proves each session has its own memory");

const string q2 = "Why did you say that? Give one sentence of reasoning, drawing on your profession.";
foreach (var (label, id) in new[] { ("chef", chefId), ("coder", coderId), ("math", mathId) })
{
    ConsoleHelpers.UserLine($"[to {label}] {q2}");
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(id, q2));
}

ConsoleHelpers.Info("\nAll three sessions kept their own history — notice how the Round 2 replies " +
                   "explain the *Round 1* answer that specific session gave.");

// --- helper -----------------------------------------------------------------
static async Task<string> StartPersona(AsposeLLMApi api, string systemPrompt)
{
    // A bare ChatParameters is enough to change persona; other knobs (context,
    // sampler) are inherited from the default preset the API was built with.
    var sessionPreset = new SmallModelPreset();
    sessionPreset.ChatParameters = new ChatParameters
    {
        SystemPrompt = systemPrompt,
        MaxTokens = 100,
    };
    return await api.StartNewChatAsync(sessionPreset);
}
