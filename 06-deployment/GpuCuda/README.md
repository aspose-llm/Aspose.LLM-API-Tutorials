# GPU Deployment — CUDA

Configures the SDK for NVIDIA CUDA inference: full-layer offload, multi-GPU
split template, Flash Attention and KQV offload turned on.

## Hardware prerequisites

- NVIDIA GPU with a CUDA-capable driver installed (any recent Game Ready
  or Studio driver does it).
- Enough VRAM for `model weights + KV cache`. Table for Qwen 2.5 7B
  `Q4_K_M` (~5 GB weights) at 4k context with F16 KV:

  | GPU              | VRAM  | Fits? | Notes                                   |
  | ---------------- | ----- | ----- | --------------------------------------- |
  | RTX 3060         | 12 GB | ✅    | Comfortable                             |
  | RTX 3060         | 6 GB  | ✗     | Use `GpuLayers = 24` for CPU/GPU split  |
  | RTX 4070         | 12 GB | ✅    | Comfortable                             |
  | RTX 4090         | 24 GB | ✅    | Lots of headroom — raise context to 32k |
  | Tesla T4         | 16 GB | ✅    | Server-grade option                     |

  For larger models (Qwen 3 8B, Llama 3 70B), size up accordingly.

## What it demonstrates

- **`BinaryManagerParameters.PreferredAcceleration = AccelerationType.CUDA`**
  — forces the CUDA-flavoured native binary. Override auto-detection when
  it guesses wrong.
- **`BaseModelInferenceParameters.GpuLayers = -1`** — offload every
  transformer layer. For partial offload, set a positive integer.
- **`MainGpu` / `TensorSplit`** — primary GPU index and multi-GPU split.
- **`ContextParameters.FlashAttention = true`** and **`OffloadKqv = true`**
  — both pay off dramatically on NVIDIA hardware.

## Run it

```bash
cd 06-deployment/GpuCuda

# First run downloads the CUDA binary (~500 MB) + Qwen 2.5 7B (~4.5 GB).
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

On a CUDA host the load-time log lines will mention `CUDA`, `ggml_cuda`,
and `offloading N layers to GPU`. The first-reply time typically lands in
the 1-3 s range on a 4070-class card for Qwen 2.5 7B.

On a macOS / CPU-only host: the SDK will note `No CUDA device found` (or
similar) and load the CPU runtime instead. The configuration is still
*correct*; it just can't run on a box without the hardware.

## Multi-GPU

Uncomment the multi-GPU block in `Program.cs`:

```csharp
preset.BaseModelInferenceParameters.SplitMode = LlmSplitMode.LLM_SPLIT_MODE_LAYER;
preset.BaseModelInferenceParameters.TensorSplit = new[] { 0.6f, 0.4f };
```

`TensorSplit` sums should equal 1.0 across the GPUs you want to use. A
split of `{0.6f, 0.4f}` on a pair puts 60% of the weights on GPU 0 and
40% on GPU 1 — sensible when GPU 0 has more VRAM than GPU 1.

`SplitMode` options:

- `LLM_SPLIT_MODE_LAYER` — partition by layers (default, best for
  most models).
- `LLM_SPLIT_MODE_ROW` — partition attention rows (niche, higher
  memory pressure on one GPU).
- `LLM_SPLIT_MODE_NONE` — don't split; ignore TensorSplit. Use when
  you want `MainGpu` to own everything.

## Partial offload (low-VRAM fallback)

If the model doesn't fit entirely, set `GpuLayers` to a positive count
below the model's layer total. Layers not offloaded run on CPU; the
boundary is automatically chosen by the engine to keep the split
efficient. Examples for a 32-layer model:

```csharp
preset.BaseModelInferenceParameters.GpuLayers = 32;  // all on GPU (= -1)
preset.BaseModelInferenceParameters.GpuLayers = 24;  // 24 on GPU, 8 on CPU
preset.BaseModelInferenceParameters.GpuLayers = 0;   // all on CPU (= default)
```

Iterate downward until the model loads without OOM. The tradeoff is
first-token latency and throughput: each layer that has to cross the
PCIe boundary per token costs a few ms.

## CUDA version pinning

Most users don't need this. If your driver is older than what the default
CUDA build targets, look at the SDK's `CudaVersion` enum (in
`Aspose.LLM.Abstractions.Acceleration`) and pin via
`BinaryManagerParameters.SystemSpecification`. Usually not needed — the
SDK ships multiple CUDA builds and picks at load time.

## Things to try

- Set `GpuLayers = 0` and rerun to time pure-CPU against full-offload.
  Speedup is usually 5-20× on a mid-range card.
- Switch `FlashAttention` on and off and measure. On a 3060/4060 you'll
  see a 20-40% throughput delta.
- Swap to `Qwen3Preset` (reasoning model). With `<think>` tokens
  consuming 300-500 tokens per reply, the speed-up from full offload is
  even more valuable.
- Monitor with `nvidia-smi dmon -s u` while the program runs — watch SM
  utilisation peg at 90%+ during generation and VRAM usage stabilise at
  weights + KV cache.
