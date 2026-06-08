# Directory Batch Processing

The example is intentionally minimal.

It shows only one pattern:

1. start one chat session
2. send images one by one into that session
3. print the reply
4. optionally call `ForceCacheCleanup(...)` after each image

The project always uses the same bundled images:

- `assets/images/route-map.png`
- `assets/images/shapes-note.png`
- `assets/images/status-board.png`
- `assets/images/warehouse-labels.png`

Run:

```bash
cd 05-tuning/DirectoryBatchProcessing
dotnet run
```

What the code demonstrates:

- one `AsposeLLMApi` for the whole batch
- one chat session reused for the sample loop
- optional explicit cache cleanup after every image
- fixed `ContextSize = 8192`
- RAM / VRAM telemetry via shared [`Telemetry.cs`](/D:/source/Aspose.LLM-API-Tutorials/_shared/Aspose.LLM.Tutorials.Common/Telemetry.cs:1)

What it does not try to demonstrate:

- custom folders
- text processing
- advanced error handling
