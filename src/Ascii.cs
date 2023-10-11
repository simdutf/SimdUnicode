using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;




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

/* PAR Unrolled twice:
|                 Method |   N |       Mean |    Error |   StdDev |
|----------------------- |---- |-----------:|---------:|---------:|
|     FastUnicodeIsAscii | 100 |   905.7 ns | 17.95 ns | 20.67 ns |
| StandardUnicodeIsAscii | 100 | 2,502.4 ns | 49.67 ns | 66.31 ns |
|         RuntimeIsAscii | 100 | 2,522.8 ns | 32.70 ns | 30.59 ns |
|     FastUnicodeIsAscii | 200 |   649.3 ns | 10.24 ns |  9.57 ns |
| StandardUnicodeIsAscii | 200 | 5,299.7 ns | 64.91 ns | 57.54 ns |
|         RuntimeIsAscii | 200 | 5,307.2 ns | 49.18 ns | 46.00 ns |
|     FastUnicodeIsAscii | 500 | 1,382.2 ns |  9.40 ns |  8.79 ns |
| StandardUnicodeIsAscii | 500 | 6,127.7 ns | 57.69 ns | 48.18 ns |
|         RuntimeIsAscii | 500 | 6,258.2 ns | 62.05 ns | 58.05 ns | */

                    // if (s.Length > 16)  // Adjusted for the unrolled loop
                    // {
                    //     Vector128<ushort> total = Sse41.LoadDquVector128((ushort*)pStart);
                    //     i += 8;

                    //     // Unrolling the loop by 2x
                    //     for (; i + 15 < s.Length; i += 16)
                    //     {
                    //         Vector128<ushort> raw1 = Sse41.LoadDquVector128((ushort*)pStart + i);
                    //         Vector128<ushort> raw2 = Sse41.LoadDquVector128((ushort*)pStart + i + 8);
                            
                    //         total = Sse2.Or(total, raw1);
                    //         total = Sse2.Or(total, raw2); 
                    //     }

                    //     Vector128<ushort> b127 = Vector128.Create((ushort)127);
                    //     Vector128<ushort> b = Sse41.Max(b127, total);
                    //     Vector128<ushort> b16 = Sse41.CompareEqual(b, b127);
                    //     int movemask = Sse2.MoveMask(b16.AsByte());
                    //     if (movemask != 0xffff)
                    //     {
                    //         return false;
                    //     }
                    // }

// |                 Method |   N |       Mean |    Error |   StdDev |
// |----------------------- |---- |-----------:|---------:|---------:|
// |     FastUnicodeIsAscii | 100 |   904.0 ns |  9.22 ns |  8.17 ns |
// | StandardUnicodeIsAscii | 100 | 2,396.5 ns | 11.33 ns | 10.04 ns |
// |         RuntimeIsAscii | 100 | 2,498.8 ns | 42.35 ns | 37.54 ns |
// |     FastUnicodeIsAscii | 200 | 1,270.0 ns |  7.69 ns |  6.01 ns |
// | StandardUnicodeIsAscii | 200 | 5,173.0 ns | 57.82 ns | 54.08 ns |
// |         RuntimeIsAscii | 200 | 5,197.5 ns | 15.40 ns | 13.65 ns |
// |     FastUnicodeIsAscii | 500 | 1,412.0 ns | 24.22 ns | 21.47 ns |
// | StandardUnicodeIsAscii | 500 | 6,196.5 ns | 60.78 ns | 53.88 ns |
// |         RuntimeIsAscii | 500 | 6,215.5 ns | 96.43 ns | 90.20 ns |


                    if (s.Length > 16)  // Adjusted for the unrolled loop
                    {
                        // Using zeroed vector as initialization
                        Vector128<ushort> total = Vector128<ushort>.Zero;
                        i += 8;

                        // Unrolling the loop by 2x
                        for (; i + 16 < s.Length; i += 16)
                        {
                            Vector128<ushort> raw1 = Sse41.LoadDquVector128((ushort*)pStart);
                            Vector128<ushort> raw2 = Sse41.LoadDquVector128((ushort*)pStart + i);
                            
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
    }
}
// Further reading:
// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Encodings.Web/src/System/Text/Unicode/UnicodeHelpers.cs

