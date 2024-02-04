using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using static System.Net.Mime.MediaTypeNames;

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

    public static Vector256<byte> Prev1(this Vector256<byte> current, Vector256<byte> prev)
    {

        // Permute2x128 takes two 128-bit lane of two 256-bit vector and fuse them into a single vector
        // 0x21 = 00 10 00 01 translates into a fusing of 
        // second 128-bit lane of first source, 
        // first 128bit lane of second source,
        // Compiles to:
        //        vperm2i128 ymm1, ymm1, ymm0, 33
        //        vpalignr ymm0, ymm0, ymm1, 15
        Vector256<byte> shuffle = Avx2.Permute2x128(prev, current, 0x21);
        return Avx2.AlignRight(current, shuffle, (byte)(16 - 1)); //shifts right by a certain amount
    }

    public static Vector256<byte> Prev2(this Vector256<byte> current, Vector256<byte> prev)
    {

        // Permute2x128 takes two 128-bit lane of two 256-bit vector and fuse them into a single vector
        // 0x21 = 00 10 00 01 translates into a fusing of 
        // second 128-bit lane of first source, 
        // first 128bit lane of second source,
        // Compiles to
        //        vperm2i128 ymm1, ymm1, ymm0, 33
        //        vpalignr ymm0, ymm0, ymm1, 14
        Vector256<byte> shuffle = Avx2.Permute2x128(prev, current, 0x21);
        return Avx2.AlignRight(current, shuffle, (byte)(16 - 2)); //shifts right by a certain amount
    }
 
    public static Vector256<byte> Prev3(this Vector256<byte> current, Vector256<byte> prev)
    {

        // Permute2x128 takes two 128-bit lane of two 256-bit vector and fuse them into a single vector
        // 0x21 = 00 10 00 01 translates into a fusing of 
        // second 128-bit lane of first source, 
        // first 128bit lane of second source,
        // Compiles to
        //       vperm2i128 ymm1, ymm1, ymm0, 33
        //       vpalignr ymm0, ymm0, ymm1, 13
        Vector256<byte> shuffle = Avx2.Permute2x128(prev, current, 0x21);
        return Avx2.AlignRight(current, shuffle, (byte)(16 - 3)); //shifts right by a certain amount
    }
    public static Vector256<byte> Lookup16(this Vector256<byte> source, Vector256<byte> lookupTable)
    {
        // Compiles to 
        //       vpshufb ymm0, ymm0, ymmword ptr[rdx]
        return Avx2.Shuffle(lookupTable, source);
    }



    public static Vector256<byte> ShiftRightLogical4(this Vector256<byte> vector)
    {
        // Compiles to
        //       vpsrlw   ymm0, ymm0, 4
        Vector256<ushort> extended = vector.AsUInt16();

        // Perform the shift operation on each 16-bit element
        Vector256<ushort> shifted = Avx2.ShiftRightLogical(extended, 4);

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
            ////////////////
            // TODO: I recommend taking this code and calling it something
            // else. Then have the current function (GetPointerToFirstInvalidByte)
            // call the SIMD function only if inputLength is sufficiently large (maybe 64 bytes),
            // otherwise, use the scalar function.
            ////////////////
            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }
            Vector256<byte> error = Vector256<byte>.Zero;
            Vector256<byte> prev_input_block = Vector256<byte>.Zero;
            Vector256<byte> prev_incomplete = Vector256<byte>.Zero;


            int processedLength = 0;

            // Helpers.CheckForGCCollections("Before AVX2 procession");
            while (processedLength + 32 <= inputLength)
            {
                // Console.WriteLine("-------New AVX2 vector blocked processing!------------");
                
                Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
                // Helpers.CheckForGCCollections($"Before check_next_input:{processedLength}");
                Utf8Validation.utf8_checker.CheckNextInput(currentBlock, ref prev_input_block, ref prev_incomplete, ref error);
                // Helpers.CheckForGCCollections($"After check_next_input:{processedLength}");

                processedLength += 32;
                
            }

            // Helpers.CheckForGCCollections("After AVX2 procession");
            
    
                // |                      Method |               FileName |      Mean |     Error |    StdDev | Allocated |
                // |---------------------------- |----------------------- |----------:|----------:|----------:|----------:|
                // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 33.062 us | 0.5046 us | 0.4720 us |      56 B |
                // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 35.609 us | 0.3369 us | 0.3152 us |      56 B |
                // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 11.603 us | 0.2232 us | 0.2293 us |      56 B |
                // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt | 12.317 us | 0.1826 us | 0.1708 us |      56 B |
                // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt | 13.726 us | 0.2471 us | 0.2311 us |      56 B |
                // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt | 13.392 us | 0.0520 us | 0.0487 us |      56 B |
                // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt | 24.345 us | 0.2012 us | 0.1882 us |      56 B |
                // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 23.778 us | 0.1892 us | 0.1769 us |      56 B |
                // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |  9.323 us | 0.0155 us | 0.0130 us |      56 B |
                // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  8.336 us | 0.0502 us | 0.0470 us |      56 B |
                // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 10.728 us | 0.1370 us | 0.1282 us |      56 B |
                // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt | 10.837 us | 0.1389 us | 0.1300 us |      56 B |
                // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 11.086 us | 0.1190 us | 0.1113 us |      56 B |
                // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 10.017 us | 0.0615 us | 0.0514 us |      56 B |


            // if (processedLength < inputLength)
            // {
            //     // Unfortunalely, this approach with stackalloc might be expensive.
            //     // TODO: replace it by a simple scalar routine. You need to handle
            //     // prev_incomplete but it should be doable.


            //     Span<byte> remainingBytes = stackalloc byte[32];
            //     for (int i = 0; i < inputLength - processedLength; i++)
            //     {
            //         remainingBytes[i] = pInputBuffer[processedLength + i];
            //     }

            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytes.ToArray());
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock, ref prev_input_block, ref prev_incomplete, ref error);
            //     processedLength += inputLength - processedLength;

            // }

            // CheckForGCCollections("After processed remaining bytes");

            // |                      Method |               FileName |      Mean |     Error |    StdDev |    Median | Allocated |
            // |---------------------------- |----------------------- |----------:|----------:|----------:|----------:|----------:|
            // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 36.968 us | 0.4637 us | 0.4111 us | 37.018 us |      56 B |
            // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 31.851 us | 0.2252 us | 0.2107 us | 31.865 us |      56 B |
            // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 10.541 us | 0.0870 us | 0.0814 us | 10.490 us |      56 B |
            // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt | 12.404 us | 0.2447 us | 0.2913 us | 12.355 us |      56 B |
            // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt | 14.297 us | 0.2786 us | 0.4655 us | 14.225 us |      56 B |
            // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt | 14.207 us | 0.0272 us | 0.0241 us | 14.211 us |      56 B |
            // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt | 23.993 us | 0.2287 us | 0.2140 us | 23.879 us |      56 B |
            // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 26.856 us | 0.5314 us | 1.4367 us | 27.005 us |      56 B |
            // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |  8.541 us | 0.1702 us | 0.2090 us |  8.456 us |      56 B |
            // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  8.728 us | 0.1618 us | 0.3938 us |  8.567 us |      56 B |
            // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 11.108 us | 0.1767 us | 0.1653 us | 11.184 us |      56 B |
            // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  9.533 us | 0.1288 us | 0.1205 us |  9.490 us |      56 B |
            // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 10.128 us | 0.0544 us | 0.0454 us | 10.126 us |      56 B |
            // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 10.078 us | 0.0499 us | 0.0467 us | 10.079 us |      56 B |

            // scalar results:
        if (processedLength < inputLength)
        {
            byte* invalidBytePointer = UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength, inputLength - processedLength);
            if (invalidBytePointer != pInputBuffer + inputLength)
            {
                // An invalid byte was found. Adjust error handling as needed.
                error = Vector256.Create((byte)1);
            }
            processedLength += (int)(invalidBytePointer - (pInputBuffer + processedLength));
        }


            // |                      Method |               FileName |      Mean |     Error |    StdDev | Allocated |
            // |---------------------------- |----------------------- |----------:|----------:|----------:|----------:|
            // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 20.136 us | 0.3869 us | 0.5031 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 19.576 us | 0.2366 us | 0.2098 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt |  6.207 us | 0.0479 us | 0.0400 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  6.169 us | 0.0541 us | 0.0506 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  9.212 us | 0.0121 us | 0.0107 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  9.373 us | 0.0250 us | 0.0209 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt | 13.726 us | 0.2609 us | 0.2900 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 13.948 us | 0.2122 us | 0.1985 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |  4.916 us | 0.0176 us | 0.0147 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  4.897 us | 0.0525 us | 0.0491 us |         - |
            // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt |  5.526 us | 0.0463 us | 0.0411 us |         - |
            // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  5.538 us | 0.0405 us | 0.0379 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt |  5.838 us | 0.0363 us | 0.0340 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt |  5.813 us | 0.0440 us | 0.0412 us |         - |


            // if (processedLength < inputLength)
            // {

            //     Span<byte> remainingBytes = stackalloc byte[32];
            //     for (int i = 0; i < inputLength - processedLength; i++)
            //     {
            //         remainingBytes[i] = pInputBuffer[processedLength + i];
            //     }

            //     ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock, ref prev_input_block, ref prev_incomplete, ref error);
            //     processedLength += inputLength - processedLength;

            // }




            // |                      Method |               FileName |      Mean |     Error |    StdDev | Allocated |
            // |---------------------------- |----------------------- |----------:|----------:|----------:|----------:|
            // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 31.216 us | 0.2960 us | 0.2624 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 31.732 us | 0.3772 us | 0.3528 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 10.281 us | 0.1234 us | 0.1154 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt | 10.370 us | 0.2019 us | 0.1889 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt | 12.003 us | 0.2378 us | 0.4102 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt | 11.403 us | 0.1818 us | 0.1700 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt | 25.936 us | 0.3735 us | 0.3311 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 22.630 us | 0.3594 us | 0.3362 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |  7.186 us | 0.0220 us | 0.0195 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  7.425 us | 0.1450 us | 0.1985 us |         - |
            // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt |  9.359 us | 0.1549 us | 0.1294 us |         - |
            // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt | 10.929 us | 0.2096 us | 0.1961 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 10.493 us | 0.2098 us | 0.5708 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt |  9.575 us | 0.1878 us | 0.1757 us |         - |
            // if (processedLength < inputLength)
            // {

            //     Span<byte> remainingBytes = stackalloc byte[32];
            //     new Span<byte>(pInputBuffer + processedLength, inputLength - processedLength).CopyTo(remainingBytes);

            //     ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock, ref prev_input_block, ref prev_incomplete, ref error);
            //     processedLength += inputLength - processedLength;

            // }

            // if (processedLength < inputLength)
            // {

            //     Span<byte> remainingBytes = stackalloc byte[32];
            //     new Span<byte>(pInputBuffer + processedLength, inputLength - processedLength).CopyTo(remainingBytes);

            //     ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock, ref prev_input_block, ref prev_incomplete, ref error);
            //     processedLength += inputLength - processedLength;

            // }

            // if (processedLength < inputLength)
            // {
            //     // Directly call the scalar function on the remaining part of the buffer
            //     byte* startOfRemaining = pInputBuffer + processedLength;
            //     int lengthOfRemaining = inputLength - processedLength;
            //     byte* invalidBytePointer = UTF8.GetPointerToFirstInvalidByte(startOfRemaining, lengthOfRemaining);

            //     // Use `invalidBytePointer` as needed, for example:
            //     // if (invalidBytePointer != startOfRemaining + lengthOfRemaining) {
            //     //     // Handle the case where an invalid byte is found
            //     // }

            //     // Update processedLength based on the result of the scalar function
            //     processedLength += (int)(invalidBytePointer - pInputBuffer);
            // }



            Utf8Validation.utf8_checker.CheckEof(ref error, prev_incomplete);
            if (Utf8Validation.utf8_checker.Errors(error))
            {
                return pInputBuffer + processedLength;
            }

            return pInputBuffer + inputLength;
        }
    }

