using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// C# already have something that is *more or less* equivalent to our simd class:
// Vector256 https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256-1?view=net-7.0
//  I will extend it as needed

public static class Vector256Extensions
{
    // Gets the second lane of the current vector and the first lane of the previous vector and returns, then shift it right by an appropriate number of bytes (less than 16, or less than 128 bits)
    public static Vector256<byte> Prev(this Vector256<byte> current, Vector256<byte> prev, int N = 1)
    {

        // Permute2x128 takes two 128-bit lane of two 256-bit vector and fuse them into a single vector
        // 0x21 = 00 10 00 01 translates into a fusing of 
        // second 128-bit lane of first source, 
        // first 128bit lane of second source,
        Vector256<byte> shuffle = Avx2.Permute2x128(current, prev, 0x21);
        return Avx2.AlignRight(shuffle, current, (byte)(16 - N)); //shifts right by a certain amount
        //uses __m256i _mm256_alignr_epi8 under the hood
    }

        public static Vector256<byte> Lookup16(this Vector256<byte> source,Vector256<byte> lookupTable)
    {
        return Avx2.Shuffle(lookupTable, source);
    }

    //     public static Vector256<byte> Lookup16(Vector256<byte> source,
    //     byte replace0,  byte replace1,  byte replace2,  byte replace3,
    //     byte replace4,  byte replace5,  byte replace6,  byte replace7,
    //     byte replace8,  byte replace9,  byte replace10, byte replace11,
    //     byte replace12, byte replace13, byte replace14, byte replace15)
    // {
    //     if (!Avx2.IsSupported)
    //     {
    //         throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
    //     }

    //     Vector256<byte> lookupTable = Vector256.Create(
    //         replace0,  replace1,  replace2,  replace3,
    //         replace4,  replace5,  replace6,  replace7,
    //         replace8,  replace9,  replace10, replace11,
    //         replace12, replace13, replace14, replace15,
    //         // Repeat the pattern for the remaining elements
    //         replace0,  replace1,  replace2,  replace3,
    //         replace4,  replace5,  replace6,  replace7,
    //         replace8,  replace9,  replace10, replace11,
    //         replace12, replace13, replace14, replace15
    //     );

    //     return Avx2.Shuffle(lookupTable, source);
    // }

}

public static class Utf8Checker
{
    public static Vector256<byte> CheckSpecialCases(Vector256<byte> input, Vector256<byte> prev1)
    {
        // Constants
        const byte TOO_SHORT = 1 << 0;
        const byte TOO_LONG = 1 << 1;
        const byte OVERLONG_3 = 1 << 2;
        const byte SURROGATE = 1 << 4;
        const byte OVERLONG_2 = 1 << 5;
        const byte TWO_CONTS = 1 << 7;
        const byte TOO_LARGE = 1 << 3;
        const byte TOO_LARGE_1000 = 1 << 6;
        const byte OVERLONG_4 = 1 << 6;

// Lookup table for byte_1_high
Vector256<byte> lookupTableForByte1High = Vector256.Create(
    TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
    TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
    TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
    TOO_SHORT | OVERLONG_2,
    TOO_SHORT,
    TOO_SHORT | OVERLONG_3 | SURROGATE,
    TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4,
    // Repeat the pattern for the remaining elements
    TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
    TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
    TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
    TOO_SHORT | OVERLONG_2,
    TOO_SHORT,
    TOO_SHORT | OVERLONG_3 | SURROGATE,
    TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4
);

    const byte CARRY = TOO_SHORT | TOO_LONG | TWO_CONTS;

    // Lookup table for byte_1_low
    Vector256<byte> lookupTableForByte1Low = Vector256.Create(
        CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
        CARRY | OVERLONG_2,
        CARRY, CARRY,
        CARRY | TOO_LARGE,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        // Repeat the pattern for the remaining elements
        CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
        CARRY | OVERLONG_2,
        CARRY, CARRY,
        CARRY | TOO_LARGE,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
        CARRY | OVERLONG_2,
        CARRY, CARRY,
        CARRY | TOO_LARGE,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000,
        CARRY | TOO_LARGE | TOO_LARGE_1000
    );

// Lookup table for byte_2_high
Vector256<byte> lookupTableForByte2High = Vector256.Create(
    TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
    TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
    // Repeat the pattern for the remaining elements
    TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
    TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
    TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE
);

        // Perform the lookups
        Vector256<byte> byte_1_high = Vector256Extensions.Lookup16(lookupTableHigh, prev1);
        Vector256<byte> byte_1_low = Vector256Extensions.Lookup16(lookupTableLow, prev1);
        Vector256<byte> byte_2_high = Vector256Extensions.Lookup16(lookupTableHigh2, input);

        // Combine the results
        return Avx2.And(byte_1_high, Avx2.And(byte_1_low, byte_2_high));
    }
}

