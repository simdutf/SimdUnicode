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

        public static Vector256<byte> Lookup16(Vector256<byte> source,
        byte replace0,  byte replace1,  byte replace2,  byte replace3,
        byte replace4,  byte replace5,  byte replace6,  byte replace7,
        byte replace8,  byte replace9,  byte replace10, byte replace11,
        byte replace12, byte replace13, byte replace14, byte replace15)
    {
        // if (!Avx2.IsSupported)
        // {
        //     throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
        // }

        Vector256<byte> lookupTable = Vector256.Create(
            replace0,  replace1,  replace2,  replace3,
            replace4,  replace5,  replace6,  replace7,
            replace8,  replace9,  replace10, replace11,
            replace12, replace13, replace14, replace15,
            // Repeat the pattern for the remaining elements
            replace0,  replace1,  replace2,  replace3,
            replace4,  replace5,  replace6,  replace7,
            replace8,  replace9,  replace10, replace11,
            replace12, replace13, replace14, replace15
        );

        return Avx2.Shuffle(lookupTable, source);
    }



}


namespace SimdUnicode {
        public static class utf8_validation {
            public class utf8_checker {
                Vector256<byte> error;
                Vector256<byte> prev_input_block;
                Vector256<byte> prev_incomplete;

                public utf8_checker() {
                    error = Vector256<byte>.Zero;
                    prev_input_block = Vector256<byte>.Zero;
                    prev_incomplete = Vector256<byte>.Zero;
                }

                public void check_utf8_bytes(Vector256<byte> input, Vector256<byte> prev_input) {
                    Vector256<byte> prev1 = input; // Adjust this as necessary for your logic
                    Vector256<byte> sc = check_special_cases(input, prev1); 
                    error = Avx2.Or(error, check_multibyte_lengths(input, prev_input, sc)); 
                }

                public void check_eof() {
                    error = Avx2.Or(error, prev_incomplete);
                }

                // public void check_next_input(Vector256<byte>[] input) {
                //     // Assuming input is an array of Vector256<byte> representing your input data
                //     for (int i = 0; i < input.Length; i++) {
                //         check_utf8_bytes(input[i], prev_input_block);  
                //         prev_input_block = input[i];
                //     }
                //     prev_incomplete = is_incomplete(input[input.Length - 1]);
                // }

                // The original C++ implementation is much more extensive and assumes a 512 bit stream as well as several implementations
                // In this case I focus solely on AVX2 instructions for prototyping and benchmarking purposes. 
                // This is the simplest least time-consuming implementation. 0
                public void check_next_input(Vector256<byte> input) {
                // Check if the entire 256-bit vector is ASCII
                if (Ascii.SIMDIsAscii(input)) { //Bug: IsAscii wants something of char type...
                    error = Avx2.Or(error, prev_incomplete);
                } else {
                    // Process the 256-bit vector
                    check_utf8_bytes(input, prev_input_block);
                }

                // Update prev_incomplete and prev_input_block for the next call
                prev_incomplete = is_incomplete(input);
                prev_input_block = input;
            }


                // This wrong implmentation assumes an AVX512 machine which I do not have:
                // public void check_next_input(Vector512<byte>[] input) {
                //     // if (input.Length < 1) {
                //     //     throw new ArgumentException("Input must contain at least one chunk.", nameof(input));
                //     // }

                //     // Check if the entire first 256-bit vector is ASCII
                //     if (Ascii.IsAscii(input[0])) {
                //         error = Avx2.Or(error, prev_incomplete);
                //     } else {
                //         // Directly extract the lower and upper 128-bit lanes from the first 256-bit vector
                //         Vector128<byte> lowerLane = input[0].GetLower();
                //         Vector128<byte> upperLane = input[0].GetUpper();

                //         // Process each 128-bit lane
                //         check_utf8_bytes(lowerLane, prev_input_block); // Check the first 128-bit lane
                //         check_utf8_bytes(upperLane, lowerLane);        // Check the second 128-bit lane

                //         // Update prev_incomplete and prev_input_block for the next call
                //         prev_incomplete = is_incomplete(upperLane);
                //         prev_input_block = upperLane;
                //     }
                // }


                public bool errors() {
                    return !Avx2.TestZ(error, error);
                }

