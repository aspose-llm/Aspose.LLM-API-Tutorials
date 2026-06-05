# Directory Batch Processing

Process a small fixed batch of independent asset files with one loaded model,
one fresh session per item, and explicit cleanup checkpoints.

This tutorial answers a practical question:

> How do I process many unrelated text files or images one by one without
> letting memory pressure grow forever?

The pattern shown here is:

- keep one `AsposeLLMApi` alive for the batch
- open a fresh session per item
- use a fixed context size
- run forced cleanup in explicit phases
- watch RAM / VRAM between sessions

There is no public per-session `CloseSession` API today. Per-item cleanup
prunes session cache, but model weights stay resident until the whole
`AsposeLLMApi` is disposed.

## What was simplified

This version is intentionally tied to bundled sample assets instead of scanning
arbitrary directories. That keeps the code focused on lifecycle and memory
control, not on file discovery.

It runs only two shapes:

- `dotnet run` for the bundled image samples
- `dotnet run -- --mode text` for the bundled text samples

## Fixed sample files

Image mode always processes these files:

- `assets/images/route-map.png`
- `assets/images/shapes-note.png`
- `assets/images/status-board.png`
- `assets/images/warehouse-labels.png`

Text mode always processes these files:

- `assets/text/incident-summary.txt`
- `assets/text/ops-shift.log`
- `assets/text/project-brief.txt`
- `assets/text/release-notes.md`

## Minimal prechecks

Only the necessary guards remain:

- bundled asset file must exist
- text file must not be empty
- text file must fit the fixed context budget approximately
- image file must stay below the SDK media limit of `50 MB`

Everything else was removed on purpose.

## Fixed context sizes

The tutorial pins conservative context sizes:

- image mode: `8192`
- text mode: `4096`

This matters because KV cache size scales with context length. A larger
context window means more RAM / VRAM pressure even when each item is processed
in isolation.

## Cleanup phases

The output makes the cleanup phases explicit:

1. `Post-item snapshot`
2. `Emergency cleanup`
3. `Periodic cleanup after 3 items`
4. `Pressure cleanup between sessions`
5. `Final cleanup`

The pressure cleanup phase is the important one for batch workloads:
after each independent session, the tutorial checks memory again and forces
cleanup before the next session if VRAM or private bytes keep drifting upward.

## Logging

The sample creates an `ILogger<AsposeLLMApi>` through `LoggerFactory` and sets
its minimum level to `Warning` before passing it into `AsposeLLMApi`.

That keeps the managed SDK side near-silent so the tutorial output stays
focused on batch lifecycle and memory telemetry. Native `llama.cpp` lines, if
they appear for a specific backend or model, are a separate channel.

## Run it

```bash
cd 05-tuning/DirectoryBatchProcessing

# Bundled sample images
dotnet run

# Bundled sample text files
dotnet run -- --mode text
```

## What not to infer from the sample

- Forced cleanup is not the same as unloading model weights.
- VRAM does not need to fall back to zero between items.
- Independent files should not share one long chat history.
- Oversized context is part of the problem, not just a neutral default.
