namespace tests;

using System.Text;
using SimdUnicode;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// Tests for UTF16.ToWellFormed and UTF16.GetPointerToFirstInvalidChar, which follow
// JavaScript's String.prototype.toWellFormed() / isWellFormed() semantics.
public unsafe class UTF16WellFormedTests
{
    public unsafe delegate void ToWellFormedFunction(char* input, int length, char* output);
    public unsafe delegate char* FirstInvalidFunction(char* input, int length);

    private static bool HasAvx512 => Vector512.IsHardwareAccelerated && Avx512BW.IsSupported;

    private sealed class FactOnAvx512Attribute : FactAttribute
    {
        public FactOnAvx512Attribute()
        {
            if (!HasAvx512)
            {
                Skip = "Test is skipped due to not meeting system requirements (AVX-512).";
            }
        }
    }

    private sealed class FactOnAvx2Attribute : FactAttribute
    {
        public FactOnAvx2Attribute()
        {
            if (!Avx2.IsSupported)
            {
                Skip = "Test is skipped due to not meeting system requirements (AVX2).";
            }
        }
    }

    private sealed class FactOnSse41Attribute : FactAttribute
    {
        public FactOnSse41Attribute()
        {
            if (!Sse41.IsSupported)
            {
                Skip = "Test is skipped due to not meeting system requirements (SSE4.1).";
            }
        }
    }

    private sealed class FactOnArm64Attribute : FactAttribute
    {
        public FactOnArm64Attribute()
        {
            if (!(System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian))
            {
                Skip = "Test is skipped due to not meeting system requirements (ARM64).";
            }
        }
    }

