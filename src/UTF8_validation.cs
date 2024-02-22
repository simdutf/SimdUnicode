using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using static System.Net.Mime.MediaTypeNames;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using static SimdUnicode.Utf8Validation;
using System.Threading.Channels;


// C# already have something that is *more or less* equivalent to our C++ simd class:
// Vector256 https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256-1?view=net-7.0
//  I extend it as needed

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

        public static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)
        {



            int processedLength = 0;

            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }
            // TODO: this is arbitrary, we should benchmark this and make sure it's the right value (128)
            if(inputLength > 128)
            {
                // We have enough data to use the SIMD function

                Vector256<byte> error = Vector256<byte>.Zero;
                Vector256<byte> prev_input_block = Vector256<byte>.Zero;
                Vector256<byte> prev_incomplete = Vector256<byte>.Zero;

                while (processedLength + 64 <= inputLength)
                {                
                    
                    Vector256<byte> currentBlock1 = Avx.LoadVector256(pInputBuffer + processedLength);
                    Vector256<byte> currentBlock2 = Avx.LoadVector256(pInputBuffer + processedLength + 32);



                    int mask1 = Avx2.MoveMask(currentBlock1);
                    int mask2 = Avx2.MoveMask(currentBlock1);
                    /*if (mask != 0)
                    {
                        // Contains non-ASCII characters, process the vector
                        
                        CheckUtf8Bytes(input);
                        prev_incomplete = IsIncomplete(input);
                    }*/



                    //prev_input_block = input;

                    /*SIMDGetPointerToFirstInvalidByte(pInputBuffer,processedLength);
                    
                    //Utf8Validation.utf8_checker.CheckEof(); // I don't think that's right??? To be verified.
                    if (Utf8Validation.utf8_checker.Errors())
                    {
                        // return pInputBuffer + processedLength;
                        return SimdUnicode.UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength,inputLength - processedLength);
                    }*/
                    //SHIT
                    processedLength += 64;

                }
        
            }



            // Process the remaining bytes with the scalar function
            if (processedLength < inputLength)
            {
                // We need to possibly backtrack to the start of the last code point
                // worst possible case is 4 bytes, where we need to backtrack 3 bytes
                // 11110xxxx 10xxxxxx 10xxxxxx 10xxxxxx <== we might be pointing at the last byte
                if ((sbyte)pInputBuffer[processedLength] <= -65 && processedLength > 0) {
                    processedLength -= 1;
                    if ((sbyte)pInputBuffer[processedLength] <= -65 && processedLength > 0) {
                        processedLength -= 1;
                        if ((sbyte)pInputBuffer[processedLength] <= -65 && processedLength > 0) {
                            processedLength -= 1;
                        }
                    }
                }
                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInputBuffer + processedLength, inputLength - processedLength);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

            return pInputBuffer + inputLength;

        }

        // |                      Method |               FileName |       Mean |     Error |     StdDev | Allocated |
        // |---------------------------- |----------------------- |-----------:|----------:|-----------:|----------:|
        // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 472.648 us | 9.2039 us | 14.3294 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 270.666 us | 1.8206 us |  1.6139 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 129.587 us | 2.4394 us |  2.2818 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  14.699 us | 0.2902 us |  0.4254 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  10.944 us | 0.1793 us |  0.1590 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  10.954 us | 0.1190 us |  0.1113 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.971 us | 0.2540 us |  0.2495 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  12.692 us | 0.1270 us |  0.1126 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.751 us | 0.0576 us |  0.0539 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.735 us | 0.0164 us |  0.0145 us |         - |
        // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 132.404 us | 1.3084 us |  1.2239 us |         - |
        // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  74.305 us | 1.4385 us |  1.4128 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 161.232 us | 1.5357 us |  1.4365 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 107.539 us | 1.0781 us |  0.9557 us |         - |

        // public static byte* SIMDGetPointerToFirstInvalidByte(byte* pInputBuffer, int processedLength)
        // {
        //     Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //     Utf8Validation.utf8_checker.CheckNextInput(currentBlock);

        //     processedLength += 32;

        //     currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //     Utf8Validation.utf8_checker.CheckNextInput(currentBlock);
        //     processedLength += 32;

        //     currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //     Utf8Validation.utf8_checker.CheckNextInput(currentBlock);

        //     processedLength += 32;

        //     currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //     Utf8Validation.utf8_checker.CheckNextInput(currentBlock);
        //     processedLength += 32;

        //     return pInputBuffer + processedLength;
        // }

        
