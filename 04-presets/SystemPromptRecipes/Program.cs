using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// System prompt recipes.
//
// The system prompt is the single cheapest lever for shaping model output —
// cheaper than fine-tuning, cheaper than sampler tuning, cheaper than swapping
// the model. This tutorial runs the same question through five different
// personas built from five different system prompts and prints the replies
// side by side.
//
// One AsposeLLMApi instance; five fresh sessions (one per recipe). Temperature
// is turned down a touch so the recipe differences dominate the noise.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — System Prompt Recipes");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
preset.SamplerParameters.Temperature = 0.4f;     // crisper, less random
preset.ChatParameters.MaxTokens = 180;
using var api = new AsposeLLMApi(preset);

const string Question =
    "How does a computer remember things when you turn it off?";

var recipes = new (string Name, string SystemPrompt)[]
{
    ("Terse",
     "Answer in one sentence. No preamble. No caveats. Full stop."),

    ("Structured",
     "Always reply in exactly three lines, each beginning with a fixed label:\n" +
     "Answer: <one sentence>\n" +
     "Why: <one sentence>\n" +
     "Caveat: <one sentence, or 'none'>"),

    ("Teacher",
     "Explain as if to a curious 10-year-old. Use simple words, one short " +
     "paragraph, and one concrete everyday analogy. Never use jargon."),

    ("Devil's advocate",
     "For every claim you'd naturally make, first state it, then immediately " +
     "give one reason a skeptic might push back on it. Keep each push-back " +
     "to one sentence. Be pedantic but fair."),

    ("Translator",
     "Reply ONLY in French. Never use English. Keep it to two sentences."),
};

foreach (var (name, systemPrompt) in recipes)
{
    ConsoleHelpers.Section($"Recipe — {name}");

    // Fresh preset per recipe so system prompts don't leak between them.
    // Everything else (model, sampler, cache path) inherits from SmallModelPreset.
    var perRecipe = new SmallModelPreset();
    perRecipe.SamplerParameters.Temperature = 0.4f;
    perRecipe.ChatParameters.MaxTokens = 180;
    perRecipe.ChatParameters.SystemPrompt = systemPrompt;

    var sid = await api.StartNewChatAsync(perRecipe);
    ConsoleHelpers.UserLine(Question);
    ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sid, Question));
}

ConsoleHelpers.Info("\nFive replies, one question. The only thing that changed was the system prompt.");