// C# docs suggests that classes are allocated on the heap:
// it doesnt seem to do much in this case but I thought the suggestion to be sensible. 
    public struct Utf8Validation
    {
        public struct utf8_checker
        {


            // This is the first point of entry for this function
            // The original C++ implementation is much more extensive and assumes a 512 bit stream as well as several implementations
            // In this case I focus solely on AVX2 instructions for prototyping and benchmarking purposes. 
            // This is the simplest least time-consuming implementation. 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static void CheckNextInput(Vector256<byte> input, ref Vector256<byte> prev_input_block, ref Vector256<byte> prev_incomplete, ref Vector256<byte> error)
            {
                // Compiles to:
                /*
    G_M000_IG02:                ;; offset=0x0003
           vmovups  ymm0, ymmword ptr [rcx]
           vpmovmskb eax, ymm0
           test     eax, eax
           je       G_M000_IG04

    G_M000_IG03:                ;; offset=0x0013
           vmovups  ymm1, ymmword ptr [rdx]
           vperm2i128 ymm1, ymm1, ymm0, 33
           vpalignr ymm2, ymm0, ymm1, 15
           vpsrlw   ymm3, ymm2, 4
           vmovups  ymm4, ymmword ptr [reloc @RWD00]
           vpshufb  ymm3, ymm4, ymm3
           vpand    ymm2, ymm2, ymmword ptr [reloc @RWD32]
           vmovups  ymm4, ymmword ptr [reloc @RWD64]
           vpshufb  ymm2, ymm4, ymm2
           vpand    ymm2, ymm3, ymm2
           vpsrlw   ymm3, ymm0, 4
           vmovups  ymm4, ymmword ptr [reloc @RWD96]
           vpshufb  ymm3, ymm4, ymm3
           vpand    ymm2, ymm2, ymm3
           vmovups  ymm3, ymmword ptr [r9]
           vpalignr ymm4, ymm0, ymm1, 14
           vpsubusb ymm4, ymm4, ymmword ptr [reloc @RWD128]
           vpalignr ymm0, ymm0, ymm1, 13
           vpsubusb ymm0, ymm0, ymmword ptr [reloc @RWD160]
           vpor     ymm0, ymm4, ymm0
           vpand    ymm0, ymm0, ymmword ptr [reloc @RWD192]
           vpxor    ymm0, ymm0, ymm2
           vpor     ymm0, ymm3, ymm0
           vmovups  ymmword ptr [r9], ymm0
           vmovups  ymm0, ymmword ptr [rcx]
           vpsubusw ymm0, ymm0, ymmword ptr [reloc @RWD224]
           vmovups  ymmword ptr [r8], ymm0

    G_M000_IG04:                ;; offset=0x00AF
           vmovups  ymm0, ymmword ptr [rcx]
           vmovups  ymmword ptr [rdx], ymm0
                */
                // Check if the entire 256-bit vector is ASCII


                Vector256<sbyte> inputSBytes = input.AsSByte(); // Reinterpret the byte vector as sbyte
                int mask = Avx2.MoveMask(inputSBytes.AsByte());
                if (mask != 0)
                {
                    // Contains non-ASCII characters, process the vector
                    
                    CheckUtf8Bytes(input, prev_input_block, ref error);
                    prev_incomplete = IsIncomplete(input);
                }



                prev_input_block = input;
                // Console.WriteLine("Error Vector after check_next_input: " + VectorToString(error));


            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static void CheckUtf8Bytes(Vector256<byte> input, Vector256<byte> prevInput, ref Vector256<byte> error)
            {
                // compiles to
                //        vmovups  ymm0, ymmword ptr [rcx]
                //        vmovups ymm1, ymmword ptr[rdx]
                //        vperm2i128 ymm1, ymm1, ymm0, 33
                //        vpalignr ymm2, ymm0, ymm1, 15
                //        vpsrlw ymm3, ymm2, 4
                //        vmovups ymm4, ymmword ptr[reloc @RWD00]
                //        vpshufb ymm3, ymm4, ymm3
                //        vpand ymm2, ymm2, ymmword ptr[reloc @RWD32]
                //         ymm4, ymmword ptr[reloc @RWD64]
                //        vpshufb ymm2, ymm4, ymm2
                //        vpand ymm2, ymm3, ymm2
                //        vpsrlw ymm3, ymm0, 4
                //        vmovups ymm4, ymmword ptr[reloc @RWD96]
                //        vpshufb ymm3, ymm4, ymm3
                //        vpand ymm2, ymm2, ymm3
                //        vmovups ymm3, ymmword ptr[r8]
                //        vpalignr ymm4, ymm0, ymm1, 14
                //         ymm4, ymm4, ymmword ptr[reloc @RWD128]
                //        vpalignr ymm0, ymm0, ymm1, 13
                //        vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD160]
                //        vpor ymm0, ymm4, ymm0
                //        vpand ymm0, ymm0, ymmword ptr[reloc @RWD192]
                //        vpxor ymm0, ymm0, ymm2
                //        vpor ymm0, ymm3, ymm0
                Vector256<byte> prev1 = input.Prev1(prevInput);
                // check 1-2 bytes character
                Vector256<byte> sc = CheckSpecialCases(input, prev1);
                // Console.WriteLine("Special_case Vector before check_multibyte_lengths: " + VectorToString(error));

                // All remaining checks are for invalid 3-4 byte sequences, which either have too many continuations
                // or not enough (section 6.2 of the paper)
                error = Avx2.Or(error, CheckMultibyteLengths(input, prevInput, sc));
                // Console.WriteLine("Error Vector after check_utf8_bytes/after check_multibyte_lengths: " + VectorToString(error));

            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static bool Errors(Vector256<byte> error)
            {
                // Console.WriteLine("Error Vector at the end: " + VectorToString(error));
                // compiles to:
                //       vptest   ymm0, ymm0
                //       setne al
                //       movzx rax, al
                return !Avx2.TestZ(error, error);
            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static void CheckEof(ref Vector256<byte> error, Vector256<byte> prev_incomplete)
            {
                // Console.WriteLine("Error Vector before check_eof(): " + VectorToString(error));
                // Console.WriteLine("prev_incomplete Vector in check_eof(): " + VectorToString(prev_incomplete));
                // Compiles to:
                //        vpor     ymm0, ymm0, ymmword ptr [rcx+0x40]
                error = Avx2.Or(error, prev_incomplete);
                // Console.WriteLine("Error Vector before check_eof(): " + VectorToString(error));

            }

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


            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            // This corresponds to section 6.1 e.g Table 6 of the paper e.g. 1-2 bytes
            private static Vector256<byte> CheckSpecialCases(Vector256<byte> input, Vector256<byte> prev1)
            {

                // define bits that indicate error code
                // Bit 0 = Too Short (lead byte/ASCII followed by lead byte/ASCII)
                // Bit 1 = Too Long (ASCII followed by continuation)
                // Bit 2 = Overlong 3-byte
                // Bit 4 = Surrogate
                // Bit 5 = Overlong 2-byte
                // Bit 7 = Two Continuations
                // Compiles to
                //        vmovups  ymm0, ymmword ptr [r8]
                //        vpsrlw ymm1, ymm0, 4
                //        vmovups ymm2, ymmword ptr[reloc @RWD00]
                //        vpshufb ymm1, ymm2, ymm1
                //        vpand ymm0, ymm0, ymmword ptr[reloc @RWD32]
                //        vmovups ymm2, ymmword ptr[reloc @RWD64]
                //        vpshufb ymm0, ymm2, ymm0
                //        vpand ymm0, ymm1, ymm0
                //        vmovups ymm1, ymmword ptr[rdx]
                //        vpsrlw ymm1, ymm1, 4
                //        vmovups ymm2, ymmword ptr[reloc @RWD96]
                //        vpshufb ymm1, ymm2, ymm1
                //        vpand ymm0, ymm0, ymm1

                Vector256<byte> shuf1 = Vector256.Create(TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                        TOO_SHORT | OVERLONG_2,
                        TOO_SHORT,
                        TOO_SHORT | OVERLONG_3 | SURROGATE,
                        TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4,
                        TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                        TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                        TOO_SHORT | OVERLONG_2,
                        TOO_SHORT,
                        TOO_SHORT | OVERLONG_3 | SURROGATE,
                        TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4);

                Vector256<byte> shuf2 = Vector256.Create(CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
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
                        CARRY | TOO_LARGE | TOO_LARGE_1000,
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
                        CARRY | TOO_LARGE | TOO_LARGE_1000);

                Vector256<byte> shuf3 = Vector256.Create(TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                        TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT);
                Vector256<byte> byte_1_high = prev1.ShiftRightLogical4().Lookup16(shuf1);

                Vector256<byte> byte_1_low = (prev1 & Vector256.Create((byte)0x0F)).Lookup16(shuf2);

                Vector256<byte> byte_2_high = input.ShiftRightLogical4().Lookup16(shuf3);

                return Avx2.And(Avx2.And(byte_1_high, byte_1_low), byte_2_high);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Vector256<byte> CheckMultibyteLengths(Vector256<byte> input, Vector256<byte> prev_input, Vector256<byte> sc)
            {
                // Console.WriteLine("sc: " + VectorToString(sc));

                // Console.WriteLine("Input: " + VectorToString(input));
                // Console.WriteLine("Input(Binary): " + VectorToBinary(input));
                // compiles to:
                //        vperm2i128 ymm1, ymm1, ymm0, 33
                //        vpalignr ymm2, ymm0, ymm1, 14
                //        vpsubusb ymm2, ymm2, ymmword ptr[reloc @RWD00]
                //        vpalignr ymm0, ymm0, ymm1, 13
                //        vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD32]
                //        vpor ymm0, ymm2, ymm0
                //        vpand ymm0, ymm0, ymmword ptr[reloc

                Vector256<byte> prev2 = input.Prev2(prev_input);
                // Console.WriteLine("Prev2: " + VectorToBinary(prev2));

                Vector256<byte> prev3 = input.Prev3(prev_input);
                // Console.WriteLine("Prev3: " + VectorToBinary(prev3));


                Vector256<byte> must23 = MustBe23Continuation(prev2, prev3);
                // Console.WriteLine("must be 2 3 continuation: " + VectorToString(must23));

                Vector256<byte> must23_80 = Avx2.And(must23, Vector256.Create((byte)0x80));

                return Avx2.Xor(must23_80, sc);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Vector256<byte> MustBe23Continuation(Vector256<byte> prev2, Vector256<byte> prev3)
            {
                // Compiles to
                //         vmovups  ymm0, ymmword ptr [rdx]
                //        vpsubusb ymm0, ymm0, ymmword ptr [reloc @RWD00]
                //        vmovups ymm1, ymmword ptr[r8]
                //        vpsubusb ymm1, ymm1, ymmword ptr[reloc @RWD32]
                //        vpor ymm0, ymm0, ymm1


                Vector256<byte> is_third_byte = Avx2.SubtractSaturate(prev2, Vector256.Create((byte)(0b11100000u - 0x80)));
                Vector256<byte> is_fourth_byte = Avx2.SubtractSaturate(prev3, Vector256.Create((byte)(0b11110000u - 0x80)));

                Vector256<byte> combined = Avx2.Or(is_third_byte, is_fourth_byte);
                return combined;

                // Vector256<sbyte> signedCombined = combined.AsSByte();

                // Vector256<sbyte> zero = Vector256<sbyte>.Zero;
                // Vector256<sbyte> comparisonResult = Avx2.CompareGreaterThan(signedCombined, zero);

                // return comparisonResult.AsByte();
            }


            //         private static readonly Vector256<byte> maxValue = Vector256.Create(
            // 255, 255, 255, 255, 255, 255, 255, 255,
            // 255, 255, 255, 255, 255, 255, 255, 255,
            // 255, 255, 255, 255, 255, 255, 255, 255,
            // 255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);


            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private static Vector256<byte> IsIncomplete(Vector256<byte> input)
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
                // Compiles to
                //        vmovups  ymm0, ymmword ptr [rdx]
                //        vpsubusw ymm0, ymm0, ymmword ptr[reloc @RWD00]
                Vector256<byte> maxValue = Vector256.Create(255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                Vector256<byte> result = SaturatingSubtractUnsigned(input, maxValue);
                // Console.WriteLine("Result Vector is_incomplete: " + VectorToString(result));

                return result;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private static Vector256<byte> SaturatingSubtractUnsigned(Vector256<byte> left, Vector256<byte> right)
            {
                // Compiles to
                //        vpsubusw ymm0, ymm0, ymmword ptr [r8]
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

