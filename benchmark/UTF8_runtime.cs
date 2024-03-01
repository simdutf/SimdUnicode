// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Text;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

// Changes made from the Runtime (most of the stuff in the runtime is behind some private/internal class or some such. The path of least resistance was to copy paste.
// Copy pasted from: https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,ca18aaa87cbdbfe8
// https://source.dot.net/#System.Text.Encodings.Web/src/libraries/System.Private.CoreLib/src/System/Text/UnicodeUtility.cs,7d9353a7dd29c82b
// https://source.dot.net/#System.Text.Encodings.Web/src/libraries/System.Private.CoreLib/src/System/Text/UnicodeDebug.cs,ac87f1ec2c614dce
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,0ca89dfb3ec2a5da
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,87a9969a4e35fdde
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,ff5eb1221e0665eb
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,068a221b81a99840
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,74fc59ef51d5afa8
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,7fc454ccbfa2fc31
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,70ca16f03c3cc41b
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,5a3126ae65f71ef2
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,6ee61246657067fb
// https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Helpers.cs,706384069881fe22
// 

namespace DotnetRuntime
{

    public static unsafe partial class Utf8Utility
    {
        /// <summary>
        /// Returns <see langword="true"/> iff the low byte of <paramref name="value"/>
        /// is a UTF-8 continuation byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLowByteUtf8ContinuationByte(uint value)
        {
            // The JIT won't emit a single 8-bit signed cmp instruction (see IsUtf8ContinuationByte),
            // so the best we can do for now is the lea / cmp pair.
            // Tracked as https://github.com/dotnet/runtime/issues/10337.

            return (byte)(value - 0x80u) <= 0x3Fu;
        }

        private static bool UInt32BeginsWithUtf8ThreeByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x00C0C0F0U;
            //     const uint comparand = 0x008080E0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xF0C0C000U;
            //     const uint comparand = 0xE0808000U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x0080_80E0u) & 0x00C0_C0F0u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0xE080_8000u) & 0xF0C0_C000u) == 0));
        }
        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a UTF-8 continuation byte;
        /// i.e., has binary representation 10xxxxxx, where x is any bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsUtf8ContinuationByte(in byte value)
        {
            // This API takes its input as a readonly ref so that the JIT can emit "cmp ModRM" statements
            // directly rather than bounce a temporary through a register. That is, we want the JIT to be
            // able to emit a single "cmp byte ptr [data], C0h" statement if we're querying a memory location
            // to see if it's a continuation byte. Data that's already enregistered will go through the
            // normal "cmp reg, C0h" code paths, perhaps with some extra unnecessary "movzx" instructions.
            //
            // The below check takes advantage of the two's complement representation of negative numbers.
            // [ 0b1000_0000, 0b1011_1111 ] is [ -127 (sbyte.MinValue), -65 ]

            return (sbyte)value < -64;
        }

        private static bool UInt32FourthByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((int)value >= 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x80u) == 0));
        }
        private static bool UInt32ThirdByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x0080_0000u) == 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x8000u) == 0));
        }


        private static bool UInt32EndsWithOverlongUtf8TwoByteSequence(uint value)
        {
            // ASSUMPTION: Caller has already checked the '110yyyyy 10xxxxxx' mask of the input.
            Debug.Assert(UInt32EndsWithUtf8TwoByteMask(value));

            // Per Table 3-7, first byte of two-byte sequence must be within range C2 .. DF.
            // We already validated that it's 80 .. DF (per mask check earlier).
            // C2 = 1100 0010
            // DF = 1101 1111
            // This means that we can AND the leading byte with the mask 0001 1110 (1E),
            // and if the result is zero the sequence is overlong.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x001E_0000u) == 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x1E00u) == 0));
        }

        private static bool UInt32BeginsWithOverlongUtf8TwoByteSequence(uint value)
        {
            // ASSUMPTION: Caller has already checked the '110yyyyy 10xxxxxx' mask of the input.
            Debug.Assert(UInt32BeginsWithUtf8TwoByteMask(value));

            // Per Table 3-7, first byte of two-byte sequence must be within range C2 .. DF.
            // Since we already validated it's 80 <= ?? <= DF (per mask check earlier), now only need
            // to check that it's < C2.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((byte)value < 0xC2u))
                || (!BitConverter.IsLittleEndian && (value < 0xC200_0000u));
        }


        private static bool UInt32BeginsWithUtf8TwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x0000C0E0U;
            //     const uint comparand = 0x000080C0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xE0C00000U;
            //     const uint comparand = 0xC0800000U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x0000_80C0u) & 0x0000_C0E0u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0xC080_0000u) & 0xE0C0_0000u) == 0));
        }
        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD on a little-endian machine,
        /// returns <see langword="true"/> iff the first two bytes of the buffer are a well-formed
        /// UTF-8 two-byte sequence. This wraps the mask check and the overlong check into a
        /// single operation. Returns <see langword="false"/> if running on a big-endian machine.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian(uint value)
        {
            // Per Table 3-7, valid 2-byte sequences are [ C2..DF ] [ 80..BF ].
            // In little-endian, that would be represented as:
            // [ ######## ######## 10xxxxxx 110yyyyy ].
            // Due to the little-endian representation we can perform a trick by ANDing the low
            // WORD with the bitmask [ 11000000 11111111 ] and checking that the value is within
            // the range [ 10000000_11000010, 10000000_11011111 ]. This performs both the
            // 2-byte-sequence bitmask check and overlong form validation with one comparison.

            Debug.Assert(BitConverter.IsLittleEndian);

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value & 0xC0FFu, 0x80C2u, 0x80DFu))
                || (!BitConverter.IsLittleEndian && false);
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the last two bytes of the buffer match
        /// the UTF-8 2-byte sequence mask [ 110yyyyy 10xxxxxx ]. This method *does not*
        /// validate that the sequence is well-formed; the caller must still perform
        /// overlong form checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32EndsWithUtf8TwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0E00000U;
            //     const uint comparand = 0x80C00000U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0x0000E0C0U;
            //     const uint comparand = 0x0000C080U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x80C0_0000u) & 0xC0E0_0000u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0x0000_C080u) & 0x0000_E0C0u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD on a little-endian machine,
        /// returns <see langword="true"/> iff the last two bytes of the buffer are a well-formed
        /// UTF-8 two-byte sequence. This wraps the mask check and the overlong check into a
        /// single operation. Returns <see langword="false"/> if running on a big-endian machine.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32EndsWithValidUtf8TwoByteSequenceLittleEndian(uint value)
        {
            // See comments in UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian.

            Debug.Assert(BitConverter.IsLittleEndian);

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value & 0xC0FF_0000u, 0x80C2_0000u, 0x80DF_0000u))
                || (!BitConverter.IsLittleEndian && false);
        }

        // Returns &inputBuffer[inputLength] if the input buffer is valid.
        /// <summary>
        /// Given an input buffer <paramref name="pInputBuffer"/> of byte length <paramref name="inputLength"/>,
        /// returns a pointer to where the first invalid data appears in <paramref name="pInputBuffer"/>.
        /// </summary>
        /// <remarks>
        /// Returns a pointer to the end of <paramref name="pInputBuffer"/> if the buffer is well-formed.
        /// </remarks>
        public static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            Debug.Assert(inputLength >= 0, "Input length must not be negative.");
            Debug.Assert(pInputBuffer != null || inputLength == 0, "Input length must be zero if input buffer pointer is null.");

            // First, try to drain off as many ASCII bytes as we can from the beginning.
            nuint numAsciiBytesCounted = DotnetRuntime.Ascii.GetIndexOfFirstNonAsciiByte(pInputBuffer, (uint)inputLength);
            pInputBuffer += numAsciiBytesCounted;

            // Quick check - did we just end up consuming the entire input buffer?
            // If so, short-circuit the remainder of the method.

            inputLength -= (int)numAsciiBytesCounted;
            if (inputLength == 0)
            {
                utf16CodeUnitCountAdjustment = 0;
                scalarCountAdjustment = 0;
                return pInputBuffer;
            }

