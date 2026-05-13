# 03 — Contract Clause Analyzer

Scan a software-services contract and flag the clauses that warrant a
human legal review: auto-renewal, unlimited liability, exclusivity, IP
assignment, indemnification carve-outs, governing law in uncommon
jurisdictions. The model emits a JSON array of `{clause_type, severity,
excerpt, location, rationale, suggested_counter}` records — downstream
CLM tooling can ingest the JSON directly, and the lawyer skips straight
to the clauses that matter.

## Important — informational, not legal advice

> **This tool is a pre-scan, not a substitute for qualified legal
> counsel.** Every clause it flags requires review by a lawyer before any
> action is taken. The `rationale` and `suggested_counter` fields are
> informational only and do not constitute legal advice. Do not rely on
> this tool alone for contracts that affect material financial,
> regulatory, or compliance outcomes. Recall is not perfect — the model
> can miss risky clauses, and it can flag harmless ones.

## What it demonstrates

- **Long-context single-shot.** The whole contract goes into the prompt;
  the model produces one structured answer. `ContextSize` is bumped to
  **16 384** so most software-services contracts fit without chunking.
- **Multi-field structured JSON output.** Six distinct fields per record
  (`clause_type`, `severity`, `excerpt`, `location`, `rationale`,
  `suggested_counter`). Per-entry text is unique, which keeps JSON
  generation away from the repetition trap that bites 0.5B models on
  homogeneous lists.
- **Controlled vocabulary.** `clause_type` is restricted to the eleven
  values from the spec. Anything outside the set is dropped after
  parsing — downstream tooling can rely on a stable category set.
- **Hallucination gate.** The `excerpt` field must be a verbatim
  substring of the contract. Paraphrased excerpts are silently dropped.
  A legal-review tool emitting fake quotes is worse than emitting
  nothing.
- **Local model is essential.** Contracts contain confidential terms —
  pricing, customer names, liability caps. Cloud APIs are off-limits in
  many jurisdictions, particularly under European or Russian
  data-residency regulations.

## Why a 7B model

Legal text is precise. A small model (Qwen 2 0.5B) hallucinates section
numbers, paraphrases excerpts, and miscategorises clauses — exactly the
failure modes the hallucination gate exists to catch, but at a rate that
hollows out the recall signal. `Qwen25Preset` (Qwen 2.5 7B Instruct,
~4.5 GB) is the recommended baseline for this task.

## Run it

```bash
cd 08-solutions/03-ContractClauseAnalyzer
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

With no arguments the tutorial scans the bundled
[`assets/sample-contract.txt`](assets/sample-contract.txt) — a
fictitious software-services agreement with several embedded risky
clauses (auto-renewal, exclusivity, unbounded indemnification, IP
assignment, long confidentiality, non-compete, offshore governing law).

### CLI

```bash
# scan everything (default)
dotnet run -- path/to/contract.txt

# restrict scan to specific clause types
dotnet run -- --focus auto_renewal,unlimited_liability,exclusivity contract.txt

