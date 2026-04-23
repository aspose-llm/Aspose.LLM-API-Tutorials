using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Hello, LLM!
//
// The smallest end-to-end use of the Aspose.LLM facade:
//   1. Apply a licence (mandatory — SendMessageAsync throws without it).
//   2. Build a preset that describes which model to load and how.
//   3. Create one AsposeLLMApi instance and send one message.
//
// On first run the SDK downloads the GGUF model (~400 MB for the small preset)
// and the matching native llama.cpp binary for your platform. Subsequent runs
// reuse both from the cache and start in seconds.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Hello World");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();
ConsoleHelpers.Info(
    $"Model: {preset.BaseModelSourceParameters.HuggingFaceRepoId}/" +
    $"{preset.BaseModelSourceParameters.HuggingFaceFileName}");
ConsoleHelpers.Info($"Cache: {preset.EngineParameters.ModelCachePath}");

// Only one AsposeLLMApi instance may exist per process; `using` disposes the
// native engine promptly when we're done.
using var api = new AsposeLLMApi(preset);

const string question = "In one sentence: what is the capital of France?";
ConsoleHelpers.UserLine(question);

var reply = await api.SendMessageAsync(question);
ConsoleHelpers.AssistantLine(reply);
