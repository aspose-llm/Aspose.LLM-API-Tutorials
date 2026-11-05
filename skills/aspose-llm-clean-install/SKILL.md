---
name: aspose-llm-clean-install
description: Validate Aspose.LLM tutorials as a fresh .NET consumer using the signed NuGet artifact from the matching GitLab pipeline and a local private feed. Use for clean package-installation, tutorial compatibility, or consumer smoke requests; do not use for SDK unit tests alone.
---

# Clean Aspose.LLM tutorial installation

Find the `Aspose.LLM-API-Tutorials` checkout and read its
`docs/clean-private-nuget-validation.md` before running. The repository script
`scripts/Test-CleanPrivateNuGet.ps1` is the executable procedure. Its default
feed is supplied with `-PrivateFeed`. Also supply `-SdkRepository`,
`-GitLabUrl`, and `-GitLabProject` for the environment being tested.

Use the SDK commit the user names, or the SDK checkout's `HEAD` if none is
named. The package must come from that commit's successful GitLab pipeline and
`validate-publish-artifacts` job. Never substitute a locally packed DLL or a
similarly numbered package from another pipeline. Keep the artifact SHA,
pipeline ID, and job ID in the result.

Run the script in a fresh test directory. It verifies package signature and
hash, installs through an isolated NuGet cache, builds a new console consumer
using tutorial sources, then builds a clean copy of the tutorial solution.
Use `-TargetFramework net10.0` when the user asks for the .NET 10 variant;
the script selects an SDK and propagates `TutorialTargetFramework` through
project references. The default remains `net8.0`.
Keep pre-existing repository edits intact. Do not put Aspose licence files or
third-party model weights in the feed or test directory. Inference requires a
valid licence and an already obtained small GGUF; use `-SkipInference` only
when the request is build-only or those prerequisites are unavailable, and
report the skipped step. Do not silently download a model.

If the matching pipeline/job is missing, signature verification fails, or
the feed already contains a different package with the same version, stop and
report that condition. Do not overwrite the feed file or fall back to a source
build. Report the fresh restore/build outcomes and the actual inference result
when run, with the printed test directory for logs.
