# 01 — Email Reply Assistant

Turn a one-line intent into a paste-ready email reply, without the message
ever leaving your machine. You write `"decline politely and suggest
mid-May instead"`; the local LLM produces the full Subject + salutation +
body + sign-off block, ready for the clipboard.

## What it demonstrates

- **One-shot generation, no parsing.** Unlike the extraction-style
  tutorials in this folder (line-based PII spans, JSON action items), the
  model's output IS the deliverable. We call `SendMessageAsync` and
  print the reply verbatim.
- **Persona via system prompt.** Tone (`formal` / `friendly` / `concise`)
  and relationship (`client` / `colleague` / `vendor`) shape the
  salutation register and sign-off through a templated system prompt.
  One model handles every persona — only the prompt changes.
- **Why local matters.** Inboxes mix routine scheduling chatter with
  sensitive material: deal terms, employee complaints, partner roadmaps.
  The whole point of this tutorial is that the message text never
  touches a third-party API.
- **Temperature = 0.4.** Higher than the deterministic extractors (0.1)
  because reply tone benefits from a touch of variation; low enough that
  re-running the same inputs doesn't swing between curt and effusive.

## Run it

```bash
cd 08-solutions/01-EmailReplyAssistant
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

With no arguments, the tutorial drafts a reply to the bundled
[`assets/incoming-sample.txt`](assets/incoming-sample.txt) using a
default intent that fits the sample (decline + reschedule).

Real flags:

```bash
# Pick a different intent
dotnet run -- --intent "accept and ask for the slide deck in advance"

# Override tone and relationship
dotnet run -- --tone friendly --relationship colleague \
              --intent "agree to take the action item, suggest a 1:1 next week"

# Point at your own message
dotnet run -- --intent "ask for pricing on the Pro tier" path/to/incoming.txt
```

## Sample run (real captured output)

```
==========================================
  Aspose.LLM — Email Reply Assistant
==========================================

[licence] applied from /.../License.lic
Incoming:     /.../assets/incoming-sample.txt (588 chars)
Intent:       decline politely, cite scheduling conflicts on our side, and
              suggest the first week of June as an alternative kick-off slot
Tone:         formal
Relationship: client

── Draft reply ──

Subject: Re: Kick-off for the Q3 integration project — proposed 17 April

Dear Margaret,

Thank you for the proposed kick-off slot and for sharing the draft
agenda and stakeholder list ahead of time.

Unfortunately, 17 April will not work on our side. Two of the
engineering leads whose input the kick-off will need are committed to
back-to-back release reviews that week, and starting the project
without their context would put us on a weak footing.

Would the first week of June suit instead? That window gives us room
to give the kick-off the attention it deserves and to align the
internal review on your side with a substantive milestone on ours. I
am happy to propose specific times once you confirm the week works.

Best regards,
<your signature>
```

The salutation ("Dear Margaret,") was inferred from the "Hi Alex," /
"Margaret Chen" combination in the incoming mail; the system prompt
asks for a salutation that mirrors the incoming register. Note that
the model didn't echo the intent ("you asked me to decline"), didn't
add a "here's a draft:" preface, and didn't quote the original message
back wholesale — three small-model failure modes the system prompt
specifically guards against.

## Tone and relationship matrix

| Flag                      | Salutation register     | Sign-off          |
|---------------------------|-------------------------|-------------------|
| `--relationship client`   | "Dear <Name>,"          | "Best regards,"   |
| `--relationship colleague`| "Hi <Name>,"            | "Best,"           |
| `--relationship vendor`   | "Dear <Name>,"          | "Kind regards,"   |

| `--tone formal`   | Complete sentences, no contractions, no exclamation marks. |
| `--tone friendly` | Contractions ok, one light human touch welcome.            |
| `--tone concise`  | Two short paragraphs max. No filler.                       |

The body picks up these cues through the system prompt — there is no
post-processing on the output.

## Pairing with Aspose.Email — the obvious next step

This tutorial deliberately stops at "produce the reply text". The
real-world flow is one step further: read the latest unread message
from IMAP, generate the reply, send it via SMTP. That's an
`Aspose.Email` job, not an `Aspose.LLM` one — but the two snap
together cleanly. **No PackageReference is added in this tutorial** so
the demo stays dependency-free; the snippet below shows what the
integration looks like when you bring `Aspose.Email` in yourself.

```csharp
// Add to your csproj:
//   <PackageReference Include="Aspose.Email" Version="..." />

using Aspose.Email.Clients.Imap;
using Aspose.Email.Clients.Smtp;
using Aspose.Email;

using var imap = new ImapClient("imap.example.com", 993, user, pass)
{
    SecurityOptions = SecurityOptions.SSLImplicit
};
imap.SelectFolder(ImapFolderInfo.InBox);

