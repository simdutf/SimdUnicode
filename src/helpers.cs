using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SimdUnicode {

    // internal static unsafe partial class Utf8Utility
    public static unsafe class Helpers
    {

    public static Vector256<byte> CompareGreaterThan(Vector256<byte> left, Vector256<byte> right)
    {
        if (!Avx2.IsSupported)
        {
            throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
        }

        // Reinterpret the vectors as Vector256<sbyte>
        Vector256<sbyte> leftSBytes = left.AsSByte();
        Vector256<sbyte> rightSBytes = right.AsSByte();

        // Perform the comparison
        Vector256<sbyte> comparisonResult = Avx2.CompareGreaterThan(leftSBytes, rightSBytes);

        // Reinterpret the results back to bytes
        Vector256<byte> result = comparisonResult.AsByte();

        return result;
    }
}
}