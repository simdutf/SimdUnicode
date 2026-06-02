# How it works

## The problem

UTF-8 is a variable-length encoding: a character spans one to four bytes, and not every
byte sequence is legal. Validation has to reject overlong encodings, stray continuation
bytes, surrogate code points, and out-of-range values. Done naïvely — one branch per byte —
this is slow and branch-mispredict heavy.

## Less than one instruction per byte

SimdUnicode implements the algorithm from:

> John Keiser, Daniel Lemire, [*Validating UTF-8 In Less Than One Instruction Per Byte*](https://arxiv.org/abs/2010.03090), Software: Practice and Experience 51 (5), 2021.

The same algorithm powers UTF-8 validation in **Node.js**, **Bun**, **Oracle GraalVM**, and
the **PHP interpreter**. The key idea is to process 16, 32, or 64 bytes at once with SIMD
lookup tables, checking the structural rules of UTF-8 with a handful of vector instructions
instead of per-byte branches.

At a high level, each vectorized block:

1. Detects ASCII fast-paths (a run of bytes `< 0x80` is trivially valid).
2. Classifies each byte by its high nibble and the preceding byte using shuffle-based lookups.
3. Verifies continuation-byte counts and rejects illegal ranges (overlong, surrogate, > U+10FFFF).
4. Accumulates the UTF-16 and scalar code-unit adjustments so the caller learns the decoded
   length without a second pass.

## Runtime dispatch

A single public method picks the best kernel for the host CPU, in priority order:

```text
ARM64 NEON  →  AVX-512 (VBMI)  →  AVX2  →  SSE4.2 / SSSE3  →  scalar fallback
```

This means you write one call and automatically get AVX-512 on a Zen 4 / Ice Lake server,
NEON on an Apple M-series laptop, and a correct scalar implementation everywhere else.

| Back-end | Vector width | Typical hardware |
|----------|--------------|------------------|
| AVX-512 (VBMI) | 512-bit | AMD Zen 4, Intel Ice Lake+ |
| AVX2 | 256-bit | Most current x64 |
| SSE4.2 / SSSE3 | 128-bit | Older x64 |
| ARM64 NEON | 128-bit | Apple Silicon, AWS Graviton, Snapdragon |
| Scalar | — | Portable fallback |

## Why a pointer, not a bool?

Returning a pointer to the first invalid byte is strictly more informative than a boolean:
callers can report the exact error offset, resume parsing, or measure how much of a stream
was valid. When the buffer is well-formed the function returns a pointer to its end, which
makes the "is this valid?" check a single pointer comparison.

See the [API reference](xref:SimdUnicode.UTF8) for every available kernel, and the
[benchmarks](benchmarks.md) for measured throughput.
