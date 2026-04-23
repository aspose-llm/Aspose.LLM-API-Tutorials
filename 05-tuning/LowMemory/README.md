# Low-Memory Tuning

Same model, two configs, back-to-back RSS measurements. Demonstrates the
three practical levers for shrinking an SDK process's memory footprint.

## What it demonstrates

- **`ContextParameters.ContextSize`** — the dominant lever. KV cache size
  is proportional to it.
- **`ContextParameters.TypeK` / `TypeV` = `GgmlType.Q8_0`** — quantises
  the KV cache from F16 to 8-bit, cutting KV memory in half. `Q4_0` halves
  it again at a quality cost.
- **`UseMemoryMapping = true`** — the default. `mmap` lets the OS page
  model weights in and out of RSS; turning it off pulls the entire GGUF
  into resident memory.
- **Process RSS comparison** via `Process.WorkingSet64`, with `GC.Collect`
  to ensure readings reflect the model, not transient garbage.

## Run it

```bash
cd 05-tuning/LowMemory
dotnet run
```

Sample output (macOS, Qwen 2 0.5B Q4_K_M):

```
── Baseline  (ctx=4096, KV=F16) ──
  First-reply time:  350 ms
  Reply sample:     'OK'
  Process RSS:      610 MB

── Low-mem   (ctx=1024, KV=Q8_0) ──
  First-reply time:  290 ms
  Reply sample:     'OK'
  Process RSS:      460 MB

── Summary ──
  Baseline RSS after load:     610 MB
  Low-mem  RSS after load:     460 MB
  Delta:                       150 MB  (24.6% smaller)
```

Absolute numbers vary. On Linux with `UseMemoryLocking = true` the baseline
would be higher; on Windows without mmap it would be much higher.

## The three levers in detail

### Lever 1 — context size

KV cache RAM is roughly:

```
ctx_size × n_layers × (d_model / n_heads) × n_heads × 2 (K + V) × bytes_per_element
```

For Qwen 2 0.5B (24 layers, d_model 896, 14 heads, 64 per head) at F16:

| Context | KV cache size (F16) |
| ------- | ------------------- |
| 1 024   | ~90 MB              |
| 4 096   | ~360 MB             |
| 16 384  | ~1.4 GB             |
| 32 768  | ~2.8 GB             |

Shrink context to what the workload actually needs. A classification
pipeline rarely exceeds 512 tokens per request.

### Lever 2 — quantised KV cache

Set `TypeK = GgmlType.Q8_0` and `TypeV = GgmlType.Q8_0` to halve the KV
cache memory at roughly no measurable quality loss on most models. `Q4_0`
halves it again but causes visible degradation on small models (7B+
tolerates it much better).

Not all combinations are supported by every model or backend. If the SDK
throws at startup with a `GgmlType` complaint, back off to F16 for the
less-quantisable side (usually V).

### Lever 3 — memory mapping

Leaving `UseMemoryMapping = true` means the GGUF sits on disk and is
paged in on demand. RSS reflects only touched pages. When memory is tight,
the OS pages them back out.

Turning it off (`UseMemoryMapping = false`) reads the full GGUF into a
normal allocation at load time. Faster first token, but you pay full RSS
even when idle.

`UseMemoryLocking = true` pins the weights in RAM so they can't be
paged out. Good on production boxes with headroom, terrible on a
resource-constrained laptop.

## What's NOT a memory lever here

- **`OffloadKqv`** is a performance knob, not a memory knob. It chooses
  where the K/V math runs (CPU or GPU), not how much memory K/V consume.
- **Smaller quantisation of the base model** (Q4_0 vs Q8_0 GGUF) would cut
  *weights* memory by ~50% but requires downloading a different GGUF —
  that's a [`BringYourOwnGGUF`](../../04-presets/BringYourOwnGGUF/)
  concern, not a preset-tuning one.
- **Batch size (`NBatch`, `NUbatch`)** controls compute-buffer size which
  is small compared to KV cache. Usually not worth tuning for memory.

## Things to try

- Compare `TypeK=Q4_0; TypeV=Q4_0` vs `Q8_0` on the same model. Look at
  the reply sample — quality degrades visibly on 0.5B before 7B.
- Run with `UseMemoryMapping = false` and watch RSS jump by the full
  model size even when idle.
- At scale: spawn 10 processes with `ctx=1024, Q8 KV` on an 8 GB machine
  and see how many survive without the OOM killer intervening.