//        |                      Method |               FileName |       Mean |     Error |     StdDev | Allocated |
//|---------------------------- |----------------------- |-----------:|----------:|-----------:|----------:|
//|  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 456.040 us | 2.3088 us |  1.8026 us |         - |
//| SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 281.697 us | 5.6153 us | 10.2680 us |         - |
//|  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 109.537 us | 1.2642 us |  1.0557 us |         - |
//| SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  17.258 us | 0.3422 us |  0.6833 us |         - |
//|  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  11.107 us | 0.2221 us |  0.3114 us |         - |
//| SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  10.859 us | 0.0686 us |  0.0608 us |         - |
//|  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.512 us | 0.1065 us |  0.0890 us |         - |
//| SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  12.530 us | 0.1196 us |  0.0998 us |         - |
//|  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.807 us | 0.0545 us |  0.0510 us |         - |
//| SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.849 us | 0.1123 us |  0.1050 us |         - |
//|  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 135.855 us | 1.2883 us |  1.0758 us |         - |
//| SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  74.063 us | 0.6956 us |  0.6507 us |         - |
//|  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 123.874 us | 0.7700 us |  0.7203 us |         - |
//| SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt |  77.987 us | 1.3605 us |  1.2726 us |         - |

        
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static byte* SIMDGetPointerToFirstInvalidByte(byte* pInputBuffer, int processedLength)
        //{
        //    Vector256<byte> currentBlock1 = Avx.LoadVector256(pInputBuffer + processedLength);
        //    Vector256<byte> currentBlock2 = Avx.LoadVector256(pInputBuffer + processedLength + 32);
        //    Vector256<byte> currentBlock3 = Avx.LoadVector256(pInputBuffer + processedLength + 64);
        //    Vector256<byte> currentBlock4 = Avx.LoadVector256(pInputBuffer + processedLength + 96);

        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock1);
        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock2);
        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock3);
        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock4);

        //    processedLength += 128;

        //    return pInputBuffer + processedLength;
        //}




        //        G_M000_IG01:; ; offset = 0x0000
        //       push rbp
        //       sub rsp, 112
        //       vzeroupper
        //       lea      rbp, [rsp+0x70]
        //       mov qword ptr[rbp + 0x10], rcx
        //       mov      dword ptr[rbp + 0x18], edx


        //G_M000_IG02:; ; offset = 0x0014
        //       mov rcx, qword ptr[rbp + 0x10]
        //       mov eax, dword ptr[rbp + 0x18]
        //       cdqe
        //       vmovups  ymm0, ymmword ptr[rcx + rax]
        //       vmovups ymmword ptr[rbp - 0x30], ymm0
        //       lea      rcx, [rbp-0x30]
        //       call[SimdUnicode.Utf8Validation + utf8_checker:CheckNextInput(System.Runtime.Intrinsics.Vector256`1[ubyte])]
        //       mov ecx, dword ptr[rbp + 0x18]
        //       add ecx, 32
        //       mov dword ptr[rbp + 0x18], ecx
        //       mov      rcx, qword ptr[rbp + 0x10]
        //       mov eax, dword ptr[rbp + 0x18]
        //       cdqe
        //       vmovups  ymm0, ymmword ptr[rcx + rax]
        //       vmovups ymmword ptr[rbp - 0x50], ymm0
        //       lea      rcx, [rbp-0x50]
        //       call[SimdUnicode.Utf8Validation + utf8_checker:CheckNextInput(System.Runtime.Intrinsics.Vector256`1[ubyte])]
        //       mov eax, dword ptr[rbp + 0x18]
        //       add eax, 32
        //       mov dword ptr[rbp + 0x18], eax
        //       mov      eax, dword ptr[rbp + 0x18]
        //       cdqe
        //       add      rax, qword ptr[rbp + 0x10]


        //G_M000_IG03:; ; offset = 0x0069
        //       vzeroupper
        //       add      rsp, 112
        //       pop rbp
        //       ret

        //; Total bytes of code 114

        ////////////////
        // TODO: I recommend taking this code and calling it something
        // else. Then have the current function (GetPointerToFirstInvalidByte)
        // call the SIMD function only if inputLength is sufficiently large (maybe 64 bytes),
        // otherwise, use the scalar function.
        ////////////////
        ///


        // unrolling benchmarks done with scalar tail
        // |                      Method |               FileName |       Mean |     Error |    StdDev | Allocated |
        // |---------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
        // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 428.127 us | 7.9313 us | 7.7896 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 263.689 us | 5.2244 us | 7.4927 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 112.669 us | 1.7434 us | 1.5455 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  16.209 us | 0.3105 us | 0.4250 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  10.804 us | 0.0878 us | 0.0821 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  10.873 us | 0.0428 us | 0.0379 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.423 us | 0.0771 us | 0.0721 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  13.878 us | 0.2719 us | 0.4152 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   6.425 us | 0.1266 us | 0.2044 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   6.452 us | 0.1281 us | 0.2277 us |         - |
        // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 148.702 us | 2.9438 us | 6.1447 us |         - |
        // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  81.048 us | 1.5900 us | 3.3538 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 177.423 us | 3.5294 us | 7.2096 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 116.685 us | 2.3214 us | 4.0044 us |         - |

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* SIMDGetPointerToFirstInvalidByte(byte* pInputBuffer, int processedLength)
        {
            Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
            Utf8Validation.utf8_checker.CheckNextInput(currentBlock);

            processedLength += 32;

            currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
            Utf8Validation.utf8_checker.CheckNextInput(currentBlock);
            processedLength += 32;

            return pInputBuffer + processedLength;
        }

        //|                      Method |               FileName |       Mean |     Error |    StdDev | Allocated |
        //|---------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
        //|  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 444.313 us | 2.2924 us | 2.1443 us |         - |
        //| SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 248.330 us | 2.8213 us | 2.6390 us |         - |
        //|  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 123.766 us | 1.0880 us | 1.0177 us |         - |
        //| SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  13.967 us | 0.1877 us | 0.1756 us |         - |
        //|  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  10.743 us | 0.0751 us | 0.0627 us |         - |
        //| SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  10.694 us | 0.0494 us | 0.0413 us |         - |
        //|  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.302 us | 0.0101 us | 0.0079 us |         - |
        //| SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  12.574 us | 0.2230 us | 0.1862 us |         - |
        //|  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.673 us | 0.0272 us | 0.0227 us |         - |
        //| SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.661 us | 0.0045 us | 0.0040 us |         - |
        //|  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 132.369 us | 0.3841 us | 0.2999 us |         - |
        //| SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  64.057 us | 1.2313 us | 1.0915 us |         - |
        //|  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 158.865 us | 0.9343 us | 0.7802 us |         - |
        //| SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt |  77.935 us | 1.5116 us | 1.6801 us |         - |


        //// Returns a pointer to the first invalid byte in the input buffer if it's invalid, or a pointer to the end if it's valid.
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static byte* SIMDGetPointerToFirstInvalidByte(byte* pInputBuffer, int processedLength)
        //{
        //    Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //    Vector256<byte> currentBlock2 = Avx.LoadVector256(pInputBuffer + processedLength +32);

        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock);
        //    Utf8Validation.utf8_checker.CheckNextInput(currentBlock2);
        //    processedLength += 64;

        //    return pInputBuffer + processedLength;
        //}


        // |                      Method |               FileName |       Mean |     Error |    StdDev | Allocated |
        // |---------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
        // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 456.220 us | 9.1097 us | 9.7472 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 263.690 us | 3.8144 us | 3.3813 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 128.735 us | 2.1841 us | 2.0430 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  14.677 us | 0.2860 us | 0.3060 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  11.059 us | 0.1237 us | 0.1157 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  11.031 us | 0.1627 us | 0.1270 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.780 us | 0.2398 us | 0.2126 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  12.776 us | 0.2530 us | 0.2367 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.851 us | 0.1000 us | 0.0887 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.801 us | 0.0567 us | 0.0530 us |         - |
        // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 133.673 us | 2.1092 us | 1.7612 us |         - |
        // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  73.525 us | 0.8027 us | 0.7116 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 165.167 us | 3.1097 us | 3.3274 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 113.276 us | 2.1983 us | 2.9346 us |         - |


        // // unroll once
        // public static byte* SIMDGetPointerToFirstInvalidByte(byte* pInputBuffer, int processedLength)
        // {
        //     Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);
        //     Utf8Validation.utf8_checker.CheckNextInput(currentBlock);

        //     processedLength += 32;

        //     return pInputBuffer + processedLength;
        // }
    }





    // C# docs suggests that classes are allocated on the heap:
    // it doesnt seem to do much in this case but I thought the suggestion to be sensible. 
    public struct Utf8Validation
    {
        public struct utf8_checker
        {


            static Vector256<byte> error = Vector256<byte>.Zero;
            static Vector256<byte> prev_input_block = Vector256<byte>.Zero;
            static Vector256<byte> prev_incomplete = Vector256<byte>.Zero;

            // Explicit constructor
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

            public static void CheckNextInput(Vector256<byte> input)
            {
                // Compiles to:
                /*
    G_M000_IG02:                ;; offset=0x0003
           vmovups  ymm0, ymmword ptr [rcx]
            eax, ymm0
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
           vmovups  ymm4, ymmword ptr [reloc @RWD96] <= changes
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

                Vector256<sbyte> inputSBytes = input.AsSByte(); // Reinterpret the byte vector as sbyte
                int mask = Avx2.MoveMask(inputSBytes.AsByte());
                if (mask != 0)
                {
                    // Contains non-ASCII characters, process the vector
                    
                    CheckUtf8Bytes(input);
                    prev_incomplete = IsIncomplete(input);
                }



                prev_input_block = input;
                // Console.WriteLine("Error Vector after check_next_input: " + VectorToString(error));


            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static void CheckUtf8Bytes(Vector256<byte> input)
            {

                //prev
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
                Vector256<byte> prev1 = input.Prev1(prev_input_block);
                // check 1-2 bytes character
                Vector256<byte> sc = CheckSpecialCases(input, prev1);
                // Console.WriteLine("Special_case Vector before check_multibyte_lengths: " + VectorToString(error));

                // All remaining checks are for invalid 3-4 byte sequences, which either have too many continuations
                // or not enough (section 6.2 of the paper)
                error = Avx2.Or(error, CheckMultibyteLengths(input, prev_input_block, sc));
                // Console.WriteLine("Error Vector after check_utf8_bytes/after check_multibyte_lengths: " + VectorToString(error));

            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static bool Errors()
            {

                // Console.WriteLine("Error Vector at the end: " + VectorToString(error));
                // compiles to:
                //       vptest   ymm0, ymm0
                //       setne al
                //       movzx rax, al
                return !Avx2.TestZ(error, error);
            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]

            public static void CheckEof()
            {

                // Console.WriteLine("Error Vector before check_eof(): " + VectorToString(error));
                // Console.WriteLine("prev_incomplete Vector in check_eof(): " + VectorToString(prev_incomplete));
                // Compiles to:
                //       vpor ymm0, ymm0, ymmword ptr[rdx]
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
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            private static Vector256<byte> IsIncomplete(Vector256<byte> input)
            {
                // Console.WriteLine("Input Vector is_incomplete: " + VectorToString(input));

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

