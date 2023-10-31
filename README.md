# SimdUnicode

This is a fast C# library to process unicode strings.

*It is currently not meant to be usable.*

## Motivation

The most important immediate goal would be to speed up the 
`Utf8Utility.GetPointerToFirstInvalidByte` function.

https://github.com/dotnet/runtime/blob/4d709cd12269fcbb3d0fccfb2515541944475954/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Validation.cs


(We may need to speed up `Ascii.GetIndexOfFirstNonAsciiByte` first, see issue https://github.com/simdutf/SimdUnicode/issues/1.)

The question is whether we could do it using this routine:

* John Keiser, Daniel Lemire, [Validating UTF-8 In Less Than One Instruction Per Byte](https://arxiv.org/abs/2010.03090), Software: Practice and Experience 51 (5), 2021

Our generic implementation is available there: https://github.com/simdutf/simdutf/blob/master/src/generic/utf8_validation/utf8_lookup4_algorithm.h

Porting it to C# is no joke, but doable.

## Requirements

We recommend you install .NET 7: https://dotnet.microsoft.com/en-us/download/dotnet/7.0


## Running tests

```
cd test
dotnet test
```

## Running Benchmarks

```
cd benchmark
dotnet run -c Release
```

If you are under macOS or Linux, you may want to run the benchmarks in privileged mode:

```
cd benchmark
sudo dotnet run -c Release
```

To run specific tests, it is helpful to use the filter parameter:

```
sudo dotnet run -c Release --filter *search_keyword_here*
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


## More reading 


https://github.com/dotnet/coreclr/pull/21948/files#diff-2a22774bd6bff8e217ecbb3a41afad033ce0ca0f33645e9d8f5bdf7c9e3ac248

https://github.com/dotnet/runtime/issues/41699
