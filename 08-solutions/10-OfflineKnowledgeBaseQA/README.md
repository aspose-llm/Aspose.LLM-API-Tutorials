# 10 — Offline Knowledge Base Q&A

Answer natural-language questions about a folder of internal docs,
with inline filename citations. RAG-lite: the whole corpus goes into
one prompt — no vector store, no retrieval pipeline, no infrastructure.
Works for small KBs that fit in 32k context (~50 pages of plain text).

## What it demonstrates

- **Long-context "stuff the corpus" pattern.** Walk the directory,
  concatenate every `.md` / `.txt` with `=== filename ===` delimiters,
  hand the lot to Qwen 2.5 7B at full 32k context.
- **Citation enforcement.** The system prompt requires every factual
  claim to end with `[filename.md]`. Inline by default; switch to
  superscript-numbered footnotes with `--style footnotes`.
- **Refuse-when-not-found.** If the corpus doesn't answer the
  question, the model says so and lists the filenames it consulted
  rather than fabricating.
- **Token budget guard.** Estimates corpus tokens (4 chars/token rule
  of thumb); errors out cleanly if the corpus exceeds the available
  context budget rather than silently truncating.

## Run it

```bash
cd 08-solutions/10-OfflineKnowledgeBaseQA
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run

# Specific question, default style (inline citations):
dotnet run -- "What is the refund policy for enterprise subscribers?"

# Footnote-style citations:
dotnet run -- --style footnotes "..."

# Point at your own corpus:
dotnet run -- --corpus ./docs "..."
```

With no question, the tutorial runs three canned questions:
- One answerable from `refund-policy.md`.
- One answerable from `vendor-onboarding.md`.
- One **not** in the corpus — exercises the "I could not find that"
  branch.

## Bundled corpus

[`assets/sample-corpus/`](assets/sample-corpus/) — five short markdown
files (~500 chars each) covering different policy areas:

- `billing-faq.md`
- `customer-success-runbook.md`
- `refund-policy.md`
- `security-policy.md`
- `vendor-onboarding.md`

## Output format

**Inline (default):**

```
Enterprise subscribers can request a refund within 30 days of invoice
date if the service has not been substantially used. [refund-policy.md]

Substantial use is defined as >20% of billed seat-hours
[refund-policy.md]. Requests are processed in 14 business days, routed
through customer-success rather than billing directly
[customer-success-runbook.md].
```

**Footnotes (`--style footnotes`):**

```
Enterprise subscribers can request a refund within 30 days of invoice
date if the service has not been substantially used¹. ...

---
1. refund-policy.md
2. customer-success-runbook.md
```

## When to graduate to real RAG

This tutorial intentionally stays in "stuff the prompt" territory.
Real RAG pipelines (vector DB + embeddings + retrieval + reranking)
become necessary when:

- Corpus size > 30k tokens. The token-budget guard refuses to even
  start in that regime.
- You need source-level chunking (e.g. paragraph-level retrieval) to
  survive context-window pressure.
- Updates are continuous. Stuff-the-prompt rebuilds the entire context
  every query; RAG indexes incrementally.

For < 50 pages of stable docs, this tutorial is genuinely sufficient
and significantly simpler than running a vector DB.

## Known limitations

- **Hallucinated citations.** The model occasionally cites a filename
  that doesn't exist in the corpus. The tutorial doesn't auto-verify
  citations against the corpus listing — stretch goal. For now,
  spot-check by opening the cited file.
- **Cross-document conflicts.** The system prompt instructs the model
  to surface conflicting policies rather than pick a side, but it
  sometimes silently merges. Ask follow-up questions to disambiguate.
- **First run is slow.** Prefilling 32k context on CPU is uncomfortable
  (~30-60s on a typical laptop). With Metal/CUDA offload it drops to
  a few seconds. Subsequent questions reuse the cached corpus when
  in the same session — but the tutorial currently uses a fresh
  session per question (intentional, prevents bleed). For a long Q&A
  loop, modify `Program.cs` to keep one session and skip the prefill
  cost from the second question onward.

## Files

- [`Program.cs`](Program.cs) — ~180 lines including comments.
- [`OfflineKnowledgeBaseQA.csproj`](OfflineKnowledgeBaseQA.csproj)
- [`assets/sample-corpus/`](assets/sample-corpus/) — five-file
  bundled corpus.