# write the report to a specific file (default: <input>.report.json)
dotnet run -- --out report.json contract.txt
```

The `--focus` value accepts comma-separated entries from the
controlled vocabulary; both hyphens and underscores are accepted
(`auto-renewal` and `auto_renewal` are equivalent). Unknown values
abort the run with a list of allowed names.

### Controlled vocabulary

`auto_renewal`, `unlimited_liability`, `exclusivity`, `assignment`,
`indemnification`, `ip_assignment`, `non_compete`,
`termination_for_convenience`, `governing_law`, `dispute_resolution`,
`confidentiality`.

### Output

JSON array, written next to the input as `<input>.report.json`:

```json
[
  {
    "clause_type": "auto_renewal",
    "severity": "high",
    "excerpt": "This Agreement shall automatically renew for successive twenty-four (24) month terms unless either party provides written notice of non-renewal at least one hundred and twenty (120) days prior to the end of the then-current term.",
    "location": "Section 4.3",
    "rationale": "120-day notice window combined with 24-month auto-renewal creates rollover risk if calendar tracking lapses.",
    "suggested_counter": "Reduce notice window to 30 days or make renewal opt-in."
  },
  {
    "clause_type": "unlimited_liability",
    "severity": "high",
    "excerpt": "Provider shall indemnify, defend, and hold harmless Client and its affiliates ... arising out of or relating to Provider's performance of the Services, without cap or limitation of any kind.",
    "location": "Section 8.2",
    "rationale": "Indemnification without a liability cap exposes Provider to unbounded financial risk.",
    "suggested_counter": "Cap indemnification at 12 months of fees paid, or at the amount of insurance coverage."
  }
]
```

Plus a colour-coded console summary that lists the flagged clauses
sorted by severity (high → medium → low) with a one-line excerpt and
the section number.

## Severity rubric

- **`high`** — unbounded liability, long auto-renewal terms, broad
  exclusivity, IP assignment of pre-existing work, governing law in an
  offshore jurisdiction with uncertain enforcement.
- **`medium`** — confidentiality terms longer than five years,
  non-compete clauses ≥ 24 months or with broad geographic scope,
  inconvenient dispute-resolution venues.
- **`low`** — stylistic concerns, standard boilerplate that is
  sometimes mis-negotiated.

The model is instructed not to flag standard boilerplate that legal
teams routinely accept (e.g. "confidentiality survives termination"
with a reasonable duration). Quality over quantity.

## `.docx` and `.pdf` ingestion (Aspose.Words / Aspose.PDF synergy)

This tutorial accepts plain-text input only. For `.docx` or `.pdf`
contracts, extract the text first with **Aspose.Words** or **Aspose.PDF**
and pass the result through this tool. Neither package is referenced
from this project to keep the dependency tree minimal — add them
manually in your own consumer.

```csharp
// .docx → plain text via Aspose.Words
using Aspose.Words;
var doc = new Document(@"C:\contracts\agreement.docx");
var text = doc.GetText();                // SaveFormat.Text also works
File.WriteAllText("agreement.txt", text);
// then: dotnet run -- agreement.txt
```

```csharp
// .pdf → plain text via Aspose.PDF
using Aspose.Pdf;
using Aspose.Pdf.Text;
var pdf = new Document(@"C:\contracts\agreement.pdf");
var sb = new System.Text.StringBuilder();
foreach (Page p in pdf.Pages)
{
    var absorber = new TextAbsorber();
    p.Accept(absorber);
    sb.AppendLine(absorber.Text);
}
File.WriteAllText("agreement.txt", sb.ToString());
// then: dotnet run -- agreement.txt
```

Aspose.Words preserves heading and section numbering, which improves
`location` attribution in the final report. Add either NuGet to your
project before pasting the snippet:

```bash
dotnet add package Aspose.Words
dotnet add package Aspose.PDF
```

## Implementation notes

- **Model.** `Qwen25Preset` (Qwen 2.5 7B Instruct, ~4.5 GB). Smaller
  presets miss too many clauses on legal text.
- **ContextSize.** 16 384. Most software-services contracts fit. Multi-
  document packages (MSA + SOW + DPA pasted together) start bumping the
  ceiling and are a stretch goal.
- **Sampler.** `Temperature = 0.1`, `Seed = 42`,
  `RepetitionPenalty = 1.15`. Near-deterministic — same contract on the
  same host produces byte-identical JSON across runs.
- **MaxTokens = 2048.** Headroom for ~15-20 flagged clauses with full
  rationale and counter strings.
- **Hallucination gate.** `ExtractClauses` rejects any entry whose
  `excerpt` is not an exact substring of the input. The model
  occasionally synthesises plausible-sounding excerpts; those are
  dropped silently.
- **Controlled-vocabulary gate.** Entries whose `clause_type` falls
  outside the eleven approved values are dropped. The system prompt
  tells the model to stick to the vocabulary, but we don't trust it
  not to drift.

## Known limitations

- **No multi-document support.** A contract package with separate MSA,
  SOW, and DPA documents needs concatenation by the caller. Section
  numbers can collide across documents in that case — the model has no
  way to disambiguate "Section 8.2" of the MSA from "Section 8.2" of
  the SOW.
- **Date arithmetic is unreliable.** The model can read "120 days
  prior" but does not compute the renewal-decision deadline. Treat
  every flagged clause as a starting point for human review, not a
  finished analysis.
- **Non-English contracts.** The system prompt does not instruct the
  model to operate in another language and keep English category keys.
  Russian, German, French contracts work but with reduced recall.
- **Missing carve-outs.** A clause may be qualified by an exception in
  a different section of the contract. The model reads the contract
  linearly and may flag a clause whose practical risk is mitigated
  elsewhere.

## Stretch goals

- **Aspose.Words ingestion** — wired into the tutorial directly so
  `.docx` files can be passed without manual pre-processing. Preserves
  heading and section numbering for richer `location` attribution.
- **Aspose.PDF ingestion** — same for `.pdf` contracts.
- **Redline export.** Generate a `.docx` with track-changes-style
  annotations using Aspose.Words. Closes the loop from "flag" to
  "suggest alternative wording".
- **Comparison mode.** Two contracts side-by-side, highlighting
  differences in their risky-clause profile.
- **Industry templates.** `--template software_services`,
  `--template procurement` — adjusts the severity rubric for
  domain-specific norms.
- **Chunking for long packages.** Split documents at section boundaries,
  scan each chunk independently, aggregate results.

## Files

- [`Program.cs`](Program.cs) — top-level statements, ~280 lines including
  comments.
- [`ContractClauseAnalyzer.csproj`](ContractClauseAnalyzer.csproj)
- [`assets/sample-contract.txt`](assets/sample-contract.txt) — bundled
  fictitious software-services agreement with several risky clauses for
  end-to-end verification.
