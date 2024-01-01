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

        public static Vector256<byte> Lookup16(this Vector256<byte> source,
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


    public static Vector256<byte> ShiftRightLogical(this Vector256<byte> vector, byte shiftAmount)
    {
        // Reinterpret the Vector256<byte> as Vector256<ushort>
        Vector256<ushort> extended = vector.AsUInt16();

        // Perform the shift operation on each 16-bit element
        Vector256<ushort> shifted = Avx2.ShiftRightLogical(extended, shiftAmount);

        // Reinterpret back to Vector256<byte>
        Vector256<byte> narrowed = shifted.AsByte();

        return narrowed;
    }







}


namespace SimdUnicode {

    // internal static unsafe partial class Utf8Utility
    public static unsafe class Utf8Utility
    {



        // Returns a pointer to the first invalid byte in the input buffer if it's invalid, or a pointer to the end if it's valid.
        public static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)//, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment) <-The 
        {
            // Initialize out parameters
            // utf16CodeUnitCountAdjustment = 0;
            // scalarCountAdjustment = 0;

            // If the input is null or length is zero, return immediately.
            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }

            var checker = new SimdUnicode.utf8_validation.utf8_checker();
            int processedLength = 0;

            // Process each 256-bit block
            while (processedLength + 32 <= inputLength)
            {
                // Load the next 256-bit block
                Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);

                // Check the block
                checker.check_next_input(currentBlock);

                // if (checker.errors())
                // {
                //     // If an error is found, return the pointer to the start of the erroneous block
                //     return pInputBuffer + processedLength;
                // }

                // Update processed length
                processedLength += 32;
            }

            // Process remaining bytes
            if (processedLength < inputLength)
            {
                // Create a buffer to hold the remaining bytes and load them
                Span<byte> remainingBytes = stackalloc byte[32];
                for (int i = 0; i < inputLength - processedLength; i++)
                {
                    remainingBytes[i] = pInputBuffer[processedLength + i];
                }

                // Load the remaining bytes as a Vector256
                Vector256<byte> remainingBlock = Vector256.Create(remainingBytes.ToArray());

                // Check the block
                checker.check_next_input(remainingBlock);

                // if (checker.errors())
                // {
                //     // If an error is found, return the pointer to the start of the erroneous block
                //     return pInputBuffer + processedLength;
                // }
            }

            // Check for EOF
            checker.check_eof();
            if (checker.errors())
            {
                // If an error is found, return the pointer to the start of the erroneous block
                return pInputBuffer + processedLength;
            }

            // If no errors were found, return a pointer to the end of the buffer
            return pInputBuffer + inputLength;
        }
    }


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

                // This is the first point of entry for this function
                // The original C++ implementation is much more extensive and assumes a 512 bit stream as well as several implementations
                // In this case I focus solely on AVX2 instructions for prototyping and benchmarking purposes. 
                // This is the simplest least time-consuming implementation. 0
                public void check_next_input(Vector256<byte> input) {

                // Skip this for now, we'll come back later
                // Check if the entire 256-bit vector is ASCII
                // if (Ascii.SIMDIsAscii(input)) { //Bug: IsAscii wants something of char type... But this is probably wasteful to use as is as <char> is represented as a 16-bit unit. 
                // I'll implement something later. 
                //     error = Avx2.Or(error, prev_incomplete);
                // } else {
                    // Process the 256-bit vector
                    check_utf8_bytes(input, prev_input_block);
                // }

                // Update prev_incomplete and prev_input_block for the next call
                prev_incomplete = is_incomplete(input);
                prev_input_block = input;
            }

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

                // I think this is where I made a mistake (Will delete this comment later).
                private Vector256<byte> check_multibyte_lengths(Vector256<byte> input, Vector256<byte> prev_input, Vector256<byte> sc) 
                {
                    // Assuming Prev is correctly implemented to shift the bytes as required
                    Vector256<byte> prev2 = input.Prev(prev_input, 2);
                    Vector256<byte> prev3 = input.Prev(prev_input, 3);

                    // Call the must_be_2_3_continuation function with prev2 and prev3
                    Vector256<byte> must23 = must_be_2_3_continuation(prev2, prev3);

                    // Perform the AND operation with 0x80
                    Vector256<byte> must23_80 = Avx2.And(must23, Vector256.Create((byte)0x80));

                    // XOR the result with sc
                    return Avx2.Xor(must23_80, sc);
                }

                // Ensure you have the must_be_2_3_continuation function implemented as discussed earlier


                private Vector256<byte> must_be_2_3_continuation(Vector256<byte> prev2, Vector256<byte> prev3)
                {
                    // Perform saturating subtraction
                    Vector256<byte> is_third_byte = Avx2.SubtractSaturate(prev2, Vector256.Create((byte)(0b11100000 - 1)));
                    Vector256<byte> is_fourth_byte = Avx2.SubtractSaturate(prev3, Vector256.Create((byte)(0b11110000 - 1)));

                    // Combine the results using bitwise OR
                    Vector256<byte> combined = Avx2.Or(is_third_byte, is_fourth_byte);

                    // Compare combined result with zero
                    Vector256<sbyte> signedCombined = combined.AsSByte();
                    Vector256<sbyte> zero = Vector256<sbyte>.Zero;
                    Vector256<sbyte> comparisonResult = Avx2.CompareGreaterThan(signedCombined, zero);

                    // Convert the comparison result back to byte
                    return comparisonResult.AsByte();
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

                    return SimdUnicode.Helpers.CompareGreaterThan(input, max_value);
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

