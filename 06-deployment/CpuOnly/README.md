# CPU-Only Deployment

Sweeps `NThreads` across 1, 2, 4, and the machine's `ProcessorCount` to find
the sweet spot for CPU-only inference on the current host.

## What it demonstrates

- **`BaseModelInferenceParameters.GpuLayers = 0`** — explicit CPU mode.
  This is the `SmallModelPreset` default; the tutorial sets it again to
  make the intent obvious in code.
- **`ContextParameters.NThreads`** — the hot-path thread pool. Controls
  parallelism during token-by-token generation.
- **`ContextParameters.NThreadsBatch`** — the cold-path thread pool. Used
  for prefill (ingesting the prompt) and batch operations.
- **Total-time comparison** across thread counts. The shape of the curve
  tells you where your host stops benefiting from more threads.

## Why sweep?

CPU scaling is not linear. Past a certain count (usually = number of
*performance* cores), you hit either:

- Memory bandwidth ceiling — all threads are starving for RAM.
- Context-switching overhead — OS scheduling eats the wins.
- Efficiency cores running out of their thermal budget.

Hard-coding `NThreads = Environment.ProcessorCount` is wrong on
heterogeneous machines. Apple Silicon mixes 4-8 performance cores with
4 efficiency cores; AMD X3D chips have asymmetric cache layouts. The
right number is workload-specific, so measure.

## Run it

```bash
cd 06-deployment/CpuOnly
dotnet run
```

Real output on an Apple M-series laptop (10 logical processors — 8
performance + 2 efficiency):

```
System reports 10 logical processors. Sweeping NThreads ∈ {1, 2, 4, 10}.

── NThreads = 1 ──  Total time: 2,881 ms
── NThreads = 2 ──  Total time: 1,754 ms
── NThreads = 4 ──  Total time: 1,159 ms
── NThreads = 10 ── Total time: 4,503 ms   ← REGRESSION

  Threads │ Total time │ Speedup vs 1-thread
  ────────┼────────────┼────────────────────
       1  │   2,881 ms │   1.00×
       2  │   1,754 ms │   1.64×
       4  │   1,159 ms │   2.49×
      10  │   4,503 ms │   0.64×
```

Look at the 10-thread row — it's *slower* than 1 thread. When llama.cpp is
told to use 10 threads on a machine that only has 8 performance cores, it
schedules 2 threads onto the efficiency cores, which are ~3× slower. All
10 threads then sync at each layer boundary, so the slowest 2 threads
determine the critical path. The result: worse throughput than running
plain serially.

This is the single most common CPU-only mis-tuning in the wild. Always
measure; never assume `NThreads = Environment.ProcessorCount` is right.

## Other CPU knobs

- **`PreferredAcceleration = AccelerationType.None`** on
  `BinaryManagerParameters` — forces the SDK to download the plain-CPU
  native runtime variant instead of whatever GPU build the auto-detector
  picks. Use this on a host with a GPU you explicitly don't want the SDK
  to touch.
- **`AccelerationType.AVX2` / `AVX512` / `NoAVX`** — pin a specific
  CPU-ISA build. Useful for older CPUs without AVX, and for squeezing
  the last few percent on AVX-512 servers.
- **`ContextParameters.FlashAttention`** — false on CPU by default. The
  Flash Attention kernels shipped with llama.cpp are GPU-oriented;
  leaving FA off on CPU sometimes wins a few percent by avoiding extra
  bookkeeping.
- **`ContextParameters.OffloadKqv`** — the name is misleading for CPU
  mode. Leave it false when `GpuLayers = 0`.

## First-token time

The facade returns the full reply as a string; it doesn't expose a
per-token callback today. If you need first-token latency, you'd need
the low-level Engine API and its streaming surface — out of scope for
this pilot. The total time reported here is an end-to-end measurement
(prefill + generation + post-processing).

## Things to try

- Turn `EnableDebugLogging = true` in the preset and watch the llama.cpp
  log lines — they include per-layer timing that's much finer-grained
  than wall-clock.
- Pin the process to specific CPU cores (`taskset` on Linux,
  `CPU_AFFINITY_MASK` on Windows) so the OS scheduler can't move your
  NThreads around — gives more reproducible measurements.
- Turn on `UseMemoryLocking = true` and see if the steady-state throughput
  improves on a memory-pressured host. (Don't do this on a laptop with
  8 GB total — you'll swap.)
