using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;





// Ideally, we would want to implement something that looks like
// https://learn.microsoft.com/en-us/dotnet/api/system.text.asciiencoding?view=net-7.0
//
// See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Text/Ascii.cs
//
// See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Text/Ascii.Transcoding.cs
namespace SimdUnicode
{
    public unsafe static class Ascii
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(this char c) => c < 128;

        public static bool IsAscii(this string s)
        {
            if (s == null) return true;
            foreach (var c in s)
            {
                if (!c.IsAscii()) return false;
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(this ReadOnlySpan<char> s)
        {
            foreach (var c in s)
            {
                if (!c.IsAscii()) return false;
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool SIMDIsAscii(this ReadOnlySpan<char> s)
        {
            if (s.IsEmpty) return true;

            if (ArmBase.Arm64.IsSupported)
            {

                // We are going to OR together all the results and then use
                // the maximum value to determine if any of the characters
                // exceeds the ASCII range. See 
                // https://github.com/simdutf/simdutf/blob/master/src/arm64/implementation.cpp

                // There is not a lot of documentation, but we can read the code at
                // https://github.com/dotnet/runtime/tree/main/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/Arm
                // and see examples at
                // https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Encodings.Web/src/System/Text/Encodings/Web/OptimizedInboxTextEncoder.AdvSimd64.cs

                // Go through https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.arm.advsimd.arm64.maxacross?view=net-8.0
                fixed (char* pStart = &MemoryMarshal.GetReference(s))
                {
                    ushort max_so_far = 0;
                    int i = 0;
                    if (s.Length > 8)
                    {
                        // instead of a load, we could have set it to zero, like so...
                        // total = Vector128<ushort>.Zero;
                        // or to a custom value like this:
                        // total = DuplicateToVector128((char)0);
                        Vector128<ushort> total = AdvSimd.LoadVector128((ushort*)pStart);
                        i += 8;
                        // unrolling could be useful here:
                        for (; i + 7 < s.Length; i += 8)
                        {
                            Vector128<ushort> raw = AdvSimd.LoadVector128((ushort*)pStart + i);
                            total = AdvSimd.Or(total, raw);
                        }

                        max_so_far =
                        AdvSimd.Arm64.MaxAcross(total).ToScalar();
                    }
                    for (; i < s.Length; i++)
                    {
                        if (pStart[i] > max_so_far) { max_so_far = pStart[i]; }
                    }
                    return max_so_far < 128;
                }
            }
            else if (Sse41.IsSupported)
            {
                // Go through https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.comparelessthan?view=net-8.0
                fixed (char* pStart = &MemoryMarshal.GetReference(s))
                {
                    int i = 0;
                    if (s.Length > 16)  // Adjusted for the unrolled loop
                    {
                        Vector128<ushort> total = Sse41.LoadDquVector128((ushort*)pStart);
                        i += 8;

                        // Unrolling the loop by 2x
                        for (; i + 15 < s.Length; i += 16)
                        {
                            Vector128<ushort> raw1 = Sse41.LoadDquVector128((ushort*)pStart + i);
                            Vector128<ushort> raw2 = Sse41.LoadDquVector128((ushort*)pStart + i + 8);

                            total = Sse2.Or(total, raw1);
                            total = Sse2.Or(total, raw2);
                        }

                        Vector128<ushort> b127 = Vector128.Create((ushort)127);
                        Vector128<ushort> b = Sse41.Max(b127, total);
                        Vector128<ushort> b16 = Sse41.CompareEqual(b, b127);
                        int movemask = Sse2.MoveMask(b16.AsByte());
                        if (movemask != 0xffff)
                        {
                            return false;
                        }
                    }
                    for (; i < s.Length; i++)
                    {
                        if (pStart[i] >= 128) return false;
                    }
                    return true;
                }
            }
            // Fallback code

            foreach (var c in s)
            {
                if (!c.IsAscii()) return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength)
        {
            if (AdvSimd.Arm64.IsSupported)
            {
                return GetIndexOfFirstNonAsciiByteArm64(pBuffer, bufferLength);
            }
            // TODO: Add support for other architectures
            /*if (Vector512.IsHardwareAccelerated && Avx512Vbmi2.IsSupported)
            {
                return GetIndexOfFirstNonAsciiByteAvx512(pBuffer, bufferLength);
            }*/
            if (Avx2.IsSupported)
            {
                return GetIndexOfFirstNonAsciiByteAvx2(pBuffer, bufferLength);
            }

            if (Sse2.IsSupported)
            {
                return GetIndexOfFirstNonAsciiByteSse2(pBuffer, bufferLength);

            }

            return GetIndexOfFirstNonAsciiByteScalar(pBuffer, bufferLength);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint GetIndexOfFirstNonAsciiByteArm64(byte* pBuffer, nuint bufferLength)
        {
            byte* buf_orig = pBuffer;
            byte* end = pBuffer + bufferLength;

            for (; pBuffer + 16 <= end; pBuffer += 16)
            {
                Vector128<byte> input = AdvSimd.LoadVector128(pBuffer);
                if (AdvSimd.Arm64.MaxAcross(input).ToScalar() > 127)
                {
                    return (nuint)(pBuffer - buf_orig) + GetIndexOfFirstNonAsciiByteScalar(pBuffer, (nuint)(end - pBuffer));
                }
            }


            // Call the scalar function for the remaining bytes
            nuint scalarResult = GetIndexOfFirstNonAsciiByteScalar(pBuffer, (nuint)(end - pBuffer));

            // Add the number of bytes processed by SIMD
            return (nuint)(pBuffer - buf_orig) + scalarResult;

        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint GetIndexOfFirstNonAsciiByteSse2(byte* pBuffer, nuint bufferLength)
        {
            byte* buf_orig = pBuffer;
            byte* end = pBuffer + bufferLength;

            for (; pBuffer + 16 <= end; pBuffer += 16)
            {
                Vector128<sbyte> input = Sse2.LoadVector128((sbyte*)pBuffer);
                int notascii = Sse2.MoveMask(input.AsByte());
                if (notascii != 0)
                {
                    return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
                }
            }

            // Call the scalar function for the remaining bytes
            nuint scalarResult = GetIndexOfFirstNonAsciiByteScalar(pBuffer, (nuint)(end - pBuffer));

            // Add the number of bytes processed by SIMD
            return (nuint)(pBuffer - buf_orig) + scalarResult;

        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint GetIndexOfFirstNonAsciiByteAvx2(byte* pBuffer, nuint bufferLength)
        {
            byte* buf_orig = pBuffer;
            byte* end = pBuffer + bufferLength;

            for (; pBuffer + 32 <= end; pBuffer += 32)
            {
                Vector256<sbyte> input = Avx.LoadVector256((sbyte*)pBuffer);
                int notascii = Avx2.MoveMask(input.AsByte());
                if (notascii != 0)
                {
                    return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
                }
            }



            // Call the scalar function for the remaining bytes
            nuint scalarResult = GetIndexOfFirstNonAsciiByteScalar(pBuffer, (nuint)(end - pBuffer));

            // Add the number of bytes processed by SIMD
            return (nuint)(pBuffer - buf_orig) + scalarResult;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint GetIndexOfFirstNonAsciiByteScalar(byte* pBuffer, nuint bufferLength)
        {
            byte* pCurrent = pBuffer;
            byte* pBufferEnd = pBuffer + bufferLength;

            // Process the tail byte-by-byte
            for (; pCurrent < pBufferEnd; pCurrent++)
            {
                if (*pCurrent >= 0b10000000)
                {
                    return (nuint)(pCurrent - pBuffer);
                }
            }

            return bufferLength;
        }




    }
}
// Further reading:
// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Encodings.Web/src/System/Text/Unicode/UnicodeHelpers.cs

