# SimdUnicode

This is a fast C# library to process unicode strings.

*It is currently not meant to be usable.*

Our goal is to provide faster methods than 
https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding?view=net-7.0

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