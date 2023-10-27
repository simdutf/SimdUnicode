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


// public static unsafe nuint GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength)
// {
//     byte* buf_orig = pBuffer;
//     byte* end = pBuffer + bufferLength;
//     Vector256<sbyte> ascii = Vector256<sbyte>.Zero;

//     for (; pBuffer + 32 <= end; pBuffer += 32)
//     {
//         Vector256<sbyte> input = Avx.LoadVector256((sbyte*)pBuffer);
//         int notascii = Avx2.MoveMask(Avx2.CompareGreaterThan(input, ascii).AsByte());
//         if (notascii != 0)
//         {
//             return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
//         }
//     }

//     // nuint remaining_bytes = (nuint)(end - pBuffer);
//     // Vector256<sbyte> mask = Vector256<sbyte>.Zero.WithElement(0, (sbyte)((1UL << (int)remaining_bytes) - 1));
//     // Vector256<sbyte> input2 = Avx2.MaskLoad((sbyte*)pBuffer, mask.As<int>());
//     // int notascii2 = Avx2.MoveMask(Avx2.CompareGreaterThan(input2, ascii).AsByte());
//     // if (notascii2 != 0)
//     // {
//     //     return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii2);
//     // }

//     // return bufferLength;

//         // Call the scalar function for the remaining bytes
//     return Scalar_GetIndexOfFirstNonAsciiByte(pBuffer, (nuint)(end - pBuffer));
// }

public static unsafe nuint GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength)
{
    byte* buf_orig = pBuffer;
    byte* end = pBuffer + bufferLength;
    Vector256<sbyte> ascii = Vector256<sbyte>.Zero;

    for (; pBuffer + 32 <= end; pBuffer += 32)
    {
        Vector256<sbyte> input = Avx.LoadVector256((sbyte*)pBuffer);
        // int notascii = Avx2.MoveMask(Avx2.CompareGreaterThan(input, ascii).AsByte());
        int notascii = Avx2.MoveMask(input.AsByte());
        if (notascii != 0)
        {
            // Print a message for debugging
            // Console.WriteLine($"Non-ASCII character found. notascii: {notascii}, index: {(nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii)}");
            
            return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
        }
    }

    // Call the scalar function for the remaining bytes
    // return Scalar_GetIndexOfFirstNonAsciiByte(pBuffer, (nuint)(end - pBuffer));

        // Call the scalar function for the remaining bytes
    nuint scalarResult = Scalar_GetIndexOfFirstNonAsciiByte(pBuffer, (nuint)(end - pBuffer));

    // Add the number of bytes processed by SIMD
    return (nuint)(pBuffer - buf_orig) + scalarResult;

}




        //     byte* pBufferEnd = pBuffer + bufferLength;
        //     byte* pCurrent = pBuffer;

        //     /*
        //     if (ArmBase.Arm64.IsSupported)
        //     {
        //         //  Sadly I do not have an ARM based computer, so this branch is a placeholder for now
        //     }
        //     else*/ if (Sse41.IsSupported)
        //     {
        //         // Create a vector with 0x80 in the first element and 0 in the rest
        //         var ascii = Vector128<byte>.Zero.WithElement(0, 0x80);

        //         // Process in blocks of 64 bytes when possible
        //         while (pCurrent + 64 <= pBufferEnd)
        //         {
        //             // Load a vector from the current position
        //             var input = Sse2.LoadVector128(pCurrent);

        //             // Compare each byte in the vector to 0x80
        //             var notAscii = Sse2.CompareGreaterThan(input, ascii).AsByte();

        //             // If any byte is greater than 0x80
        //             if (Sse41.TestZ(notAscii, notAscii))
        //             {
        //                 // Return the index of the first non-ASCII byte
        //                 return (nuint)(pCurrent - pBuffer + BitOperations.TrailingZeroCount(notAscii.ToScalar()));
        //             }

        //             // Move to the next block
        //             pCurrent += 64;
        //         }

        //         // Process the remaining bytes
        //         {
        //             // Create a mask for the remaining bytes
        //             var mask = Vector128<byte>.Zero.WithElement(0, (byte)((1UL << (pBufferEnd - pCurrent)) - 1));

        //             // Load a vector from the current position using the mask
        //             var input = Sse41.MaskLoad(pCurrent, mask);

        //             // Compare each byte in the vector to 0x80
        //             var notAscii = Sse2.CompareGreaterThan(input, ascii).AsByte();

        //             // If any byte is greater than 0x80
        //             if (Sse41.TestZ(notAscii, notAscii))
        //             {
        //                 // Return the index of the first non-ASCII byte
        //                 return (nuint)(pCurrent - pBuffer + BitOperations.TrailingZeroCount(notAscii.ToScalar()));
        //             }
        //         }
        //     }
        // else {
        //         return SSE_GetIndexOfFirstNonAsciiByte((byte*) pCurrent, (nuint) bufferLength, (byte*) pBufferEnd);
        //     }

        // }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static unsafe nuint SSE_GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength, byte* pBufferEnd)
        // {
        //     return 0;
        // }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static unsafe nuint Scalar_GetIndexOfFirstNonAsciiByte(byte* pCurrent, nuint bufferLength, byte* pBufferEnd)
        // {
        //         // Process in blocks of 16 bytes when possible
        //         while (pCurrent + 16 <= pBufferEnd)
        //         {
        //             ulong v1 = *(ulong*)pCurrent;
        //             ulong v2 = *(ulong*)(pCurrent + 8);
        //             ulong v = v1 | v2;

        //             if ((v & 0x8080808080808080) != 0)
        //             {
        //                 for (; pCurrent < pBufferEnd; pCurrent++)
        //                 {
        //                     if (*pCurrent >= 0b10000000)
        //                     {
        //                         return (nuint)(pCurrent - pBuffer);
        //                     }
        //                 }
        //             }

        //             pCurrent += 16;
        //         }

        //         // Process the tail byte-by-byte
        //         for (; pCurrent < pBufferEnd; pCurrent++)
        //         {
        //             if (*pCurrent >= 0b10000000)
        //             {
        //                 return (nuint)(pCurrent - pBuffer);
        //             }
        //         }

        //     return bufferLength;
        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe nuint Scalar_GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength)
        {
            byte* pCurrent = pBuffer;
            byte* pBufferEnd = pBuffer + bufferLength;

            // Process in blocks of 16 bytes when possible
            while (pCurrent + 16 <= pBufferEnd)
            {
                ulong v1 = *(ulong*)pCurrent;
                ulong v2 = *(ulong*)(pCurrent + 8);
                ulong v = v1 | v2;

                if ((v & 0x8080808080808080) != 0)
                {
                    for (; pCurrent < pBufferEnd; pCurrent++)
                    {
                        if (*pCurrent >= 0b10000000)
                        {
                            return (nuint)(pCurrent - pBuffer);
                        }
                    }
                }

                pCurrent += 16;
            }

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

