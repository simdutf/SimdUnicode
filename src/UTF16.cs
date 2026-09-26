using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

// Making UTF-16 strings well formed: every lone surrogate is replaced by U+FFFD,
// which matches JavaScript's String.prototype.toWellFormed().
//
// The AVX-512, AVX2, SSE and ARM64 kernels are based on simdutf's utf16fix functions, described in
//
//   Robert Clausecker, Daniel Lemire, Fixing ill-formed UTF-16 strings with SIMD
//   instructions, Software: Practice and Experience, 2026.
//
// See https://github.com/simdutf/simdutf/blob/master/src/icelake/icelake_utf16fix.cpp
// Unlike simdutf, we carry the high-surrogate bitmask across blocks instead of loading
// a lookback block, and we skip blocks of 128 code units without surrogates quickly.
namespace SimdUnicode
{
    public static class UTF16
    {
        /// <summary>The replacement character U+FFFD.</summary>
        public const char ReplacementCharacter = '\uFFFD';

        /// <summary>
        /// Given an input buffer <paramref name="pInputBuffer"/> of <paramref name="inputLength"/> UTF-16 code units,
        /// returns a pointer to the first lone (unpaired) surrogate.
        /// </summary>
        /// <remarks>
        /// Returns a pointer to the end of <paramref name="pInputBuffer"/> if the buffer is well-formed.
        /// </remarks>
        public unsafe static char* GetPointerToFirstInvalidChar(char* pInputBuffer, int inputLength)
        {
            if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
            {
                return GetPointerToFirstInvalidCharArm64(pInputBuffer, inputLength);
            }
            if (Vector512.IsHardwareAccelerated && Avx512BW.IsSupported)
            {
                return GetPointerToFirstInvalidCharAvx512(pInputBuffer, inputLength);
            }
            if (Avx2.IsSupported)
            {
                return GetPointerToFirstInvalidCharAvx2(pInputBuffer, inputLength);
            }
            if (Sse41.IsSupported)
            {
                return GetPointerToFirstInvalidCharSse(pInputBuffer, inputLength);
            }
            return GetPointerToFirstInvalidCharFallback(pInputBuffer, inputLength);
        }

        /// <summary>
        /// Returns true if <paramref name="input"/> contains no lone surrogate.
        /// </summary>
        public unsafe static bool IsWellFormed(ReadOnlySpan<char> input)
        {
            fixed (char* p = input)
            {
                return GetPointerToFirstInvalidChar(p, input.Length) == p + input.Length;
            }
        }

        /// <summary>
        /// Copies <paramref name="inputLength"/> UTF-16 code units from <paramref name="pInputBuffer"/> to
        /// <paramref name="pOutputBuffer"/>, replacing every lone surrogate by U+FFFD.
        /// The two buffers must either be identical (in-place operation) or not overlap.
        /// </summary>
        public unsafe static void ToWellFormed(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
            {
                ToWellFormedArm64(pInputBuffer, inputLength, pOutputBuffer);
                return;
            }
            if (Vector512.IsHardwareAccelerated && Avx512BW.IsSupported)
            {
                ToWellFormedAvx512(pInputBuffer, inputLength, pOutputBuffer);
                return;
            }
            if (Avx2.IsSupported)
            {
                ToWellFormedAvx2(pInputBuffer, inputLength, pOutputBuffer);
                return;
            }
            if (Sse41.IsSupported)
            {
                ToWellFormedSse(pInputBuffer, inputLength, pOutputBuffer);
                return;
            }
            ToWellFormedFallback(pInputBuffer, inputLength, pOutputBuffer);
        }

        /// <summary>
        /// Copies <paramref name="source"/> to <paramref name="destination"/>, replacing every lone surrogate by U+FFFD.
        /// The spans must either start at the same address (in-place operation) or not overlap.
        /// </summary>
        public unsafe static void ToWellFormed(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (destination.Length < source.Length)
            {
                throw new ArgumentException("Destination is too short.", nameof(destination));
            }
            fixed (char* pIn = source)
            fixed (char* pOut = destination)
            {
                if (pIn != pOut && source.Overlaps(destination))
                {
                    throw new ArgumentException("Source and destination must be identical or must not overlap.", nameof(destination));
                }
                ToWellFormed(pIn, source.Length, pOut);
            }
        }

