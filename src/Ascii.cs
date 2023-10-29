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

public static unsafe nuint GetIndexOfFirstNonAsciiByte(byte* pBuffer, nuint bufferLength)
{
    byte* buf_orig = pBuffer;
    byte* end = pBuffer + bufferLength;
    Vector256<sbyte> ascii = Vector256<sbyte>.Zero;



            // if (Vector256.IsHardwareAccelerated && bufferLength >= 2 * (uint)Vector256<byte>.Count){
            if (Vector256.IsHardwareAccelerated){


            //this just balloons upwards

            // |                                       Method |    N |       Mean |     Error |    StdDev |     Median |
            // |--------------------------------------------- |----- |-----------:|----------:|----------:|-----------:|
            // |            Error_GetIndexOfFirstNonAsciiByte |  100 |   157.5 ns |   2.84 ns |   2.51 ns |   157.4 ns |
            // |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   177.8 ns |   3.24 ns |   3.03 ns |   177.6 ns |
            // |         allAscii_GetIndexOfFirstNonAsciiByte |  100 |   349.6 ns |   4.17 ns |   3.48 ns |   349.1 ns |
            // | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   349.6 ns |   2.40 ns |   2.01 ns |   348.7 ns |
            // |            Error_GetIndexOfFirstNonAsciiByte |  200 |   278.6 ns |   3.67 ns |   3.43 ns |   278.6 ns |
            // |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   178.6 ns |   3.44 ns |   3.83 ns |   177.5 ns |
            // |         allAscii_GetIndexOfFirstNonAsciiByte |  200 |   301.3 ns |   2.89 ns |   2.56 ns |   301.1 ns |
            // | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   455.5 ns |   5.33 ns |   4.45 ns |   455.1 ns |
            // |            Error_GetIndexOfFirstNonAsciiByte |  500 |   980.8 ns |   4.78 ns |   4.24 ns |   982.5 ns |
            // |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   189.3 ns |   3.70 ns |   4.81 ns |   187.7 ns |
            // |         allAscii_GetIndexOfFirstNonAsciiByte |  500 |   545.9 ns |  10.85 ns |  18.72 ns |   542.1 ns |
            // | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   863.1 ns |  13.59 ns |  11.35 ns |   861.4 ns |
            // |            Error_GetIndexOfFirstNonAsciiByte | 1000 | 1,946.5 ns |  25.56 ns |  21.35 ns | 1,941.8 ns |
            // |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 1000 |   240.0 ns |   2.04 ns |   1.71 ns |   239.9 ns |
            // |         allAscii_GetIndexOfFirstNonAsciiByte | 1000 |   919.2 ns |  11.39 ns |  10.10 ns |   918.1 ns |
            // | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 1000 | 1,637.4 ns |  32.43 ns |  33.30 ns | 1,635.1 ns |
            // |            Error_GetIndexOfFirstNonAsciiByte | 2000 | 3,874.8 ns |  74.00 ns | 181.51 ns | 3,850.3 ns |
            // |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 2000 |   231.6 ns |   4.55 ns |   4.47 ns |   232.8 ns |
            // |         allAscii_GetIndexOfFirstNonAsciiByte | 2000 | 1,720.2 ns |  22.45 ns |  19.90 ns | 1,726.1 ns |
            // | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 2000 | 3,111.4 ns |  35.00 ns |  29.23 ns | 3,116.3 ns |
            // for (; pBuffer + 128 <= end; pBuffer += 128) 
            // {
            //     Vector256<sbyte> input1 = Avx.LoadVector256((sbyte*)pBuffer); // Load first 32 bytes into a vector
            //     Vector256<sbyte> input2 = Avx.LoadVector256((sbyte*)(pBuffer + 32)); // Load next 32 bytes into a vector
            //     Vector256<sbyte> input3 = Avx.LoadVector256((sbyte*)(pBuffer + 64)); // Load next 32 bytes into a vector
            //     Vector256<sbyte> input4 = Avx.LoadVector256((sbyte*)(pBuffer + 96)); // Load next 32 bytes into a vector

            //     int notascii1 = Avx2.MoveMask(input1.AsByte()); // Compare first vector with zero and create a mask
            //     int notascii2 = Avx2.MoveMask(input2.AsByte()); // Compare second vector with zero and create a mask
            //     int notascii3 = Avx2.MoveMask(input3.AsByte()); // Compare third vector with zero and create a mask
            //     int notascii4 = Avx2.MoveMask(input4.AsByte()); // Compare fourth vector with zero and create a mask

            //     // Combine the masks
            //     long combined = notascii1 | ((long)notascii2 << 16) | ((long)notascii3 << 32) | ((long)notascii4 << 48);

            //     // If any non-ASCII character is found, fast forward
            //     if (combined == 0) 
            //     {
            //         pBuffer += 128;
            //     }
            // }


        //Unrolling isn't promising : Buggy but fixing it would add yet more instructions
        // Process 64 bytes at a time 

// |                                       Method |    N |       Mean |     Error |    StdDev |     Median |
// |--------------------------------------------- |----- |-----------:|----------:|----------:|-----------:|
// |                           FastUnicodeIsAscii |  100 |   791.9 ns |  15.57 ns |  25.15 ns |   789.7 ns |
// |                       StandardUnicodeIsAscii |  100 | 2,895.1 ns |  88.75 ns | 261.69 ns | 2,976.3 ns |
// |                               RuntimeIsAscii |  100 |   422.4 ns |   2.72 ns |   2.41 ns |   421.9 ns |
// |            Error_GetIndexOfFirstNonAsciiByte |  100 |   171.3 ns |   1.80 ns |   1.50 ns |   170.6 ns |
// |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   178.7 ns |   2.92 ns |   2.87 ns |   178.3 ns |
// |         allAscii_GetIndexOfFirstNonAsciiByte |  100 |   345.9 ns |   2.86 ns |   2.54 ns |   345.9 ns |
// | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   336.3 ns |   6.11 ns |  11.02 ns |   335.3 ns |
// |                           FastUnicodeIsAscii |  200 |   811.3 ns |  15.79 ns |  18.18 ns |   816.2 ns |
// |                       StandardUnicodeIsAscii |  200 | 5,532.7 ns | 109.88 ns | 229.36 ns | 5,473.8 ns |
// |                               RuntimeIsAscii |  200 |   663.9 ns |   4.14 ns |   3.67 ns |   662.1 ns |
// |            Error_GetIndexOfFirstNonAsciiByte |  200 |   173.0 ns |   3.41 ns |   3.50 ns |   171.6 ns |
// |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   188.6 ns |   3.68 ns |   4.51 ns |   187.4 ns |
// |         allAscii_GetIndexOfFirstNonAsciiByte |  200 |   524.6 ns |   6.67 ns |   5.91 ns |   524.5 ns |
// | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   469.0 ns |   8.83 ns |   8.26 ns |   466.6 ns |
// |                           FastUnicodeIsAscii |  500 | 1,593.1 ns |  28.96 ns |  27.09 ns | 1,588.4 ns |
// |                       StandardUnicodeIsAscii |  500 | 6,535.1 ns | 117.66 ns | 199.80 ns | 6,510.1 ns |
// |                               RuntimeIsAscii |  500 |   609.9 ns |  12.04 ns |  11.83 ns |   609.5 ns |
// |            Error_GetIndexOfFirstNonAsciiByte |  500 |   171.4 ns |   1.41 ns |   1.18 ns |   171.0 ns |
// |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   187.4 ns |   3.67 ns |   4.08 ns |   186.3 ns |
// |         allAscii_GetIndexOfFirstNonAsciiByte |  500 |   876.4 ns |  12.14 ns |  10.76 ns |   873.6 ns |
// | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   860.9 ns |   8.72 ns |   7.73 ns |   857.7 ns |
// |                           FastUnicodeIsAscii | 1000 | 2,810.6 ns |  48.56 ns |  53.98 ns | 2,785.4 ns |
// |                       StandardUnicodeIsAscii | 1000 | 6,483.5 ns | 129.47 ns | 201.57 ns | 6,486.4 ns |
// |                               RuntimeIsAscii | 1000 |   613.8 ns |  12.26 ns |  19.09 ns |   611.2 ns |
// |            Error_GetIndexOfFirstNonAsciiByte | 1000 |   229.3 ns |   2.57 ns |   2.28 ns |   229.5 ns |
// |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 1000 |   239.7 ns |   2.80 ns |   2.34 ns |   239.7 ns |
// |         allAscii_GetIndexOfFirstNonAsciiByte | 1000 | 1,581.4 ns |  14.00 ns |  13.10 ns | 1,581.9 ns |
// | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 1000 | 1,624.3 ns |  29.10 ns |  27.22 ns | 1,619.9 ns |
// |                           FastUnicodeIsAscii | 2000 | 7,115.9 ns | 141.30 ns | 416.63 ns | 6,966.5 ns |
// |                       StandardUnicodeIsAscii | 2000 | 6,756.1 ns | 127.17 ns | 226.05 ns | 6,752.3 ns |
// |                               RuntimeIsAscii | 2000 |   668.3 ns |  13.17 ns |  19.30 ns |   669.6 ns |
// |            Error_GetIndexOfFirstNonAsciiByte | 2000 |   217.9 ns |   4.28 ns |   4.58 ns |   220.3 ns |
// |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 2000 |   235.8 ns |   3.68 ns |   3.26 ns |   236.5 ns |
// |         allAscii_GetIndexOfFirstNonAsciiByte | 2000 | 2,827.0 ns |  15.93 ns |  14.90 ns | 2,829.8 ns |
// | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 2000 | 3,081.2 ns |  33.03 ns |  27.58 ns | 3,077.4 ns |

/*         for (; pBuffer + 64 <= end; pBuffer += 64) 
        {
            Vector256<sbyte> input1 = Avx.LoadVector256((sbyte*)pBuffer); // Load first 32 bytes into a vector
            Vector256<sbyte> input2 = Avx.LoadVector256((sbyte*)(pBuffer + 32)); // Load next 32 bytes into a vector

            int notascii1 = Avx2.MoveMask(input1.AsByte()); // Compare first vector with zero and create a mask
            int notascii2 = Avx2.MoveMask(input2.AsByte()); // Compare second vector with zero and create a mask

            // Combine the masks
            int combined = notascii1 | (notascii2 << 16);

            // If any non-ASCII character is found, return its index
            if (combined != 0)  //buggy
            {
                return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(combined);
            }
        } */


        /* |                                       Method |    N |       Mean |     Error |    StdDev |     Median |
        |--------------------------------------------- |----- |-----------:|----------:|----------:|-----------:|

        |            Error_GetIndexOfFirstNonAsciiByte |  100 |   156.2 ns |   3.06 ns |   5.19 ns |   155.1 ns |
        |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   195.1 ns |   3.77 ns |   4.35 ns |   194.2 ns |
        |         allAscii_GetIndexOfFirstNonAsciiByte |  100 |   340.4 ns |   5.87 ns |   6.53 ns |   339.0 ns |
        | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  100 |   328.8 ns |   6.45 ns |   6.33 ns |   328.8 ns |

        |            Error_GetIndexOfFirstNonAsciiByte |  200 |   152.5 ns |   1.30 ns |   1.02 ns |   152.4 ns |
        |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   184.7 ns |   3.66 ns |   7.04 ns |   181.7 ns |
        |         allAscii_GetIndexOfFirstNonAsciiByte |  200 |   581.7 ns |  11.64 ns |  23.77 ns |   582.6 ns |
        | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  200 |   438.0 ns |   8.31 ns |   6.94 ns |   439.9 ns |

        |            Error_GetIndexOfFirstNonAsciiByte |  500 |   158.8 ns |   3.16 ns |   6.08 ns |   158.6 ns |
        |    Error_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   193.0 ns |   3.82 ns |   6.79 ns |   192.4 ns |
        |         allAscii_GetIndexOfFirstNonAsciiByte |  500 |   965.3 ns |  19.14 ns |  33.52 ns |   956.8 ns |
        | allAscii_Runtime_GetIndexOfFirstNonAsciiByte |  500 |   882.7 ns |  16.72 ns |  17.89 ns |   879.7 ns |

        |            Error_GetIndexOfFirstNonAsciiByte | 1000 |   192.0 ns |   3.22 ns |   2.85 ns |   191.9 ns |
        |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 1000 |   226.5 ns |   2.19 ns |   1.83 ns |   226.5 ns |
        |         allAscii_GetIndexOfFirstNonAsciiByte | 1000 | 1,621.0 ns |  31.91 ns |  46.77 ns | 1,616.9 ns |
        | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 1000 | 1,624.1 ns |  23.23 ns |  20.60 ns | 1,621.3 ns |

        |            Error_GetIndexOfFirstNonAsciiByte | 2000 |   182.9 ns |   1.45 ns |   1.29 ns |   182.8 ns |
        |    Error_Runtime_GetIndexOfFirstNonAsciiByte | 2000 |   228.7 ns |   4.51 ns |   5.01 ns |   228.4 ns |
        |         allAscii_GetIndexOfFirstNonAsciiByte | 2000 | 3,035.2 ns |  57.31 ns |  56.29 ns | 3,041.9 ns |
        | allAscii_Runtime_GetIndexOfFirstNonAsciiByte | 2000 | 3,069.2 ns |  50.54 ns |  44.80 ns | 3,056.9 ns | */

        
        for (; pBuffer + 32 <= end; pBuffer += 32)
        {
            Vector256<sbyte> input = Avx.LoadVector256((sbyte*)pBuffer);
            int notascii = Avx2.MoveMask(input.AsByte());
            if (notascii != 0)
            {
                // Print a message for debugging
                // Console.WriteLine($"Non-ASCII character found. notascii: {notascii}, index: {(nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii)}");
                
                return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
            } else {
                pBuffer += 32;
            }
        }
    }

    if (Vector128.IsHardwareAccelerated){
        for (; pBuffer + 16 <= end; pBuffer += 16)
        {
            Vector128<sbyte> input = Sse2.LoadVector128((sbyte*)pBuffer);
            int notascii = Sse2.MoveMask(input.AsByte());
            if (notascii != 0)
            {
                // Print a message for debugging
                // Console.WriteLine($"Non-ASCII character found. notascii: {notascii}, index: {(nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii)}");
                
                return (nuint)(pBuffer - buf_orig) + (nuint)BitOperations.TrailingZeroCount(notascii);
            } else {
                pBuffer += 16;
            }
        }
    }


        // Call the scalar function for the remaining bytes
    nuint scalarResult = Scalar_GetIndexOfFirstNonAsciiByte(pBuffer, (nuint)(end - pBuffer));

    // Add the number of bytes processed by SIMD
    return (nuint)(pBuffer - buf_orig) + scalarResult;

}

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

