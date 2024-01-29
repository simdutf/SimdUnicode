using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Linq;
using System.Runtime.CompilerServices;

// C# already have something that is *more or less* equivalent to our C++ simd class:
// Vector256 https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256-1?view=net-7.0
//  I extend it as needed


// |                      Method |    N |       Mean |     Error |    StdDev |   Gen0 | Allocated |
// |---------------------------- |----- |-----------:|----------:|----------:|-------:|----------:|
// | SIMDUtf8ValidationValidUtf8 |  100 |   165.8 us |   2.87 us |   2.55 us | 0.4883 |  54.69 KB |
// | SIMDUtf8ValidationValidUtf8 | 8000 | 8,733.5 us | 167.05 us | 211.27 us |      - |  33.21 KB |

public static class Vector256Extensions
{
    // Gets the second lane of the current vector and the first lane of the previous vector and returns, then shift it right by an appropriate number of bytes (less than 16, or less than 128 bits)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static Vector256<byte> Prev(this Vector256<byte> current, Vector256<byte> prev, int N = 1)
    {

        // Permute2x128 takes two 128-bit lane of two 256-bit vector and fuse them into a single vector
        // 0x21 = 00 10 00 01 translates into a fusing of 
        // second 128-bit lane of first source, 
        // first 128bit lane of second source,
        Vector256<byte> shuffle = Avx2.Permute2x128(prev, current, 0x21);
        return Avx2.AlignRight(current, shuffle, (byte)(16 - N)); //shifts right by a certain amount
    }

    public static Vector256<byte> Lookup16(this Vector256<byte> source, Vector256<byte> lookupTable)
    {
        return Avx2.Shuffle(lookupTable, source);
    }

    public static Vector256<byte> Lookup16(this Vector256<byte> source,
    byte replace0, byte replace1, byte replace2, byte replace3,
    byte replace4, byte replace5, byte replace6, byte replace7,
    byte replace8, byte replace9, byte replace10, byte replace11,
    byte replace12, byte replace13, byte replace14, byte replace15)
    {
        // if (!Avx2.IsSupported)
        // {
        //     throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
        // }

        Vector256<byte> lookupTable = Vector256.Create(
            replace0, replace1, replace2, replace3,
            replace4, replace5, replace6, replace7,
            replace8, replace9, replace10, replace11,
            replace12, replace13, replace14, replace15,
            // Repeat the pattern for the remaining elements
            replace0, replace1, replace2, replace3,
            replace4, replace5, replace6, replace7,
            replace8, replace9, replace10, replace11,
            replace12, replace13, replace14, replace15
        );

        return Avx2.Shuffle(lookupTable, source);
    }


    public static Vector256<byte> ShiftRightLogical(this Vector256<byte> vector, byte shiftAmount)
    {
        Vector256<ushort> extended = vector.AsUInt16();

        // Perform the shift operation on each 16-bit element
        Vector256<ushort> shifted = Avx2.ShiftRightLogical(extended, shiftAmount);

        Vector256<byte> narrowed = shifted.AsByte();

        return narrowed;
    }







}


namespace SimdUnicode
{

    public static unsafe class Utf8Utility
    {

        // Helper functions for debugging
        // string VectorToString(Vector256<byte> vector)
        // {
        //     Span<byte> span = stackalloc byte[Vector256<byte>.Count];
        //     vector.CopyTo(span);
        //     return BitConverter.ToString(span.ToArray());
        // }

        // string VectorToBinary(Vector256<byte> vector)
        // {
        //     Span<byte> span = stackalloc byte[Vector256<byte>.Count];
        //     vector.CopyTo(span);

        //     var binaryStrings = span.ToArray().Select(b => Convert.ToString(b, 2).PadLeft(8, '0'));
        //     return string.Join(" ", binaryStrings);
        // }





        // Returns a pointer to the first invalid byte in the input buffer if it's invalid, or a pointer to the end if it's valid.
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)
        {
            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }

            var checker = new SimdUnicode.utf8_validation.utf8_checker();
            int processedLength = 0;

