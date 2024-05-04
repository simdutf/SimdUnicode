# SimdUnicode
[![.NET](https://github.com/simdutf/SimdUnicode/actions/workflows/dotnet.yml/badge.svg)](https://github.com/simdutf/SimdUnicode/actions/workflows/dotnet.yml)

This is a fast C# library to process unicode strings.


## Motivation

We seek to speed up the `Utf8Utility.GetPointerToFirstInvalidByte` function. Using the algorithm used by Node.js, Oracle GraalVM  and other important systems.

- John Keiser, Daniel Lemire, [Validating UTF-8 In Less Than One Instruction Per Byte](https://arxiv.org/abs/2010.03090), Software: Practice and Experience 51 (5), 2021

The function is private in the Runtime, but we can expose it manually.

https://github.com/dotnet/runtime/blob/4d709cd12269fcbb3d0fccfb2515541944475954/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Validation.cs


## Requirements

We recommend you install .NET 8: https://dotnet.microsoft.com/en-us/download/dotnet/8.0


## Running tests

```
dotnet test
```

To get a list of available tests, enter the command:

```
dotnet test --list-tests
```

To run specific tests, it is helpful to use the filter parameter:

```
dotnet test -c Release --filter Ascii
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
dotnet run --configuration Release --filter "*Arabic-Lipsum*"
```

If you are under macOS or Linux, you may want to run the benchmarks in privileged mode:

```
cd benchmark
sudo dotnet run -c Release
```

## Building the library

```
cd src
dotnet build
```

## Code format

We recommend you use `dotnet format`. E.g.,

```
cd test
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

- Be careful: `Vector128.Shuffle` is not the same as `Ssse3.Shuffle` nor is  `Vector128.Shuffle` the same as `Avx2.Shuffle`. Prefer the latter.

## More reading 


https://github.com/dotnet/coreclr/pull/21948/files#diff-2a22774bd6bff8e217ecbb3a41afad033ce0ca0f33645e9d8f5bdf7c9e3ac248

https://github.com/dotnet/runtime/issues/41699

https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/

https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions