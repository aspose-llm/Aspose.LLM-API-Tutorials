# 04 — PII Redactor

Scrub personally-identifiable information from plain text using a **local**
LLM, with a deterministic pipeline and an auditable report. One of the
flagship use cases for an on-prem model — the whole point of redacting is
that the raw content can't be trusted to a third party, so cloud APIs are
out.

## What it demonstrates

- The **two-stage "LLM-as-extractor"** pattern: the model identifies PII
  spans as a line-delimited list (`TYPE|VALUE`); replacement happens in
  plain C# on the original text. This keeps whitespace, punctuation, and
  layout exactly preserved.
- **Deterministic output** — a tiny temperature (`0.1`), a fixed seed, and
  a pure-code replacement step. Two back-to-back runs on the same input
  produce **byte-identical** output.
- **Line-based output format, not JSON.** JSON arrays invite 0.5B models
  into a repetition loop because every entry starts with `"type":`. The
  `NAME|John Smith` format varies at the very first token per line and
  sidesteps the trap.
- **Verification against hallucination** — before accepting each span, we
  require the `value` to be a verbatim substring of the input. Made-up
  entities are silently dropped.

## Three replacement styles

Choose via `--style`:

| Style      | Example output                                    | Use case                                         |
|------------|---------------------------------------------------|--------------------------------------------------|
| `labeled`  | `<REDACTED_NAME>`, `<REDACTED_EMAIL>`             | Default. Quickest human scan.                    |
| `stable`   | `<PERSON_1>`, `<EMAIL_2>`, `<PERSON_1>` again     | Keeps narrative readable — same person stays `<PERSON_1>` throughout. |
| `generic`  | `████`                                            | Maximum obliteration when even category is sensitive. |

## Detected PII categories

`NAME`, `EMAIL`, `PHONE`, `ADDRESS`, `CREDIT_CARD`, `ID_NUMBER`, `URL`.

Narrow the set with `--categories`:

```bash
dotnet run -- --categories email,phone ticket.txt
```

## Run it

```bash
cd 08-solutions/04-PIIRedactor
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

With no arguments, the tool redacts the bundled
[`assets/sample-ticket.txt`](assets/sample-ticket.txt).

Sample summary (real captured output against Qwen 2.5 7B):

```
==========================================
  Aspose.LLM — PII Redactor
==========================================

[licence] applied from /.../License.lic
Input:  /.../assets/sample-ticket.txt (520 chars)
Style:  labeled
Filter: all categories

Model identified 9 PII span(s).

── Summary ──
  Input chars:    520
  Output chars:   461
  Redactions:     9
    3× EMAIL
    2× NAME
    2× ID_NUMBER
    1× PHONE
    1× CREDIT_CARD
```

The redacted text:

```
Ticket #<REDACTED_ID_NUMBER> — Shipment escalation

<REDACTED_NAME> emailed us from <REDACTED_EMAIL> yesterday asking about
order #<REDACTED_ID_NUMBER>. He is frustrated because the shipment to
142 Stuart Street, Boston, MA 02116 has not arrived on time. Please call
him back at <REDACTED_PHONE> or reply to his email. His customer ID is
<REDACTED_ID_NUMBER>.

The card on file <REDACTED_CREDIT_CARD> was charged twice — see
<REDACTED_EMAIL> for the refund flow.

Internal note: assigning this ticket to <REDACTED_NAME>
(<REDACTED_EMAIL>) for immediate handling.
```

Notice the **imperfections** visible in this real run:

- The ticket number `A-4421` was flagged as `ID_NUMBER`. It's a business
  identifier, not personal data — a false positive that gets replaced in
  two places (`Ticket #<REDACTED_ID_NUMBER>` and `order #<REDACTED_ID_NUMBER>`).
- The street address `142 Stuart Street, Boston, MA 02116` was **missed**.
  Multi-part addresses split across a line break are the weakest category
  for this model out of the box.

These are the real-world behaviour of a 7B open-source model on this task
without fine-tuning. **Read the audit JSON, spot-check the redacted
output, adjust the system prompt if your domain has a pattern the default
vocabulary mishandles.** This is the "first-line filter" disclaimer put
into concrete terms.

## Output files

Two files are written next to the input:

- `<input>.redacted.txt` — the redacted text, same shape as the input.
- `<input>.redactions.json` — the audit trail:

  ```json
  {
    "style": "labeled",
    "input": "/.../sample-ticket.txt",
    "redactions": [
      {
        "type": "EMAIL",
        "value": "john.smith@globex.com",
        "replacement": "<REDACTED_EMAIL>",
        "occurrences": 1
      },
      {
        "type": "NAME",
        "value": "John Smith",
        "replacement": "<REDACTED_NAME>",
        "occurrences": 1
      }
    ]
  }
  ```

Every PII span ends up in the audit with a count of how many times it was
replaced in the original.

## Stable style in action

Same input, `--style stable`:

```
<PERSON_1> emailed us from <EMAIL_1> yesterday asking about
order #A-4421. He is frustrated because the shipment to <ADDRESS_1>
has not arrived on time. Please call him back at <PHONE_1> or reply
to his email. His customer ID is <ID_1>.

The card on file ending in ████ was charged twice — see <EMAIL_2>
for the refund flow.

Internal note: assigning this ticket to <PERSON_2>
(<EMAIL_3>) for immediate handling.
```

Every mention of the same underlying value — person, email, phone —
collapses to the same stable identifier. Works because the replacement
step maps `(type, value)` to a unique ID before writing.

## Things that stay unredacted

The system prompt teaches the model to distinguish PII from **business
identifiers**:

- Ticket numbers (`Ticket #A-4421`), order numbers (`order #A-4421`),
  invoice IDs, version numbers — stay.
- Bare city / country names (`in Moscow`) — stay.
- Public documentation URLs — stay.

This is a soft rule. The model is not infallible; inspect the audit JSON
and adjust the system prompt if your domain flags something the default
vocabulary doesn't cover.

## ⚠️ Scope — first-line filter, not compliance

This tutorial is a **first pass** for developer productivity: scrub the
obvious PII before handing a log bundle to a partner, or before pasting
a customer ticket into an engineering chat. It is **not** a compliance
oracle. Specifically:

- Do not rely on it alone for GDPR, HIPAA, PCI-DSS, or any regulated
  redaction pipeline. Follow a real compliance review.
- The model can miss subtle PII (nicknames, maiden names, specific
  pronoun + role combinations that identify one person uniquely).
- "Moscow" stays as a city name — but "Muscovite Alexander Ivanov of the
  Kurchatov Institute" identifies a person by context; model coverage
  of such cases is imperfect.

A pragmatic deployment pattern: run this tool first, then a human eyes
the `.redactions.json` audit for one minute before release.

## Stretch goals (not implemented in v1)

- **Second-pass consistency.** Explicitly re-scan the redacted output and
  catch partial mentions (`"John"` after `"John Smith"` was redacted).
- **Aspose.Words in-place `.docx`.** Load a `.docx`, extract text,
  redact, write a new `.docx` with formatting preserved.
- **Structured-format awareness.** JSON/YAML/CSV parsing so only
  relevant field values get touched.
- **Locale packs.** Russian addresses, Japanese names, Arabic honorifics
  — tuned system prompts per locale.

## Implementation notes

- **Model.** `Qwen25Preset` (Qwen 2.5 7B Instruct, ~4.5 GB). This is
  deliberately a larger preset than the other tutorials use — the
  extraction task benefits from the step up. Qwen 2 0.5B runs 3-5× faster
  but stops at ~65% recall, which is too low for even a first-line filter
  to be useful.
- **Sampler.** `Temperature = 0.1`, `Seed = 42`, `RepetitionPenalty =
  1.15`. Temperature strictly 0 triggers a repetition loop in small models
  on this kind of structured output, so we sit a whisker above zero and
  rely on the replacement step downstream to provide byte-identical
  re-runs.
- **MaxTokens** is sized proportionally to input length
  (`input.Length / 2`, clamped to `[512, 2048]`). The line-based list is
  much shorter than the input; this gives headroom without wasting budget.
- **Hallucination gate.** `ExtractEntities` rejects any entry whose
  `VALUE` is not an exact substring of the input. The model occasionally
  paraphrases ("John" instead of "John Smith"); those get dropped
  silently.
- **Facade session.** `AsposeLLMApi.SendMessageAsync` is a one-shot call,
  no chat state to manage.

## Files

- [`Program.cs`](Program.cs) — ~220 lines including comments.
- [`PIIRedactor.csproj`](PIIRedactor.csproj)
- [`assets/sample-ticket.txt`](assets/sample-ticket.txt) — bundled sample
  with nine PII items for end-to-end verification.
