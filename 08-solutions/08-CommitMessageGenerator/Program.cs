using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Prompt;

// -----------------------------------------------------------------------------
// Commit Message Generator — read a git diff, emit a Conventional Commits
// message ready to paste into `git commit -m "$(...)"`.
//
// The flow is the simplest in this section: feed the diff to the model with
// a strict system prompt, print the reply verbatim. No JSON parsing, no
// audit trail. The "structure" is what's inside the message itself
// (subject + body + optional BREAKING CHANGE footer).
//
// Pipeline:
//   git diff --staged | dotnet run
//   git diff --staged | dotnet run -- --type feat --scope auth
//   dotnet run -- --in some.patch
//   dotnet run                 # falls back to the bundled sample.patch
//
// The drop-in commit pattern:
//   git commit -m "$(git diff --staged | dotnet run --project ...)"
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? inputPath = null;
string? scopeOverride = null;
string? typeOverride = null;
string? prefix = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--in" when i + 1 < args.Length:    inputPath = args[++i]; break;
        case "--scope" when i + 1 < args.Length: scopeOverride = args[++i]; break;
        case "--type" when i + 1 < args.Length:  typeOverride = args[++i]; break;
        case "--prefix" when i + 1 < args.Length: prefix = args[++i]; break;
        default: if (inputPath is null) inputPath = args[i]; break;
    }
}

// ---------- Resolve diff source --------------------------------------------
// Priority: explicit --in path > non-empty stdin > bundled sample.
// Console.IsInputRedirected is true in any non-interactive shell even when
// stdin is /dev/null, so we also confirm the redirect carries actual bytes.
string diff = "";
if (inputPath is not null)
{
    if (!File.Exists(inputPath)) { ConsoleHelpers.Fail($"Diff file not found: {inputPath}"); return 1; }
    diff = await File.ReadAllTextAsync(inputPath);
}
else if (Console.IsInputRedirected)
{
    var stdinText = await Console.In.ReadToEndAsync();
    if (!string.IsNullOrWhiteSpace(stdinText)) diff = stdinText;
}

if (string.IsNullOrWhiteSpace(diff))
{
    inputPath = Path.Combine(AppContext.BaseDirectory, "assets", "sample.patch");
    if (!File.Exists(inputPath)) { ConsoleHelpers.Fail($"Bundled sample missing at {inputPath}."); return 1; }
    diff = await File.ReadAllTextAsync(inputPath);
}

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Commit Message Generator");
TutorialEnvironment.ApplyLicense();
ConsoleHelpers.Info($"Diff source: {(inputPath is null ? "stdin" : inputPath)} ({diff.Length:N0} chars)");
if (scopeOverride is not null) ConsoleHelpers.Info($"Scope override: {scopeOverride}");
if (typeOverride is not null) ConsoleHelpers.Info($"Type override:  {typeOverride}");
if (prefix is not null) ConsoleHelpers.Info($"Prefix:         {prefix}");
Console.WriteLine();

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.SamplerParameters.Temperature = 0.3f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = SystemPrompt;
preset.ChatParameters.MaxTokens = 400;

using var api = new AsposeLLMApi(preset);

var userMessage =
    (typeOverride is not null  ? $"User-supplied type override: {typeOverride}\n" : "") +
    (scopeOverride is not null ? $"User-supplied scope override: {scopeOverride}\n" : "") +
    (prefix is not null        ? $"Subject MUST start with this prefix verbatim: {prefix} \n" : "") +
    "Generate one Conventional Commits message for the following diff:\n\n" +
    diff;

var reply = await api.SendMessageAsync(userMessage);
var message = NormaliseReply(reply);

ConsoleHelpers.Section("Generated commit message");
Console.WriteLine();
Console.WriteLine(message);
Console.WriteLine();

ConsoleHelpers.Info(
    "Pipe directly into a commit:\n" +
    "    git commit -m \"$(git diff --staged | dotnet run --project ...)\"\n" +
    "Always inspect first — the model is suggestive, not authoritative.");

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string NormaliseReply(string reply)
{
    // Trim outer whitespace.
    var s = reply.Trim();

    // Strip common markdown code fences the model occasionally adds despite
    // the system prompt forbidding them.
    if (s.StartsWith("```"))
    {
        var firstNewline = s.IndexOf('\n');
        if (firstNewline > 0) s = s[(firstNewline + 1)..];
    }
    if (s.EndsWith("```")) s = s[..^3].TrimEnd();

    // Drop "Here is a commit message:" style preambles. Heuristic: if the
    // first line doesn't look like `<type>(<scope>):` or `<type>:`, drop
    // until we find one that does.
    var lines = s.Split('\n');
    int startIdx = 0;
    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i].TrimStart();
        if (LooksLikeConventionalSubject(line)) { startIdx = i; break; }
    }
    if (startIdx > 0) s = string.Join('\n', lines[startIdx..]);

    return s.TrimEnd();
}

static bool LooksLikeConventionalSubject(string line)
{
    // Matches `feat:`, `feat(auth):`, `[PROJ-1] feat(auth):`, ...
    var idx = line.IndexOf(':');
    if (idx <= 0 || idx > 80) return false;
    var head = line[..idx];
    string[] types = {
        "feat", "fix", "refactor", "docs", "test", "chore",
        "perf", "style", "ci", "build", "revert"
    };
    foreach (var t in types)
    {
        if (head.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
}

internal static class Prompt
{
    public const string SystemPrompt = """
        You are a commit-message generator. The user supplies a unified-format
        git diff. You emit ONE Conventional Commits message, nothing else.

        Required structure:

            <type>(<scope>): <subject>             # ≤ 72 chars total
                                                    # blank line
            <body — 2-4 lines explaining WHAT and WHY>
                                                    # optional blank line
            <footer — BREAKING CHANGE: ..., or Closes #NN, optional>

        Allowed types: feat, fix, refactor, docs, test, chore, perf, style,
        ci, build, revert.

        Inference rules:
        1. Pick ONE type. If the diff mixes (say, fix + docs), choose the
           dominant one; mention the secondary in the body.
        2. Scope = a short tag derived from the file paths
           (`src/Auth/...` → `auth`). Lowercase, no spaces. If the change
           crosses many areas, omit the scope and use just `<type>: <subject>`.
        3. Subject in IMPERATIVE mood ("add", not "added"). No trailing period.
        4. Body: 2-4 lines explaining WHAT changed and WHY. Skip WHERE — the
           diff already shows that. Bullet sub-points are fine.
        5. If the diff introduces a backwards-incompatible change (removed
           public API, schema rename, config key change), add a
           `BREAKING CHANGE:` footer with a one-sentence migration hint.

        OUTPUT RULES — these are non-negotiable:
        - Output ONLY the message. No "Here is a commit message:", no
          explanation, no markdown code fences.
        - DO NOT add a `Co-Authored-By:` trailer of any kind, ever.
        - DO NOT add a "Generated with..." or "Written by..." footer.
        - DO NOT add any AI-attribution line. The user is the sole author.
        - If the user has supplied a type, scope, or prefix override, honour
          it verbatim.
        """;
}

