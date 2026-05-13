using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static Prompts;

// -----------------------------------------------------------------------------
// Email Reply Assistant — turn a one-line intent into a paste-ready reply.
//
// You supply: the incoming email body, plus a short directive describing how
// you want to respond ("decline politely and suggest mid-May instead",
// "ask for pricing on the Pro tier", "accept but push the date to Monday").
// The model produces the full reply — Subject line, salutation matched to
// the incoming greeting, 2-4 paragraphs, sign-off — ready to paste into a
// mail client.
//
// Why a local model. Inboxes mix routine scheduling chatter with sensitive
// material: deal terms, employee complaints, client roadmaps. The whole
// premise of this tutorial is that the message text never leaves your
// machine. Cloud APIs are off the table.
//
// Design notes:
//   * One-shot generation, not a chat. AsposeLLMApi.SendMessageAsync sends
//     a single user turn; the system prompt carries the persona. There's
//     no transcript to preserve between runs — every reply is independent.
//   * Tone via system prompt, not separate models. The same Qwen 2.5 7B
//     preset handles formal/friendly/concise replies; only the system
//     prompt's tone clause changes per invocation.
//   * Temperature 0.4 — the spec recommendation. Higher than the
//     extraction tutorials (0.1) because reply tone benefits from a touch
//     of variety; low enough that re-running the same inputs doesn't swing
//     between "curt" and "effusive".
//   * No JSON, no line-based list, no parsing. The output IS the deliverable.
//     We print the model's reply verbatim and stop.
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? intent = null;
string tone = "formal";
string relationship = "client";
string? inputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--intent" when i + 1 < args.Length:
            intent = args[++i];
            break;
        case "--tone" when i + 1 < args.Length:
            tone = args[++i].ToLowerInvariant();
            break;
        case "--relationship" when i + 1 < args.Length:
            relationship = args[++i].ToLowerInvariant();
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

if (tone is not ("formal" or "friendly" or "concise"))
{
    ConsoleHelpers.Fail($"Unknown --tone '{tone}'. Use formal | friendly | concise.");
    return 1;
}

if (relationship is not ("client" or "colleague" or "vendor"))
{
    ConsoleHelpers.Fail($"Unknown --relationship '{relationship}'. Use client | colleague | vendor.");
    return 1;
}

// Default to the bundled sample next to the executable so `dotnet run` with
// no arguments still produces a meaningful demo.
inputPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "incoming-sample.txt");
if (!File.Exists(inputPath))
{
    ConsoleHelpers.Fail($"Input file not found: {inputPath}");
    return 1;
}

// If the user didn't pass --intent, fall back to a directive that fits the
// bundled sample. Keeps the no-arguments demo paste-able into a slide.
intent ??= "decline politely, cite scheduling conflicts on our side, " +
           "and suggest the first week of June as an alternative kick-off slot";

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — Email Reply Assistant");
TutorialEnvironment.ApplyLicense();

var incoming = await File.ReadAllTextAsync(inputPath);
ConsoleHelpers.Info($"Incoming:     {inputPath} ({incoming.Length:N0} chars)");
ConsoleHelpers.Info($"Intent:       {intent}");
ConsoleHelpers.Info($"Tone:         {tone}");
ConsoleHelpers.Info($"Relationship: {relationship}\n");

// Model choice. Qwen 2.5 7B (Qwen25Preset) is the recommended preset for this
// task — the cross-reference reasoning needed to pick up the sender's name
// from a "Hi Alex," line and mirror its register in the salutation falls
// apart on the 0.5B preset, which produces blander, more boilerplate prose.
// Both presets share the tutorial cache, so the model is already on disk if
// any prior tutorial has used it.
//
// Sampler. Temperature 0.4 — middle ground between the deterministic
// extractors (0.1) and the chat tutorials (0.6+). Reply tone gets a touch of
// variation without regenerations swinging from curt to effusive on the
// same inputs. Fixed seed keeps re-runs reproducible on a given host.
var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.SamplerParameters.Temperature = 0.4f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = BuildSystemPrompt(tone, relationship);
preset.ChatParameters.MaxTokens = 400;  // 3-paragraph reply with headroom.

using var api = new AsposeLLMApi(preset);

