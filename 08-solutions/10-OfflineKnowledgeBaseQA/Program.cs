using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static KbPrompt;

// -----------------------------------------------------------------------------
// Offline Knowledge Base Q&A — answer natural-language questions about a
// fixed corpus of internal docs, with inline filename citations. The whole
// corpus goes into one prompt (no vector store, no retrieval pipeline);
// works for small KBs that fit in 32k context (~50 pages of plain text).
//
// Pattern intentionally minimal:
//   1. Walk the corpus directory, pull every .md / .txt.
//   2. Concatenate with `=== <filename> ===` delimiters.
//   3. Prefix the question, hand to Qwen 2.5 7B at full 32k context.
//   4. Print the answer verbatim.
//
// The model is instructed to refuse-with-grace ("I could not find that in
// the corpus") rather than fabricate. The system prompt also enforces an
// inline-citation style: every factual claim ends with `[filename.md]` so
// the user can audit the source.
//
// CLI:
//   dotnet run                                  # canned demo against bundled corpus
//   dotnet run -- "What is the refund window for enterprise?"
//   dotnet run -- --corpus ./docs --style footnotes "..."
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? corpusDir = null;
string style = "inline";
string? question = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--corpus" when i + 1 < args.Length: corpusDir = args[++i]; break;
        case "--style" when i + 1 < args.Length:  style = args[++i].ToLowerInvariant(); break;
        case "--q" when i + 1 < args.Length:      question = args[++i]; break;
        default: question ??= args[i]; break;
    }
}

if (style is not ("inline" or "footnotes"))
{
    ConsoleHelpers.Fail($"--style must be 'inline' or 'footnotes' (got '{style}').");
    return 1;
}

corpusDir ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-corpus");
if (!Directory.Exists(corpusDir))
{
    ConsoleHelpers.Fail($"Corpus directory not found: {corpusDir}");
    return 1;
}

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Offline KB Q&A");
TutorialEnvironment.ApplyLicense();

var (corpus, fileCount) = LoadCorpus(corpusDir);
var estimatedTokens = corpus.Length / 4; // crude: ~4 chars/token on English

ConsoleHelpers.Info($"Corpus:   {corpusDir} ({fileCount} files, {corpus.Length:N0} chars, ~{estimatedTokens:N0} tokens)");
ConsoleHelpers.Info($"Style:    {style}");

const int ContextBudget = 32768;
const int OutputBudget = 800;
const int SafetyMargin = 1500;     // system prompt + chat template + question
if (estimatedTokens + OutputBudget + SafetyMargin > ContextBudget)
{
    ConsoleHelpers.Fail(
        $"Corpus too large for one-shot context window (~{estimatedTokens:N0} tokens vs. " +
        $"{ContextBudget - OutputBudget - SafetyMargin:N0} available). " +
        "For larger KBs you need real retrieval — see the README's RAG pointer.");
    return 1;
}

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.ContextParameters.ContextSize = ContextBudget;
preset.SamplerParameters.Temperature = 0.2f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = SystemPrompt;
preset.ChatParameters.MaxTokens = OutputBudget;

using var api = new AsposeLLMApi(preset);

// Canned demo questions when none supplied. Three deliberately chosen:
// two answerable from the bundled corpus, one not.
var questions = question is not null
    ? new[] { question }
    : new[]
    {
        "What is the refund policy for enterprise subscribers?",
        "Who approves vendor onboarding requests?",
        "What is our company's policy on cryptocurrency payments?"  // not in corpus
    };

foreach (var q in questions)
{
    ConsoleHelpers.Section($"Question: {q}");
    Console.WriteLine();

    var userMessage =
        $"Style: {style}\n\n" +
        $"Question: {q}\n\n" +
        $"Corpus follows. Use ONLY this corpus to answer; cite the filename in [brackets] for every claim.\n\n" +
        $"--- CORPUS START ---\n{corpus}\n--- CORPUS END ---";

    // Fresh session per question — keeps the corpus paid-for once but
    // doesn't carry contamination between unrelated queries.
    await api.StartNewChatAsync();
    var reply = await api.SendMessageAsync(userMessage);

    Console.WriteLine(reply.Trim());
    Console.WriteLine();
}

ConsoleHelpers.Info(
    "Citations are filenames the model claims to have used. Spot-check by\n" +
    "opening the cited file. If a citation references a file that doesn't\n" +
    "exist in your corpus, treat the whole answer as suspect.");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static (string Text, int FileCount) LoadCorpus(string dir)
{
    var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
        .Where(p =>
        {
            var name = Path.GetFileName(p);
            if (name.StartsWith(".")) return false;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            return ext is ".md" or ".txt";
        })
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();

    var sb = new System.Text.StringBuilder();
    foreach (var path in files)
    {
        var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
        sb.AppendLine($"=== {rel} ===");
        sb.AppendLine(File.ReadAllText(path).TrimEnd());
        sb.AppendLine();
    }
    return (sb.ToString(), files.Count);
}

internal static class KbPrompt
{
    public const string SystemPrompt = """
        You are a question-answering assistant grounded in a fixed corpus of
        documents the user pastes in their message. The corpus is delimited
        by `--- CORPUS START ---` and `--- CORPUS END ---`, with each
        constituent file introduced by a `=== filename ===` header.

        Hard rules:

        1. Answer ONLY using information from the corpus. If the corpus
           doesn't contain the answer, say so plainly:
             "I could not find that in the corpus. Sources consulted:
              <comma-separated filenames>."
           Do not draw on outside knowledge.

        2. Cite. Every factual claim in your answer ends with the
           filename in square brackets, e.g. "Refunds are 30 days
           [refund-policy.md]." If a single sentence draws from two
           files, cite both: "[file1.md] [file2.md]".

        3. Quote sparingly. Paraphrase when you can; quote verbatim
           only when the exact wording matters (legal terms, prices).

        4. Conflict surfacing. If two documents disagree, present both:
           "Doc A says X [a.md]; Doc B says Y [b.md]; these conflict."

        5. Style. The user's message specifies `Style: inline` or
           `Style: footnotes`.
           - inline:    `<sentence>. [filename.md]`
           - footnotes: superscript-numeric markers in the prose, then
                        a `---` separator line, then a numbered list:
                        `1. filename.md`.

        6. Brevity. 2-4 sentences typical. Long only when the question
           genuinely requires it.

        7. No preamble. Start directly with the answer. Do NOT start
           with "Sure," "Of course," or "Based on the corpus,".
        """;
}
