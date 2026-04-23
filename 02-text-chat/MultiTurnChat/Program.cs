using Aspose.LLM;
using Aspose.LLM.Abstractions.Models;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Multi-turn chat — proving the session is stateful.
//
// We run a scripted conversation: the user tells the model three facts, then
// asks questions that can only be answered by recalling those facts. If the
// model responds correctly it means the SDK is threading the full chat history
// through each generation — exactly what the session abstraction promises.
//
// Bonus: the second half of the program shows a different way to set up
// history — pre-seeding ChatParameters.History before starting a session.
// That's how you import a conversation from another system (database,
// previous run, logs) without replaying it turn by turn.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Multi-Turn Chat");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
preset.ChatParameters.SystemPrompt =
    "You are a precise, friendly assistant. You remember any facts the user tells you and recall them accurately when asked.";
preset.ChatParameters.MaxTokens = 160;

using var api = new AsposeLLMApi(preset);

// --- Part A: build history turn by turn ------------------------------------
ConsoleHelpers.Section("Part A — accumulate history across turns");

var chatA = await api.StartNewChatAsync();

// Lines the user says; each one is sent and the reply is printed. By the time
// we reach turn 4 the model has all three facts in its context window and can
// answer the recall question.
var turns = new[]
{
    "My name is Alex.",
    "I live in Novosibirsk.",
    "My favourite programming language is C#.",
    "Recall the three facts I just told you, as a numbered list.",
};

foreach (var line in turns)
{
    ConsoleHelpers.UserLine(line);
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(chatA, line));
}

// --- Part B: pre-seed history from outside the SDK -------------------------
ConsoleHelpers.Section("Part B — pre-seed history via ChatParameters");

// Imagine these three turns came from a database or a prior run of the app.
// You hand them to the SDK before starting the session so the model behaves
// as if the conversation was already in progress.
var seeded = new SmallModelPreset();
seeded.ChatParameters.SystemPrompt = preset.ChatParameters.SystemPrompt;
seeded.ChatParameters.MaxTokens = 160;
seeded.ChatParameters.History = new List<ChatMessage>
{
    ChatMessage.CreateUserMessage("I'm building a small data-quality tool in Go."),
    ChatMessage.CreateAssistantMessage("Sounds fun — what's the hardest part so far?"),
    ChatMessage.CreateUserMessage("Deciding how to represent schema drift over time."),
};

var chatB = await api.StartNewChatAsync(seeded);

// The first message the model sees is the seeded one; our next question
// lands in the *fourth* turn of a conversation it never actually had.
const string followUp = "Given what I said earlier about my project, suggest one concrete next step.";
ConsoleHelpers.UserLine(followUp);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(chatB, followUp));

ConsoleHelpers.Info("\nDone — see README.md for what to change and try.");
