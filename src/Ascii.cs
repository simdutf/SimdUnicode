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

 /* PAR:  not unrolled
|                 Method |   N |       Mean |    Error |   StdDev |
|----------------------- |---- |-----------:|---------:|---------:|
|     FastUnicodeIsAscii | 100 |   652.6 ns |  2.20 ns |  1.95 ns |
| StandardUnicodeIsAscii | 100 | 2,466.5 ns | 21.77 ns | 20.36 ns |
|         RuntimeIsAscii | 100 | 2,502.7 ns | 29.81 ns | 27.89 ns |
|     FastUnicodeIsAscii | 200 | 1,300.8 ns | 17.95 ns | 14.99 ns |
| StandardUnicodeIsAscii | 200 | 5,216.6 ns | 62.48 ns | 55.38 ns |
|         RuntimeIsAscii | 200 | 5,293.2 ns | 41.50 ns | 38.82 ns |
|     FastUnicodeIsAscii | 500 | 2,978.6 ns | 34.99 ns | 32.73 ns |
| StandardUnicodeIsAscii | 500 | 6,172.9 ns | 74.53 ns | 69.71 ns |
|         RuntimeIsAscii | 500 | 6,210.8 ns | 80.82 ns | 63.10 ns | */


/*                     if (s.Length > 8)
                    {
                        Vector128<ushort> total = Sse41.LoadDquVector128((ushort*)pStart);
                        i += 8;
                        // unrolling could be useful here:
                        for (; i + 7 < s.Length; i += 8)
                        {
                            Vector128<ushort> raw = Sse41.LoadDquVector128((ushort*)pStart + i);
                            total = Sse2.Or(total, raw);
                        } */


/* Unrolled twice:
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

// |                 Method |   N |       Mean |     Error |    StdDev |
// |----------------------- |---- |-----------:|----------:|----------:|
// |     FastUnicodeIsAscii | 100 | 1,601.3 ns |  31.62 ns |  31.05 ns |
// | StandardUnicodeIsAscii | 100 | 2,502.5 ns |  49.20 ns |  65.68 ns |
// |         RuntimeIsAscii | 100 | 2,478.5 ns |  30.08 ns |  26.66 ns |
// |     FastUnicodeIsAscii | 200 |   653.0 ns |   6.26 ns |   5.86 ns |
// | StandardUnicodeIsAscii | 200 | 5,282.7 ns | 102.28 ns | 105.03 ns |
// |         RuntimeIsAscii | 200 | 5,366.1 ns |  65.50 ns |  61.27 ns |
// |     FastUnicodeIsAscii | 500 | 1,305.4 ns |  11.85 ns |  11.09 ns |
// | StandardUnicodeIsAscii | 500 | 6,235.6 ns | 103.06 ns |  96.40 ns |
// |         RuntimeIsAscii | 500 | 6,389.6 ns | 103.20 ns |  96.53 ns |


                    // if (s.Length > 32)  // Adjusted for the 4x unrolled loop
                    // {
                    //     Vector128<ushort> total = Sse41.LoadDquVector128((ushort*)pStart);
                    //     i += 8;

                    //     // 4x loop unrolling
                    //     for (; i + 31 < s.Length; i += 32)
                    //     {
                    //         Vector128<ushort> raw1 = Sse41.LoadDquVector128((ushort*)pStart + i);
                    //         Vector128<ushort> raw2 = Sse41.LoadDquVector128((ushort*)pStart + i + 8);
                    //         Vector128<ushort> raw3 = Sse41.LoadDquVector128((ushort*)pStart + i + 16);
                    //         Vector128<ushort> raw4 = Sse41.LoadDquVector128((ushort*)pStart + i + 24);
                            
                    //         total = Sse2.Or(total, raw1);
                    //         total = Sse2.Or(total, raw2);
                    //         total = Sse2.Or(total, raw3);
                    //         total = Sse2.Or(total, raw4);
                    //     }
                    


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

