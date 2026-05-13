# 07 — Code Review Companion

Read a unified-format git diff and emit a list of concerns a careful
human reviewer would want to investigate — logic bugs, security smells,
concurrency mistakes, missing tests, swallowed exceptions. **Pre-pass
for PR review, not a replacement for a human.**

## What it demonstrates

- **JSON-array extraction with severity vocabulary.** The model returns
  `[{severity, file, line, category, message, snippet}, ...]`; the
  parser drops anything outside the controlled vocabulary
  (`logic`, `security`, `concurrency`, `performance`, `testing`,
  `readability`, `error_handling`, `other`).
- **Severity-coloured console output.** HIGH in red, MEDIUM in yellow,
  LOW in dark grey — same UX language as `git diff --color`.
- **Stdin and file input.** `git diff | dotnet run` is the natural
  flow; `--in path/to.patch` works too.
- **Severity filter + JSON report.** `--only high` for the "stop the
  press" subset; full report always lands in
  `<input>.review.json`.

## Run it

```bash
cd 08-solutions/07-CodeReviewCompanion
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run

# Or with a real diff:
git diff main..HEAD | ASPOSE_LLM_LICENSE=... dotnet run

# Filter to the high-severity items only:
git diff | dotnet run -- --only high
```

With no input, runs against the bundled
[`assets/sample.patch`](assets/sample.patch) — a small C# diff with
embedded HIGH / MEDIUM / LOW issues.

## Severity rubric

| Severity | What it means                                                      |
| -------- | ------------------------------------------------------------------ |
| `high`   | Almost certainly wrong / dangerous (race, missing await, SQLi,     |
|          | resource leak in hot path, auth bypass).                           |
| `medium` | Suspicious / brittle (timing-unsafe `==`, swallowed exception,     |
|          | potential null deref, missing edge case).                          |
| `low`    | Style, missing tests, dead parameter, unclear naming.              |

## Output

Console (severity-grouped, colour-coded):

```
[HIGH  ] src/Auth/LoginHandler.cs:142  (concurrency)
      Missing await on async call — fire-and-forget may race with the
      session-cookie write below it.
      | _ = _auditLog.RecordLoginAttempt(user.Id, ip);

[MEDIUM] src/Auth/LoginHandler.cs:168  (security)
      Password comparison uses `==`; vulnerable to timing attacks.
      | if (input == storedHash) { ... }

[LOW   ] src/Auth/Tests/LoginHandlerTests.cs  (testing)
      No tests added for the new HandleLockout branch.
```

Plus a JSON file at `<input>.review.json` with the full structured
report.

## Known limitations

- **Hallucinated line numbers.** The model counts lines approximately;
  trust the file + snippet, verify the line by eye.
- **Out-of-context accusations.** A "missing null check" might be
  redundant if the caller already checks — the model can't see code
  outside the diff context. Default-down severity when in doubt.
- **Language idiom blindness.** Rust's `?` isn't an exception swallow;
  Python context managers handle dispose. `--lang csharp|python|...`
  nudges the model toward the right idioms.
- **Pre-pass only.** Don't auto-merge based on a green report. The
  README's job is to set this expectation; please read it before
  showing the tool to your team.

## Files

- [`Program.cs`](Program.cs) — ~270 lines including comments.
- [`CodeReviewCompanion.csproj`](CodeReviewCompanion.csproj)
- [`assets/sample.patch`](assets/sample.patch) — bundled diff with
  HIGH / MEDIUM / LOW issues.
