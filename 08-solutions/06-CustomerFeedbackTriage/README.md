# 06 — Customer Feedback Triage

Classify each row of a CSV of customer-feedback lines: sentiment,
category, urgency, one-line summary. Output is the same CSV with four
new columns appended, plus a console rollup.

## What it demonstrates

- **Batch processing on one loaded model.** One `AsposeLLMApi` instance
  serves the whole batch; per-item fresh sessions stop prior
  classifications from biasing the next.
- **Per-item structured JSON.** Each row gets a tight 4-field schema
  parsed defensively (the same playbook as #02).
- **CSV in / CSV out.** A small hand-rolled reader/writer handles
  quoted fields with embedded commas (RFC 4180). No extra dependency.
- **Rollup printing.** Counts by sentiment / category / urgency, plus
  the top 5 `now`-urgency items so the operator knows what to action
  first.

## Run it

```bash
cd 08-solutions/06-CustomerFeedbackTriage
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

With no arguments, runs against the bundled
[`assets/sample-feedback.csv`](assets/sample-feedback.csv) (20 rows).

```bash
dotnet run -- --in feedback.csv --out triaged.csv
```

## Input / output shape

Input columns: `id,source,text,timestamp` (timestamp optional).
Output columns: input columns **plus** `sentiment,category,urgency,summary`.

Categories: `bug`, `feature_request`, `billing`, `onboarding`,
`compliment`, `security`, `other`.
Urgency: `now`, `this_week`, `anytime`.
Sentiment: `positive`, `negative`, `neutral`.

## Model choice

The spec said 0.5B should be enough for shallow classification. Empirical
results from the PII Redactor build (where 0.5B stopped early) led us to
default to **Qwen 2.5 7B**. For very large batches where speed dominates,
swap `Qwen25Preset` for `SmallModelPreset` in `Program.cs` — one-line
change.

## Scaling notes

- **Engine is not thread-safe.** Process feedback rows sequentially
  within a single process. Spawn multiple processes for horizontal scale.
- **Long batches.** Watch session-cache pressure; the tutorial uses a
  fresh session per item which evicts cleanly. For 1000+ rows on the
  same model run, consider periodic `api.ForceCacheCleanup(...)` calls.
- **Costs.** ~30-100ms per row on 7B with Metal/CUDA; ~500ms+ on CPU.
  Plan accordingly.

## Known limitations

- **Sarcasm.** "Yeah, super helpful" gets labelled positive — small
  models miss tone reversals. Acceptable for first-pass triage; humans
  catch sarcasm on the `now`-flagged items they review.
- **Mixed signals.** "Update is great but it crashes" picks one
  dominant label. If you need multi-label output, fork the tutorial
  and ask for an array of labels per item.
- **Domain drift.** Default vocabulary works for SaaS / consumer-app
  feedback. Custom domains (e.g. medical device complaints) need a
  tuned `category` set in the system prompt.

## Files

- [`Program.cs`](Program.cs) — ~543 lines including comments.
- [`CustomerFeedbackTriage.csproj`](CustomerFeedbackTriage.csproj)
- [`assets/sample-feedback.csv`](assets/sample-feedback.csv) — 20-row
  bundled sample mixing app-store reviews, survey responses, support
  emails, and social mentions.
