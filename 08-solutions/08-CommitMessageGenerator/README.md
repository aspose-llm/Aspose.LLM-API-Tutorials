# 08 — Commit Message Generator

Read a git diff (typically `git diff --staged`) and emit a Conventional
Commits message ready to paste into `git commit -m "$(...)"`. Personal
productivity recipe — runs locally, on demand, no cloud involvement.

## What it demonstrates

- **Single-text passthrough.** No JSON, no parsing — the model writes
  the message, we trim and print. Simplest of the solution recipes.
- **stdin pipe + file fallback.** `git diff | dotnet run` works; so does
  `--in some.patch`; so does plain `dotnet run` (falls back to the
  bundled sample).
- **Conventional Commits structure** enforced by the system prompt:
  `<type>(<scope>): <subject>` / blank line / body / optional
  `BREAKING CHANGE` footer.
- **No co-author trailers, no AI-attribution.** Hard rule in the
  system prompt — Alexander Golshtein is the sole author of every
  generated message.

## Run it

```bash
cd 08-solutions/08-CommitMessageGenerator
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run

# The natural flow — pipe staged diff straight in:
git diff --staged | dotnet run

# Inline commit (one-liner from the project folder):
git commit -m "$(git diff --staged | dotnet run --project ...)"

# Override the inferred type or scope:
git diff --staged | dotnet run -- --type feat --scope auth

# Prepend a Jira-ticket prefix:
git diff --staged | dotnet run -- --prefix '[PROJ-1234]'
```

With no piped input and no `--in`, falls back to
[`assets/sample.patch`](assets/sample.patch) so `dotnet run` from the
project folder always produces a meaningful demo message.

## Sample output (illustrative)

```
feat(auth): add account lockout after 5 failed login attempts

Introduces a sliding-window counter on LoginHandler that locks an
account for 15 minutes after five consecutive failed password attempts
from the same IP. Counter resets on a successful login.

- Persists lockout state to Redis (new IAccountLockoutStore).
- Emits AccountLockedOutEvent on the audit log for compliance.

BREAKING CHANGE: Login responses now return 423 Locked instead of 401
Unauthorized when the account is locked. Clients checking specifically
for 401 must also handle 423.
```

## Pre-commit hook integration

Drop this into `.git/hooks/prepare-commit-msg` and `chmod +x` it:

```bash
#!/usr/bin/env bash
# Auto-suggest a commit message when none was supplied.
COMMIT_MSG_FILE=$1
COMMIT_SOURCE=$2

if [ -z "$COMMIT_SOURCE" ] && [ ! -s "$COMMIT_MSG_FILE" ]; then
    git diff --staged | dotnet run \
        --project /path/to/08-solutions/08-CommitMessageGenerator \
        > "$COMMIT_MSG_FILE"
fi
```

The next `git commit` opens the editor pre-filled with the model's
suggestion. Edit, save, done.

## Known limitations

- **Huge diffs.** Beyond ~10k tokens of patch, the model starts
  summarising at the file level rather than the line level. Stage
  smaller commits or split with `git add -p`.
- **Multi-concern diffs.** A diff that mixes auth + billing + tests
  produces ONE message, choosing the dominant theme. The system
  prompt mentions secondaries in the body, but if you wanted three
  separate commits, you should have staged them separately.
- **Breaking-change detection.** Removed public methods, renamed
  config keys, schema changes — usually caught. Subtle behavioural
  changes (a default value tweak that silently affects callers) often
  miss the BREAKING CHANGE footer; check before committing.

## Files

- [`Program.cs`](Program.cs) — ~150 lines including comments.
- [`CommitMessageGenerator.csproj`](CommitMessageGenerator.csproj)
- [`assets/sample.patch`](assets/sample.patch) — bundled diff for the
  no-args demo.