                private Vector256<byte> check_special_cases(Vector256<byte> input, Vector256<byte> prev1) {
                    // Define constants
                    const byte TOO_SHORT = 1 << 0;
                    const byte TOO_LONG = 1 << 1;
                    const byte OVERLONG_3 = 1 << 2;
                    const byte SURROGATE = 1 << 4;
                    const byte OVERLONG_2 = 1 << 5;
                    const byte TWO_CONTS = 1 << 7;
                    const byte TOO_LARGE = 1 << 3;
                    const byte TOO_LARGE_1000 = 1 << 6;
                    const byte OVERLONG_4 = 1 << 6;
                    const byte CARRY = TOO_SHORT | TOO_LONG | TWO_CONTS;

                    Vector256<byte> byte_1_high = prev1.ShiftRightLogical(4).Lookup16(
                        TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                        TOO_SHORT | OVERLONG_2,
                        TOO_SHORT,
                        TOO_SHORT | OVERLONG_3 | SURROGATE,
                        TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4
                    );

                    Vector256<byte> byte_1_low = (prev1 & Vector256.Create((byte)0x0F)).Lookup16(
                        CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
                        CARRY | OVERLONG_2,
                        CARRY,
                        CARRY,
                        CARRY | TOO_LARGE,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000 | SURROGATE,
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
                        CARRY | TOO_LARGE | TOO_LARGE_1000
                    );

                    Vector256<byte> byte_2_high = input.ShiftRightLogical(4).Lookup16(
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT
                    );

                    return Avx2.And(Avx2.And(byte_1_high, byte_1_low), byte_2_high);
                }

                private Vector256<byte> check_multibyte_lengths(Vector256<byte> input, Vector256<byte> prev_input, Vector256<byte> sc) {
                    Vector256<byte> prev2 = prev_input; 
                    Vector256<byte> prev3 = prev_input; 
                    Vector256<byte> must23 = new Vector256<byte>(); // Placeholder for must_be_2_3_continuation logic

                    Vector256<byte> must23_80 = Avx2.And(must23, Vector256.Create((byte)0x80));
                    return Avx2.Xor(must23_80, sc);
                }

                private Vector256<byte> is_incomplete(Vector256<byte> input) {
                    // Define the max_value as per your logic
                    byte[] maxArray = new byte[32] {
                        255, 255, 255, 255, 255, 255, 255, 255,
                        255, 255, 255, 255, 255, 255, 255, 255,
                        255, 255, 255, 255, 255, 255, 255, 255,
                        255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1
                    };
                    Vector256<byte> max_value = Vector256.Create(maxArray);

                    return Avx2.CompareGreaterThan(input, max_value);
                }
            }
        }
    }


// public static class Utf8Checker
// {
//     public static Vector256<byte> CheckSpecialCases(Vector256<byte> input, Vector256<byte> prev1)
//     {
//         // Constants
//         const byte TOO_SHORT = 1 << 0;
//         const byte TOO_LONG = 1 << 1;
//         const byte OVERLONG_3 = 1 << 2;
//         const byte SURROGATE = 1 << 4;
//         const byte OVERLONG_2 = 1 << 5;
//         const byte TWO_CONTS = 1 << 7;
//         const byte TOO_LARGE = 1 << 3;
//         const byte TOO_LARGE_1000 = 1 << 6;
//         const byte OVERLONG_4 = 1 << 6;

// // Lookup table for byte_1_high
// Vector256<byte> lookupTableForByte1High = Vector256.Create(
//     TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
//     TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
//     TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
//     TOO_SHORT | OVERLONG_2,
//     TOO_SHORT,
//     TOO_SHORT | OVERLONG_3 | SURROGATE,
//     TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4,
//     // Repeat the pattern for the remaining elements
//     TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
//     TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
//     TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
//     TOO_SHORT | OVERLONG_2,
//     TOO_SHORT,
//     TOO_SHORT | OVERLONG_3 | SURROGATE,
//     TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4
// );

//     const byte CARRY = TOO_SHORT | TOO_LONG | TWO_CONTS;

//     // Lookup table for byte_1_low
//     Vector256<byte> lookupTableForByte1Low = Vector256.Create(
//         CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
//         CARRY | OVERLONG_2,
//         CARRY, CARRY,
//         CARRY | TOO_LARGE,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         // Repeat the pattern for the remaining elements
//         CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
//         CARRY | OVERLONG_2,
//         CARRY, CARRY,
//         CARRY | TOO_LARGE,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
//         CARRY | OVERLONG_2,
//         CARRY, CARRY,
//         CARRY | TOO_LARGE,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000,
//         CARRY | TOO_LARGE | TOO_LARGE_1000
//     );

// // Lookup table for byte_2_high
// Vector256<byte> lookupTableForByte2High = Vector256.Create(
//     TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
//     TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
//     // Repeat the pattern for the remaining elements
//     TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
//     TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE,
//     TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE  | TOO_LARGE
// );

//         // Perform the lookups
//         Vector256<byte> byte_1_high = Vector256Extensions.Lookup16(lookupTableHigh, prev1);
//         Vector256<byte> byte_1_low = Vector256Extensions.Lookup16(lookupTableLow, prev1);
//         Vector256<byte> byte_2_high = Vector256Extensions.Lookup16(lookupTableHigh2, input);

//         // Combine the results
//         return Avx2.And(byte_1_high, Avx2.And(byte_1_low, byte_2_high));
//     }
// }

