# 02 — Meeting Notes → Action Items

Read a speaker-prefixed meeting transcript and emit a JSON array of
explicit action items: `{owner, task, due, confidence, source_quote}`.
Downstream tools (Asana, Linear, Jira) ingest the JSON directly — no
human has to comb the transcript for "who's doing what by when".

## What it demonstrates

- **Long-context single-shot.** The whole transcript goes into the
  prompt; the model produces one structured answer. `ContextSize` is set
  to **32 768** (Qwen 2.5 native window) so a typical 20-30k-token
  transcript from Otter.ai or Whisper fits without chunking.
- **Structured JSON output.** Each action item has five distinct fields
  (owner, task, due, confidence, source_quote). Unlike the PII
  redactor's homogeneous list, every entry here is textually unique,
  which keeps JSON generation out of the repetition trap.
- **Date anchoring.** A `Today's date: YYYY-MM-DD (Weekday)` line at the
  top of the user message lets the model resolve "by Friday", "next
  Tuesday", or "end of the week" into absolute ISO dates.
- **Hallucination gate.** `source_quote` must be a verbatim substring of
  the transcript. Invented quotes are dropped silently.
- **Deferral detection.** The system prompt teaches the model to ignore
  proposals that were parked by the chair ("let's revisit when X
  lands") — those are not commitments.

## Input format

A plain-text transcript with speaker-prefixed lines. Any continuation
style works (indented, same line, repeated prefix per turn):

```
Date: 2026-04-01
Attendees: Mira Tanaka, Amir Kovacs, Priya Sharma, ...

Mira Tanaka: Let's do status round-robin. Amir, wire protocol spec?
Amir Kovacs: Good progress. I can have the full spec drafted by March 31st.
...
```

The tutorial auto-detects a `Date: YYYY-MM-DD` line in the first five
lines and uses that as the date anchor; override with `--date
YYYY-MM-DD`.

## Output

JSON array, written to `<input>.actions.json`:

```json
[
  {
    "owner": "Amir Kovacs",
    "task": "Draft the full wire protocol spec",
    "due": "2026-03-31",
    "confidence": "high",
    "source_quote": "I can have the full spec drafted by March 31st."
  },
  ...
]
```

Also a pretty console summary:

```
── Action items ──
   1. [high  ] 2026-03-31  Amir Kovacs
       → Draft the full spec
       "I can have the full spec drafted by March 31st."

   2. [high  ] 2026-05-15  Priya Sharma
       → Deliver the prototype against integration cluster

   3. [high  ] 2026-04-07  Sofia Martinelli
       → Provide updated pricing memo to Mira Tanaka
       "Yes, I'll have the pricing memo on your desk by Friday."

   4. [high  ] 2026-04-11  Deepak Iyer
       → Circulate a full draft of memory-layout chapter
```

This is the real output of the tutorial on the bundled sample. Four items
from a transcript that had six explicit commitments — see "Known
limitations" below for why the other two slipped through.

## Run it

```bash
cd 08-solutions/02-MeetingNotesToActions
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run

# override the date anchor if the transcript doesn't include one:
dotnet run -- --date 2026-04-01 path/to/transcript.txt

# write to a specific output file:
dotnet run -- --out q1-kickoff.json transcript.txt
```

With no arguments the tutorial runs against the bundled
[`assets/sample-transcript.txt`](assets/sample-transcript.txt) — a 40-line
Halyard weekly-sync meeting with 6 embedded commitments and one deferred
proposal.

## Confidence levels

- **`high`** — explicit commitment with an explicit or derivable date.
  "I'll have the prototype running by 15 May."
- **`medium`** — explicit commitment, fuzzy date. "I'll look at it
  sometime next week."
- **`low`** — implied or tentative commitment. "We should probably
  update the runbook." (Often filtered by downstream UI.)

Filter the JSON with `jq '.[] | select(.confidence=="high")'` to take
only the rock-solid items into your project tracker.

## Implementation notes

- **Model.** `Qwen25Preset` (Qwen 2.5 7B Instruct, ~4.5 GB). 0.5B does
  not have the cross-reference reasoning to correctly attribute "I'll
  handle it" after a speaker change.
- **Context size.** Raised to 32 768. KV cache at 16k is ~1 GB on CPU;
  at 32k it's ~2 GB. With `OffloadKqv = true` (enabled in the preset)
  most of that lives on GPU when one is present.
- **Temperature 0.1, Seed 42.** Near-deterministic; the same transcript
  + same date anchor produces byte-identical JSON across re-runs on the
  same host/backend.
- **MaxTokens = 2048.** Enough room for ~30 action items even with long
  quotes. If the model hits the cap, the defensive parser recovers
  whatever complete items came through.
- **Date parsing.** Tries `--date` first, then `Date: YYYY-MM-DD` in the
  first five lines of the transcript, then falls back to system clock.

## Known limitations

Observed behaviour against the bundled 2 200-char sample (6 real
commitments in the transcript):

- **Recall — typically 4 of 6.** The model catches the cleanly-stated
  single-commitment utterances but stumbles on:
  - An utterance with two commitments in one sentence (prototype by X,
    production by Y) — the model tends to merge them into the earlier
    date.
  - Commitments where the chair self-assigns near the end of the
    transcript.
- **Date arithmetic is unreliable.** Even with a pre-computed
  weekday-to-date lookup table in the user message, Qwen 2.5 7B
  consistently miscounts offsets ("Friday" on a Wednesday gets resolved
  to a Tuesday). Treat `due` as a starting-point suggestion and reconcile
  against the source quote in your UI.
- **Paraphrased source quotes.** Some entries come back with `source_quote:
  null` because the model paraphrased instead of quoting verbatim — the
  hallucination gate nulls them rather than letting a fake audit line
  slip through. The task description is still correct, just the quote
  is missing.
- **Long multi-hour meetings.** Beyond 25 k tokens you'll feel the
  prefill cost on CPU (20-60 s). GPU offload is strongly recommended for
  production workloads.
- **Speaker attribution.** If the transcript lacks clear speaker
  prefixes (e.g. a raw Whisper output without diarisation), attribution
  collapses — every action item ends up as `owner: null`.

The tutorial is still valuable for the pattern it demonstrates —
long-context extraction, JSON schema, audit quotes, date anchoring. For
production use, pair it with a human review pass on `due` and
`source_quote`, or layer a rule-based date resolver over the raw output.

## Stretch goals

- **Aspose.Words ingestion.** Accept `.docx` transcripts directly;
  extract text with Aspose.Words before passing to the model.
- **CSV / Markdown export.** Alongside JSON, emit human-readable
  markdown checklist or Linear-ready CSV.
- **Chunking for long meetings.** Split transcripts at 30-minute
  boundaries, run each chunk independently, aggregate results. Unlocks
  multi-hour meetings on low-memory hosts.
- **Confidence filter.** `--min-confidence medium` to suppress low-
  confidence entries.
- **Active learning.** Accumulate user corrections as few-shot examples
  that prepend the system prompt on subsequent runs.

## Files

- [`Program.cs`](Program.cs) — ~250 lines including comments.
- [`MeetingNotesToActions.csproj`](MeetingNotesToActions.csproj)
- [`assets/sample-transcript.txt`](assets/sample-transcript.txt) —
  bundled 40-line sample: Halyard weekly sync, 2026-04-01.
