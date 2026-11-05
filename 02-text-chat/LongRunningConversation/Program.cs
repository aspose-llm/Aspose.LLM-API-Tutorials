using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Long-running conversation — what happens when the context fills up.
//
// Every chat session has a finite KV cache sized by ContextParameters.ContextSize.
// Once the history outgrows it, the SDK applies a CacheCleanupStrategy that
// drops older messages. Facts from the far past disappear; the model starts
// contradicting things it "knew" 30 turns ago.
//
// This tutorial uses a deliberately tiny context (1024 tokens) so cleanup
// fires after ~8-10 turns instead of ~30, and runs two scripted loops to
// compare two cleanup strategies head-to-head:
//
//   Part A — RemoveOldestMessages: classic FIFO ring buffer.
//   Part B — KeepSystemPromptAndHalf: keeps the prompt and a useful prefix.
//
// At the end, each session is quizzed about an early fact and a late fact.
// The difference in recall tells you which strategy suits which workload.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Long-Running Conversation");

TutorialEnvironment.ApplyLicense();

using var api = new AsposeLLMApi(NewPreset(CacheCleanupStrategy.RemoveOldestMessages));

// --- Part A -----------------------------------------------------------------
await RunPart(
    "A — RemoveOldestMessages (FIFO)",
    api,
    CacheCleanupStrategy.RemoveOldestMessages);

// --- Part B -----------------------------------------------------------------
await RunPart(
    "B — KeepSystemPromptAndHalf (preserve a useful prefix)",
    api,
    CacheCleanupStrategy.KeepSystemPromptAndHalf);

ConsoleHelpers.Info(
    "\nSame loop, two strategies. FIFO drops the earliest fact once the buffer " +
    "rolls over; keeping the prompt plus a larger prefix is the more suitable " +
    "choice for this scenario.");

// ---------------------------------------------------------------------------

static async Task RunPart(string title, AsposeLLMApi api, CacheCleanupStrategy strategy)
{
    ConsoleHelpers.Section($"Part {title}");

    var preset = NewPreset(strategy);
    var sid = await api.StartNewChatAsync(preset);

    // Turn 1 — establish a concrete, memorable fact. FIFO will eventually
    // evict it; the prefix-preserving strategy has a better chance to retain
    // enough early context for the final quiz.
    const string earlyFact =
        "Please remember: I own a small bookstore on Volkov Street and its mascot is a one-eared orange cat named Hemingway.";
    ConsoleHelpers.UserLine($"[t01] {earlyFact}");
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sid, earlyFact));

    // Turns 2-11 — filler that pushes the context past the 1024-token window.
    // Each question is unrelated to the bookstore fact.
    var filler = new[]
    {
        "In one sentence, why is sea water salty?",
        "Give one tip for a beginner learning to ride a bicycle.",
        "Explain photosynthesis briefly.",
        "Name a common pitfall when baking bread at home.",
        "Why do leaves change colour in autumn?",
        "Recommend one strategy for better sleep.",
        "What's a notable feature of Saturn?",
        "Describe the purpose of a sourdough starter.",
        "Suggest one habit for improving focus.",
        "What is the capital of Portugal?",
    };
    for (int i = 0; i < filler.Length; i++)
    {
        var turn = $"t{i + 2:00}";
        ConsoleHelpers.UserLine($"[{turn}] {filler[i]}");
        ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sid, filler[i]));
    }

    // Final turn — quiz the model about turn 1. If cleanup dropped it, the
    // model will either confess it forgot or invent something.
    const string earlyQuiz =
        "Earlier I told you what street my bookstore is on and what my cat is called. " +
        "What were those two facts? Answer in one sentence.";
    ConsoleHelpers.UserLine("[quiz] " + earlyQuiz);
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sid, earlyQuiz));
}

static SmallModelPreset NewPreset(CacheCleanupStrategy strategy)
{
    var p = new SmallModelPreset();

    // Very tight window — 512 tokens forces cleanup after ~4-5 turns of
    // filler. Production apps should use the largest context the model and
    // hardware can handle; this demo specifically engineers overflow.
    p.ContextParameters.ContextSize = 512;
    p.ContextParameters.NBatch = 256;
    p.ContextParameters.NUbatch = 128;

    // Generous per-turn budget so replies actually fill the buffer. A small
    // MaxTokens value keeps every turn tiny and fails to push the buffer
    // past its limit.
    p.ChatParameters.MaxTokens = 160;
    p.ChatParameters.SystemPrompt =
        "You are a concise assistant. Answer each question in one or two sentences at most. " +
        "Remember specific facts the user gives you and recall them accurately when asked.";
    p.ChatParameters.CacheCleanupStrategy = strategy;

    return p;
}
