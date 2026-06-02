# Contributing &amp; building

SimdUnicode is an open-source project under the
[Apache 2.0 License](https://github.com/simdutf/SimdUnicode/blob/main/LICENSE).
Contributions, issues, and benchmark reports from new hardware are welcome.

## Building the library

```bash
cd src
dotnet build
```

## Running the tests

```bash
dotnet test
```

See which tests run by raising the verbosity:

```bash
dotnet test -v=normal     # normal
dotnet test -v d          # detailed
dotnet test --list-tests  # list available tests
```

Filter to a specific test or category:

```bash
dotnet test --filter TooShortErrorAvx2
dotnet test --filter "Category=scalar"
```

## Running the benchmarks

```bash
cd benchmark
dotnet run -c Release
```

Filter to a single input:

```bash
dotnet run -c Release --filter "*Twitter*"
dotnet run -c Release --filter "*Lipsum*"
```

On macOS or Linux you may want privileged mode for hardware counters:

```bash
cd benchmark
sudo dotnet run -c Release
```

## Code formatting

```bash
dotnet format
```

## Performance notes

A few hard-won tips when working on the SIMD kernels:

- `Vector128.Shuffle` is **not** the same as `Ssse3.Shuffle`, nor is `Vector256.Shuffle`
  the same as `Avx2.Shuffle`. Prefer the architecture-specific intrinsics.
- Likewise, `Vector128.Shuffle` differs from `AdvSimd.Arm64.VectorTableLookup`; use the latter on ARM.
- Avoid `stackalloc` arrays in class instances.
- Prefer `struct` over `class` to make thread-local data explicit.
- Dump JIT assembly with `DOTNET_JitDisasm=...` and gather profiling data with `dotnet run -c Release -- -p EP`.

## Further reading

- [Add optimized UTF-8 validation and transcoding APIs (dotnet/coreclr#21948)](https://github.com/dotnet/coreclr/pull/21948)
- [dotnet/runtime#41699](https://github.com/dotnet/runtime/issues/41699)
- [.NET framework design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/)