        /// <summary>
        /// Returns a well-formed copy of <paramref name="input"/> where every lone surrogate is replaced by U+FFFD.
        /// If <paramref name="input"/> is already well formed, it is returned as is (no allocation).
        /// </summary>
        public unsafe static string ToWellFormed(string input)
        {
            ArgumentNullException.ThrowIfNull(input);
            int first;
            fixed (char* p = input)
            {
                first = (int)(GetPointerToFirstInvalidChar(p, input.Length) - p);
            }
            if (first == input.Length)
            {
                return input;
            }
            return string.Create(input.Length, (input, first), static (dst, state) =>
            {
                (string src, int first) = state;
                src.AsSpan().CopyTo(dst);
                // Nothing before 'first' needs fixing, and the code unit before 'first'
                // is not a high surrogate, so we can fix the tail on its own.
                fixed (char* p = dst)
                {
                    ToWellFormed(p + first, dst.Length - first, p + first);
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHighSurrogate(char c) => (c & 0xFC00) == 0xD800;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLowSurrogate(char c) => (c & 0xFC00) == 0xDC00;

        /// <summary>
        /// Straightforward scalar version, a port of simdutf's scalar::utf16::to_well_formed_utf16.
        /// </summary>
        public unsafe static void ToWellFormedScalar(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            ToWellFormedScalarFrom((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer, 0, false);
        }

        // Processes input[i..n), where highSurrogatePrev tells whether input[i-1] is a high surrogate
        // (output[i-1] is then overwritten if it is a lone surrogate).
        private unsafe static void ToWellFormedScalarFrom(ushort* pInputBuffer, int inputLength, ushort* pOutputBuffer, int i, bool highSurrogatePrev)
        {
            for (; i < inputLength; i++)
            {
                char c = (char)pInputBuffer[i];
                bool highSurrogate = IsHighSurrogate(c);
                bool lowSurrogate = IsLowSurrogate(c);
                if (highSurrogatePrev && !lowSurrogate)
                {
                    pOutputBuffer[i - 1] = ReplacementCharacter;
                }
                pOutputBuffer[i] = (!highSurrogatePrev && lowSurrogate) ? ReplacementCharacter : c;
                highSurrogatePrev = highSurrogate;
            }
            // The string may not end with a high surrogate.
            if (highSurrogatePrev)
            {
                pOutputBuffer[i - 1] = ReplacementCharacter;
            }
        }

        public unsafe static char* GetPointerToFirstInvalidCharScalar(char* pInputBuffer, int inputLength)
        {
            return GetPointerToFirstInvalidCharScalarFrom(pInputBuffer, inputLength, 0, false);
        }

        // Scans pInputBuffer[i..n), where highSurrogatePrev tells whether pInputBuffer[i-1] is a high surrogate.
        private unsafe static char* GetPointerToFirstInvalidCharScalarFrom(char* pInputBuffer, int inputLength, int i, bool highSurrogatePrev)
        {
            if (highSurrogatePrev)
            {
                if (i == inputLength || !IsLowSurrogate(pInputBuffer[i]))
                {
                    return pInputBuffer + i - 1;
                }
                i++;
            }
            for (; i < inputLength; i++)
            {
                char c = pInputBuffer[i];
                if (IsLowSurrogate(c))
                {
                    return pInputBuffer + i;
                }
                if (IsHighSurrogate(c))
                {
                    if (i + 1 == inputLength || !IsLowSurrogate(pInputBuffer[i + 1]))
                    {
                        return pInputBuffer + i;
                    }
                    i++;
                }
            }
            return pInputBuffer + inputLength;
        }

        /// <summary>
        /// Portable version used when no SIMD kernel applies (no ARM64 NEON, AVX-512, AVX2 or SSE4.1):
        /// we rely on the runtime's vectorized IndexOfAnyInRange to skip everything that is not a surrogate.
        /// </summary>
        public unsafe static char* GetPointerToFirstInvalidCharFallback(char* pInputBuffer, int inputLength)
        {
            ReadOnlySpan<char> s = new ReadOnlySpan<char>(pInputBuffer, inputLength);
            int i = 0;
            while (true)
            {
                int k = s.Slice(i).IndexOfAnyInRange('\uD800', '\uDFFF');
                if (k < 0)
                {
                    return pInputBuffer + inputLength;
                }
                i += k;
                if (IsHighSurrogate(s[i]) && i + 1 < s.Length && IsLowSurrogate(s[i + 1]))
                {
                    i += 2;
                }
                else
                {
                    return pInputBuffer + i;
                }
            }
        }

        /// <summary>
        /// Portable version used when no SIMD kernel applies (see <see cref="GetPointerToFirstInvalidCharFallback"/>).
        /// </summary>
        public unsafe static void ToWellFormedFallback(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (pInputBuffer != pOutputBuffer)
            {
                Buffer.MemoryCopy(pInputBuffer, pOutputBuffer, (long)inputLength * sizeof(char), (long)inputLength * sizeof(char));
            }
            // Replacing a lone surrogate never changes whether another surrogate is paired,
            // so we can keep scanning the (partially fixed) output.
            char* end = pOutputBuffer + inputLength;
            char* p = GetPointerToFirstInvalidCharFallback(pOutputBuffer, inputLength);
            while (p != end)
            {
                *p++ = ReplacementCharacter;
                p = GetPointerToFirstInvalidCharFallback(p, (int)(end - p));
            }
        }

        // ARM64 NEON.
        //
        // We gather the most significant bytes of 16 code units into one vector (uzp2), so
        // that one comparison classifies 16 code units. The 'lookback' (is the previous
        // code unit a high surrogate?) is obtained by shifting the high-surrogate vector by
        // one lane (ext), carrying the last lane over from the previous block. (simdutf
        // loads a second, lookback block instead.) A block is well formed exactly where
        // lookback == low.

        // Most significant bytes of the 16 code units in v0 and v1 (little endian).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Arm64HighBytes(Vector128<ushort> v0, Vector128<ushort> v1)
        {
            return AdvSimd.Arm64.UnzipOdd(v0.AsByte(), v1.AsByte());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Arm64Classify(Vector128<byte> highBytes, out Vector128<byte> isHigh, out Vector128<byte> isLow)
        {
            Vector128<byte> masked = AdvSimd.And(highBytes, Vector128.Create((byte)0xFC));
            isHigh = AdvSimd.CompareEqual(masked, Vector128.Create((byte)0xD8));
            isLow = AdvSimd.CompareEqual(masked, Vector128.Create((byte)0xDC));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Arm64AnyNonZero(Vector128<byte> v)
        {
            return AdvSimd.Arm64.MaxPairwise(v, v).AsUInt64().ToScalar() != 0;
        }

        // True if any of the 64 code units (given by their most significant bytes) is a
        // surrogate: adding 0x28 maps 0xD8 to 0xDF to 0 to 7.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Arm64AnySurrogate(Vector128<byte> h0, Vector128<byte> h1, Vector128<byte> h2, Vector128<byte> h3)
        {
            Vector128<byte> offset = Vector128.Create((byte)0x28);
            Vector128<byte> min = AdvSimd.Min(AdvSimd.Min(AdvSimd.Add(h0, offset), AdvSimd.Add(h1, offset)),
                                              AdvSimd.Min(AdvSimd.Add(h2, offset), AdvSimd.Add(h3, offset)));
            return AdvSimd.Arm64.MinAcross(min).ToScalar() < 8;
        }

        // One bit per lane (lanes set to 0xFF or 0), 64 lanes. From simdutf.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Arm64Bits(Vector128<byte> v0, Vector128<byte> v1, Vector128<byte> v2, Vector128<byte> v3)
        {
            Vector128<byte> bitMask = Vector128.Create((byte)0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80,
                                                       0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80);
            Vector128<byte> sum0 = AdvSimd.Arm64.AddPairwise(AdvSimd.And(v0, bitMask), AdvSimd.And(v1, bitMask));
            Vector128<byte> sum1 = AdvSimd.Arm64.AddPairwise(AdvSimd.And(v2, bitMask), AdvSimd.And(v3, bitMask));
            sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum1);
            sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum0);
            return sum0.AsUInt64().ToScalar();
        }

        // Replaces the lone surrogates flagged in 'illseq' (lookback XOR low). Bit j refers
        // to the pair (output[j-1], output[j]): the lone surrogate is output[j-1] when
        // output[j-1] is a high surrogate (bit j of lookback), output[j] otherwise.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void Arm64FixBits(ushort* output, ulong illseq, ulong lookback)
        {
            while (illseq != 0)
            {
                int j = BitOperations.TrailingZeroCount(illseq);
                output[j - (int)((lookback >> j) & 1)] = ReplacementCharacter;
                illseq &= illseq - 1;
            }
        }

        // Generic flags: the JIT specializes generic methods over struct type arguments,
        // so 'TInPlace.Value' is a constant in each instantiation.
        private interface IFlag
        {
            static abstract bool Value { get; }
        }

        private struct TrueFlag : IFlag
        {
            public static bool Value => true;
        }

        private struct FalseFlag : IFlag
        {
            public static bool Value => false;
        }

        public unsafe static void ToWellFormedArm64(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (pInputBuffer == pOutputBuffer)
            {
                Arm64FixAll<TrueFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
            else
            {
                Arm64FixAll<FalseFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
        }

        // Lookback and illegal-sequence vectors (one byte per code unit) for the 16 code
        // units at p, where p[-1] is readable.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static Vector128<byte> Arm64Window16(ushort* p, out Vector128<byte> lookback)
        {
            Arm64Classify(Arm64HighBytes(AdvSimd.LoadVector128(p - 1), AdvSimd.LoadVector128(p + 7)), out lookback, out _);
            Arm64Classify(Arm64HighBytes(AdvSimd.LoadVector128(p), AdvSimd.LoadVector128(p + 8)), out _, out Vector128<byte> isLow);
            return AdvSimd.Xor(lookback, isLow);
        }

        // Same for the 8 code units at p (in the first 8 lanes, the others are zero);
        // p[-1] is read only if hasPrevious.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static Vector128<byte> Arm64Window8(ushort* p, bool hasPrevious, out Vector128<byte> lookback)
        {
            Vector128<ushort> block = AdvSimd.LoadVector128(p);
            Vector128<ushort> lb = hasPrevious ? AdvSimd.LoadVector128(p - 1) : AdvSimd.ExtractVector128(Vector128<ushort>.Zero, block, 7);
            Vector128<ushort> mask = Vector128.Create((ushort)0xFC00);
            Vector128<ushort> lbIsHigh = AdvSimd.CompareEqual(AdvSimd.And(lb, mask), Vector128.Create((ushort)0xD800));
            Vector128<ushort> isLow = AdvSimd.CompareEqual(AdvSimd.And(block, mask), Vector128.Create((ushort)0xDC00));
            lookback = AdvSimd.ExtractNarrowingLower(lbIsHigh).ToVector128();
            return AdvSimd.ExtractNarrowingLower(AdvSimd.Xor(lbIsHigh, isLow)).ToVector128();
        }

        // Replaces the lone surrogates of a window, if any.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void Arm64FixWindow(ushort* output, Vector128<byte> illseq, Vector128<byte> lookback)
        {
            if (Arm64AnyNonZero(illseq))
            {
                Vector128<byte> zero = Vector128<byte>.Zero;
                Arm64FixBits(output, Arm64Bits(illseq, zero, zero, zero), Arm64Bits(lookback, zero, zero, zero));
            }
        }

        // Offset of the first lone surrogate of a window, or int.MinValue if there is none.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Arm64WindowError(Vector128<byte> illseq, Vector128<byte> lookback)
        {
            if (!Arm64AnyNonZero(illseq))
            {
                return int.MinValue;
            }
            Vector128<byte> zero = Vector128<byte>.Zero;
            return Arm64FirstError(Arm64Bits(illseq, zero, zero, zero), Arm64Bits(lookback, zero, zero, zero));
        }

        // Not inlined: its own compilation unit, so that the JIT can inline all the helpers.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void Arm64FixAll<TInPlace>(ushort* input, int n, ushort* output) where TInPlace : struct, IFlag
        {
            ushort* p = input;
            ushort* q = output;
            ushort* end = input + n;
            // Lane 15 tells whether the code unit before p is a high surrogate.
            Vector128<byte> prevIsHigh = Vector128<byte>.Zero;
            for (; end - p >= 64; p += 64, q += 64)
            {
                Vector128<ushort> v0 = AdvSimd.LoadVector128(p);
                Vector128<ushort> v1 = AdvSimd.LoadVector128(p + 8);
                Vector128<ushort> v2 = AdvSimd.LoadVector128(p + 16);
                Vector128<ushort> v3 = AdvSimd.LoadVector128(p + 24);
                Vector128<ushort> v4 = AdvSimd.LoadVector128(p + 32);
                Vector128<ushort> v5 = AdvSimd.LoadVector128(p + 40);
                Vector128<ushort> v6 = AdvSimd.LoadVector128(p + 48);
                Vector128<ushort> v7 = AdvSimd.LoadVector128(p + 56);
                if (!TInPlace.Value)
                {
                    AdvSimd.Store(q, v0);
                    AdvSimd.Store(q + 8, v1);
                    AdvSimd.Store(q + 16, v2);
                    AdvSimd.Store(q + 24, v3);
                    AdvSimd.Store(q + 32, v4);
                    AdvSimd.Store(q + 40, v5);
                    AdvSimd.Store(q + 48, v6);
                    AdvSimd.Store(q + 56, v7);
                }
                Vector128<byte> h0 = Arm64HighBytes(v0, v1);
                Vector128<byte> h1 = Arm64HighBytes(v2, v3);
                Vector128<byte> h2 = Arm64HighBytes(v4, v5);
                Vector128<byte> h3 = Arm64HighBytes(v6, v7);
                // Most text has no surrogate at all.
                if (prevIsHigh.GetElement(15) == 0 && !Arm64AnySurrogate(h0, h1, h2, h3))
                {
                    continue;
                }
                Arm64Classify(h0, out Vector128<byte> isHigh0, out Vector128<byte> isLow0);
                Arm64Classify(h1, out Vector128<byte> isHigh1, out Vector128<byte> isLow1);
                Arm64Classify(h2, out Vector128<byte> isHigh2, out Vector128<byte> isLow2);
                Arm64Classify(h3, out Vector128<byte> isHigh3, out Vector128<byte> isLow3);
                Vector128<byte> lb0 = AdvSimd.ExtractVector128(prevIsHigh, isHigh0, 15);
                Vector128<byte> lb1 = AdvSimd.ExtractVector128(isHigh0, isHigh1, 15);
                Vector128<byte> lb2 = AdvSimd.ExtractVector128(isHigh1, isHigh2, 15);
                Vector128<byte> lb3 = AdvSimd.ExtractVector128(isHigh2, isHigh3, 15);
                prevIsHigh = isHigh3;
                Vector128<byte> ill0 = AdvSimd.Xor(lb0, isLow0);
                Vector128<byte> ill1 = AdvSimd.Xor(lb1, isLow1);
                Vector128<byte> ill2 = AdvSimd.Xor(lb2, isLow2);
                Vector128<byte> ill3 = AdvSimd.Xor(lb3, isLow3);
                if (Arm64AnyNonZero(AdvSimd.Or(AdvSimd.Or(ill0, ill1), AdvSimd.Or(ill2, ill3))))
                {
                    Arm64FixBits(q, Arm64Bits(ill0, ill1, ill2, ill3), Arm64Bits(lb0, lb1, lb2, lb3));
                }
            }
            for (; end - p >= 16; p += 16, q += 16)
            {
                Vector128<ushort> v0 = AdvSimd.LoadVector128(p);
                Vector128<ushort> v1 = AdvSimd.LoadVector128(p + 8);
                if (!TInPlace.Value)
                {
                    AdvSimd.Store(q, v0);
                    AdvSimd.Store(q + 8, v1);
                }
                Arm64Classify(Arm64HighBytes(v0, v1), out Vector128<byte> isHigh, out Vector128<byte> isLow);
                Vector128<byte> lb = AdvSimd.ExtractVector128(prevIsHigh, isHigh, 15);
                prevIsHigh = isHigh;
                Vector128<byte> ill = AdvSimd.Xor(lb, isLow);
                if (Arm64AnyNonZero(ill))
                {
                    Vector128<byte> zero = Vector128<byte>.Zero;
                    Arm64FixBits(q, Arm64Bits(ill, zero, zero, zero), Arm64Bits(lb, zero, zero, zero));
                }
            }
            if (p != end)
            {
                // The last 1 to 15 code units, using overlapping windows. Code units that were
                // already written are recomputed identically. (In place, a replaced code unit
                // was a lone surrogate: replacing it does not change the pairing of the others.)
                if (n > 16)
                {
                    ushort* w = end - 16;
                    if (!TInPlace.Value)
                    {
                        AdvSimd.Store(output + n - 16, AdvSimd.LoadVector128(w));
                        AdvSimd.Store(output + n - 8, AdvSimd.LoadVector128(w + 8));
                    }
                    Arm64FixWindow(output + n - 16, Arm64Window16(w, out Vector128<byte> lookback), lookback);
                }
                else if (n >= 8)
                {
                    Vector128<ushort> first = AdvSimd.LoadVector128(input);
                    Vector128<ushort> last = AdvSimd.LoadVector128(input + n - 8);
                    if (!TInPlace.Value)
                    {
                        AdvSimd.Store(output, first);
                        AdvSimd.Store(output + n - 8, last);
                    }
                    Arm64FixWindow(output, Arm64Window8(input, false, out Vector128<byte> lookback), lookback);
                    if (n > 8)
                    {
                        Arm64FixWindow(output + n - 8, Arm64Window8(input + n - 8, true, out lookback), lookback);
                    }
                }
                else
                {
                    ToWellFormedScalarFrom(input, n, output, 0, false);
                    return;
                }
            }
            // The string may not end with a high surrogate.
            if (n > 0 && IsHighSurrogate((char)output[n - 1]))
            {
                output[n - 1] = ReplacementCharacter;
            }
        }

        // For inputs of fewer than 64 code units: true if there is no surrogate at all,
        // which is the common case. We use overlapping loads.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static bool Arm64ShortHasNoSurrogate(ushort* input, int n)
        {
            if (n >= 8)
            {
                Vector128<ushort> offset = Vector128.Create((ushort)0x2800);
                Vector128<ushort> min = AdvSimd.Add(AdvSimd.LoadVector128(input + n - 8), offset);
                for (int i = 0; i + 8 < n; i += 8)
                {
                    min = AdvSimd.Min(min, AdvSimd.Add(AdvSimd.LoadVector128(input + i), offset));
                }
                return AdvSimd.Arm64.MinAcross(min).ToScalar() >= 0x800;
            }
            if (n >= 4)
            {
                Vector64<ushort> offset = Vector64.Create((ushort)0x2800);
                Vector64<ushort> min = AdvSimd.Min(AdvSimd.Add(AdvSimd.LoadVector64(input), offset),
                                                   AdvSimd.Add(AdvSimd.LoadVector64(input + n - 4), offset));
                return AdvSimd.Arm64.MinAcross(min).ToScalar() >= 0x800;
            }
            for (int i = 0; i < n; i++)
            {
                if ((ushort)(input[i] - 0xD800) < 0x800)
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static char* GetPointerToFirstInvalidCharArm64(char* pInputBuffer, int inputLength)
        {
            ushort* input = (ushort*)pInputBuffer;
            int n = inputLength;
            if (n < 64 && Arm64ShortHasNoSurrogate(input, n))
            {
                return pInputBuffer + n;
            }
            ushort* p = input;
            ushort* end = input + n;
            Vector128<byte> prevIsHigh = Vector128<byte>.Zero;
            for (; end - p >= 64; p += 64)
            {
                Vector128<byte> h0 = Arm64HighBytes(AdvSimd.LoadVector128(p), AdvSimd.LoadVector128(p + 8));
                Vector128<byte> h1 = Arm64HighBytes(AdvSimd.LoadVector128(p + 16), AdvSimd.LoadVector128(p + 24));
                Vector128<byte> h2 = Arm64HighBytes(AdvSimd.LoadVector128(p + 32), AdvSimd.LoadVector128(p + 40));
                Vector128<byte> h3 = Arm64HighBytes(AdvSimd.LoadVector128(p + 48), AdvSimd.LoadVector128(p + 56));
                if (prevIsHigh.GetElement(15) == 0 && !Arm64AnySurrogate(h0, h1, h2, h3))
                {
                    continue;
                }
                Arm64Classify(h0, out Vector128<byte> isHigh0, out Vector128<byte> isLow0);
                Arm64Classify(h1, out Vector128<byte> isHigh1, out Vector128<byte> isLow1);
                Arm64Classify(h2, out Vector128<byte> isHigh2, out Vector128<byte> isLow2);
                Arm64Classify(h3, out Vector128<byte> isHigh3, out Vector128<byte> isLow3);
                Vector128<byte> lb0 = AdvSimd.ExtractVector128(prevIsHigh, isHigh0, 15);
                Vector128<byte> lb1 = AdvSimd.ExtractVector128(isHigh0, isHigh1, 15);
                Vector128<byte> lb2 = AdvSimd.ExtractVector128(isHigh1, isHigh2, 15);
                Vector128<byte> lb3 = AdvSimd.ExtractVector128(isHigh2, isHigh3, 15);
                prevIsHigh = isHigh3;
                Vector128<byte> ill0 = AdvSimd.Xor(lb0, isLow0);
                Vector128<byte> ill1 = AdvSimd.Xor(lb1, isLow1);
                Vector128<byte> ill2 = AdvSimd.Xor(lb2, isLow2);
                Vector128<byte> ill3 = AdvSimd.Xor(lb3, isLow3);
                if (Arm64AnyNonZero(AdvSimd.Or(AdvSimd.Or(ill0, ill1), AdvSimd.Or(ill2, ill3))))
                {
                    return (char*)p + Arm64FirstError(Arm64Bits(ill0, ill1, ill2, ill3), Arm64Bits(lb0, lb1, lb2, lb3));
                }
            }
            for (; end - p >= 16; p += 16)
            {
                Arm64Classify(Arm64HighBytes(AdvSimd.LoadVector128(p), AdvSimd.LoadVector128(p + 8)),
                              out Vector128<byte> isHigh, out Vector128<byte> isLow);
                Vector128<byte> lb = AdvSimd.ExtractVector128(prevIsHigh, isHigh, 15);
                prevIsHigh = isHigh;
                Vector128<byte> ill = AdvSimd.Xor(lb, isLow);
                if (Arm64AnyNonZero(ill))
                {
                    Vector128<byte> zero = Vector128<byte>.Zero;
                    return (char*)p + Arm64FirstError(Arm64Bits(ill, zero, zero, zero), Arm64Bits(lb, zero, zero, zero));
                }
            }
            if (p != end)
            {
                // The last 1 to 15 code units, using overlapping windows: the pairs that
                // were already checked are well formed.
                if (n > 16)
                {
                    int j = Arm64WindowError(Arm64Window16(end - 16, out Vector128<byte> lookback), lookback);
                    if (j != int.MinValue)
                    {
                        return pInputBuffer + n - 16 + j;
                    }
                }
                else if (n >= 8)
                {
                    int j = Arm64WindowError(Arm64Window8(input, false, out Vector128<byte> lookback), lookback);
                    if (j != int.MinValue)
                    {
                        return pInputBuffer + j;
                    }
                    if (n > 8)
                    {
                        j = Arm64WindowError(Arm64Window8(input + n - 8, true, out lookback), lookback);
                        if (j != int.MinValue)
                        {
                            return pInputBuffer + n - 8 + j;
                        }
                    }
                }
                else
                {
                    return GetPointerToFirstInvalidCharScalarFrom(pInputBuffer, n, 0, false);
                }
            }
            return (n > 0 && IsHighSurrogate(pInputBuffer[n - 1])) ? pInputBuffer + n - 1 : pInputBuffer + n;
        }

        // Offset of the first lone surrogate: see Arm64FixBits.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Arm64FirstError(ulong illseq, ulong lookback)
        {
            int j = BitOperations.TrailingZeroCount(illseq);
            return j - (int)((lookback >> j) & 1);
        }

        // AVX2 (Haswell and better).
        //
        // Same approach as the AVX-512 kernel, but AVX2 has no mask registers: vpmovmskb
        // gives us 2 bits per code unit, so a 64-bit word covers 32 code units and the
        // high-surrogate bitmask is shifted by 2 (carrying 2 bits between words). We fix
        // errors with a scalar loop over the bitmask (as simdutf does on ARM), and handle
        // the end of the input with overlapping windows since AVX2 has no 16-bit masked loads.

        // Two bits per code unit (2j and 2j+1 for block[j]) for high and low surrogates.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Avx2Classify(Vector256<ushort> block, out uint high, out uint low)
        {
            Vector256<ushort> masked = block & Vector256.Create((ushort)0xFC00);
            high = (uint)Avx2.MoveMask(Vector256.Equals(masked, Vector256.Create((ushort)0xD800)).AsByte());
            low = (uint)Avx2.MoveMask(Vector256.Equals(masked, Vector256.Create((ushort)0xDC00)).AsByte());
        }

        // True if any of the 64 code units is a surrogate: adding 0xA800 maps the surrogates
        // (0xD800 to 0xDFFF) to the smallest signed 16-bit values (-32768 to -30721).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Avx2AnySurrogate(Vector256<ushort> b0, Vector256<ushort> b1, Vector256<ushort> b2, Vector256<ushort> b3)
        {
            Vector256<short> offset = Vector256.Create(unchecked((short)0xA800));
            Vector256<short> min = Vector256.Min(Vector256.Min(b0.AsInt16() + offset, b1.AsInt16() + offset),
                                                 Vector256.Min(b2.AsInt16() + offset, b3.AsInt16() + offset));
            return Vector256.LessThanAny(min, Vector256.Create((short)-30720));
        }

        // Replaces the lone surrogates given illseq = lookback ^ low (2 bits per code unit).
        // The pair (output[j-1], output[j]) is ill formed; output[j-1] is the lone surrogate
        // when it is a high surrogate (bit 2j of lookback), output[j] otherwise.
        // Inlined: a call inside the main loop makes the JIT spill registers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void Avx2FixBits(ushort* output, ulong illseq, ulong lookback)
        {
            illseq &= 0x5555555555555555UL;
            while (illseq != 0)
            {
                int bit = BitOperations.TrailingZeroCount(illseq);
                output[(bit >> 1) - (int)((lookback >> bit) & 1)] = ReplacementCharacter;
                illseq &= illseq - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Avx2FirstError(ulong illseq, ulong lookback)
        {
            int bit = BitOperations.TrailingZeroCount(illseq);
            return (bit >> 1) - (int)((lookback >> bit) & 1);
        }

        // Illegal-sequence and lookback vectors for the 16 code units at p, where p[-1] is readable.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static Vector256<ushort> Avx2Window16(ushort* p, out Vector256<ushort> lookback)
        {
            Vector256<ushort> mask = Vector256.Create((ushort)0xFC00);
            lookback = Vector256.Equals(Vector256.Load(p - 1) & mask, Vector256.Create((ushort)0xD800));
            Vector256<ushort> isLow = Vector256.Equals(Vector256.Load(p) & mask, Vector256.Create((ushort)0xDC00));
            return lookback ^ isLow;
        }

        // Same for the 8 code units at p; p[-1] is read only if hasPrevious.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static Vector128<ushort> Sse2Window8(ushort* p, bool hasPrevious, out Vector128<ushort> lookback)
        {
            Vector128<ushort> block = Vector128.Load(p);
            Vector128<ushort> lb = hasPrevious ? Vector128.Load(p - 1) : Sse2.ShiftLeftLogical128BitLane(block, 2);
            Vector128<ushort> mask = Vector128.Create((ushort)0xFC00);
            lookback = Vector128.Equals(lb & mask, Vector128.Create((ushort)0xD800));
            Vector128<ushort> isLow = Vector128.Equals(block & mask, Vector128.Create((ushort)0xDC00));
            return lookback ^ isLow;
        }

        // For inputs of fewer than 64 code units: true if there is no surrogate at all.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static bool Avx2ShortHasNoSurrogate(ushort* input, int n)
        {
            if (n >= 16)
            {
                Vector256<short> offset = Vector256.Create(unchecked((short)0xA800));
                Vector256<short> min = Vector256.Load((short*)input + n - 16) + offset;
                for (int i = 0; i + 16 < n; i += 16)
                {
                    min = Vector256.Min(min, Vector256.Load((short*)input + i) + offset);
                }
                return !Vector256.LessThanAny(min, Vector256.Create((short)-30720));
            }
            return SseShortHasNoSurrogate(input, n);
        }

        // For inputs of fewer than 64 code units: true if there is no surrogate at all.
        // SSE2 only: overlapping 128-bit loads.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static bool SseShortHasNoSurrogate(ushort* input, int n)
        {
            if (n >= 8)
            {
                Vector128<short> offset = Vector128.Create(unchecked((short)0xA800));
                Vector128<short> min = Vector128.Load((short*)input + n - 8) + offset;
                for (int i = 0; i + 8 < n; i += 8)
                {
                    min = Vector128.Min(min, Vector128.Load((short*)input + i) + offset);
                }
                return !Vector128.LessThanAny(min, Vector128.Create((short)-30720));
            }
            if (n >= 4)
            {
                Vector128<short> offset = Vector128.Create(unchecked((short)0xA800));
                Vector128<short> a = Sse2.X64.ConvertScalarToVector128UInt64(*(ulong*)input).AsInt16();
                Vector128<short> b = Sse2.X64.ConvertScalarToVector128UInt64(*(ulong*)(input + n - 4)).AsInt16();
                // The upper lanes are zero, and 0 + 0xA800 is not below -30720.
                Vector128<short> min = Vector128.Min(a + offset, b + offset);
                return !Vector128.LessThanAny(min, Vector128.Create((short)-30720));
            }
            for (int i = 0; i < n; i++)
            {
                if ((ushort)(input[i] - 0xD800) < 0x800)
                {
                    return false;
                }
            }
            return true;
        }

        public unsafe static void ToWellFormedAvx2(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (pInputBuffer == pOutputBuffer)
            {
                Avx2FixAll<TrueFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
            else
            {
                Avx2FixAll<FalseFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
        }

        // Processes the 64 code units at p. The state holds the carry (bits 0-1: 0b11 if the
        // code unit before p is a high surrogate) and bit 2 (the previous block had surrogates,
        // e.g., emojis: we then skip the pre-check). Returned by value to keep it in a register.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong Avx2Fix64<TInPlace>(ushort* p, ushort* q, ulong state) where TInPlace : struct, IFlag
        {
            Vector256<ushort> v0 = Vector256.Load(p);
            Vector256<ushort> v1 = Vector256.Load(p + 16);
            Vector256<ushort> v2 = Vector256.Load(p + 32);
            Vector256<ushort> v3 = Vector256.Load(p + 48);
            if (!TInPlace.Value)
            {
                v0.Store(q);
                v1.Store(q + 16);
                v2.Store(q + 32);
                v3.Store(q + 48);
            }
            // Most text has no surrogate at all.
            if (state == 0 && !Avx2AnySurrogate(v0, v1, v2, v3))
            {
                return 0;
            }
            Avx2Classify(v0, out uint high0, out uint low0);
            Avx2Classify(v1, out uint high1, out uint low1);
            Avx2Classify(v2, out uint high2, out uint low2);
            Avx2Classify(v3, out uint high3, out uint low3);
            ulong highA = high0 | ((ulong)high1 << 32);
            ulong lowA = low0 | ((ulong)low1 << 32);
            ulong highB = high2 | ((ulong)high3 << 32);
            ulong lowB = low2 | ((ulong)low3 << 32);
            ulong lbA = (highA << 2) | (state & 3);
            ulong lbB = (highB << 2) | (highA >> 62);
            if (((lbA ^ lowA) | (lbB ^ lowB)) != 0)
            {
                Avx2FixBits(q, lbA ^ lowA, lbA);
                Avx2FixBits(q + 32, lbB ^ lowB, lbB);
            }
            return (highB >> 62) | ((highA | lowA | highB | lowB) != 0 ? 4UL : 0UL);
        }

        // Not inlined: its own compilation unit, so that the JIT can inline all the helpers.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void Avx2FixAll<TInPlace>(ushort* input, int n, ushort* output) where TInPlace : struct, IFlag
        {
            ushort* p = input;
            ushort* q = output;
            ushort* end = input + n;
            ulong state = 0;
            if (!TInPlace.Value && n >= 128)
            {
                // Misaligned stores are expensive: after the first block, we step back so that
                // the output is 32-byte aligned. The overlapping code units are stored and
                // checked again, with the carry recomputed from the input.
                state = Avx2Fix64<TInPlace>(p, q, state);
                p += 64;
                q += 64;
                nuint misalignment = (nuint)q & 31;
                if (misalignment != 0 && (misalignment & 1) == 0)
                {
                    p -= misalignment / 2;
                    q -= misalignment / 2;
                    state = IsHighSurrogate((char)p[-1]) ? 3UL : 0UL;
                }
            }
            for (; p + 64 <= end; p += 64, q += 64)
            {
                state = Avx2Fix64<TInPlace>(p, q, state);
            }
            ulong carry = state & 3;
            for (; p + 16 <= end; p += 16, q += 16)
            {
                Vector256<ushort> v = Vector256.Load(p);
                if (!TInPlace.Value)
                {
                    v.Store(q);
                }
                Avx2Classify(v, out uint high, out uint low);
                ulong lb = ((ulong)high << 2) | carry;
                carry = high >> 30;
                if ((uint)lb != low)
                {
                    Avx2FixBits(q, (uint)lb ^ low, lb);
                }
            }
            if (p != end)
            {
                // The last 1 to 15 code units, using overlapping windows. Code units that were
                // already written are recomputed identically. (In place, a replaced code unit
                // was a lone surrogate: replacing it does not change the pairing of the others.)
                if (n > 16)
                {
                    Vector256<ushort> last = Vector256.Load(end - 16);
                    if (!TInPlace.Value)
                    {
                        last.Store(output + n - 16);
                    }
                    Vector256<ushort> ill = Avx2Window16(end - 16, out Vector256<ushort> lookback);
                    if (ill != Vector256<ushort>.Zero)
                    {
                        Avx2FixBits(output + n - 16, (uint)Avx2.MoveMask(ill.AsByte()), (uint)Avx2.MoveMask(lookback.AsByte()));
                    }
                }
                else if (n >= 8)
                {
                    Vector128<ushort> first = Vector128.Load(input);
                    Vector128<ushort> last = Vector128.Load(input + n - 8);
                    if (!TInPlace.Value)
                    {
                        first.Store(output);
                        last.Store(output + n - 8);
                    }
                    Vector128<ushort> ill = Sse2Window8(input, false, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        Avx2FixBits(output, (uint)Sse2.MoveMask(ill.AsByte()), (uint)Sse2.MoveMask(lookback.AsByte()));
                    }
                    if (n > 8)
                    {
                        ill = Sse2Window8(input + n - 8, true, out lookback);
                        if (ill != Vector128<ushort>.Zero)
                        {
                            Avx2FixBits(output + n - 8, (uint)Sse2.MoveMask(ill.AsByte()), (uint)Sse2.MoveMask(lookback.AsByte()));
                        }
                    }
                }
                else
                {
                    ToWellFormedScalarFrom(input, n, output, 0, false);
                    return;
                }
            }
            // The string may not end with a high surrogate.
            if (n > 0 && IsHighSurrogate((char)output[n - 1]))
            {
                output[n - 1] = ReplacementCharacter;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static char* GetPointerToFirstInvalidCharAvx2(char* pInputBuffer, int inputLength)
        {
            ushort* input = (ushort*)pInputBuffer;
            int n = inputLength;
            if (n < 64 && Avx2ShortHasNoSurrogate(input, n))
            {
                return pInputBuffer + n;
            }
            ushort* p = input;
            ushort* end = input + n;
            ulong carry = 0;
            bool surrogates = false;
            for (; p + 64 <= end; p += 64)
            {
                Vector256<ushort> v0 = Vector256.Load(p);
                Vector256<ushort> v1 = Vector256.Load(p + 16);
                Vector256<ushort> v2 = Vector256.Load(p + 32);
                Vector256<ushort> v3 = Vector256.Load(p + 48);
                if (!surrogates && carry == 0 && !Avx2AnySurrogate(v0, v1, v2, v3))
                {
                    continue;
                }
                Avx2Classify(v0, out uint high0, out uint low0);
                Avx2Classify(v1, out uint high1, out uint low1);
                ulong high = high0 | ((ulong)high1 << 32);
                ulong low = low0 | ((ulong)low1 << 32);
                surrogates = (high | low) != 0;
                ulong lb = (high << 2) | carry;
                if (lb != low)
                {
                    return (char*)p + Avx2FirstError(lb ^ low, lb);
                }
                carry = high >> 62;
                Avx2Classify(v2, out high0, out low0);
                Avx2Classify(v3, out high1, out low1);
                high = high0 | ((ulong)high1 << 32);
                low = low0 | ((ulong)low1 << 32);
                lb = (high << 2) | carry;
                if (lb != low)
                {
                    return (char*)p + 32 + Avx2FirstError(lb ^ low, lb);
                }
                carry = high >> 62;
            }
            for (; p + 16 <= end; p += 16)
            {
                Avx2Classify(Vector256.Load(p), out uint high, out uint low);
                ulong lb = ((ulong)high << 2) | carry;
                if ((uint)lb != low)
                {
                    return (char*)p + Avx2FirstError((uint)lb ^ low, lb);
                }
                carry = high >> 30;
            }
            if (p != end)
            {
                // The last 1 to 15 code units, using overlapping windows: the pairs that
                // were already checked are well formed.
                if (n > 16)
                {
                    Vector256<ushort> ill = Avx2Window16(end - 16, out Vector256<ushort> lookback);
                    if (ill != Vector256<ushort>.Zero)
                    {
                        return pInputBuffer + n - 16 + Avx2FirstError((uint)Avx2.MoveMask(ill.AsByte()), (uint)Avx2.MoveMask(lookback.AsByte()));
                    }
                }
                else if (n >= 8)
                {
                    Vector128<ushort> ill = Sse2Window8(input, false, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        return pInputBuffer + Avx2FirstError((uint)Sse2.MoveMask(ill.AsByte()), (uint)Sse2.MoveMask(lookback.AsByte()));
                    }
                    if (n > 8)
                    {
                        ill = Sse2Window8(input + n - 8, true, out lookback);
                        if (ill != Vector128<ushort>.Zero)
                        {
                            return pInputBuffer + n - 8 + Avx2FirstError((uint)Sse2.MoveMask(ill.AsByte()), (uint)Sse2.MoveMask(lookback.AsByte()));
                        }
                    }
                }
                else
                {
                    return GetPointerToFirstInvalidCharScalarFrom(pInputBuffer, n, 0, false);
                }
            }
            return (n > 0 && IsHighSurrogate(pInputBuffer[n - 1])) ? pInputBuffer + n - 1 : pInputBuffer + n;
        }

        // SSE (Westmere and better, no AVX).
        //
        // Same approach as the AVX-512 kernel with 128-bit registers: we pack the comparison
        // results of two registers (packsswb) so that one pmovmskb gives one bit per code
        // unit for 16 code units. A 64-bit word covers 64 code units, the high-surrogate
        // bitmask is shifted by one, carrying one bit between words. Errors are fixed with a
        // scalar loop over the bitmask; the end of the input uses overlapping windows.

        // One bit per code unit for the 16 code units in v0 and v1.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SseClassify(Vector128<ushort> v0, Vector128<ushort> v1, out uint high, out uint low)
        {
            Vector128<ushort> fc = Vector128.Create((ushort)0xFC00);
            Vector128<ushort> d8 = Vector128.Create((ushort)0xD800);
            Vector128<ushort> dc = Vector128.Create((ushort)0xDC00);
            Vector128<ushort> m0 = v0 & fc;
            Vector128<ushort> m1 = v1 & fc;
            high = (uint)Sse2.MoveMask(Sse2.PackSignedSaturate(Vector128.Equals(m0, d8).AsInt16(), Vector128.Equals(m1, d8).AsInt16()));
            low = (uint)Sse2.MoveMask(Sse2.PackSignedSaturate(Vector128.Equals(m0, dc).AsInt16(), Vector128.Equals(m1, dc).AsInt16()));
        }

        // True if any of the 64 code units is a surrogate (see Avx2AnySurrogate).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SseAnySurrogate(Vector128<ushort> v0, Vector128<ushort> v1, Vector128<ushort> v2, Vector128<ushort> v3,
                                            Vector128<ushort> v4, Vector128<ushort> v5, Vector128<ushort> v6, Vector128<ushort> v7)
        {
            Vector128<short> offset = Vector128.Create(unchecked((short)0xA800));
            Vector128<short> m0 = Vector128.Min(v0.AsInt16() + offset, v1.AsInt16() + offset);
            Vector128<short> m1 = Vector128.Min(v2.AsInt16() + offset, v3.AsInt16() + offset);
            Vector128<short> m2 = Vector128.Min(v4.AsInt16() + offset, v5.AsInt16() + offset);
            Vector128<short> m3 = Vector128.Min(v6.AsInt16() + offset, v7.AsInt16() + offset);
            Vector128<short> min = Vector128.Min(Vector128.Min(m0, m1), Vector128.Min(m2, m3));
            return Vector128.LessThanAny(min, Vector128.Create((short)-30720));
        }

        // Replaces the lone surrogates given illseq = lookback ^ low, one bit per code unit
        // (see Arm64FixBits). Inlined: a call inside the main loop makes the JIT spill registers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void SseFixBits(ushort* output, ulong illseq, ulong lookback)
        {
            while (illseq != 0)
            {
                int j = BitOperations.TrailingZeroCount(illseq);
                output[j - (int)((lookback >> j) & 1)] = ReplacementCharacter;
                illseq &= illseq - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SseFirstError(ulong illseq, ulong lookback)
        {
            int j = BitOperations.TrailingZeroCount(illseq);
            return j - (int)((lookback >> j) & 1);
        }

        // High and low bits for 64 code units.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SseClassify64(Vector128<ushort> v0, Vector128<ushort> v1, Vector128<ushort> v2, Vector128<ushort> v3,
                                          Vector128<ushort> v4, Vector128<ushort> v5, Vector128<ushort> v6, Vector128<ushort> v7,
                                          out ulong high, out ulong low)
        {
            SseClassify(v0, v1, out uint h0, out uint l0);
            SseClassify(v2, v3, out uint h1, out uint l1);
            SseClassify(v4, v5, out uint h2, out uint l2);
            SseClassify(v6, v7, out uint h3, out uint l3);
            high = h0 | ((ulong)h1 << 16) | ((ulong)h2 << 32) | ((ulong)h3 << 48);
            low = l0 | ((ulong)l1 << 16) | ((ulong)l2 << 32) | ((ulong)l3 << 48);
        }

        public unsafe static void ToWellFormedSse(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (pInputBuffer == pOutputBuffer)
            {
                SseFixAll<TrueFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
            else
            {
                SseFixAll<FalseFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
        }

        // Processes the 64 code units at p. The state holds the carry (bit 0: the code unit
        // before p is a high surrogate) and bit 1 (the previous block had surrogates: we then
        // skip the pre-check). We return the new state by value: a 'ref' parameter would keep
        // it out of registers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong SseFix64<TInPlace>(ushort* p, ushort* q, ulong state) where TInPlace : struct, IFlag
        {
            Vector128<ushort> v0 = Vector128.Load(p);
            Vector128<ushort> v1 = Vector128.Load(p + 8);
            Vector128<ushort> v2 = Vector128.Load(p + 16);
            Vector128<ushort> v3 = Vector128.Load(p + 24);
            Vector128<ushort> v4 = Vector128.Load(p + 32);
            Vector128<ushort> v5 = Vector128.Load(p + 40);
            Vector128<ushort> v6 = Vector128.Load(p + 48);
            Vector128<ushort> v7 = Vector128.Load(p + 56);
            if (!TInPlace.Value)
            {
                v0.Store(q);
                v1.Store(q + 8);
                v2.Store(q + 16);
                v3.Store(q + 24);
                v4.Store(q + 32);
                v5.Store(q + 40);
                v6.Store(q + 48);
                v7.Store(q + 56);
            }
            // Most text has no surrogate at all.
            if (state == 0 && !SseAnySurrogate(v0, v1, v2, v3, v4, v5, v6, v7))
            {
                return 0;
            }
            SseClassify64(v0, v1, v2, v3, v4, v5, v6, v7, out ulong high, out ulong low);
            ulong lb = (high << 1) | (state & 1);
            if (lb != low)
            {
                SseFixBits(q, lb ^ low, lb);
            }
            return (high >> 63) | ((high | low) != 0 ? 2UL : 0UL);
        }

        // Not inlined: as its own compilation unit, the JIT has the budget to inline all the
        // helpers above (otherwise, they become calls in the main loop).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void SseFixAll<TInPlace>(ushort* input, int n, ushort* output) where TInPlace : struct, IFlag
        {
            ushort* p = input;
            ushort* q = output;
            ushort* end = input + n;
            ulong state = 0;
            if (!TInPlace.Value && n >= 128)
            {
                // Misaligned 16-byte stores are expensive: after the first block, we step
                // back so that the output is 16-byte aligned. The overlapping code units are
                // stored and checked again, with the carry recomputed from the input.
                state = SseFix64<TInPlace>(p, q, state);
                p += 64;
                q += 64;
                nuint misalignment = (nuint)q & 15;
                if (misalignment != 0 && (misalignment & 1) == 0)
                {
                    p -= misalignment / 2;
                    q -= misalignment / 2;
                    state = IsHighSurrogate((char)p[-1]) ? 1UL : 0UL;
                }
            }
            for (; p + 64 <= end; p += 64, q += 64)
            {
                state = SseFix64<TInPlace>(p, q, state);
            }
            ulong carry = state & 1;
            for (; p + 16 <= end; p += 16, q += 16)
            {
                Vector128<ushort> v0 = Vector128.Load(p);
                Vector128<ushort> v1 = Vector128.Load(p + 8);
                if (!TInPlace.Value)
                {
                    v0.Store(q);
                    v1.Store(q + 8);
                }
                SseClassify(v0, v1, out uint high, out uint low);
                // 16 bits per block: bit 16 of lb belongs to the next block.
                ulong lb = ((ulong)high << 1) | carry;
                carry = high >> 15;
                ulong illseq = (lb ^ low) & 0xFFFF;
                if (illseq != 0)
                {
                    SseFixBits(q, illseq, lb);
                }
            }
            if (p != end)
            {
                // The last 1 to 15 code units, using overlapping 8-wide windows. Code units that
                // were already written are recomputed identically. (In place, a replaced code unit
                // was a lone surrogate: replacing it does not change the pairing of the others.)
                if (n < 8)
                {
                    ToWellFormedScalarFrom(input, n, output, 0, false);
                    return;
                }
                if (end - p > 8 || p == input)
                {
                    // 9 to 15 code units left, or an input of 8 to 15 code units: one window at p
                    // (which may be the start of the input).
                    Vector128<ushort> first = Vector128.Load(p);
                    if (!TInPlace.Value)
                    {
                        first.Store(q);
                    }
                    Vector128<ushort> ill = Sse2Window8(p, p != input, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        SseFixBits(q, Sse2Bits8(ill), Sse2Bits8(lookback));
                    }
                }
                if (n > 8)
                {
                    Vector128<ushort> last = Vector128.Load(end - 8);
                    if (!TInPlace.Value)
                    {
                        last.Store(output + n - 8);
                    }
                    Vector128<ushort> ill = Sse2Window8(end - 8, true, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        SseFixBits(output + n - 8, Sse2Bits8(ill), Sse2Bits8(lookback));
                    }
                }
            }
            // The string may not end with a high surrogate.
            if (n > 0 && IsHighSurrogate((char)output[n - 1]))
            {
                output[n - 1] = ReplacementCharacter;
            }
        }

        // One bit per 16-bit lane.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Sse2Bits8(Vector128<ushort> v)
        {
            return (uint)Sse2.MoveMask(Sse2.PackSignedSaturate(v.AsInt16(), Vector128<short>.Zero));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static char* GetPointerToFirstInvalidCharSse(char* pInputBuffer, int inputLength)
        {
            ushort* input = (ushort*)pInputBuffer;
            int n = inputLength;
            if (n < 64 && SseShortHasNoSurrogate(input, n))
            {
                return pInputBuffer + n;
            }
            ushort* p = input;
            ushort* end = input + n;
            ulong carry = 0;
            bool surrogates = false;
            for (; p + 64 <= end; p += 64)
            {
                Vector128<ushort> v0 = Vector128.Load(p);
                Vector128<ushort> v1 = Vector128.Load(p + 8);
                Vector128<ushort> v2 = Vector128.Load(p + 16);
                Vector128<ushort> v3 = Vector128.Load(p + 24);
                Vector128<ushort> v4 = Vector128.Load(p + 32);
                Vector128<ushort> v5 = Vector128.Load(p + 40);
                Vector128<ushort> v6 = Vector128.Load(p + 48);
                Vector128<ushort> v7 = Vector128.Load(p + 56);
                if (!surrogates && carry == 0 && !SseAnySurrogate(v0, v1, v2, v3, v4, v5, v6, v7))
                {
                    continue;
                }
                SseClassify64(v0, v1, v2, v3, v4, v5, v6, v7, out ulong high, out ulong low);
                surrogates = (high | low) != 0;
                ulong lb = (high << 1) | carry;
                if (lb != low)
                {
                    return (char*)p + SseFirstError(lb ^ low, lb);
                }
                carry = high >> 63;
            }
            for (; p + 16 <= end; p += 16)
            {
                SseClassify(Vector128.Load(p), Vector128.Load(p + 8), out uint high, out uint low);
                // 16 bits per block: bit 16 of lb belongs to the next block.
                ulong lb = ((ulong)high << 1) | carry;
                ulong illseq = (lb ^ low) & 0xFFFF;
                if (illseq != 0)
                {
                    return (char*)p + SseFirstError(illseq, lb);
                }
                carry = high >> 15;
            }
            if (p != end)
            {
                if (n < 8)
                {
                    return GetPointerToFirstInvalidCharScalarFrom(pInputBuffer, n, 0, false);
                }
                if (end - p > 8 || p == input)
                {
                    Vector128<ushort> ill = Sse2Window8(p, p != input, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        return (char*)p + SseFirstError(Sse2Bits8(ill), Sse2Bits8(lookback));
                    }
                }
                if (n > 8)
                {
                    Vector128<ushort> ill = Sse2Window8(end - 8, true, out Vector128<ushort> lookback);
                    if (ill != Vector128<ushort>.Zero)
                    {
                        return pInputBuffer + n - 8 + SseFirstError(Sse2Bits8(ill), Sse2Bits8(lookback));
                    }
                }
            }
            return (n > 0 && IsHighSurrogate(pInputBuffer[n - 1])) ? pInputBuffer + n - 1 : pInputBuffer + n;
        }

        // AVX-512 (Ice Lake and better, AMD Zen 4 and better).
        //
        // A low surrogate must be preceded by a high surrogate and a high surrogate must be
        // followed by a low surrogate. With one bit per code unit, the input is well formed
        // exactly where (high << 1) == low, where the bit shifted out of a block is carried
        // into the next one. A string may not end with a high surrogate: we treat the end
        // of the string as a code unit that is not a low surrogate.
        //
        // simdutf compares each block with a 'lookback' block loaded one code unit earlier;
        // shifting bitmasks instead lets us load each code unit only once.

        // Bit j is set when block[j] is a high (resp. low) surrogate.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Avx512Classify(Vector512<ushort> block, out uint high, out uint low)
        {
            Vector512<ushort> masked = block & Vector512.Create((ushort)0xFC00);
            high = (uint)Vector512.Equals(masked, Vector512.Create((ushort)0xD800)).ExtractMostSignificantBits();
            low = (uint)Vector512.Equals(masked, Vector512.Create((ushort)0xDC00)).ExtractMostSignificantBits();
        }

        // True if any of the 128 code units is a surrogate (0xD800 to 0xDFFF): adding 0x2800
        // maps the surrogates to 0 to 0x7FF, so we only need one comparison on the minimum.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Avx512AnySurrogate(Vector512<ushort> b0, Vector512<ushort> b1, Vector512<ushort> b2, Vector512<ushort> b3)
        {
            Vector512<ushort> offset = Vector512.Create((ushort)0x2800);
            Vector512<ushort> min = Vector512.Min(Vector512.Min(b0 + offset, b1 + offset), Vector512.Min(b2 + offset, b3 + offset));
            return Vector512.LessThanAny(min, Vector512.Create((ushort)0x800));
        }

        // Lanes 0 to count - 1 set.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<ushort> Avx512FirstLanes(int count)
        {
            return Vector512.LessThan(Vector512<ushort>.Indices, Vector512.Create((ushort)count));
        }

        // Lane j set when bit j of 'bits' is set.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<ushort> Avx512MaskFromBits(uint bits)
        {
            // Lanes 0-15 get the low 16 bits, lanes 16-31 the high 16 bits (vpermw). We avoid
            // Vector512.Create(Vector256, Vector256), which the JIT compiles to calls.
            Vector512<ushort> v = Avx512BW.PermuteVar32x16(Vector512.Create(bits).AsUInt16(),
                Vector512.Create((ushort)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                                 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
            Vector512<ushort> bit = Vector512.Create(
                (ushort)0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080,
                0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000, 0x8000,
                0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080,
                0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000, 0x8000);
            return Vector512.Equals(v & bit, bit);
        }

        // Writes the (up to) 32 code units of 'block' to 'output', replacing the lone
        // surrogates, given lbHigh = (high << 1) | carry. Bit 0 of the lone high
        // surrogates refers to output[-1], which was already written.
        // Inlined: a call inside the main loop would force the JIT to spill vector registers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void Avx512FixSlow(Vector512<ushort> block, ushort* output, ulong lbHigh, uint low, Vector512<ushort> storeMask)
        {
            ulong loneHigh = lbHigh & ~(ulong)low;
            uint loneLow = low & ~(uint)lbHigh;
            if ((loneHigh & 1) != 0)
            {
                output[-1] = ReplacementCharacter;
            }
            uint bad = loneLow | (uint)(loneHigh >> 1);
            Vector512<ushort> fixedBlock = Vector512.ConditionalSelect(Avx512MaskFromBits(bad), Vector512.Create((ushort)ReplacementCharacter), block);
            Avx512BW.MaskStore(output, storeMask, fixedBlock);
        }

        // 32 code units, not at the end of the input. Returns the new carry.
        // (We avoid 'ref' parameters: they keep the carry out of registers.)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong Avx512Fix32(Vector512<ushort> block, ushort* output, bool inPlace, ulong carry)
        {
            Avx512Classify(block, out uint high, out uint low);
            ulong lbHigh = ((ulong)high << 1) | carry;
            if ((uint)lbHigh == low)
            {
                if (!inPlace)
                {
                    block.Store(output);
                }
            }
            else
            {
                Avx512FixSlow(block, output, (uint)lbHigh, low, Vector512<ushort>.AllBitsSet);
            }
            return high >> 31;
        }

        // 64 code units, not at the end of the input.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong Avx512Fix64(Vector512<ushort> block1, Vector512<ushort> block2, ushort* output, bool inPlace, ulong carry)
        {
            Avx512Classify(block1, out uint high1, out uint low1);
            Avx512Classify(block2, out uint high2, out uint low2);
            ulong high = high1 | ((ulong)high2 << 32);
            ulong low = low1 | ((ulong)low2 << 32);
            if (((high << 1) | carry) == low)
            {
                if (!inPlace)
                {
                    block1.Store(output);
                    block2.Store(output + 32);
                }
                return high >> 63;
            }
            carry = Avx512Fix32(block1, output, inPlace, carry);
            return Avx512Fix32(block2, output + 32, inPlace, carry);
        }

        // The last 0 to 31 code units.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void Avx512FixTail(ushort* input, int count, ushort* output, bool inPlace, ulong carry)
        {
            Vector512<ushort> mask = Avx512FirstLanes(count);
            Vector512<ushort> block = Avx512BW.MaskLoad(input, mask, Vector512<ushort>.Zero);
            Avx512Classify(block, out uint high, out uint low);
            // Bit 'count' of lbHigh is set if the input ends with a high surrogate:
            // it is then a lone surrogate, since bit 'count' of low is zero.
            ulong lbHigh = ((ulong)high << 1) | carry;
            if (lbHigh == low)
            {
                if (!inPlace)
                {
                    Avx512BW.MaskStore(output, mask, block);
                }
                return;
            }
            Avx512FixSlow(block, output, lbHigh, low, mask);
        }

        public unsafe static void ToWellFormedAvx512(char* pInputBuffer, int inputLength, char* pOutputBuffer)
        {
            if (inputLength <= 0)
            {
                return;
            }
            // The loops are duplicated so that the JIT specializes them.
            if (pInputBuffer == pOutputBuffer)
            {
                Avx512FixAll<TrueFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
            else
            {
                Avx512FixAll<FalseFlag>((ushort*)pInputBuffer, inputLength, (ushort*)pOutputBuffer);
            }
        }

        // Not inlined: as its own compilation unit, the JIT has the budget to inline all the
        // helpers (otherwise, some of them become calls in the main loop).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void Avx512FixAll<TInPlace>(ushort* input, int n, ushort* output) where TInPlace : struct, IFlag
        {
            bool inPlace = TInPlace.Value;
            ulong carry = 0;
            int i = 0;
            if (!inPlace && n >= 256)
            {
                // Misaligned stores are expensive: after the first 64 code units, we step back
                // so that the output is 64-byte aligned. The overlapping code units are stored
                // and checked again, with the carry recomputed from the input.
                carry = Avx512Fix64(Vector512.Load(input), Vector512.Load(input + 32), output, inPlace, carry);
                i = 64;
                nuint misalignment = (nuint)(output + i) & 63;
                if (misalignment != 0 && (misalignment & 1) == 0)
                {
                    i -= (int)(misalignment / 2);
                    carry = IsHighSurrogate((char)input[i - 1]) ? 1UL : 0UL;
                }
            }
            for (; i + 128 <= n; i += 128)
            {
                Vector512<ushort> block0 = Vector512.Load(input + i);
                Vector512<ushort> block1 = Vector512.Load(input + i + 32);
                Vector512<ushort> block2 = Vector512.Load(input + i + 64);
                Vector512<ushort> block3 = Vector512.Load(input + i + 96);
                // Most text has no surrogate at all.
                if (carry == 0 && !Avx512AnySurrogate(block0, block1, block2, block3))
                {
                    if (!inPlace)
                    {
                        block0.Store(output + i);
                        block1.Store(output + i + 32);
                        block2.Store(output + i + 64);
                        block3.Store(output + i + 96);
                    }
                    continue;
                }
                carry = Avx512Fix64(block0, block1, output + i, inPlace, carry);
                carry = Avx512Fix64(block2, block3, output + i + 64, inPlace, carry);
            }
            if (i + 64 <= n)
            {
                carry = Avx512Fix64(Vector512.Load(input + i), Vector512.Load(input + i + 32), output + i, inPlace, carry);
                i += 64;
            }
            if (i + 32 <= n)
            {
                carry = Avx512Fix32(Vector512.Load(input + i), output + i, inPlace, carry);
                i += 32;
            }
            Avx512FixTail(input + i, n - i, output + i, inPlace, carry);
        }

        // Offset of the first lone surrogate given lbHigh = (high << 1) | carry, when
        // lbHigh != low: either the code unit at j - 1 is a high surrogate without a low
        // surrogate after it, or the code unit at j is a low surrogate without a high
        // surrogate before it. The result is -1 when the lone surrogate precedes the block.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Avx512FirstError(ulong lbHigh, ulong low)
        {
            int j = BitOperations.TrailingZeroCount(lbHigh ^ low);
            return j - (int)((lbHigh >> j) & 1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe static char* GetPointerToFirstInvalidCharAvx512(char* pInputBuffer, int inputLength)
        {
            ushort* input = (ushort*)pInputBuffer;
            int n = inputLength;
            ulong carry = 0;
            ulong high, low, lbHigh;
            int i = 0;
            for (; i + 128 <= n; i += 128)
            {
                Vector512<ushort> block0 = Vector512.Load(input + i);
                Vector512<ushort> block1 = Vector512.Load(input + i + 32);
                Vector512<ushort> block2 = Vector512.Load(input + i + 64);
                Vector512<ushort> block3 = Vector512.Load(input + i + 96);
                if (carry == 0 && !Avx512AnySurrogate(block0, block1, block2, block3))
                {
                    continue;
                }
                Avx512Classify(block0, out uint high0, out uint low0);
                Avx512Classify(block1, out uint high1, out uint low1);
                high = high0 | ((ulong)high1 << 32);
                low = low0 | ((ulong)low1 << 32);
                lbHigh = (high << 1) | carry;
                if (lbHigh != low)
                {
                    return pInputBuffer + i + Avx512FirstError(lbHigh, low);
                }
                carry = high >> 63;
                Avx512Classify(block2, out uint high2, out uint low2);
                Avx512Classify(block3, out uint high3, out uint low3);
                high = high2 | ((ulong)high3 << 32);
                low = low2 | ((ulong)low3 << 32);
                lbHigh = (high << 1) | carry;
                if (lbHigh != low)
                {
                    return pInputBuffer + i + 64 + Avx512FirstError(lbHigh, low);
                }
                carry = high >> 63;
            }
            for (; i + 32 <= n; i += 32)
            {
                Avx512Classify(Vector512.Load(input + i), out uint high0, out uint low0);
                lbHigh = ((ulong)high0 << 1) | carry;
                if ((uint)lbHigh != low0)
                {
                    return pInputBuffer + i + Avx512FirstError((uint)lbHigh, low0);
                }
                carry = high0 >> 31;
            }
            {
                // The last 0 to 31 code units. Bit n - i of lbHigh is set if the input
                // ends with a high surrogate.
                Vector512<ushort> block = Avx512BW.MaskLoad(input + i, Avx512FirstLanes(n - i), Vector512<ushort>.Zero);
                Avx512Classify(block, out uint high0, out uint low0);
                lbHigh = ((ulong)high0 << 1) | carry;
                if (lbHigh != low0)
                {
                    return pInputBuffer + i + Avx512FirstError(lbHigh, low0);
                }
            }
            return pInputBuffer + n;
        }
    }
}
