# API reference

SimdUnicode exposes a small, focused public surface in the `SimdUnicode` namespace.

## Core types

- [`SimdUnicode.UTF8`](xref:SimdUnicode.UTF8) — UTF-8 validation. The main entry point is
  `GetPointerToFirstInvalidByte`, with architecture-specific kernels
  (`...Avx512`, `...Avx2`, `...Sse`, `...Arm64`, `...Scalar`).
- [`SimdUnicode.Ascii`](xref:SimdUnicode.Ascii) — fast ASCII detection and scanning, including
  `GetIndexOfFirstNonAsciiByte` and its per-architecture variants, plus `IsAscii`/`SIMDIsAscii`
  extension methods.

The members below are generated directly from the source XML documentation comments.