            // Helpers.CheckForGCCollections("Before AVX2 procession");
            while (processedLength + 32 <= inputLength)
            {
                // Console.WriteLine("-------New AVX2 vector blocked processing!------------");
                
                Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
                // Helpers.CheckForGCCollections($"Before check_next_input:{processedLength}");
                checker.check_next_input(currentBlock);
                // Helpers.CheckForGCCollections($"After check_next_input:{processedLength}");

                processedLength += 32;
                
            }

            // Helpers.CheckForGCCollections("After AVX2 procession");


            if (processedLength < inputLength)
            {
                Span<byte> remainingBytes = stackalloc byte[32];
                for (int i = 0; i < inputLength - processedLength; i++)
                {
                    remainingBytes[i] = pInputBuffer[processedLength + i];
                }

                Vector256<byte> remainingBlock = Vector256.Create(remainingBytes.ToArray());

                checker.check_next_input(remainingBlock);
                processedLength += inputLength - processedLength;

            }

            // CheckForGCCollections("After processed remaining bytes");


            // if (processedLength < inputLength)
            // {
            //     // Directly call the scalar function on the remaining part of the buffer
            //     byte* invalidBytePointer = GetPointerToFirstInvalidByte(pInputBuffer + processedLength, inputLength - processedLength -1);
                
            //     // You can then use `invalidBytePointer` as needed, for example:
            //     // if (invalidBytePointer != pInputBuffer + inputLength) {
            //     //     // Handle the case where an invalid byte is found
            //     // }

            //     // Update processedLength to reflect the processing done by the scalar function
            //     processedLength += (int)(invalidBytePointer - pInputBuffer);
            // }
            

            checker.check_eof();
            if (checker.errors())
            {
                return pInputBuffer + processedLength;
            }

            return pInputBuffer + inputLength;
        }
    }