// Pull the most recent unread message.
var unread = imap.ListMessages()
                 .OrderByDescending(m => m.InternalDate)
                 .First(m => !m.IsRead);

var raw = imap.FetchMessage(unread.UniqueId);
var incomingBody = raw.GetHtmlBodyText(false) ?? raw.Body;

// ---- this is where the tutorial above slots in -----------------------
var reply = await api.SendMessageAsync(
    $"Incoming email:\n----\n{incomingBody}\n----\n\n" +
    $"Intent: confirm receipt and propose a 30-minute call next Tuesday");
// ----------------------------------------------------------------------

// Send via SMTP.
using var smtp = new SmtpClient("smtp.example.com", 587, user, pass)
{
    SecurityOptions = SecurityOptions.SSLExplicit
};
var outgoing = new MailMessage(user, raw.From.Address, ExtractSubject(reply), ExtractBody(reply));
smtp.Send(outgoing);
```

The local model handles every byte of message content; `Aspose.Email`
handles the wire protocols. Neither component needs to know the other
exists.

## Implementation notes

- **Model.** `Qwen25Preset` (Qwen 2.5 7B Instruct, ~4.5 GB). The 0.5B
  preset works but produces noticeably blander prose — boilerplate
  phrasing that reads like ChatGPT circa 2022. The 7B step-up earns
  its disk space here.
- **Sampler.** `Temperature = 0.4`, `Seed = 42`,
  `RepetitionPenalty = 1.15`. Deterministic per host: same incoming +
  same intent + same flags produces the same reply on re-run.
- **MaxTokens = 400.** Enough room for a 3-4 paragraph reply with a
  comfortable cushion. If your intents routinely produce longer
  drafts, raise this.
- **System prompt is built per-invocation** to splice the right
  tone/relationship clauses in. The persona shifts but the model
  doesn't — one warm Qwen instance handles every combination.
- **No file output.** The reply prints to stdout; pipe it where you
  want.
  ```bash
  dotnet run -- --intent "accept" 2>/dev/null | pbcopy   # macOS
  ```

## Known limitations

Observed on Qwen 2.5 7B against the bundled sample over many runs:

- **Hallucinated names.** If the incoming email lacks a clear sender
  name, the model occasionally invents one rather than emitting the
  literal `<Name>` placeholder the system prompt asks for. Mitigation:
  the system prompt includes the placeholder rule explicitly; falls
  through cleanly when the rule is followed.
- **Paragraph count drift.** "Two paragraphs maximum" in `concise`
  tone is sometimes interpreted as "two long paragraphs" rather than
  "two short paragraphs". The reply is still usable; tighten by
  re-running with a more specific intent ("decline in one sentence").
- **Subject line over-rewrites.** The model sometimes paraphrases the
  incoming subject heavily ("Re: Kick-off proposal — declining" vs the
  expected "Re: Kick-off for the Q3 integration project"). Fine for
  drafting; rewrite the subject in your mail client if the literal
  echo matters.
- **Very long incoming threads.** The default `MaxTokens = 400` reply
  size assumes a normal back-and-forth. For multi-thousand-character
  threads, the model may run out of budget mid-paragraph. Raise
  `MaxTokens` or trim the incoming to the most recent turn.
- **Mixed-language threads.** "Incoming Russian, intent English" is
  handled by the system prompt's language rule, but the rule fires on
  the dominant language of the incoming — a half-Russian, half-English
  thread can produce a mixed-language reply.

These are the real-world behaviours of a 7B open-source model on this
task without fine-tuning. The tutorial is a productivity tool — pair
it with one human glance before hitting send.

## Stretch goals (not implemented in v1)

- **Multi-draft compare.** Run three replies with different tones
  (`formal` / `friendly` / `concise`) side-by-side and let the user
  pick.
- **Thread context.** Extend to multi-turn chat so the model
  acknowledges history when replying to message #5 in a thread.
- **Guardrails.** A pre-check that refuses to draft replies containing
  promises of discounts, delivery dates, or legal commitments unless
  the intent explicitly permits them.
- **Aspose.Email auto-send.** Wire the IMAP/SMTP sketch above behind
  an env flag (`ASPOSE_EMAIL_AUTOSEND=true`) so the tutorial can
  demonstrate end-to-end "read inbox → generate reply → send" without
  becoming a dependency-heavy default.

## Files

- [`Program.cs`](Program.cs) — ~150 lines including comments.
- [`EmailReplyAssistant.csproj`](EmailReplyAssistant.csproj)
- [`assets/incoming-sample.txt`](assets/incoming-sample.txt) — the
  bundled fictitious kick-off email this README and the no-args
  default both target.
