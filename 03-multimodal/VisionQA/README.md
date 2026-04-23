# Vision Q&A

Ask a multimodal model what's in an image. Uses `Gemma3VisionPreset`, which
loads both a language model and a vision projector so it can tokenise images
alongside text.

## What it demonstrates

- Using a **vision-capable preset** (`Gemma3VisionPreset`) that auto-downloads
  a `*.gguf` base model AND a matching `mmproj-*.gguf` projector.
- Passing image bytes through `SendMessageAsync(text, media: ...)`. The SDK
  detects the image format from magic bytes; you don't tell it `image/png`.
- Accepting either a bundled sample or a user-supplied CLI path — so users
  can run the demo immediately, then swap in their own images.

## First-run cost

**~3.2 GB** of downloads the first time:

| File                                  | Size   |
| ------------------------------------- | ------ |
| `Gemma-3-Vision-Latex.Q4_K_M.gguf`    | 2.6 GB |
| `Gemma-3-Vision-Latex.mmproj-f16.gguf`| 600 MB |

All files land in the shared `ASPOSE_LLM_MODEL_CACHE` directory and are reused
by any subsequent vision tutorial.

## Run it

```bash
cd 03-multimodal/VisionQA
dotnet run

# Or with your own image:
dotnet run -- /path/to/photo.jpg
```

Example output with the bundled `assets/three-shapes.png`:

```
==========================
  Aspose.LLM — Vision Q&A
==========================

[licence] applied from /.../License.lic
Image: /.../bin/Debug/net8.0/assets/three-shapes.png (4,371 bytes)
Model:     mradermacher/Gemma-3-Vision-Latex-GGUF/Gemma-3-Vision-Latex.Q4_K_M.gguf
Projector: mradermacher/Gemma-3-Vision-Latex-GGUF/Gemma-3-Vision-Latex.mmproj-f16.gguf
Cache: /var/folders/.../aspose-llm-tutorials-models
You > Describe this image in 2-3 sentences. Name the shapes and their colours, ...
Bot < I see a red circle in the upper left, a blue triangle in the upper right, and a green
      square at the bottom. Beneath them is the caption "three simple shapes".
```

## Bundled sample

[`assets/three-shapes.png`](assets/three-shapes.png) — a 480×320 PNG generated
with three distinct geometric shapes and a text label. Deliberately simple so
even a compact vision model describes it correctly.

## Image format support

`SendMessageAsync(string, IEnumerable<byte[]>?)` auto-detects the format from
magic bytes. Supported today (per `MediaAttachment.DetectFormat`):

- PNG (`89 50 4E 47`)
- JPEG (`FF D8`)
- BMP (`42 4D`)
- GIF (`47 49 46`)
- WebP (`RIFF`…`WEBP`)

Max attachment size is **50 MB** per image — the SDK throws if you pass more.

## Things to try

- Run the demo against a real-world photograph. Vision models handle busy
  photos less reliably than diagrams; expect more hedging.
- Ask the model for structured output: `"List every object you see, one per
  line, in the format: object — dominant colour."`
- Raise `preset.ChatParameters.MaxTokens` to 512 and ask for a long-form
  description.
- Multi-image: pass two byte arrays in the `media` enumerable and ask
  "Compare these two pictures." Support varies by model; some vision presets
  expect exactly one image per message.
