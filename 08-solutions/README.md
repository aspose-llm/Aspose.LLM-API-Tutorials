# 08 — Solutions

Whereas sections 01–07 teach **how the SDK works** (one API knob at a
time), this section collects **end-to-end recipes for real problems** —
small self-contained projects that solve an everyday developer or business
task with a local LLM.

Each recipe is a runnable `dotnet run` project with its own README, code,
and bundled sample inputs so you can try it without external setup.

## Shipped

| Folder                                                          | Use case                                                       |
| --------------------------------------------------------------- | -------------------------------------------------------------- |
| [`01-EmailReplyAssistant`](01-EmailReplyAssistant/)             | Draft a polished reply from an incoming email + a one-line intent. Pairs with Aspose.Email. |
| [`02-MeetingNotesToActions`](02-MeetingNotesToActions/)         | Extract `{owner, task, due, confidence, source_quote}` from speaker-prefixed transcripts. Long-context + structured JSON + date anchoring. |
| [`03-ContractClauseAnalyzer`](03-ContractClauseAnalyzer/)       | Flag risky clauses (auto-renewal, unlimited liability, exclusivity, ...) with severity + rationale + suggested counter. |
| [`04-PIIRedactor`](04-PIIRedactor/)                             | Scrub PII from text. LLM-as-extractor pattern; deterministic output; audit trail. |
| [`05-InvoiceToJson`](05-InvoiceToJson/)                         | Turn OCR-style receipt/invoice text into a strictly-typed JSON object with vendor, line items, totals. |
| [`06-CustomerFeedbackTriage`](06-CustomerFeedbackTriage/)       | Classify CSV of feedback rows: sentiment, category, urgency, summary. Batch-friendly with throughput rollup. |
| [`07-CodeReviewCompanion`](07-CodeReviewCompanion/)             | Read a `git diff`, emit severity-ranked concerns (logic, security, concurrency, testing). |
| [`08-CommitMessageGenerator`](08-CommitMessageGenerator/)       | `git diff --staged \| dotnet run` → Conventional Commits message ready for `git commit -m`. |
| [`09-SqlFromNaturalLanguage`](09-SqlFromNaturalLanguage/)       | Plain-language question + schema → SQL query for six dialects. Never executes. |
| [`10-OfflineKnowledgeBaseQA`](10-OfflineKnowledgeBaseQA/)       | Q&A over a folder of `.md` policies with inline filename citations. RAG-lite. |

## Common design themes

Solutions in this section share a few recurring patterns you'll see
repeated:

- **LLM-as-extractor.** Rather than ask the model to emit the final
  output text (which invites reflow and hallucination), ask it for a
  structured JSON list of findings and do the transformation in C#.
  Keeps layout exact and makes every run auditable.
- **Determinism by default.** Temperature `0.0` plus a fixed seed, so
  the same input produces the same output on a re-run. Essential for
  anything that lands in a diff.
- **Verification gate.** Before trusting the model's extracted spans,
  cross-check they actually appear in the input. Drops hallucinated
  entities silently.
- **Local-first.** Every recipe is designed to run without any outbound
  network. The SDK downloads the GGUF model on first run and caches it;
  subsequent runs are fully offline.

## Why local LLM matters for these

Nearly every recipe here has a cloud-API equivalent. The reason to use a
local model instead:

- **Privacy.** PII redaction, contract review, internal knowledge base —
  the data can't leave the host.
- **Offline / air-gapped environments.** Corporate laptops, on-prem
  deployments, field workstations.
- **Cost at scale.** Classifying 100k feedback lines nightly on a cloud
  API is expensive; local inference is capex, not opex.
- **Bundled distribution.** Ship a `.nupkg`, it runs on day one — no API
  keys, no subscriptions, no vendor lock-in.

## Prerequisites

All recipes inherit from the tutorials repo's shared conventions:

- Aspose.LLM licence — drop `License.lic` in the working directory or set
  `ASPOSE_LLM_LICENSE=/path/to/License.lic`. See the
  [repo README](../README.md) for details.
- First-run download of a GGUF model (cached thereafter). Each recipe's
  README tells you which preset it uses.
