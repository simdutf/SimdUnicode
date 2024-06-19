# SimdUnicode
[![.NET](https://github.com/simdutf/SimdUnicode/actions/workflows/dotnet.yml/badge.svg)](https://github.com/simdutf/SimdUnicode/actions/workflows/dotnet.yml)

This is a fast C# library to validate UTF-8 strings.


## Motivation

We seek to speed up the `Utf8Utility.GetPointerToFirstInvalidByte` function from the C# runtime library.
[The function is private in the Microsoft Runtime](https://github.com/dotnet/runtime/blob/4d709cd12269fcbb3d0fccfb2515541944475954/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Validation.cs), but we can expose it manually. The C# runtime 
function is well optimized and it makes use of advanced CPU instructions. Nevertheless, we propose
an alternative that can be several times faster.

Specifically, we provide the function `SimdUnicode.UTF8.GetPointerToFirstInvalidByte` which is a faster
drop-in replacement:
```cs
// Returns &inputBuffer[inputLength] if the input buffer is valid.
/// <summary>
/// Given an input buffer <paramref name="pInputBuffer"/> of byte length <paramref name="inputLength"/>,
/// returns a pointer to where the first invalid data appears in <paramref name="pInputBuffer"/>.
/// The parameter <paramref name="Utf16CodeUnitCountAdjustment"/> is set according to the content of the valid UTF-8 characters encountered, counting -1 for each 2-byte character, -2 for each 3-byte and 4-byte characters.
/// The parameter <paramref name="ScalarCodeUnitCountAdjustment"/> is set according to the content of the valid UTF-8 characters encountered, counting -1 for each 4-byte character.
/// </summary>
/// <remarks>
/// Returns a pointer to the end of <paramref name="pInputBuffer"/> if the buffer is well-formed.
/// </remarks>
public unsafe static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength, out int Utf16CodeUnitCountAdjustment, out int ScalarCodeUnitCountAdjustment);
```

The function uses advanced instructions (SIMD) on 64-bit ARM and x64 processors, but fallbacks on a
conventional implementation on other systems. We provide extensive tests and benchmarks.

We apply the algorithm used by Node.js, Bun, Oracle GraalVM, by the PHP interpreter and other important systems. The algorithm has been described in the follow article:

- John Keiser, Daniel Lemire, [Validating UTF-8 In Less Than One Instruction Per Byte](https://arxiv.org/abs/2010.03090), Software: Practice and Experience 51 (5), 2021


## Requirements

We recommend you install .NET 8 or better: https://dotnet.microsoft.com/en-us/download/dotnet/8.0


## Running tests

```
dotnet test
```

To see which tests are running, we recommend setting the verbosity level:

```
dotnet test -v=normal
```

More details could be useful:
```
dotnet test -v d
```

To get a list of available tests, enter the command:

```
dotnet test --list-tests
```

To run specific tests, it is helpful to use the filter parameter:


```
dotnet test --filter TooShortErrorAvx2
```

Or to target specific categories:

```
dotnet test --filter "Category=scalar"
```

## Running Benchmarks

To run the benchmarks, run the following command:
```
cd benchmark
dotnet run -c Release
```

To run just one benchmark, use a filter:

```
cd benchmark
dotnet run --configuration Release --filter "*Twitter*"
dotnet run --configuration Release --filter "*Arabic-Lipsum*"
```

If you are under macOS or Linux, you may want to run the benchmarks in privileged mode:

```
cd benchmark
sudo dotnet run -c Release
```

## Results (x64)

On x64 system, we offer several functions: a fallback function for legacy systems,
a SSE42 function for older CPUs, an AVX2 function for current x64 systems, and an
AVX-512 function for the most recent systems (AMD Zen 4, Intel Ice lake, etc.).

On an Intel Ice Lake system, our validation function is up to several times
faster than the standard library when using at least the AVX2 routines. Only on pure
ASCII inputs (Latin-Lipsum) is the standard library seemingly faster, but all functions
are effectively at "memory speed" so the difference is practically not significant.
A more realistic input is Twitter.json which is mostly ASCII with some Unicode content.

| data set        | SimdUnicode SSE42 (GB/s) | SimdUnicode AVX2 (GB/s) | .NET speed (GB/s) |
|:----------------|:-------------------------|:------------------------|-------------------|
| Twitter.json    |  16                      | 24                      | 12                |
| Arabic-Lipsum   |  4.9                     | 9.0                     | 2.3               |
| Chinese-Lipsum  |  4.9                     | 9.0                     | 3.9               |
| Emoji-Lipsum    |  4.3                     | 7.1                     | 0.9               |
| Hebrew-Lipsum   |  4.5                     | 8.0                     | 2.3               |
| Hindi-Lipsum    |  4.3                     | 8.0                     | 2.1               |
| Japanese-Lipsum |  4.5                     | 8.0                     | 3.5               |
| Korean-Lipsum   |  4.5                     | 8.0                     | 1.3               |
| Latin-Lipsum    |  50                      | 76                      | 96                |
| Russian-Lipsum  |  4.3                     | 8.0                     | 1.2               |

## Results (ARM)

On an Apple M2 system, our validation function is two to three times
faster than the standard library.

| data set      | SimdUnicode speed (GB/s) | .NET speed (GB/s) |
|:----------------|:-----------|:--------------------------|
| Twitter.json    |  25        | 14                        |
| Arabic-Lipsum   |  7.4       | 3.5                       |
| Chinese-Lipsum  |  7.4       | 4.8                       |
| Emoji-Lipsum    |  7.4       | 2.5                       |
| Hebrew-Lipsum   |  7.4       | 3.5                       |
| Hindi-Lipsum    |  7.3       | 3.0                       |
| Japanese-Lipsum |  7.3       | 4.6                       |
| Korean-Lipsum   |  7.4       | 1.8                       |
| Latin-Lipsum    |  87        | 38                        |
| Russian-Lipsum  |  7.4       | 2.7                       |


## Building the library

```
cd src
dotnet build
```

## Code format

We recommend you use `dotnet format`. E.g.,

```
dotnet format
```

## Programming tips

You can print the content of a vector register like so:

```C#
        public static void ToString(Vector256<byte> v)
        {
            Span<byte> b = stackalloc byte[32];
            v.CopyTo(b);
            Console.WriteLine(Convert.ToHexString(b));
        }
        public static void ToString(Vector128<byte> v)
        {
            Span<byte> b = stackalloc byte[16];
            v.CopyTo(b);
            Console.WriteLine(Convert.ToHexString(b));
        }
```

## Performance tips

- Be careful: `Vector128.Shuffle` is not the same as `Ssse3.Shuffle` nor is  `Vector256.Shuffle` the same as `Avx2.Shuffle`. Prefer the latter.
- Similarly `Vector128.Shuffle` is not the same as `AdvSimd.Arm64.VectorTableLookup`, use the latter.

## More reading 

- [Add optimized UTF-8 validation and transcoding apis, hook them up to UTF8Encoding](https://github.com/dotnet/coreclr/pull/21948/files#diff-2a22774bd6bff8e217ecbb3a41afad033ce0ca0f33645e9d8f5bdf7c9e3ac248)
- https://github.com/dotnet/runtime/issues/41699
- https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
- https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
