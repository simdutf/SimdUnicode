# Getting started

SimdUnicode is a small, dependency-free C# library that validates UTF-8 with SIMD
instructions. It targets **.NET 10** (or better) and runs on x64 and ARM64.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) or newer.
- A 64-bit x64 or ARM64 CPU for the SIMD kernels (a portable scalar fallback covers everything else).

## Build &amp; reference

Clone the repository and build the library:

```bash
git clone https://github.com/simdutf/SimdUnicode.git
cd SimdUnicode/src
dotnet build -c Release
```

Then add a project reference to `src/SimdUnicode.csproj` from your own project:

```bash
dotnet add reference path/to/SimdUnicode/src/SimdUnicode.csproj
```

## Validating a buffer

The core entry point is [`UTF8.GetPointerToFirstInvalidByte`](xref:SimdUnicode.UTF8).
It scans `pInputBuffer` and returns a pointer to the **first invalid byte**, or a pointer to
the end of the buffer when the input is well-formed.

```csharp
using SimdUnicode;

static unsafe bool IsValidUtf8(ReadOnlySpan<byte> data)
{
    fixed (byte* p = data)
    {
        byte* end = UTF8.GetPointerToFirstInvalidByte(
            p, data.Length,
            out _ /* utf16 code-unit adjustment */,
            out _ /* scalar code-unit adjustment */);

        return end == p + data.Length;
    }
}
```

### The out parameters

| Parameter | Meaning |
|-----------|---------|
| `Utf16CodeUnitCountAdjustment` | Add this to the byte count to get the number of UTF-16 code units. Counts `-1` for each 2-byte character and `-2` for each 3- or 4-byte character. |
| `ScalarCodeUnitCountAdjustment` | Add this to the byte count to get the number of Unicode scalar values. Counts `-1` for each 4-byte character. |

These adjustments let you compute the resulting UTF-16 length (or scalar/code-point count)
of a valid buffer **for free**, during validation — no second pass required.

```csharp
unsafe
{
    fixed (byte* p = data)
    {
        byte* end = UTF8.GetPointerToFirstInvalidByte(p, data.Length,
            out int utf16Adjust, out int scalarAdjust);

        if (end == p + data.Length)
        {
            int utf16Length = data.Length + utf16Adjust;
            int codePointCount = data.Length + scalarAdjust;
        }
    }
}
```

## Choosing a specific kernel

`GetPointerToFirstInvalidByte` dispatches to the fastest kernel your CPU supports.
You can also call a specific implementation directly — useful for testing or pinning behaviour:

- [`GetPointerToFirstInvalidByteAvx512`](xref:SimdUnicode.UTF8) — AMD Zen 4 / Intel Ice Lake and newer.
- [`GetPointerToFirstInvalidByteAvx2`](xref:SimdUnicode.UTF8) — current x64.
- [`GetPointerToFirstInvalidByteSse`](xref:SimdUnicode.UTF8) — older x64 (SSE4.2 / SSSE3).
- [`GetPointerToFirstInvalidByteArm64`](xref:SimdUnicode.UTF8) — ARM NEON (Apple Silicon, Graviton…).
- [`GetPointerToFirstInvalidByteScalar`](xref:SimdUnicode.UTF8) — portable fallback.

## Finding the first non-ASCII byte

The [`Ascii`](xref:SimdUnicode.Ascii) helper class offers fast ASCII scanning,
including `GetIndexOfFirstNonAsciiByte` and architecture-specific variants.

```csharp
using SimdUnicode;

unsafe
{
    fixed (byte* p = data)
    {
        nuint idx = Ascii.GetIndexOfFirstNonAsciiByte(p, (nuint)data.Length);
        bool allAscii = idx == (nuint)data.Length;
    }
}
```

Continue to [How it works](how-it-works.md) or jump to the [API reference](xref:SimdUnicode.UTF8).

## Making UTF-16 strings well formed

.NET strings are sequences of UTF-16 code units and may contain *lone surrogates*
(a high surrogate not followed by a low surrogate, or a low surrogate not preceded by a
high surrogate). [`UTF16.ToWellFormed`](xref:SimdUnicode.UTF16) replaces each lone surrogate
by the replacement character U+FFFD, like JavaScript's `String.prototype.toWellFormed()`.

```csharp
using SimdUnicode;

string fixedString = UTF16.ToWellFormed("ab\uD800cd"); // "ab\uFFFDcd"

// Already well-formed strings are returned as is, without allocation.
string s = "héllo 😀";
bool same = ReferenceEquals(UTF16.ToWellFormed(s), s); // true

// Validation only (like JavaScript's isWellFormed()).
bool ok = UTF16.IsWellFormed(s.AsSpan());

// Buffer to buffer, or in place when source and destination are the same.
char[] buffer = "x\uDC00y".ToCharArray();
UTF16.ToWellFormed(buffer, buffer);
```

There is also a pointer API: `UTF16.ToWellFormed(char* input, int length, char* output)` and
`UTF16.GetPointerToFirstInvalidChar(char* input, int length)`, which returns a pointer to the
first lone surrogate, or to the end of the buffer when the input is well formed.

On AVX-512, AVX2, SSE4.1 and ARM64 (NEON) hardware, we use algorithms based on simdutf's, described in
Robert Clausecker, Daniel Lemire, *Fixing ill-formed UTF-16 strings with SIMD instructions*,
Software: Practice and Experience, 2026. Other systems fall back on the runtime's
vectorized `IndexOfAnyInRange`.