// C# docs suggests that classes are allocated on the heap:
// it doesnt seem to do much in this case but I thought the suggestion to be sensible. 
    public struct utf8_validation
    {
        public struct utf8_checker
        {
            Vector256<byte> error;
            Vector256<byte> prev_input_block;
            Vector256<byte> prev_incomplete;




            public utf8_checker()
            {
                error = Vector256<byte>.Zero;
                prev_input_block = Vector256<byte>.Zero;
                prev_incomplete = Vector256<byte>.Zero;
            }

            // This is the first point of entry for this function
            // The original C++ implementation is much more extensive and assumes a 512 bit stream as well as several implementations
            // In this case I focus solely on AVX2 instructions for prototyping and benchmarking purposes. 
            // This is the simplest least time-consuming implementation. 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public void check_next_input(Vector256<byte> input)
            {
                // Check if the entire 256-bit vector is ASCII
                
                Vector256<sbyte> inputSBytes = input.AsSByte(); // Reinterpret the byte vector as sbyte
                int mask = Avx2.MoveMask(inputSBytes.AsByte());
                if (mask != 0)
                {
                    // Contains non-ASCII characters, process the vector
                    
                    check_utf8_bytes(input, prev_input_block);
                    prev_incomplete = is_incomplete(input);
                }



                prev_input_block = input;
                // Console.WriteLine("Error Vector after check_next_input: " + VectorToString(error));


            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public void check_utf8_bytes(Vector256<byte> input, Vector256<byte> prev_input)
            {
                Vector256<byte> prev1 = input.Prev(prev_input, 1);
                // check 1-2 bytes character
                Vector256<byte> sc = check_special_cases(input, prev1);
                // Console.WriteLine("Special_case Vector before check_multibyte_lengths: " + VectorToString(error));

                // All remaining checks are for invalid 3-4 byte sequences, which either have too many continuations
                // or not enough (section 6.2 of the paper)
                error = Avx2.Or(error, check_multibyte_lengths(input, prev_input, sc));
                // Console.WriteLine("Error Vector after check_utf8_bytes/after check_multibyte_lengths: " + VectorToString(error));

            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public bool errors()
            {
                // Console.WriteLine("Error Vector at the end: " + VectorToString(error));

                return !Avx2.TestZ(error, error);
            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public void check_eof()
            {
                // Console.WriteLine("Error Vector before check_eof(): " + VectorToString(error));
                // Console.WriteLine("prev_incomplete Vector in check_eof(): " + VectorToString(prev_incomplete));

                error = Avx2.Or(error, prev_incomplete);
                // Console.WriteLine("Error Vector before check_eof(): " + VectorToString(error));

            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            // This corresponds to section 6.1 e.g Table 6 of the paper e.g. 1-2 bytes
            private Vector256<byte> check_special_cases(Vector256<byte> input, Vector256<byte> prev1)
            {

                // define bits that indicate error code
                // Bit 0 = Too Short (lead byte/ASCII followed by lead byte/ASCII)
                // Bit 1 = Too Long (ASCII followed by continuation)
                // Bit 2 = Overlong 3-byte
                // Bit 4 = Surrogate
                // Bit 5 = Overlong 2-byte
                // Bit 7 = Two Continuations
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private Vector256<byte> check_multibyte_lengths(Vector256<byte> input, Vector256<byte> prev_input, Vector256<byte> sc)
            {
                // Console.WriteLine("sc: " + VectorToString(sc));

                // Console.WriteLine("Input: " + VectorToString(input));
                // Console.WriteLine("Input(Binary): " + VectorToBinary(input));

                Vector256<byte> prev2 = input.Prev(prev_input, 2);
                // Console.WriteLine("Prev2: " + VectorToBinary(prev2));

                Vector256<byte> prev3 = input.Prev(prev_input, 3);
                // Console.WriteLine("Prev3: " + VectorToBinary(prev3));


                Vector256<byte> must23 = must_be_2_3_continuation(prev2, prev3);
                // Console.WriteLine("must be 2 3 continuation: " + VectorToString(must23));

                Vector256<byte> must23_80 = Avx2.And(must23, Vector256.Create((byte)0x80));

                return Avx2.Xor(must23_80, sc);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private Vector256<byte> must_be_2_3_continuation(Vector256<byte> prev2, Vector256<byte> prev3)
            {
                Vector256<byte> is_third_byte = Avx2.SubtractSaturate(prev2, Vector256.Create((byte)(0b11100000u - 1)));
                Vector256<byte> is_fourth_byte = Avx2.SubtractSaturate(prev3, Vector256.Create((byte)(0b11110000u - 1)));

                Vector256<byte> combined = Avx2.Or(is_third_byte, is_fourth_byte);
                return combined;

                // Vector256<sbyte> signedCombined = combined.AsSByte();

                // Vector256<sbyte> zero = Vector256<sbyte>.Zero;
                // Vector256<sbyte> comparisonResult = Avx2.CompareGreaterThan(signedCombined, zero);

                // return comparisonResult.AsByte();
            }


            private static readonly byte[] MaxArray = new byte[32]
            {
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1
            };
            Vector256<byte> maxValue = Vector256.Create(MaxArray);

    //         private static readonly Vector256<byte> maxValue = Vector256.Create(
    // 255, 255, 255, 255, 255, 255, 255, 255,
    // 255, 255, 255, 255, 255, 255, 255, 255,
    // 255, 255, 255, 255, 255, 255, 255, 255,
    // 255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);


            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private Vector256<byte> is_incomplete(Vector256<byte> input)
            {
                // Console.WriteLine("Input Vector is_incomplete: " + VectorToString(input));
                // byte[] maxArray = new byte[32]
                // {
                //         255, 255, 255, 255, 255, 255, 255, 255,
                //         255, 255, 255, 255, 255, 255, 255, 255,
                //         255, 255, 255, 255, 255, 255, 255, 255,
                //         255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1
                // };
                // Vector256<byte> max_value = Vector256.Create(maxArray);

                Vector256<byte> result = SaturatingSubtractUnsigned(input, maxValue);
                // Console.WriteLine("Result Vector is_incomplete: " + VectorToString(result));

                return result;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private Vector256<byte> SaturatingSubtractUnsigned(Vector256<byte> left, Vector256<byte> right)
            {
                if (!Avx2.IsSupported)
                {
                    throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
                }

                Vector256<ushort> leftUShorts = left.AsUInt16();
                Vector256<ushort> rightUShorts = right.AsUInt16();

                Vector256<ushort> subtractionResult = Avx2.SubtractSaturate(leftUShorts, rightUShorts);

                return subtractionResult.AsByte();
            }
        }
    }
}

