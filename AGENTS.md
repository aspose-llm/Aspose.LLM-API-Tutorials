# Repository guidance for AI agents

This repository is public. Treat every tracked file and commit, including a draft or a test branch, as visible to anyone. Review the complete staged change before committing or pushing.

## Keep private data out of Git

- Never commit licences, tokens, passwords, API keys, private keys, signing certificates, `.env` values, customer data, model weights, or generated build artifacts.
- Do not include real workstation paths, user names, private NuGet feed paths, internal GitLab or API addresses, private repository URLs, or internal pipeline and job identifiers in source, examples, docs, scripts, comments, screenshots, or commit messages. Use variables and clearly fictional placeholders instead.
- Keep local `NuGet.Config` and `Properties/launchSettings.json` files outside Git. A portable configuration example must contain no credentials or machine paths and needs explicit review before it is added.
- Use `Path.Combine` and platform-aware path comparisons in shared tutorial code. Do not assume a Windows drive or a particular user's cache location.
- A public installation command must use a package version available anonymously from the stated feed. If a tutorial targets an unpublished release candidate, label that branch and its prerequisites clearly; do not present it as a ready-to-run public release.

`.gitignore` is a safety net, not permission to add an ignored file with `git add -f`. Before every commit, stage only the intended files by name, inspect `git diff --cached --name-status`, run `git diff --cached --check`, and read `git diff --cached`. Inspect each new or binary file separately. If a value might be sensitive, replace it with a safe example before committing; never paste the value into chat or a shared log.

If sensitive data reaches a commit, treat it as exposed and revoke or rotate credentials immediately. Remove the offending commit or rewrite it so the data is absent from every affected branch and tag history. A `git revert`, a later deletion commit, or a local `git reset` after pushing does not clean the server. Coordinate the rewrite with collaborators, update affected remote refs with `--force-with-lease`, and inspect all branches and tags in a fresh clone. Check pull request refs, forks, other clones, CI logs, artifacts, and caches; ask the hosting provider to remove unreachable objects and cached views when needed. Do not claim complete erasure while any copy remains unverified. Follow [GitHub's sensitive-data removal procedure](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).
