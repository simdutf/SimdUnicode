# Benchmarks

All numbers are validation throughput in **GB/s** (higher is better) measured with
[BenchmarkDotNet](https://benchmarkdotnet.org/). `Twitter.json` is a realistic, mostly-ASCII
document; the `*-Lipsum` inputs are dense single-script text that stresses the multi-byte paths.

To reproduce on your own machine:

```bash
cd benchmark
dotnet run -c Release
# or a single input:
dotnet run -c Release --filter "*Twitter*"
```

## x64 — Intel Xeon Gold 6548N (AVX-512, .NET 10)

Up to **14× faster** than the standard library on multibyte text; **2.5×** on realistic Twitter data.

| data set        | SimdUnicode AVX-512 (GB/s) | .NET (GB/s) | speed-up |
|:----------------|:--------------------------:|:-----------:|:--------:|
| Twitter.json    | 36 | 14  | 2.5× |
| Arabic-Lipsum   | 15 | 3.3 | 4.5× |
| Chinese-Lipsum  | 15 | 5.3 | 2.8× |
| Emoji-Lipsum    | 15 | 1.1 | 14×  |
| Hebrew-Lipsum   | 15 | 3.2 | 4.5× |
| Hindi-Lipsum    | 15 | 2.5 | 5.8× |
| Japanese-Lipsum | 15 | 4.5 | 3.2× |
| Korean-Lipsum   | 15 | 1.6 | 9.1× |
| Latin-Lipsum    | 72 | 107 | — |
| Russian-Lipsum  | 15 | 3.3 | 4.5× |

On pure ASCII (Latin-Lipsum), .NET 10's own UTF-8 scanner is faster.

On x64 SimdUnicode ships four kernels — a scalar fallback for legacy systems, SSE4.2 for
older CPUs, AVX2 for current x64, and AVX-512 for the most recent processors (AMD Zen 4 or
better, Intel Ice Lake, etc.).

## ARM — Apple M2 (NEON)

1.5×–4× faster than the standard library.

| data set        | SimdUnicode (GB/s) | .NET (GB/s) | speed-up |
|:----------------|:------------------:|:-----------:|:--------:|
| Twitter.json    | 25  | 14  | 1.8× |
| Arabic-Lipsum   | 7.4 | 3.5 | 2.1× |
| Chinese-Lipsum  | 7.4 | 4.8 | 1.5× |
| Emoji-Lipsum    | 7.4 | 2.5 | 3.0× |
| Hebrew-Lipsum   | 7.4 | 3.5 | 2.1× |
| Hindi-Lipsum    | 7.3 | 3.0 | 2.4× |
| Japanese-Lipsum | 7.3 | 4.6 | 1.6× |
| Korean-Lipsum   | 7.4 | 1.8 | 4.1× |
| Latin-Lipsum    | 87  | 38  | 2.3× |
| Russian-Lipsum  | 7.4 | 2.7 | 2.7× |

## ARM — AWS Graviton 3 (Neoverse V1)

1.2× to over 5× faster than the standard library.

| data set        | SimdUnicode (GB/s) | .NET (GB/s) | speed-up |
|:----------------|:------------------:|:-----------:|:--------:|
| Twitter.json    | 19  | 11  | 1.7× |
| Arabic-Lipsum   | 5.2 | 2.7 | 1.9× |
| Chinese-Lipsum  | 5.2 | 4.5 | 1.2× |
| Emoji-Lipsum    | 5.2 | 0.9 | 5.8× |
| Hebrew-Lipsum   | 5.2 | 2.7 | 1.9× |
| Hindi-Lipsum    | 5.2 | 2.4 | 2.2× |
| Japanese-Lipsum | 5.2 | 3.9 | 1.3× |
| Korean-Lipsum   | 5.2 | 1.5 | 3.5× |
| Latin-Lipsum    | 57  | 26  | 2.2× |
| Russian-Lipsum  | 5.2 | 2.8 | 1.9× |

## ARM — Qualcomm 8cx Gen 3 (Windows Dev Kit 2023)

| data set        | SimdUnicode (GB/s) | .NET (GB/s) | speed-up |
|:----------------|:------------------:|:-----------:|:--------:|
| Twitter.json    | 17  | 10  | 1.7× |
| Arabic-Lipsum   | 5.0 | 2.3 | 2.2× |
| Chinese-Lipsum  | 5.0 | 2.9 | 1.7× |
| Emoji-Lipsum    | 5.0 | 0.9 | 5.5× |
| Hebrew-Lipsum   | 5.0 | 2.3 | 2.2× |
| Hindi-Lipsum    | 5.0 | 1.9 | 2.6× |
| Japanese-Lipsum | 5.0 | 2.7 | 1.9× |
| Korean-Lipsum   | 5.0 | 1.5 | 3.3× |
| Latin-Lipsum    | 50  | 20  | 2.5× |
| Russian-Lipsum  | 5.0 | 1.2 | 5.2× |

## ARM — AWS Graviton 2 (Neoverse N1)

| data set        | SimdUnicode (GB/s) | .NET (GB/s) | speed-up |
|:----------------|:------------------:|:-----------:|:--------:|
| Twitter.json    | 12  | 8.7 | 1.4× |
| Arabic-Lipsum   | 3.4 | 2.0 | 1.7× |
| Chinese-Lipsum  | 3.4 | 2.6 | 1.3× |
| Emoji-Lipsum    | 3.4 | 0.8 | 4.3× |
| Hebrew-Lipsum   | 3.4 | 2.0 | 1.7× |
| Hindi-Lipsum    | 3.4 | 1.6 | 2.1× |
| Japanese-Lipsum | 3.4 | 2.4 | 1.4× |
| Korean-Lipsum   | 3.4 | 1.3 | 2.6× |
| Latin-Lipsum    | 42  | 17  | 2.5× |
| Russian-Lipsum  | 3.3 | 0.95| 3.5× |

> Hardware, compiler and input all affect these numbers. Treat the tables as representative,
> and measure on your own target hardware for decisions that matter.