#if DEBUG
            // Keep these around for final validation at the end of the method.
            byte* pOriginalInputBuffer = pInputBuffer;
            int originalInputLength = inputLength;
#endif

            // Enregistered locals that we'll eventually out to our caller.

            int tempUtf16CodeUnitCountAdjustment = 0;
            int tempScalarCountAdjustment = 0;

            if (inputLength < sizeof(uint))
            {
                goto ProcessInputOfLessThanDWordSize;
            }

            byte* pFinalPosWhereCanReadDWordFromInputBuffer = pInputBuffer + (uint)inputLength - sizeof(uint);

            // Begin the main loop.

#if DEBUG
            byte* pLastBufferPosProcessed = null; // used for invariant checking in debug builds
#endif

            while (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
            {
                // Read 32 bits at a time. This is enough to hold any possible UTF8-encoded scalar.

                uint thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

            AfterReadDWord:

#if DEBUG
                Debug.Assert(pLastBufferPosProcessed < pInputBuffer, "Algorithm should've made forward progress since last read.");
                pLastBufferPosProcessed = pInputBuffer;
#endif

                // First, check for the common case of all-ASCII bytes.

                if (DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(thisDWord))
                {
                    // We read an all-ASCII sequence.

                    pInputBuffer += sizeof(uint);

                    // If we saw a sequence of all ASCII, there's a good chance a significant amount of following data is also ASCII.
                    // Below is basically unrolled loops with poor man's vectorization.

                    // Below check is "can I read at least five DWORDs from the input stream?"
                    // n.b. Since we incremented pInputBuffer above the below subtraction may result in a negative value,
                    // hence using nint instead of nuint.

                    if ((nint)(void*)Unsafe.ByteOffset(ref *pInputBuffer, ref *pFinalPosWhereCanReadDWordFromInputBuffer) >= 4 * sizeof(uint))
                    {
                        // We want reads in the inner loop to be aligned. So let's perform a quick
                        // ASCII check of the next 32 bits (4 bytes) now, and if that succeeds bump
                        // the read pointer up to the next aligned address.

                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                        if (!DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(thisDWord))
                        {
                            goto AfterReadDWordSkipAllBytesAsciiCheck;
                        }

                        pInputBuffer = (byte*)((nuint)(pInputBuffer + 4) & ~(nuint)3);

                        // At this point, the input buffer offset points to an aligned DWORD. We also know that there's
                        // enough room to read at least four DWORDs from the buffer. (Heed the comment a few lines above:
                        // the original 'if' check confirmed that there were 5 DWORDs before the alignment check, and
                        // the alignment check consumes at most a single DWORD.)

                        byte* pInputBufferFinalPosAtWhichCanSafelyLoop = pFinalPosWhereCanReadDWordFromInputBuffer - 3 * sizeof(uint); // can safely read 4 DWORDs here
                        nuint trailingZeroCount;

                        // pInputBuffer is 32-bit aligned but not necessary 128-bit aligned, so we're
                        // going to perform an unaligned load. We don't necessarily care about aligning
                        // this because we pessimistically assume we'll encounter non-ASCII data at some
                        // point in the not-too-distant future (otherwise we would've stayed entirely
                        // within the all-ASCII vectorized code at the entry to this method).
                        if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
                        {
                            // declare bitMask128 inside of the AdvSimd.Arm64.IsSupported check
                            // so it gets removed on non-Arm64 builds.
                            Vector128<byte> bitMask128 = BitConverter.IsLittleEndian ?
                                Vector128.Create((ushort)0x1001).AsByte() :
                                Vector128.Create((ushort)0x0110).AsByte();
                            do
                            {
                                ulong mask = GetNonAsciiBytes(AdvSimd.LoadVector128(pInputBuffer), bitMask128);
                                if (mask != 0)
                                {
                                    trailingZeroCount = (nuint)BitOperations.TrailingZeroCount(mask) >> 2;
                                    goto LoopTerminatedEarlyDueToNonAsciiData;
                                }

                                pInputBuffer += 4 * sizeof(uint); // consumed 4 DWORDs
                            } while (pInputBuffer <= pInputBufferFinalPosAtWhichCanSafelyLoop);
                        }
                        else
                        {
                            do
                            {
                                if (Sse2.IsSupported)
                                {
                                    uint mask = (uint)Sse2.MoveMask(Sse2.LoadVector128(pInputBuffer));
                                    if (mask != 0)
                                    {
                                        trailingZeroCount = (nuint)BitOperations.TrailingZeroCount(mask);
                                        goto LoopTerminatedEarlyDueToNonAsciiData;
                                    }
                                }
                                else
                                {
                                    if (!DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(((uint*)pInputBuffer)[0] | ((uint*)pInputBuffer)[1]))
                                    {
                                        goto LoopTerminatedEarlyDueToNonAsciiDataInFirstPair;
                                    }

                                    if (!DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(((uint*)pInputBuffer)[2] | ((uint*)pInputBuffer)[3]))
                                    {
                                        goto LoopTerminatedEarlyDueToNonAsciiDataInSecondPair;
                                    }
                                }

                                pInputBuffer += 4 * sizeof(uint); // consumed 4 DWORDs
                            } while (pInputBuffer <= pInputBufferFinalPosAtWhichCanSafelyLoop);
                        }

                        continue; // need to perform a bounds check because we might be running out of data

                    LoopTerminatedEarlyDueToNonAsciiData:
                        // x86 can only be little endian, while ARM can be big or little endian
                        // so if we reached this label we need to check both combinations are supported
                        Debug.Assert((AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian) || Sse2.IsSupported);


                        // The 'mask' value will have a 0 bit for each ASCII byte we saw and a 1 bit
                        // for each non-ASCII byte we saw. trailingZeroCount will count the number of ASCII bytes,
                        // bump our input counter by that amount, and resume processing from the
                        // "the first byte is no longer ASCII" portion of the main loop.
                        // We should not expect a total number of zeroes equal or larger than 16.
                        Debug.Assert(trailingZeroCount < 16);

                        pInputBuffer += trailingZeroCount;
                        if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            goto ProcessRemainingBytesSlow;
                        }

                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer); // no longer guaranteed to be aligned
                        goto BeforeProcessTwoByteSequence;

                    LoopTerminatedEarlyDueToNonAsciiDataInSecondPair:

                        pInputBuffer += 2 * sizeof(uint); // consumed 2 DWORDs

                    LoopTerminatedEarlyDueToNonAsciiDataInFirstPair:

                        // We know that there's *at least* two DWORDs of data remaining in the buffer.
                        // We also know that one of them (or both of them) contains non-ASCII data somewhere.
                        // Let's perform a quick check here to bypass the logic at the beginning of the main loop.

                        thisDWord = *(uint*)pInputBuffer; // still aligned here
                        if (DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(thisDWord))
                        {
                            pInputBuffer += sizeof(uint); // consumed 1 more DWORD
                            thisDWord = *(uint*)pInputBuffer; // still aligned here
                        }

                        goto AfterReadDWordSkipAllBytesAsciiCheck;
                    }

                    continue; // not enough data remaining to unroll loop - go back to beginning with bounds checks
                }

            AfterReadDWordSkipAllBytesAsciiCheck:

                Debug.Assert(!DotnetRuntime.Ascii.AllBytesInUInt32AreAscii(thisDWord)); // this should have been handled earlier

                // Next, try stripping off ASCII bytes one at a time.
                // We only handle up to three ASCII bytes here since we handled the four ASCII byte case above.

                {
                    uint numLeadingAsciiBytes = DotnetRuntime.Ascii.CountNumberOfLeadingAsciiBytesFromUInt32WithSomeNonAsciiData(thisDWord);
                    pInputBuffer += numLeadingAsciiBytes;

                    if (pFinalPosWhereCanReadDWordFromInputBuffer < pInputBuffer)
                    {
                        goto ProcessRemainingBytesSlow; // Input buffer doesn't contain enough data to read a DWORD
                    }
                    else
                    {
                        // The input buffer at the current offset contains a non-ASCII byte.
                        // Read an entire DWORD and fall through to multi-byte consumption logic.
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                    }
                }

            BeforeProcessTwoByteSequence:

                // At this point, we suspect we're working with a multi-byte code unit sequence,
                // but we haven't yet validated it for well-formedness.

                // The masks and comparands are derived from the Unicode Standard, Table 3-6.
                // Additionally, we need to check for valid byte sequences per Table 3-7.

                // Check the 2-byte case.

                thisDWord -= (BitConverter.IsLittleEndian) ? 0x0000_80C0u : 0xC080_0000u;
                if ((thisDWord & (BitConverter.IsLittleEndian ? 0x0000_C0E0u : 0xE0C0_0000u)) == 0)
                {
                    // Per Table 3-7, valid sequences are:
                    // [ C2..DF ] [ 80..BF ]
                    //
                    // Due to our modification of 'thisDWord' above, this becomes:
                    // [ 02..1F ] [ 00..3F ]
                    //
                    // We've already checked that the leading byte was originally in the range [ C0..DF ]
                    // and that the trailing byte was originally in the range [ 80..BF ], so now we only need
                    // to check that the modified leading byte is >= [ 02 ].

                    if ((BitConverter.IsLittleEndian && (byte)thisDWord < 0x02u)
                        || (!BitConverter.IsLittleEndian && thisDWord < 0x0200_0000u))
                    {
                        goto Error; // overlong form - leading byte was [ C0 ] or [ C1 ]
                    }

                ProcessTwoByteSequenceSkipOverlongFormCheck:

                    // Optimization: If this is a two-byte-per-character language like Cyrillic or Hebrew,
                    // there's a good chance that if we see one two-byte run then there's another two-byte
                    // run immediately after. Let's check that now.

                    // On little-endian platforms, we can check for the two-byte UTF8 mask *and* validate that
                    // the value isn't overlong using a single comparison. On big-endian platforms, we'll need
                    // to validate the mask and validate that the sequence isn't overlong as two separate comparisons.

                    if ((BitConverter.IsLittleEndian && UInt32EndsWithValidUtf8TwoByteSequenceLittleEndian(thisDWord))
                        || (!BitConverter.IsLittleEndian && (UInt32EndsWithUtf8TwoByteMask(thisDWord) && !UInt32EndsWithOverlongUtf8TwoByteSequence(thisDWord))))
                    {
                        // We have two runs of two bytes each.
                        pInputBuffer += 4;
                        tempUtf16CodeUnitCountAdjustment -= 2; // 4 UTF-8 code units -> 2 UTF-16 code units (and 2 scalars)

                        if (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            // Optimization: If we read a long run of two-byte sequences, the next sequence is probably
                            // also two bytes. Check for that first before going back to the beginning of the loop.

                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                            if (BitConverter.IsLittleEndian)
                            {
                                if (UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian(thisDWord))
                                {
                                    // The next sequence is a valid two-byte sequence.
                                    goto ProcessTwoByteSequenceSkipOverlongFormCheck;
                                }
                            }
                            else
                            {
                                if (UInt32BeginsWithUtf8TwoByteMask(thisDWord))
                                {
                                    if (UInt32BeginsWithOverlongUtf8TwoByteSequence(thisDWord))
                                    {
                                        goto Error; // The next sequence purports to be a 2-byte sequence but is overlong.
                                    }

                                    goto ProcessTwoByteSequenceSkipOverlongFormCheck;
                                }
                            }

                            // If we reached this point, the next sequence is something other than a valid
                            // two-byte sequence, so go back to the beginning of the loop.
                            goto AfterReadDWord;
                        }
                        else
                        {
                            goto ProcessRemainingBytesSlow; // Running out of data - go down slow path
                        }
                    }

                    // The buffer contains a 2-byte sequence followed by 2 bytes that aren't a 2-byte sequence.
                    // Unlikely that a 3-byte sequence would follow a 2-byte sequence, so perhaps remaining
                    // bytes are ASCII?

                    tempUtf16CodeUnitCountAdjustment--; // 2-byte sequence + (some number of ASCII bytes) -> 1 UTF-16 code units (and 1 scalar) [+ trailing]

                    if (UInt32ThirdByteIsAscii(thisDWord))
                    {
                        if (UInt32FourthByteIsAscii(thisDWord))
                        {
                            pInputBuffer += 4;
                        }
                        else
                        {
                            pInputBuffer += 3;

                            // A two-byte sequence followed by an ASCII byte followed by a non-ASCII byte.
                            // Read in the next DWORD and jump directly to the start of the multi-byte processing block.

                            if (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
                            {
                                thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                                goto BeforeProcessTwoByteSequence;
                            }
                        }
                    }
                    else
                    {
                        pInputBuffer += 2;
                    }

                    continue;
                }

                // Check the 3-byte case.
                // We need to restore the C0 leading byte we stripped out earlier, then we can strip out the expected E0 byte.

                thisDWord -= (BitConverter.IsLittleEndian) ? (0x0080_00E0u - 0x0000_00C0u) : (0xE000_8000u - 0xC000_0000u);
                if ((thisDWord & (BitConverter.IsLittleEndian ? 0x00C0_C0F0u : 0xF0C0_C000u)) == 0)
                {
                ProcessThreeByteSequenceWithCheck:

                    // We assume the caller has confirmed that the bit pattern is representative of a three-byte
                    // sequence, but it may still be overlong or surrogate. We need to check for these possibilities.
                    //
                    // Per Table 3-7, valid sequences are:
                    // [   E0   ] [ A0..BF ] [ 80..BF ]
                    // [ E1..EC ] [ 80..BF ] [ 80..BF ]
                    // [   ED   ] [ 80..9F ] [ 80..BF ]
                    // [ EE..EF ] [ 80..BF ] [ 80..BF ]
                    //
                    // Big-endian examples of using the above validation table:
                    // E0A0 = 1110 0000 1010 0000 => invalid (overlong ) patterns are 1110 0000 100# ####
                    // ED9F = 1110 1101 1001 1111 => invalid (surrogate) patterns are 1110 1101 101# ####
                    // If using the bitmask ......................................... 0000 1111 0010 0000 (=0F20),
                    // Then invalid (overlong) patterns match the comparand ......... 0000 0000 0000 0000 (=0000),
                    // And invalid (surrogate) patterns match the comparand ......... 0000 1101 0010 0000 (=0D20).
                    //
                    // It's ok if the caller has manipulated 'thisDWord' (e.g., by subtracting 0xE0 or 0x80)
                    // as long as they haven't touched the bits we're about to use in our mask checking below.

                    if (BitConverter.IsLittleEndian)
                    {
                        // The "overlong or surrogate" check can be implemented using a single jump, but there's
                        // some overhead to moving the bits into the correct locations in order to perform the
                        // correct comparison, and in practice the processor's branch prediction capability is
                        // good enough that we shouldn't bother. So we'll use two jumps instead.

                        // Can't extract this check into its own helper method because JITter produces suboptimal
                        // assembly, even with aggressive inlining.

                        // Code below becomes 5 instructions: test, jz, lea, test, jz

                        if (((thisDWord & 0x0000_200Fu) == 0) || (((thisDWord - 0x0000_200Du) & 0x0000_200Fu) == 0))
                        {
                            goto Error; // overlong or surrogate
                        }
                    }
                    else
                    {
                        if (((thisDWord & 0x0F20_0000u) == 0) || (((thisDWord - 0x0D20_0000u) & 0x0F20_0000u) == 0))
                        {
                            goto Error; // overlong or surrogate
                        }
                    }

                ProcessSingleThreeByteSequenceSkipOverlongAndSurrogateChecks:

                    // Occasionally one-off ASCII characters like spaces, periods, or newlines will make their way
                    // in to the text. If this happens strip it off now before seeing if the next character
                    // consists of three code units.

                    // Branchless: consume a 3-byte UTF-8 sequence and optionally an extra ASCII byte from the end.

                    nint asciiAdjustment;
                    if (BitConverter.IsLittleEndian)
                    {
                        asciiAdjustment = (int)thisDWord >> 31; // smear most significant bit across entire value
                    }
                    else
                    {
                        asciiAdjustment = (nint)(sbyte)thisDWord >> 7; // smear most significant bit of least significant byte across entire value
                    }

                    // asciiAdjustment = 0 if fourth byte is ASCII; -1 otherwise

                    // Please *DO NOT* reorder the below two lines. It provides extra defense in depth in case this method
                    // is ever changed such that pInputBuffer becomes a 'ref byte' instead of a simple 'byte*'. It's valid
                    // to add 4 before backing up since we already checked previously that the input buffer contains at
                    // least a DWORD's worth of data, so we're not going to run past the end of the buffer where the GC can
                    // no longer track the reference. However, we can't back up before adding 4, since we might back up to
                    // before the start of the buffer, and the GC isn't guaranteed to be able to track this.

                    pInputBuffer += 4; // optimistically, assume consumed a 3-byte UTF-8 sequence plus an extra ASCII byte
                    pInputBuffer += asciiAdjustment; // back up if we didn't actually consume an ASCII byte

                    tempUtf16CodeUnitCountAdjustment -= 2; // 3 (or 4) UTF-8 bytes -> 1 (or 2) UTF-16 code unit (and 1 [or 2] scalar)

                SuccessfullyProcessedThreeByteSequence:

                    if (IntPtr.Size >= 8 && BitConverter.IsLittleEndian)
                    {
                        // x64 little-endian optimization: A three-byte character could indicate CJK text,
                        // which makes it likely that the character following this one is also CJK.
                        // We'll try to process several three-byte sequences at a time.

                        // The check below is really "can we read 9 bytes from the input buffer?" since 'pFinalPos...' is already offset
                        // n.b. The subtraction below could result in a negative value (since we advanced pInputBuffer above), so
                        // use nint instead of nuint.

                        if ((nint)(pFinalPosWhereCanReadDWordFromInputBuffer - pInputBuffer) >= 5)
                        {
                            ulong thisQWord = Unsafe.ReadUnaligned<ulong>(pInputBuffer);

                            // Stage the next 32 bits into 'thisDWord' so that it's ready for us in case we need to jump backward
                            // to a previous location in the loop. This offers defense against reading main memory again (which may
                            // have been modified and could lead to a race condition).

                            thisDWord = (uint)thisQWord;

                            // Is this three 3-byte sequences in a row?
                            // thisQWord = [ 10yyyyyy 1110zzzz | 10xxxxxx 10yyyyyy 1110zzzz | 10xxxxxx 10yyyyyy 1110zzzz ] [ 10xxxxxx ]
                            //               ---- CHAR 3  ----   --------- CHAR 2 ---------   --------- CHAR 1 ---------     -CHAR 3-
                            if ((thisQWord & 0xC0F0_C0C0_F0C0_C0F0ul) == 0x80E0_8080_E080_80E0ul && IsUtf8ContinuationByte(in pInputBuffer[8]))
                            {
                                // Saw a proper bitmask for three incoming 3-byte sequences, perform the
                                // overlong and surrogate sequence checking now.

                                // Check the first character.
                                // If the first character is overlong or a surrogate, fail immediately.

                                if ((((uint)thisQWord & 0x200Fu) == 0) || ((((uint)thisQWord - 0x200Du) & 0x200Fu) == 0))
                                {
                                    goto Error;
                                }

                                // Check the second character.
                                // At this point, we now know the first three bytes represent a well-formed sequence.
                                // If there's an error beyond here, we'll jump back to the "process three known good bytes"
                                // logic.

                                thisQWord >>= 24;
                                if ((((uint)thisQWord & 0x200Fu) == 0) || ((((uint)thisQWord - 0x200Du) & 0x200Fu) == 0))
                                {
                                    goto ProcessSingleThreeByteSequenceSkipOverlongAndSurrogateChecks;
                                }

                                // Check the third character (we already checked that it's followed by a continuation byte).

                                thisQWord >>= 24;
                                if ((((uint)thisQWord & 0x200Fu) == 0) || ((((uint)thisQWord - 0x200Du) & 0x200Fu) == 0))
                                {
                                    goto ProcessSingleThreeByteSequenceSkipOverlongAndSurrogateChecks;
                                }

                                pInputBuffer += 9;
                                tempUtf16CodeUnitCountAdjustment -= 6; // 9 UTF-8 bytes -> 3 UTF-16 code units (and 3 scalars)

                                goto SuccessfullyProcessedThreeByteSequence;
                            }

                            // Is this two 3-byte sequences in a row?
                            // thisQWord = [ ######## ######## | 10xxxxxx 10yyyyyy 1110zzzz | 10xxxxxx 10yyyyyy 1110zzzz ]
                            //                                   --------- CHAR 2 ---------   --------- CHAR 1 ---------
                            if ((thisQWord & 0xC0C0_F0C0_C0F0ul) == 0x8080_E080_80E0ul)
                            {
                                // Saw a proper bitmask for two incoming 3-byte sequences, perform the
                                // overlong and surrogate sequence checking now.

                                // Check the first character.
                                // If the first character is overlong or a surrogate, fail immediately.

                                if ((((uint)thisQWord & 0x200Fu) == 0) || ((((uint)thisQWord - 0x200Du) & 0x200Fu) == 0))
                                {
                                    goto Error;
                                }

                                // Check the second character.
                                // At this point, we now know the first three bytes represent a well-formed sequence.
                                // If there's an error beyond here, we'll jump back to the "process three known good bytes"
                                // logic.

                                thisQWord >>= 24;
                                if ((((uint)thisQWord & 0x200Fu) == 0) || ((((uint)thisQWord - 0x200Du) & 0x200Fu) == 0))
                                {
                                    goto ProcessSingleThreeByteSequenceSkipOverlongAndSurrogateChecks;
                                }

                                pInputBuffer += 6;
                                tempUtf16CodeUnitCountAdjustment -= 4; // 6 UTF-8 bytes -> 2 UTF-16 code units (and 2 scalars)

                                // The next byte in the sequence didn't have a 3-byte marker, so it's probably
                                // an ASCII character. Jump back to the beginning of loop processing.

                                continue;
                            }

                            if (UInt32BeginsWithUtf8ThreeByteMask(thisDWord))
                            {
                                // A single three-byte sequence.
                                goto ProcessThreeByteSequenceWithCheck;
                            }
                            else
                            {
                                // Not a three-byte sequence; perhaps ASCII?
                                goto AfterReadDWord;
                            }
                        }
                    }

                    if (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
                    {
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                        // Optimization: A three-byte character could indicate CJK text, which makes it likely
                        // that the character following this one is also CJK. We'll check for a three-byte sequence
                        // marker now and jump directly to three-byte sequence processing if we see one, skipping
                        // all of the logic at the beginning of the loop.

                        if (UInt32BeginsWithUtf8ThreeByteMask(thisDWord))
                        {
                            goto ProcessThreeByteSequenceWithCheck; // Found another [not yet validated] three-byte sequence; process
                        }
                        else
                        {
                            goto AfterReadDWord; // Probably ASCII punctuation or whitespace; go back to start of loop
                        }
                    }
                    else
                    {
                        goto ProcessRemainingBytesSlow; // Running out of data
                    }
                }

                // Assume the 4-byte case, but we need to validate.

                if (BitConverter.IsLittleEndian)
                {
                    thisDWord &= 0xC0C0_FFFFu;

                    // After the above modifications earlier in this method, we expect 'thisDWord'
                    // to have the structure [ 10000000 00000000 00uuzzzz 00010uuu ]. We'll now
                    // perform two checks to confirm this. The first will verify the
                    // [ 10000000 00000000 00###### ######## ] structure by taking advantage of two's
                    // complement representation to perform a single *signed* integer check.

                    if ((int)thisDWord > unchecked((int)0x8000_3FFF))
                    {
                        goto Error; // didn't have three trailing bytes
                    }

                    // Now we want to confirm that 0x01 <= uuuuu (otherwise this is an overlong encoding)
                    // and that uuuuu <= 0x10 (otherwise this is an out-of-range encoding).

                    thisDWord = BitOperations.RotateRight(thisDWord, 8);

                    // Now, thisDWord = [ 00010uuu 10000000 00000000 00uuzzzz ].
                    // The check is now a simple add / cmp / jcc combo.

                    if (!UnicodeUtility.IsInRangeInclusive(thisDWord, 0x1080_0010u, 0x1480_000Fu))
                    {
                        goto Error; // overlong or out-of-range
                    }
                }
                else
                {
                    thisDWord -= 0x80u;

                    // After the above modifications earlier in this method, we expect 'thisDWord'
                    // to have the structure [ 00010uuu 00uuzzzz 00yyyyyy 00xxxxxx ]. We'll now
                    // perform two checks to confirm this. The first will verify the
                    // [ ######## 00###### 00###### 00###### ] structure.

                    if ((thisDWord & 0x00C0_C0C0u) != 0)
                    {
                        goto Error; // didn't have three trailing bytes
                    }

                    // Now we want to confirm that 0x01 <= uuuuu (otherwise this is an overlong encoding)
                    // and that uuuuu <= 0x10 (otherwise this is an out-of-range encoding).
                    // This is a simple range check. (We don't care about the low two bytes.)

                    if (!UnicodeUtility.IsInRangeInclusive(thisDWord, 0x1010_0000u, 0x140F_FFFFu))
                    {
                        goto Error; // overlong or out-of-range
                    }
                }

                // Validation of 4-byte case complete.

                pInputBuffer += 4;
                tempUtf16CodeUnitCountAdjustment -= 2; // 4 UTF-8 bytes -> 2 UTF-16 code units
                tempScalarCountAdjustment--; // 2 UTF-16 code units -> 1 scalar

                continue; // go back to beginning of loop for processing
            }

            goto ProcessRemainingBytesSlow;

        ProcessInputOfLessThanDWordSize:

            Debug.Assert(inputLength < 4);
            nuint inputBufferRemainingBytes = (uint)inputLength;
            goto ProcessSmallBufferCommon;

        ProcessRemainingBytesSlow:

            inputBufferRemainingBytes = (nuint)(void*)Unsafe.ByteOffset(ref *pInputBuffer, ref *pFinalPosWhereCanReadDWordFromInputBuffer) + 4;

        ProcessSmallBufferCommon:

            Debug.Assert(inputBufferRemainingBytes < 4);
            while (inputBufferRemainingBytes > 0)
            {
                uint firstByte = pInputBuffer[0];

                if ((byte)firstByte < 0x80u)
                {
                    // 1-byte (ASCII) case
                    pInputBuffer++;
                    inputBufferRemainingBytes--;
                    continue;
                }
                else if (inputBufferRemainingBytes >= 2)
                {
                    uint secondByte = pInputBuffer[1]; // typed as 32-bit since we perform arithmetic (not just comparisons) on this value
                    if ((byte)firstByte < 0xE0u)
                    {
                        // 2-byte case
                        if ((byte)firstByte >= 0xC2u && IsLowByteUtf8ContinuationByte(secondByte))
                        {
                            pInputBuffer += 2;
                            tempUtf16CodeUnitCountAdjustment--; // 2 UTF-8 bytes -> 1 UTF-16 code unit (and 1 scalar)
                            inputBufferRemainingBytes -= 2;
                            continue;
                        }
                    }
                    else if (inputBufferRemainingBytes >= 3)
                    {
                        if ((byte)firstByte < 0xF0u)
                        {
                            if ((byte)firstByte == 0xE0u)
                            {
                                if (!UnicodeUtility.IsInRangeInclusive(secondByte, 0xA0u, 0xBFu))
                                {
                                    goto Error; // overlong encoding
                                }
                            }
                            else if ((byte)firstByte == 0xEDu)
                            {
                                if (!UnicodeUtility.IsInRangeInclusive(secondByte, 0x80u, 0x9Fu))
                                {
                                    goto Error; // would be a UTF-16 surrogate code point
                                }
                            }
                            else
                            {
                                if (!IsLowByteUtf8ContinuationByte(secondByte))
                                {
                                    goto Error; // first trailing byte doesn't have proper continuation marker
                                }
                            }

                            if (IsUtf8ContinuationByte(in pInputBuffer[2]))
                            {
                                pInputBuffer += 3;
                                tempUtf16CodeUnitCountAdjustment -= 2; // 3 UTF-8 bytes -> 2 UTF-16 code units (and 2 scalars)
                                inputBufferRemainingBytes -= 3;
                                continue;
                            }
                        }
                    }
                }

                // Error - no match.

                goto Error;
            }

            // If we reached this point, we're out of data, and we saw no bad UTF8 sequence.

#if DEBUG
            // Quick check that for the success case we're going to fulfill our contract of returning &inputBuffer[inputLength].
            Debug.Assert(pOriginalInputBuffer + originalInputLength == pInputBuffer, "About to return an unexpected value.");
#endif

        Error:

            // Report back to our caller how far we got before seeing invalid data.
            // (Also used for normal termination when falling out of the loop above.)

            utf16CodeUnitCountAdjustment = tempUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment = tempScalarCountAdjustment;
            return pInputBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CompExactlyDependsOn(typeof(AdvSimd.Arm64))]
        private static ulong GetNonAsciiBytes(Vector128<byte> value, Vector128<byte> bitMask128)
        {
            if (!AdvSimd.Arm64.IsSupported || !BitConverter.IsLittleEndian)
            {
                throw new PlatformNotSupportedException();
            }

            Vector128<byte> mostSignificantBitIsSet = AdvSimd.ShiftRightArithmetic(value.AsSByte(), 7).AsByte();
            Vector128<byte> extractedBits = AdvSimd.And(mostSignificantBitIsSet, bitMask128);
            extractedBits = AdvSimd.Arm64.AddPairwise(extractedBits, extractedBits);
            return extractedBits.AsUInt64().ToScalar();
        }
    }

    internal static class UnicodeDebug
    {
        [Conditional("DEBUG")]
        internal static void AssertIsBmpCodePoint(uint codePoint)
        {
            if (!UnicodeUtility.IsBmpCodePoint(codePoint))
            {
                Debug.Fail($"The value {ToHexString(codePoint)} is not a valid BMP code point.");
            }
        }

        [Conditional("DEBUG")]
        internal static void AssertIsHighSurrogateCodePoint(uint codePoint)
        {
            if (!UnicodeUtility.IsHighSurrogateCodePoint(codePoint))
            {
                Debug.Fail($"The value {ToHexString(codePoint)} is not a valid UTF-16 high surrogate code point.");
            }
        }

        [Conditional("DEBUG")]
        internal static void AssertIsLowSurrogateCodePoint(uint codePoint)
        {
            if (!UnicodeUtility.IsLowSurrogateCodePoint(codePoint))
            {
                Debug.Fail($"The value {ToHexString(codePoint)} is not a valid UTF-16 low surrogate code point.");
            }
        }

        [Conditional("DEBUG")]
        internal static void AssertIsValidCodePoint(uint codePoint)
        {
            if (!UnicodeUtility.IsValidCodePoint(codePoint))
            {
                Debug.Fail($"The value {ToHexString(codePoint)} is not a valid Unicode code point.");
            }
        }

        [Conditional("DEBUG")]
        internal static void AssertIsValidScalar(uint scalarValue)
        {
            if (!UnicodeUtility.IsValidUnicodeScalar(scalarValue))
            {
                Debug.Fail($"The value {ToHexString(scalarValue)} is not a valid Unicode scalar value.");
            }
        }

        [Conditional("DEBUG")]
        internal static void AssertIsValidSupplementaryPlaneScalar(uint scalarValue)
        {
            if (!UnicodeUtility.IsValidUnicodeScalar(scalarValue) || UnicodeUtility.IsBmpCodePoint(scalarValue))
            {
                Debug.Fail($"The value {ToHexString(scalarValue)} is not a valid supplementary plane Unicode scalar value.");
            }
        }

        /// <summary>
        /// Formats a code point as the hex string "U+XXXX".
        /// </summary>
        /// <remarks>
        /// The input value doesn't have to be a real code point in the Unicode codespace. It can be any integer.
        /// </remarks>
        private static string ToHexString(uint codePoint)
        {
            return FormattableString.Invariant($"U+{codePoint:X4}");
        }
    }

    internal static class UnicodeUtility
    {
        /// <summary>
        /// The Unicode replacement character U+FFFD.
        /// </summary>
        public const uint ReplacementChar = 0xFFFD;

        /// <summary>
        /// Returns the Unicode plane (0 through 16, inclusive) which contains this code point.
        /// </summary>
        public static int GetPlane(uint codePoint)
        {
            UnicodeDebug.AssertIsValidCodePoint(codePoint);

            return (int)(codePoint >> 16);
        }

        /// <summary>
        /// Returns a Unicode scalar value from two code points representing a UTF-16 surrogate pair.
        /// </summary>
        public static uint GetScalarFromUtf16SurrogatePair(uint highSurrogateCodePoint, uint lowSurrogateCodePoint)
        {
            UnicodeDebug.AssertIsHighSurrogateCodePoint(highSurrogateCodePoint);
            UnicodeDebug.AssertIsLowSurrogateCodePoint(lowSurrogateCodePoint);

            // This calculation comes from the Unicode specification, Table 3-5.
            // Need to remove the D800 marker from the high surrogate and the DC00 marker from the low surrogate,
            // then fix up the "wwww = uuuuu - 1" section of the bit distribution. The code is written as below
            // to become just two instructions: shl, lea.

            return (highSurrogateCodePoint << 10) + lowSurrogateCodePoint - ((0xD800U << 10) + 0xDC00U - (1 << 16));
        }

        /// <summary>
        /// Given a Unicode scalar value, gets the number of UTF-16 code units required to represent this value.
        /// </summary>
        public static int GetUtf16SequenceLength(uint value)
        {
            UnicodeDebug.AssertIsValidScalar(value);

            value -= 0x10000;   // if value < 0x10000, high byte = 0xFF; else high byte = 0x00
            value += (2 << 24); // if value < 0x10000, high byte = 0x01; else high byte = 0x02
            value >>= 24;       // shift high byte down
            return (int)value;  // and return it
        }

        /// <summary>
        /// Decomposes an astral Unicode scalar into UTF-16 high and low surrogate code units.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetUtf16SurrogatesFromSupplementaryPlaneScalar(uint value, out char highSurrogateCodePoint, out char lowSurrogateCodePoint)
        {
            UnicodeDebug.AssertIsValidSupplementaryPlaneScalar(value);

            // This calculation comes from the Unicode specification, Table 3-5.

            highSurrogateCodePoint = (char)((value + ((0xD800u - 0x40u) << 10)) >> 10);
            lowSurrogateCodePoint = (char)((value & 0x3FFu) + 0xDC00u);
        }

        /// <summary>
        /// Given a Unicode scalar value, gets the number of UTF-8 code units required to represent this value.
        /// </summary>
        public static int GetUtf8SequenceLength(uint value)
        {
            UnicodeDebug.AssertIsValidScalar(value);

            // The logic below can handle all valid scalar values branchlessly.
            // It gives generally good performance across all inputs, and on x86
            // it's only six instructions: lea, sar, xor, add, shr, lea.

            // 'a' will be -1 if input is < 0x800; else 'a' will be 0
            // => 'a' will be -1 if input is 1 or 2 UTF-8 code units; else 'a' will be 0

            int a = ((int)value - 0x0800) >> 31;

            // The number of UTF-8 code units for a given scalar is as follows:
            // - U+0000..U+007F => 1 code unit
            // - U+0080..U+07FF => 2 code units
            // - U+0800..U+FFFF => 3 code units
            // - U+10000+       => 4 code units
            //
            // If we XOR the incoming scalar with 0xF800, the chart mutates:
            // - U+0000..U+F7FF => 3 code units
            // - U+F800..U+F87F => 1 code unit
            // - U+F880..U+FFFF => 2 code units
            // - U+10000+       => 4 code units
            //
            // Since the 1- and 3-code unit cases are now clustered, they can
            // both be checked together very cheaply.

            value ^= 0xF800u;
            value -= 0xF880u;   // if scalar is 1 or 3 code units, high byte = 0xFF; else high byte = 0x00
            value += (4 << 24); // if scalar is 1 or 3 code units, high byte = 0x03; else high byte = 0x04
            value >>= 24;       // shift high byte down

            // Final return value:
            // - U+0000..U+007F => 3 + (-1) * 2 = 1
            // - U+0080..U+07FF => 4 + (-1) * 2 = 2
            // - U+0800..U+FFFF => 3 + ( 0) * 2 = 3
            // - U+10000+       => 4 + ( 0) * 2 = 4
            return (int)value + (a * 2);
        }

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is an ASCII
        /// character ([ U+0000..U+007F ]).
        /// </summary>
        /// <remarks>
        /// Per http://www.unicode.org/glossary/#ASCII, ASCII is only U+0000..U+007F.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiCodePoint(uint value) => value <= 0x7Fu;

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is in the
        /// Basic Multilingual Plane (BMP).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBmpCodePoint(uint value) => value <= 0xFFFFu;

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a UTF-16 high surrogate code point,
        /// i.e., is in [ U+D800..U+DBFF ], inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHighSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xD800U, 0xDBFFU);

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is between
        /// <paramref name="lowerBound"/> and <paramref name="upperBound"/>, inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInRangeInclusive(uint value, uint lowerBound, uint upperBound) => (value - lowerBound) <= (upperBound - lowerBound);

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a UTF-16 low surrogate code point,
        /// i.e., is in [ U+DC00..U+DFFF ], inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLowSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xDC00U, 0xDFFFU);

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a UTF-16 surrogate code point,
        /// i.e., is in [ U+D800..U+DFFF ], inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xD800U, 0xDFFFU);

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="codePoint"/> is a valid Unicode code
        /// point, i.e., is in [ U+0000..U+10FFFF ], inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidCodePoint(uint codePoint) => codePoint <= 0x10FFFFU;

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a valid Unicode scalar
        /// value, i.e., is in [ U+0000..U+D7FF ], inclusive; or [ U+E000..U+10FFFF ], inclusive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidUnicodeScalar(uint value)
        {
            // This is an optimized check that on x86 is just three instructions: lea, xor, cmp.
            //
            // After the subtraction operation, the input value is modified as such:
            // [ 00000000..0010FFFF ] -> [ FFEF0000..FFFFFFFF ]
            //
            // We now want to _exclude_ the range [ FFEFD800..FFEFDFFF ] (surrogates) from being valid.
            // After the xor, this particular exclusion range becomes [ FFEF0000..FFEF07FF ].
            //
            // So now the range [ FFEF0800..FFFFFFFF ] contains all valid code points,
            // excluding surrogates. This allows us to perform a single comparison.

            return ((value - 0x110000u) ^ 0xD800u) >= 0xFFEF0800u;
        }
    }
}