    // Independent reference: decode rune by rune, as the runtime does.
    private static string Reference(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (Rune r in s.EnumerateRunes())
        {
            sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    private static int ReferenceFirstInvalid(string s)
    {
        int i = 0;
        foreach (Rune r in s.EnumerateRunes())
        {
            if (r == Rune.ReplacementChar && s[i] != '\uFFFD')
            {
                return i;
            }
            i += r.Utf16SequenceLength;
        }
        return s.Length;
    }

    // Random strings mixing ASCII, other BMP characters, U+FFFD, valid pairs and lone surrogates.
    private static string RandomString(Random rand, int length, int errorWeight)
    {
        var sb = new StringBuilder(length);
        while (sb.Length < length)
        {
            int kind = rand.Next(8 + errorWeight);
            switch (kind)
            {
                case 0:
                case 1:
                    sb.Append((char)rand.Next(0x80));
                    break;
                case 2:
                    sb.Append((char)rand.Next(0x80, 0xD800));
                    break;
                case 3:
                    sb.Append((char)rand.Next(0xE000, 0x10000));
                    break;
                case 4:
                    sb.Append('\uFFFD');
                    break;
                case 5:
                case 6:
                case 7:
                    sb.Append((char)rand.Next(0xD800, 0xDC00));
                    sb.Append((char)rand.Next(0xDC00, 0xE000));
                    break;
                default:
                    sb.Append((char)rand.Next(0xD800, 0xE000)); // lone (or accidentally paired) surrogate
                    break;
            }
        }
        return sb.ToString(0, length);
    }

    private static readonly string[] HardCoded = {
        "",
        "a",
        "\uD800",
        "\uDC00",
        "􏿿",
        "\uDC00\uD800",
        "\uD800𐀀",
        "𐀀\uDC00",
        "ab😀cd",
        "ab\uD83Dcd",
        "ab\uDE00cd",
        "😀😀\uD83D",
        "\uDE00😀😀",
        new string('\uD800', 100),
        new string('\uDC00', 100),
        string.Concat(Enumerable.Repeat("𐀀", 100)),
        string.Concat(Enumerable.Repeat("\uDC00\uD800", 100)),
        new string('x', 31) + "\uD800",
        new string('x', 32) + "\uD800",
        new string('x', 33) + "\uD800",
        new string('x', 31) + "𐀀",
        new string('x', 32) + "𐀀" + new string('x', 32),
        "\uDC00" + new string('x', 40),
        "\uD800" + new string('x', 40),
        "𐀀" + new string('x', 40),
    };

    private static string Run(ToWellFormedFunction f, string s, bool inPlace)
    {
        // Pad on both sides with high surrogates to catch out-of-bounds reads and writes.
        char[] buffer = new char[s.Length + 2];
        buffer[0] = '\uD800';
        buffer[^1] = '\uDC00';
        char[] output = new char[s.Length + 2];
        output[0] = 'A';
        output[^1] = 'Z';
        s.CopyTo(0, buffer, 1, s.Length);
        fixed (char* pIn = buffer)
        fixed (char* pOut = output)
        {
            if (inPlace)
            {
                f(pIn + 1, s.Length, pIn + 1);
                Assert.Equal('\uD800', buffer[0]);
                Assert.Equal('\uDC00', buffer[^1]);
                return new string(buffer, 1, s.Length);
            }
            f(pIn + 1, s.Length, pOut + 1);
            Assert.Equal('A', output[0]);
            Assert.Equal('Z', output[^1]);
            Assert.Equal(s, new string(buffer, 1, s.Length)); // input untouched
            return new string(output, 1, s.Length);
        }
    }

    private static int RunFirst(FirstInvalidFunction f, string s)
    {
        char[] buffer = new char[s.Length + 2];
        buffer[0] = '\uD800';
        buffer[^1] = '\uDC00';
        s.CopyTo(0, buffer, 1, s.Length);
        fixed (char* p = buffer)
        {
            return (int)(f(p + 1, s.Length) - (p + 1));
        }
    }

    private static void Check(ToWellFormedFunction f, FirstInvalidFunction first, string s)
    {
        string expected = Reference(s);
        Assert.Equal(expected, Run(f, s, false));
        Assert.Equal(expected, Run(f, s, true));
        Assert.Equal(ReferenceFirstInvalid(s), RunFirst(first, s));
    }

    private static void HardCodedTest(ToWellFormedFunction f, FirstInvalidFunction first)
    {
        foreach (string s in HardCoded)
        {
            Check(f, first, s);
        }
    }

    private static void RandomTest(ToWellFormedFunction f, FirstInvalidFunction first)
    {
        var rand = new Random(1234);
        for (int length = 0; length <= 200; length++)
        {
            for (int trial = 0; trial < 20; trial++)
            {
                Check(f, first, RandomString(rand, length, trial % 4));
            }
        }
        foreach (int length in new[] { 511, 512, 513, 1000, 4096, 10007 })
        {
            for (int trial = 0; trial < 20; trial++)
            {
                Check(f, first, RandomString(rand, length, trial % 3));
            }
        }
    }

    // Well-formed input with a single lone surrogate at every possible position.
    private static void SingleErrorTest(ToWellFormedFunction f, FirstInvalidFunction first)
    {
        var rand = new Random(4321);
        foreach (int length in new[] { 1, 2, 17, 31, 32, 33, 34, 63, 64, 65, 66, 97, 130 })
        {
            string valid = RandomString(rand, length, 0);
            // RandomString may cut a pair in half at the end.
            valid = Reference(valid);
            for (int pos = 0; pos <= length; pos++)
            {
                foreach (char bad in new[] { '\uD800', '\uDBFF', '\uDC00', '\uDFFF' })
                {
                    Check(f, first, valid.Insert(pos, bad.ToString()));
                }
            }
        }
    }

    // Mostly surrogate-free input (the SIMD fast path) with a single valid pair, lone
    // surrogate or pair followed by a lone surrogate at every position.
    private static void SparseTest(ToWellFormedFunction f, FirstInvalidFunction first)
    {
        var rand = new Random(777);
        foreach (int length in Enumerable.Range(0, 71).Concat(new[] { 300, 520 }))
        {
            var sb = new StringBuilder(length);
            for (int k = 0; k < length; k++)
            {
                sb.Append(rand.Next(2) == 0 ? (char)rand.Next(0x80) : (char)rand.Next(0xE000, 0x10000));
            }
            string valid = sb.ToString();
            foreach (string insert in new[] { "😀", "\uD83D", "\uDE00", "😀\uDE00", "\uD83D😀" })
            {
                for (int pos = 0; pos <= length; pos++)
                {
                    Check(f, first, valid.Insert(pos, insert));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void SparseScalar() => SparseTest(UTF16.ToWellFormedScalar, UTF16.GetPointerToFirstInvalidCharScalar);

    [FactOnAvx512]
    [Trait("Category", "avx512")]
    public void SparseAvx512() => SparseTest(UTF16.ToWellFormedAvx512, UTF16.GetPointerToFirstInvalidCharAvx512);

    [Fact]
    public void SparseDefault() => SparseTest(UTF16.ToWellFormed, UTF16.GetPointerToFirstInvalidChar);

    [Fact]
    [Trait("Category", "scalar")]
    public void HardCodedScalar() => HardCodedTest(UTF16.ToWellFormedScalar, UTF16.GetPointerToFirstInvalidCharScalar);

    [Fact]
    [Trait("Category", "scalar")]
    public void RandomScalar() => RandomTest(UTF16.ToWellFormedScalar, UTF16.GetPointerToFirstInvalidCharScalar);

    [Fact]
    [Trait("Category", "scalar")]
    public void SingleErrorScalar() => SingleErrorTest(UTF16.ToWellFormedScalar, UTF16.GetPointerToFirstInvalidCharScalar);

    [FactOnAvx512]
    [Trait("Category", "avx512")]
    public void HardCodedAvx512() => HardCodedTest(UTF16.ToWellFormedAvx512, UTF16.GetPointerToFirstInvalidCharAvx512);

    [FactOnAvx512]
    [Trait("Category", "avx512")]
    public void RandomAvx512() => RandomTest(UTF16.ToWellFormedAvx512, UTF16.GetPointerToFirstInvalidCharAvx512);

    [FactOnAvx512]
    [Trait("Category", "avx512")]
    public void SingleErrorAvx512() => SingleErrorTest(UTF16.ToWellFormedAvx512, UTF16.GetPointerToFirstInvalidCharAvx512);

    [FactOnSse41]
    [Trait("Category", "sse")]
    public void HardCodedSse() => HardCodedTest(UTF16.ToWellFormedSse, UTF16.GetPointerToFirstInvalidCharSse);

    [FactOnSse41]
    [Trait("Category", "sse")]
    public void RandomSse() => RandomTest(UTF16.ToWellFormedSse, UTF16.GetPointerToFirstInvalidCharSse);

    [FactOnSse41]
    [Trait("Category", "sse")]
    public void SingleErrorSse() => SingleErrorTest(UTF16.ToWellFormedSse, UTF16.GetPointerToFirstInvalidCharSse);

    [FactOnSse41]
    [Trait("Category", "sse")]
    public void SparseSse() => SparseTest(UTF16.ToWellFormedSse, UTF16.GetPointerToFirstInvalidCharSse);

    [FactOnAvx2]
    [Trait("Category", "avx")]
    public void HardCodedAvx2() => HardCodedTest(UTF16.ToWellFormedAvx2, UTF16.GetPointerToFirstInvalidCharAvx2);

    [FactOnAvx2]
    [Trait("Category", "avx")]
    public void RandomAvx2() => RandomTest(UTF16.ToWellFormedAvx2, UTF16.GetPointerToFirstInvalidCharAvx2);

    [FactOnAvx2]
    [Trait("Category", "avx")]
    public void SingleErrorAvx2() => SingleErrorTest(UTF16.ToWellFormedAvx2, UTF16.GetPointerToFirstInvalidCharAvx2);

    [FactOnAvx2]
    [Trait("Category", "avx")]
    public void SparseAvx2() => SparseTest(UTF16.ToWellFormedAvx2, UTF16.GetPointerToFirstInvalidCharAvx2);

    [FactOnArm64]
    [Trait("Category", "arm64")]
    public void HardCodedArm64() => HardCodedTest(UTF16.ToWellFormedArm64, UTF16.GetPointerToFirstInvalidCharArm64);

    [FactOnArm64]
    [Trait("Category", "arm64")]
    public void RandomArm64() => RandomTest(UTF16.ToWellFormedArm64, UTF16.GetPointerToFirstInvalidCharArm64);

    [FactOnArm64]
    [Trait("Category", "arm64")]
    public void SingleErrorArm64() => SingleErrorTest(UTF16.ToWellFormedArm64, UTF16.GetPointerToFirstInvalidCharArm64);

    [FactOnArm64]
    [Trait("Category", "arm64")]
    public void SparseArm64() => SparseTest(UTF16.ToWellFormedArm64, UTF16.GetPointerToFirstInvalidCharArm64);

    // The IndexOfAnyInRange fallback, used only when no SIMD kernel applies: we call it
    // directly so that it is tested on every system.
    [Fact]
    [Trait("Category", "scalar")]
    public void HardCodedFallback() => HardCodedTest(UTF16.ToWellFormedFallback, UTF16.GetPointerToFirstInvalidCharFallback);

    [Fact]
    [Trait("Category", "scalar")]
    public void RandomFallback() => RandomTest(UTF16.ToWellFormedFallback, UTF16.GetPointerToFirstInvalidCharFallback);

    [Fact]
    [Trait("Category", "scalar")]
    public void SingleErrorFallback() => SingleErrorTest(UTF16.ToWellFormedFallback, UTF16.GetPointerToFirstInvalidCharFallback);

    [Fact]
    [Trait("Category", "scalar")]
    public void SparseFallback() => SparseTest(UTF16.ToWellFormedFallback, UTF16.GetPointerToFirstInvalidCharFallback);

    // The dispatching entry points (AVX-512 or the IndexOfAnyInRange fallback).
    [Fact]
    public void HardCodedDefault() => HardCodedTest(UTF16.ToWellFormed, UTF16.GetPointerToFirstInvalidChar);

    [Fact]
    public void RandomDefault() => RandomTest(UTF16.ToWellFormed, UTF16.GetPointerToFirstInvalidChar);

    [Fact]
    public void SingleErrorDefault() => SingleErrorTest(UTF16.ToWellFormed, UTF16.GetPointerToFirstInvalidChar);

    [Fact]
    public void StringApi()
    {
        var rand = new Random(99);
        for (int length = 0; length <= 300; length += 7)
        {
            for (int trial = 0; trial < 10; trial++)
            {
                string s = RandomString(rand, length, trial % 3);
                string expected = Reference(s);
                string actual = UTF16.ToWellFormed(s);
                Assert.Equal(expected, actual);
                Assert.Equal(expected == s, UTF16.IsWellFormed(s));
                if (UTF16.IsWellFormed(s))
                {
                    Assert.Same(s, actual); // no allocation for well-formed strings
                }
            }
        }
        Assert.Throws<ArgumentNullException>(() => UTF16.ToWellFormed((string)null!));
    }

    [Fact]
    public void SpanApi()
    {
        string s = "ab\uD800cd\uDC00😀";
        char[] dst = new char[s.Length];
        UTF16.ToWellFormed(s.AsSpan(), dst);
        Assert.Equal(Reference(s), new string(dst));

        char[] inplace = s.ToCharArray();
        UTF16.ToWellFormed(inplace, inplace);
        Assert.Equal(Reference(s), new string(inplace));

        Assert.Throws<ArgumentException>(() => UTF16.ToWellFormed(s.AsSpan(), new char[s.Length - 1]));
        char[] overlap = new char[s.Length + 1];
        Assert.Throws<ArgumentException>(() => UTF16.ToWellFormed(overlap.AsSpan(0, s.Length), overlap.AsSpan(1)));
    }
}
