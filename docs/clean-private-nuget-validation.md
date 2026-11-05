# Clean private NuGet validation

Use this procedure to reproduce a developer installing the Aspose.LLM version
pinned by the tutorials. The package must come from the successful GitLab
`validate-publish-artifacts` job for the SDK commit being tested. A package
assembled from local DLLs does not prove the release artifact works.

## Prerequisites

- Windows PowerShell and the SDK for the target framework: .NET 8 by default,
  or .NET 10 when testing `net10.0` (`dotnet --list-sdks`). The script can also
  find a user-local `dotnet.exe`; pass `-DotnetPath` if needed.
- Read access to the GitLab project that builds the SDK. Set `GITLAB_TOKEN` in
  the current process. The script never prints or saves its value.
- A local SDK checkout and a writable local package feed. Supply their paths
  through `-SdkRepository` and `-PrivateFeed`.
- The GitLab server URL and SDK project path, supplied through `-GitLabUrl`
  and `-GitLabProject`.
- For the inference step: set `ASPOSE_LLM_LICENSE` to a valid licence file and
  keep the small GGUF used by `HelloWorld` in the model cache described in the
  root README. The script does not acquire or redistribute model weights.
  Pass `-SkipInference` when only package installation and compilation are needed.

## Run

From the tutorials repository root, set the four variables for your own
environment and use the same arguments for each target framework:

```powershell
.\scripts\Test-CleanPrivateNuGet.ps1 `
    -SdkRepository $sdkRepository `
    -PrivateFeed $privateFeed `
    -GitLabUrl $gitLabUrl `
    -GitLabProject $gitLabProject

# Repeat the same check with a clean .NET 10 consumer and tutorial solution:
.\scripts\Test-CleanPrivateNuGet.ps1 `
    -SdkRepository $sdkRepository `
    -PrivateFeed $privateFeed `
    -GitLabUrl $gitLabUrl `
    -GitLabProject $gitLabProject `
    -TargetFramework net10.0
```

The script defaults to SDK `HEAD`; use `-CommitSha <full-40-character-sha>` to
test another pipeline. The package version comes from `Directory.Build.props`.
Use `-LicensePath`, `-ModelCachePath`, or `-RuntimeCachePath` to override the
corresponding runtime inputs. Each invocation creates a unique directory under
`%TEMP%` and prints its path. Existing test directories are never overwritten.
`-TargetFramework net10.0` selects a .NET 10 SDK and retargets the clean
tutorial copy through `TutorialTargetFramework`; it leaves the repository's
default `net8.0` setting intact.

## What the script verifies

1. Finds a successful pipeline for the exact SDK commit and the successful
   `validate-publish-artifacts` job in that pipeline. Confirms the job commit SHA.
2. Downloads that job's artifact archive; checks the package ID and pinned
   version in its nuspec, the pipeline version file, and the NuGet signature.
3. Copies the signed `.nupkg` to the private feed only if the version is absent.
   An existing file with a different SHA-256 causes a failure. The downloaded,
   feed, and installed package hashes must match.
4. Creates a new console project for the selected .NET version, adds the exact
   package version, and
   restores it with an empty `NUGET_PACKAGES` directory and HTTP cache. A
   generated `NuGet.Config` maps `Aspose.LLM` to the private feed and other
   dependencies to nuget.org. The project uses the actual `HelloWorld` and
   shared tutorial source files. It builds and, unless skipped, sends one prompt.
5. Copies the tracked tutorial files from the current working tree to a fresh
   directory, generates its NuGet configuration, restores with a *second* empty
   package cache, checks the package hash again, and builds the whole solution.

The fresh copy includes edits to tracked files that have not been committed.
It excludes untracked files, `.git`, and old `bin`/`obj` outputs. A generated
NuGet configuration replaces local machine settings, including an existing
untracked `NuGet.Config`. A `global.json` in the test directory selects the
latest installed SDK for the requested target framework.

`provenance.json` records the SDK commit, pipeline, job, package version, and
SHA-256. Logs for every .NET command and the inference output remain in the
printed test directory. A successful run ends with `PASS clean private NuGet
consumer test`.

## Codex skill

The reusable skill is versioned at
`skills/aspose-llm-clean-install/SKILL.md`. Install its folder into your Codex
skills directory, then invoke `$aspose-llm-clean-install` for later checks.
Install the skill in your Codex skills directory according to your local setup.

## Scope of the clean test

Project files, build outputs, NuGet global packages, and NuGet HTTP caches are
fresh for each invocation. Inference intentionally reuses an existing licensed
model file and may reuse the native runtime cache. This isolates package
installation from model acquisition. If the native runtime is absent, the SDK
may download it on first use. A separate first-model-download test requires
explicit selection and acceptance of that model's licence; do not put weights
or licence files into the NuGet feed or test artifacts.

This workflow requires access to a GitLab pipeline that produced the pinned
package. It is intended for validating a release candidate before that
version appears on a public NuGet feed. Public consumers should install a
version available from their configured package source.
