# Offline Deployment

Run the SDK on a machine with no internet by pre-populating both caches on
a host that does have internet, then pointing the SDK at local paths.

## What it demonstrates

- Resolving a local `.gguf` from the tutorial cache directory.
- Setting `BaseModelSourceParameters.ModelFilePath` — the highest-priority
  entry in `ModelSourceParameters`. When it's present, the SDK never tries
  `AsposeModelId` or `HuggingFaceRepoId` lookup.
- Explicitly setting `BinaryManagerParameters.BinaryPath` to the cached
  native-runtime directory so the `BinaryManager` reuses the binaries
  already on disk instead of contacting the `ggml-org/llama.cpp` GitHub
  release assets.
- **Verification**: if you run this on a host with no network, it still
  works — because every file it needs is local.

## The two caches

| Cache                | Default location                                           | What it holds                         |
| -------------------- | ---------------------------------------------------------- | ------------------------------------- |
| Model cache          | `$TMPDIR/aspose-llm-tutorials-models` (set by `TutorialEnvironment`) | `.gguf` model files                   |
| Binary cache         | `%LocalAppData%/Aspose.LLM/runtimes`                       | Native llama.cpp DLLs/dylibs per arch |

The tutorial discovers the model cache via `TutorialEnvironment.ResolveModelCachePath()`
and the binary cache via the SDK's documented default path.

## Pre-populating the caches

On a host with internet:

```bash
# Populates both caches for Qwen 2 0.5B (~400 MB model + ~100 MB binaries).
cd 01-getting-started/HelloWorld
dotnet run
```

Then copy the two directories to the offline host:

```bash
# Adjust paths for your platform; these are macOS examples.
rsync -a "$TMPDIR/aspose-llm-tutorials-models/" offline-host:~/cache/models/
rsync -a "$HOME/Library/Application Support/Aspose.LLM/runtimes/" offline-host:~/cache/runtimes/
```

On the offline host, point the SDK at those paths with `ASPOSE_LLM_MODEL_CACHE`
and a `BinaryManagerParameters.BinaryPath` override.

## Run it

```bash
cd 06-deployment/OfflineDeployment
dotnet run
```

If the cache is present:

```
[licence] applied from /.../License.lic
Model cache:  /var/folders/.../aspose-llm-tutorials-models
Model file:   /var/folders/.../Qwen2-0.5B-Instruct.Q4_K_M.gguf (397 MB)
Binary cache: /Users/you/Library/Application Support/Aspose.LLM/runtimes (exists)

Loading model with explicit local paths (no network lookups expected)...

You > Introduce yourself in one sentence.
Bot < I'm a small language model assistant, ready to help you offline.
```

If the cache is missing the program exits with a pointer to HelloWorld
instead of crashing — it's safer to fail fast than to secretly hit the
network from "an offline demo".

## Proving it's actually offline

To be sure:

```bash
# Disable networking on macOS (requires admin):
sudo route add -host releases.aspose.com 127.0.0.1
sudo route add -host huggingface.co    127.0.0.1
sudo route add -host github.com        127.0.0.1
# ... or just pull the Ethernet cable / disable Wi-Fi.

# Then rerun:
dotnet run

# Clean up:
sudo route delete -host releases.aspose.com
sudo route delete -host huggingface.co
sudo route delete -host github.com
```

If the SDK still answers, you're fully offline-capable.

## What if the binary cache is missing?

The native llama.cpp runtime is per-OS / per-arch / per-acceleration-type.
Pre-populate by running the SDK once on a host with the same OS+arch as
the target. The binary cache layout uses the `ReleaseTag`, OS, arch, and
acceleration in the path — switch tags and you need fresh binaries.

If you cannot provision internet even once, extract the release manually:

```bash
# From a system with internet, download the matching release artefact:
curl -LO https://github.com/ggml-org/llama.cpp/releases/download/b8816/llama-b8816-bin-macos-arm64.zip
unzip llama-b8816-bin-macos-arm64.zip -d ~/cache/runtimes/b8816/macos-arm64/
```

Then on the offline host set `BinaryPath` to the directory you built and
the `BinaryManager` will trust it.

## Things to try

- Comment out the `HuggingFaceRepoId = null` lines and see that the SDK
  still works — `ModelFilePath` already has first-priority.
- Change `preset.BinaryManagerParameters.ReleaseTag` to a different tag
  (e.g. `b8000`) and watch the SDK complain that the binary cache
  doesn't have those specific artefacts.
- Move the model file to an unexpected path (e.g. on a USB stick) and
  point `ModelFilePath` at that — the SDK treats it as just another
  absolute path.
