using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Three quick wins — each is a self-contained use of the SDK that shows off one
// capability at a time. Run the program and watch them execute in order.
//
// Win 1: one-shot Q&A.
// Win 2: multi-turn — the session remembers what you told it earlier.
// Win 3: swap the system prompt by starting a fresh session with a new preset.
//
// Reuse a single AsposeLLMApi instance across all three (the facade enforces
// one-instance-per-process, and loading the model is the expensive step).
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Quick Wins");

TutorialEnvironment.ApplyLicense();
using var api = new AsposeLLMApi(new SmallModelPreset());

// --- Win 1 ------------------------------------------------------------------
ConsoleHelpers.Section("Win 1 — one-shot question");

var one = "Name three primary colours. Comma-separated list only.";
ConsoleHelpers.UserLine(one);
ConsoleHelpers.AssistantLine(await api.SendMessageAsync(one));

// --- Win 2 ------------------------------------------------------------------
ConsoleHelpers.Section("Win 2 — multi-turn memory");

// Start a brand-new session so Win 1's history does not leak into this demo.
var memoryChat = await api.StartNewChatAsync();

var fact = "Remember this: my favourite colour is cerulean blue.";
ConsoleHelpers.UserLine(fact);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(memoryChat, fact));

var recall = "Earlier I told you my favourite colour. What was it? One word.";
ConsoleHelpers.UserLine(recall);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(memoryChat, recall));

// --- Win 3 ------------------------------------------------------------------
ConsoleHelpers.Section("Win 3 — swap the system prompt");

// A preset is just a parameter bag; you can mutate a fresh copy before passing
// it to StartNewChatAsync to change persona / limits for the next session.
var pirate = new SmallModelPreset();
pirate.ChatParameters.SystemPrompt =
    "You are a seasoned pirate captain. Always speak in pirate voice, use nautical metaphors, and keep replies under two sentences.";

var pirateChat = await api.StartNewChatAsync(pirate);

var q = "How should I start my day to be productive?";
ConsoleHelpers.UserLine(q);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(pirateChat, q));

ConsoleHelpers.Info("\nAll three wins done — see README.md for what to try next.");