// User message format: clearly delimit the incoming email from the intent
// directive so the model never confuses the two. The triple-dash markers
// also discourage the model from quoting the input back wholesale.
var userMessage =
    $"Incoming email — read but do not repeat quoted text in your reply:\n" +
    $"----\n{incoming.TrimEnd()}\n----\n\n" +
    $"Intent: {intent}";

var reply = await api.SendMessageAsync(userMessage);

// ---------- Output ----------------------------------------------------------
// The reply IS the deliverable. Trim trailing whitespace, print it verbatim,
// done. No parser, no JSON, no audit file.
ConsoleHelpers.Section("Draft reply");
Console.WriteLine();
Console.WriteLine(reply.Trim());
Console.WriteLine();

return 0;

// ---------------------------------------------------------------------------
// Prompts
// ---------------------------------------------------------------------------

internal static class Prompts
{
    public static string BuildSystemPrompt(string tone, string relationship)
    {
        // Tone clause — keeps the body language consistent. "Formal" is the
        // safe default for client-facing mail; "friendly" warms it up for
        // colleagues; "concise" strips it down to two short paragraphs.
        var toneClause = tone switch
        {
            "formal" =>
                "Write in a formal, professional register. Complete sentences, " +
                "no contractions, no exclamation marks. Polite but not effusive.",
            "friendly" =>
                "Write in a warm, friendly register. Contractions are fine. " +
                "One light human touch is welcome (a brief acknowledgement, a " +
                "sentence of context) but stay professional.",
            "concise" =>
                "Write in a concise register. Two short paragraphs maximum. " +
                "No filler, no preamble. Get to the point in the first sentence.",
            _ => throw new ArgumentOutOfRangeException(nameof(tone))
        };

        // Sign-off clause. Different relationships get different sign-offs
        // (and the body register shifts subtly with them — colleagues get
        // first-name salutations, clients and vendors get titles when
        // available).
        var signOffClause = relationship switch
        {
            "client" =>
                "End with 'Best regards,' on its own line, followed by " +
                "'<your signature>' on the next line.",
            "colleague" =>
                "End with 'Best,' on its own line, followed by " +
                "'<your signature>' on the next line.",
            "vendor" =>
                "End with 'Kind regards,' on its own line, followed by " +
                "'<your signature>' on the next line.",
            _ => throw new ArgumentOutOfRangeException(nameof(relationship))
        };

        return $$"""
            You are an email-reply assistant. The user will give you the full
            text of an incoming email and a one-line intent describing how
            they want to respond. Produce ONE polished draft, ready to paste
            directly into a mail client.

            Your output MUST follow this exact shape:

                Subject: Re: <your inferred subject from the incoming mail>

                <Salutation>,

                <Body — 2 to 4 paragraphs that execute the intent precisely>

                <Sign-off>,
                <your signature>

            Hard rules:

            1. SALUTATION mirrors the incoming email's register. If the sender
               wrote "Hi Alex," reply "Hi Margaret,". If they wrote "Dear
               Mr. Smith," reply "Dear Ms. Chen,". If you cannot identify a
               clear sender name, write "Dear <Name>," — leave the placeholder
               literal.
            2. BODY executes the user's intent precisely. If the intent says
               "decline", decline. Do not soften a decline into a "maybe".
               Do not promise discounts, delivery dates, prices, or legal
               commitments unless the intent explicitly authorises them.
            3. LANGUAGE matches the incoming email. If the incoming text is
               in Russian, reply in Russian even if the intent is in English.
               If it is in English, reply in English.
            4. NEVER preface the reply with commentary like "Here's a draft"
               or "Sure, here is your reply". Output begins with the literal
               word "Subject:". The reply IS the entire response.
            5. NEVER echo the user's intent back as content. The reader sees
               only the reply, not the directive that produced it. Phrases
               like "You asked me to decline" or "As you instructed" are
               forbidden.
            6. NEVER repeat or quote the incoming email's body. Read it for
               context, address it in your reply, but do not paste it back.
               Quoted thread history at the bottom of the incoming mail is
               read-only — do not echo it.
            7. Mention attachments only if the user's intent explicitly says
               to.

            Tone: {{toneClause}}

            Sign-off: {{signOffClause}}
            """;
    }
}
