---
_layout: landing
title: SimdUnicode — fast UTF-8 validation for .NET
---

<div class="hero">
  <div class="hero-inner">
    <h1 class="hero-title">SimdUnicode</h1>
    <p class="hero-tagline">A blazing-fast C# library that validates UTF-8 strings <strong>up to&nbsp;13&times; faster</strong> than the .NET standard library — using AVX-512, AVX2, SSE and ARM NEON.</p>
    <div class="hero-cta">
      <a class="btn btn-primary" href="articles/getting-started.md">Get started &rarr;</a>
      <a class="btn btn-ghost" href="api/index.md">API reference</a>
      <a class="btn btn-ghost" href="https://github.com/simdutf/SimdUnicode">GitHub</a>
    </div>
  </div>
</div>

<div class="stat-row">
  <div class="stat-card">
    <div class="stat-num">13&times;</div>
    <div class="stat-label">faster on Emoji-heavy text (Ice Lake, AVX-512)</div>
  </div>
  <div class="stat-card">
    <div class="stat-num">&lt; 1</div>
    <div class="stat-label">instruction per byte to validate UTF-8</div>
  </div>
  <div class="stat-card">
    <div class="stat-num">4</div>
    <div class="stat-label">SIMD back-ends: AVX-512, AVX2, SSE4.2, NEON</div>
  </div>
  <div class="stat-card">
    <div class="stat-num">0</div>
    <div class="stat-label">allocations — works directly on your buffer</div>
  </div>
</div>

## Drop-in replacement

SimdUnicode provides `SimdUnicode.UTF8.GetPointerToFirstInvalidByte`, a faster drop-in replacement for the runtime's private `Utf8Utility.GetPointerToFirstInvalidByte`. It returns a pointer to the first invalid byte — or the end of the buffer when the input is well-formed.

```csharp
using SimdUnicode;

byte[] data = File.ReadAllBytes("twitter.json");

unsafe
{
    fixed (byte* p = data)
    {
        byte* invalid = UTF8.GetPointerToFirstInvalidByte(
            p, data.Length,
            out int utf16Adjustment,
            out int scalarAdjustment);

        bool isValid = invalid == p + data.Length;
        Console.WriteLine(isValid ? "Valid UTF-8 ✅" : $"Invalid at offset {invalid - p}");
    }
}
```

The right SIMD kernel is selected automatically at runtime: **ARM64 NEON**, **AVX-512** (Zen 4 / Ice Lake), **AVX2**, **SSE4.2**, or a portable scalar fallback.

<div class="feature-grid">
  <div class="feature">
    <div class="feature-icon">⚡</div>
    <h3>Less than one instruction per byte</h3>
    <p>Implements the Keiser–Lemire algorithm used by Node.js, Bun, Oracle GraalVM and the PHP interpreter.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🧭</div>
    <h3>Runtime dispatch</h3>
    <p>One call, the best available kernel. AVX-512, AVX2, SSE4.2, ARM NEON or scalar — chosen for your CPU.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🧪</div>
    <h3>Extensively tested</h3>
    <p>A large suite of correctness tests across architectures, plus reproducible BenchmarkDotNet benchmarks.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🍏</div>
    <h3>x64 &amp; ARM</h3>
    <p>First-class support for modern Intel/AMD and Apple Silicon / Graviton processors.</p>
  </div>
</div>

## How fast is it?

Throughput on an **Intel Ice Lake** system (AVX-512), validating UTF-8. Longer bars are faster — SimdUnicode in purple, the .NET standard library in grey.

<div class="bench" data-unit="GB/s">
  <div class="bench-row"><span class="bench-name">Emoji-Lipsum</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>12 GB/s</span></div><div class="bar bar-net" style="--v:7.5%"><span>0.9</span></div></div><span class="bench-x">13&times;</span></div>
  <div class="bench-row"><span class="bench-name">Russian-Lipsum</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>12 GB/s</span></div><div class="bar bar-net" style="--v:10%"><span>1.2</span></div></div><span class="bench-x">10&times;</span></div>
  <div class="bench-row"><span class="bench-name">Korean-Lipsum</span><div class="bench-bars"><div class="bar bar-simd" style="--v:83%"><span>10 GB/s</span></div><div class="bar bar-net" style="--v:10.8%"><span>1.3</span></div></div><span class="bench-x">7.7&times;</span></div>
  <div class="bench-row"><span class="bench-name">Hindi-Lipsum</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>12 GB/s</span></div><div class="bar bar-net" style="--v:17.5%"><span>2.1</span></div></div><span class="bench-x">5.7&times;</span></div>
  <div class="bench-row"><span class="bench-name">Arabic-Lipsum</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>12 GB/s</span></div><div class="bar bar-net" style="--v:19%"><span>2.3</span></div></div><span class="bench-x">5.2&times;</span></div>
  <div class="bench-row"><span class="bench-name">Twitter.json</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>29 GB/s</span></div><div class="bar bar-net" style="--v:41%"><span>12</span></div></div><span class="bench-x">2.4&times;</span></div>
</div>

<p class="bench-note">On an <strong>Apple M2</strong> (NEON), SimdUnicode is 1.5&times;–4&times; faster than the standard library. See the full set of measurements across x64 and ARM in the <a href="articles/benchmarks.md">benchmarks</a>.</p>

## Build it

```bash
git clone https://github.com/simdutf/SimdUnicode.git
cd SimdUnicode/src
dotnet build -c Release
```

Then add a project reference to `src/SimdUnicode.csproj`. Head to the [getting started guide](articles/getting-started.md) or dive into the [API reference](xref:SimdUnicode.UTF8).

## Citing

The algorithm is described in:

> John Keiser, Daniel Lemire, [*Validating UTF-8 In Less Than One Instruction Per Byte*](https://arxiv.org/abs/2010.03090), Software: Practice and Experience 51 (5), 2021.
