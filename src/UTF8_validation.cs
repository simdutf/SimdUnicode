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



// par:
// |                             Method |               FileName |       Mean |     Error |    StdDev | Allocated |
// |----------------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
// |  CompetitionUtf8ValidationRealData |   data/arabic.utf8.txt | 199.315 us | 0.2632 us | 0.2334 us |         - |
// | CompetitionUtf8ValidationErrorData |   data/arabic.utf8.txt | 132.782 us | 0.5135 us | 0.4552 us |         - |
// |  CompetitionUtf8ValidationRealData |  data/chinese.utf8.txt |  29.674 us | 0.3246 us | 0.2710 us |         - |
// | CompetitionUtf8ValidationErrorData |  data/chinese.utf8.txt |   5.185 us | 0.0177 us | 0.0148 us |         - |
// |  CompetitionUtf8ValidationRealData |  data/english.utf8.txt |  16.251 us | 0.2844 us | 0.2793 us |         - |
// | CompetitionUtf8ValidationErrorData |  data/english.utf8.txt |  11.119 us | 0.0405 us | 0.0379 us |         - |
// |  CompetitionUtf8ValidationRealData |   data/french.utf8.txt |  70.772 us | 0.2132 us | 0.1890 us |         - |
// | CompetitionUtf8ValidationErrorData |   data/french.utf8.txt |  22.515 us | 0.1278 us | 0.1195 us |         - |
// |  CompetitionUtf8ValidationRealData |   data/german.utf8.txt |  14.132 us | 0.0722 us | 0.0640 us |         - |
// | CompetitionUtf8ValidationErrorData |   data/german.utf8.txt |   6.889 us | 0.0231 us | 0.0205 us |         - |
// |  CompetitionUtf8ValidationRealData | data/japanese.utf8.txt |  25.023 us | 0.1017 us | 0.0952 us |         - |
// | CompetitionUtf8ValidationErrorData | data/japanese.utf8.txt |  17.504 us | 0.0712 us | 0.0666 us |         - |
// |  CompetitionUtf8ValidationRealData |  data/turkish.utf8.txt |  23.755 us | 0.3332 us | 0.3117 us |         - |
// | CompetitionUtf8ValidationErrorData |  data/turkish.utf8.txt |  21.983 us | 0.1308 us | 0.1223 us |         - |

//G_M000_IG01:                ;; offset=0x0000
//       push rbp
//       push r15
//       push r14
//       push r13
//       push rdi
//       push rsi
//       push rbx
//       sub rsp, 112
//       vzeroupper
//       vmovaps  xmmword ptr[rsp + 0x60], xmm6
//       vmovaps xmmword ptr[rsp + 0x50], xmm7
//       vmovaps  xmmword ptr[rsp + 0x40], xmm8
//       vmovaps xmmword ptr[rsp + 0x30], xmm9
//       lea      rbp, [rsp+0x20]
//        mov rax, 0x552066960748
//       mov qword ptr[rbp + 0x08], rax
//       mov      rbx, rcx
//       mov      esi, edx

//G_M000_IG02:                ;; offset=0x0041
//       xor edi, edi
//       test rbx, rbx
//       je SHORT G_M000_IG04

//G_M000_IG03:                ;; offset=0x0048
//       test esi, esi
//       jg G_M000_IG16 


//G_M000_IG04:                ;; offset=0x0050
//       mov rax, rbx
//       mov rcx, 0x552066960748
//       cmp qword ptr[rbp + 0x08], rcx
//       je       SHORT G_M000_IG05
//       call CORINFO_HELP_FAIL_FAST


//G_M000_IG05:                ;; offset=0x0068
//       nop

//G_M000_IG06:                ;; offset=0x0069
//       vmovaps xmm6, xmmword ptr[rbp + 0x40]
//       vmovaps  xmm7, xmmword ptr[rbp + 0x30]
//       vmovaps xmm8, xmmword ptr[rbp + 0x20]
//       vmovaps  xmm9, xmmword ptr[rbp + 0x10]
//       vzeroupper
//       lea      rsp, [rbp+0x50]
//        pop rbx
//       pop rsi
//       pop rdi
//       pop r13
//       pop r14
//       pop r15
//       pop rbp
//       ret

//G_M000_IG07:                ;; offset=0x008F
//       mov r15d, edi
//       movsxd rax, r15d
//       vmovups ymm6, ymmword ptr[rbx + rax]
//       vmovaps  ymm7, ymm6
//       vpmovmskb eax, ymm7
//       test     eax, eax
//       je       G_M000_IG09
//       test     byte ptr[(reloc 0x7ff825f90950)], 1
//       je G_M000_IG26


//G_M000_IG08:                ;; offset=0x00B7
//       mov rax, 0x2132D1715C8
//       vmovups ymm0, ymmword ptr[rax]
//       vperm2i128 ymm0, ymm0, ymm7, 33
//       vpalignr ymm1, ymm7, ymm0, 15
//       vpsrlw ymm2, ymm1, 4
//       vmovups ymm3, ymmword ptr[reloc @RWD00]
//       vpshufb  ymm2, ymm3, ymm2
//       vpand    ymm1, ymm1, ymmword ptr[reloc @RWD32]
//       vmovups ymm3, ymmword ptr[reloc @RWD64]
//       vpshufb  ymm1, ymm3, ymm1
//       vpand    ymm1, ymm2, ymm1
//       vpsrlw   ymm2, ymm7, 4
//       vmovups ymm3, ymmword ptr[reloc @RWD96]
//       vpshufb  ymm2, ymm3, ymm2
//       vpand    ymm1, ymm1, ymm2
//       mov      r13, 0x2132D171628
//       vmovups ymm2, ymmword ptr[r13]
//       vpalignr ymm3, ymm7, ymm0, 13
//       vpsubusb ymm3, ymm3, ymmword ptr[reloc @RWD128]
//       vpalignr ymm0, ymm7, ymm0, 14
//       vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD160]
//       vpor     ymm0, ymm0, ymm3
//       vpand    ymm0, ymm0, ymmword ptr[reloc @RWD192]
//       vpxor ymm0, ymm0, ymm1
//       vpor ymm0, ymm2, ymm0
//       vmovups ymmword ptr[r13], ymm0
//       vpsubusw ymm0, ymm7, ymmword ptr[reloc @RWD224]
//       mov rax, 0x2132D1715F8
//       vmovups ymmword ptr[rax], ymm0

//G_M000_IG09:                ;; offset=0x016E
//       test byte ptr[(reloc 0x7ff825f90950)], 1
//       je G_M000_IG27


//G_M000_IG10:                ;; offset=0x017B
//       mov rax, 0x2132D1715C8
//       vmovups ymmword ptr[rax], ymm7
//       add      r15d, 32
//       movsxd rax, r15d
//       vmovups ymm0, ymmword ptr[rbx + rax]
//       vpmovmskb eax, ymm0
//       test     eax, eax
//       je       G_M000_IG11
//       vperm2i128 ymm1, ymm6, ymm0, 33
//       vpalignr ymm2, ymm0, ymm1, 15
//       vpsrlw ymm3, ymm2, 4
//       vmovups ymm4, ymmword ptr[reloc @RWD00]
//       vpshufb  ymm3, ymm4, ymm3
//       vpand    ymm2, ymm2, ymmword ptr[reloc @RWD32]
//       vmovups ymm4, ymmword ptr[reloc @RWD64]
//       vpshufb  ymm2, ymm4, ymm2
//       vpand    ymm2, ymm3, ymm2
//       vpsrlw   ymm3, ymm0, 4
//       vmovups ymm4, ymmword ptr[reloc @RWD96]
//       vpshufb  ymm3, ymm4, ymm3
//       vpand    ymm2, ymm2, ymm3
//       mov      r13, 0x2132D171628
//       vmovups ymm3, ymmword ptr[r13]
//       vpalignr ymm4, ymm0, ymm1, 13
//       vpsubusb ymm4, ymm4, ymmword ptr[reloc @RWD128]
//       vpalignr ymm1, ymm0, ymm1, 14
//       vpsubusb ymm1, ymm1, ymmword ptr[reloc @RWD160]
//       vpor     ymm1, ymm1, ymm4
//       vpand    ymm1, ymm1, ymmword ptr[reloc @RWD192]
//       vpxor ymm1, ymm1, ymm2
//       vpor ymm1, ymm3, ymm1
//       vmovups ymmword ptr[r13], ymm1
//       vpsubusw ymm1, ymm0, ymmword ptr[reloc @RWD224]
//       mov rax, 0x2132D1715F8
//       vmovups ymmword ptr[rax], ymm1

//G_M000_IG11:                ;; offset=0x024A
//       mov rax, 0x2132D1715C8
//       vmovups ymmword ptr[rax], ymm0
//       call[SimdUnicode.Utf8Validation + utf8_checker:CheckEof()]
//       call[SimdUnicode.Utf8Validation + utf8_checker:Errors():bool]
//       test     eax, eax
//       je       SHORT G_M000_IG15


//G_M000_IG12:                ;; offset=0x0268
//       movsxd rcx, edi
//       add rcx, rbx
//       mov edx, esi
//       sub edx, edi
//       call[SimdUnicode.UTF8:RewindAndValidateWithErrors(ulong, int):ulong]
//       mov      rcx, 0x552066960748
//       cmp qword ptr[rbp + 0x08], rcx
//       je       SHORT G_M000_IG13
//       call CORINFO_HELP_FAIL_FAST


//G_M000_IG13:                ;; offset=0x028D
//       nop

//G_M000_IG14:                ;; offset=0x028E
//       vmovaps xmm6, xmmword ptr[rbp + 0x40]
//       vmovaps  xmm7, xmmword ptr[rbp + 0x30]
//       vmovaps xmm8, xmmword ptr[rbp + 0x20]
//       vmovaps  xmm9, xmmword ptr[rbp + 0x10]
//       vzeroupper
//       lea      rsp, [rbp+0x50]
//        pop rbx
//       pop rsi
//       pop rdi
//       pop r13
//       pop r14
//       pop r15
//       pop rbp
//       ret

//G_M000_IG15:                ;; offset=0x02B4
//       mov edi, r14d


//G_M000_IG16:                ;; offset=0x02B7
//       lea r14d, [rdi + 0x40]
//       cmp r14d, esi
//       jle G_M000_IG07


//G_M000_IG17:                ;; offset=0x02C4
//       cmp edi, esi
//       jge G_M000_IG23
//       test dword ptr[rsp], esp
//       sub      rsp, 64
//       lea rcx, [rsp + 0x20]
//       vxorps ymm0, ymm0, ymm0
//       vmovdqu ymmword ptr[rcx], ymm0
//       vmovdqu  ymmword ptr[rcx + 0x20], ymm0
//       mov r13, rcx
//       mov ecx, esi
//       sub ecx, edi
//       js G_M000_IG28
//       movsxd rdx, edi
//       add rdx, rbx
//       cmp ecx, 64
//       ja G_M000_IG29
//       mov r8d, ecx
//       mov rcx, r13
//       call[System.Buffer:Memmove(byref, byref, ulong)]
//       vmovups  ymm6, ymmword ptr[r13]
//       vpmovmskb eax, ymm6
//       test eax, eax
//       je G_M000_IG19
//       test byte ptr[(reloc 0x7ff825f90950)], 1
//       je G_M000_IG30


//G_M000_IG18:                ;; offset=0x032C
//       mov rax, 0x2132D1715C8
//       vmovups ymm0, ymmword ptr[rax]
//       vperm2i128 ymm0, ymm0, ymm6, 33
//       vpalignr ymm1, ymm6, ymm0, 15
//       vpsrlw ymm2, ymm1, 4
//       vmovups ymm3, ymmword ptr[reloc @RWD00]
//       vpshufb  ymm2, ymm3, ymm2
//       vpand    ymm1, ymm1, ymmword ptr[reloc @RWD32]
//       vmovups ymm3, ymmword ptr[reloc @RWD64]
//       vpshufb  ymm1, ymm3, ymm1
//       vpand    ymm1, ymm2, ymm1
//       vpsrlw   ymm2, ymm6, 4
//       vmovups ymm3, ymmword ptr[reloc @RWD96]
//       vpshufb  ymm2, ymm3, ymm2
//       vpand    ymm1, ymm1, ymm2
//       mov      r13, 0x2132D171628
//       vmovups ymm2, ymmword ptr[r13]
//       vpalignr ymm3, ymm6, ymm0, 13
//       vpsubusb ymm3, ymm3, ymmword ptr[reloc @RWD128]
//       vpalignr ymm0, ymm6, ymm0, 14
//       vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD160]
//       vpor     ymm0, ymm0, ymm3
//       vpand    ymm0, ymm0, ymmword ptr[reloc @RWD192]
//       vpxor ymm0, ymm0, ymm1
//       vpor ymm0, ymm2, ymm0
//       vmovups ymmword ptr[r13], ymm0
//       vpsubusw ymm0, ymm6, ymmword ptr[reloc @RWD224]
//       mov rax, 0x2132D1715F8
//       vmovups ymmword ptr[rax], ymm0

//G_M000_IG19:                ;; offset=0x03E3
//       test byte ptr[(reloc 0x7ff825f90950)], 1
//       je G_M000_IG31


//G_M000_IG20:                ;; offset=0x03F0
//       mov rax, 0x2132D1715C8
//       vmovups ymmword ptr[rax], ymm6
//       call[SimdUnicode.Utf8Validation + utf8_checker:CheckEof()]
//       call[SimdUnicode.Utf8Validation + utf8_checker:Errors():bool]
//       test     eax, eax
//       je       SHORT G_M000_IG23
//       movsxd rcx, edi
//       add rcx, rbx
//       mov edx, esi
//       sub edx, edi
//       call[SimdUnicode.UTF8:GetPointerToFirstInvalidByte(ulong, int):ulong]
//       mov      rcx, 0x552066960748
//       cmp qword ptr[rbp + 0x08], rcx
//       je       SHORT G_M000_IG21
//       call CORINFO_HELP_FAIL_FAST


        public static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)
        {



            int processedLength = 0;

            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }

            // while (processedLength + 128 <= inputLength)
            // {                
                
            //     SIMDGetPointerToFirstInvalidByte(pInputBuffer,processedLength);
                
            //     Utf8Validation.utf8_checker.CheckEof();
            //     if (Utf8Validation.utf8_checker.Errors())
            //     {
            //         // return pInputBuffer + processedLength;
            //         return SimdUnicode.UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength,inputLength - processedLength);
            //     }
            //     processedLength += 128;

            // }  

            while (processedLength + 64 <= inputLength)
            {                
                
                SIMDGetPointerToFirstInvalidByte(pInputBuffer,processedLength);
                
                Utf8Validation.utf8_checker.CheckEof();
                if (Utf8Validation.utf8_checker.Errors())
                {
                    // return pInputBuffer + processedLength;
                    return SimdUnicode.UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength,inputLength - processedLength);
                }
                processedLength += 64;

            }  



            // while (processedLength + 32 <= inputLength)
            // {                
                
            //     SIMDGetPointerToFirstInvalidByte(pInputBuffer,processedLength);
                
            //     Utf8Validation.utf8_checker.CheckEof();
            //     if (Utf8Validation.utf8_checker.Errors())
            //     {
            //         // return pInputBuffer + processedLength;
            //         return SimdUnicode.UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength,inputLength - processedLength);
            //     }
            //     processedLength += 32;

            // }

            // First fix bencrmarks static utf checker 
            // 
        // |                      Method |               FileName |       Mean |     Error |     StdDev | Allocated |
        // |---------------------------- |----------------------- |-----------:|----------:|-----------:|----------:|
        // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 478.655 us | 8.9312 us | 15.4059 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 283.895 us | 5.2810 us |  8.9675 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 134.967 us | 2.6698 us |  5.1438 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  17.403 us | 0.3361 us |  0.4820 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  11.186 us | 0.0707 us |  0.0626 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  11.167 us | 0.1118 us |  0.0991 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  13.303 us | 0.2523 us |  0.2236 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  13.002 us | 0.1448 us |  0.1284 us |         - |
        // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.965 us | 0.1016 us |  0.0901 us |         - |
        // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.981 us | 0.0683 us |  0.0639 us |         - |
        // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 138.114 us | 2.6217 us |  3.0191 us |         - |
        // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  66.023 us | 1.2819 us |  1.1364 us |         - |
        // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 168.166 us | 2.4131 us |  2.2572 us |         - |
        // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 112.761 us | 2.2175 us |  1.9657 us |         - |

        

                // Process the remaining bytes with the scalar function
            if (processedLength < inputLength)
            {
                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInputBuffer + processedLength, inputLength - processedLength);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

    //  benches done with 2 times unroll

//             |                      Method |               FileName |       Mean |     Error |    StdDev | Allocated |
// |---------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
// |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 454.633 us | 4.3116 us | 3.8221 us |         - |
// | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 270.426 us | 4.4255 us | 4.5447 us |         - |
// |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 129.489 us | 2.3981 us | 2.3553 us |         - |
// | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  16.391 us | 0.3104 us | 0.2752 us |         - |
// |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  11.192 us | 0.0790 us | 0.0660 us |         - |
// | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  11.244 us | 0.1760 us | 0.1646 us |         - |
// |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.826 us | 0.0646 us | 0.0573 us |         - |
// | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  13.416 us | 0.2554 us | 0.4921 us |         - |
// |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   6.006 us | 0.1154 us | 0.1617 us |         - |
// | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.925 us | 0.0930 us | 0.0870 us |         - |
// |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 138.051 us | 1.2770 us | 1.1945 us |         - |
// | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  75.751 us | 0.9603 us | 0.7498 us |         - |
// |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 173.199 us | 3.4289 us | 5.4386 us |         - |
// | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 112.989 us | 1.7684 us | 1.5677 us |         - |
            // if (processedLength < inputLength)
            // {

            //     Span<byte> remainingBytes = stackalloc byte[64];
            //     new Span<byte>(pInputBuffer + processedLength, inputLength - processedLength).CopyTo(remainingBytes);

            //     ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock);

            //     Utf8Validation.utf8_checker.CheckEof();
            //     if (Utf8Validation.utf8_checker.Errors())
            //     {
            //         // return pInputBuffer + processedLength;
            //         return SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInputBuffer + processedLength,inputLength - processedLength);
            //     }
            //     processedLength += inputLength - processedLength;

            // }

            // |                      Method |               FileName |       Mean |     Error |    StdDev | Allocated |
            // |---------------------------- |----------------------- |-----------:|----------:|----------:|----------:|
            // |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 454.353 us | 6.0327 us | 5.3478 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 278.734 us | 5.3031 us | 5.8943 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 127.542 us | 2.2544 us | 2.1087 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  15.822 us | 0.3030 us | 0.3832 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  11.016 us | 0.1309 us | 0.1225 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  11.030 us | 0.1580 us | 0.1400 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/french.utf8.txt |  12.547 us | 0.0740 us | 0.0656 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/french.utf8.txt |  12.652 us | 0.1455 us | 0.1290 us |         - |
            // |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |   5.755 us | 0.0277 us | 0.0246 us |         - |
            // | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |   5.669 us | 0.0079 us | 0.0070 us |         - |
            // |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 130.835 us | 0.5999 us | 0.5612 us |         - |
            // | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  71.814 us | 1.0399 us | 0.9727 us |         - |
            // |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 167.163 us | 3.1610 us | 4.1103 us |         - |
            // | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 109.607 us | 0.6636 us | 0.5542 us |         - |


            // if (processedLength < inputLength)
            // {

            //     Span<byte> remainingBytes = stackalloc byte[64];
            //     for (int i = 0; i < inputLength - processedLength; i++)
            //     {
            //         remainingBytes[i] = pInputBuffer[processedLength + i];
            //     }

            //     ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
            //     Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
            //     Utf8Validation.utf8_checker.CheckNextInput(remainingBlock);
            //     Utf8Validation.utf8_checker.CheckEof();
            //     if (Utf8Validation.utf8_checker.Errors())
            //     {
            //         // return pInputBuffer + processedLength;
            //         return SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInputBuffer + processedLength,inputLength - processedLength);
            //     }
            //     processedLength += inputLength - processedLength;
            // }



            
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

        //// Returns a pointer to the first invalid byte in the input buffer if it's invalid, or a pointer to the end if it's valid.
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


                            // Check if the entire 256-bit vector is ASCII


            //                ; Assembly listing for method SimdUnicode.Utf8Validation + utf8_checker:CheckNextInput(System.Runtime.Intrinsics.Vector256`1[ubyte])(FullOpts)
            //; Emitting BLENDED_CODE for X64 with AVX - Windows
            //; FullOpts code
            //; optimized code
            //; rsp based frame
            //; partially interruptible
            //; No PGO data
            //; 0 inlinees with PGO data; 13 single block inlinees; 1 inlinees without PGO data

            //G_M000_IG01:; ; offset = 0x0000
            //       push rbx
            //       sub rsp, 32
            //       vzeroupper
            //       mov      rbx, rcx

            //G_M000_IG02:                ; ; offset = 0x000B
            //       vmovups ymm0, ymmword ptr[rbx]
            //       vpmovmskb ecx, ymm0
            //       test ecx, ecx
            //       je G_M000_IG05


            //G_M000_IG03:; ; offset = 0x001B
            //       test     byte ptr[(reloc 0x7ff825f80950)], 1
            //       je G_M000_IG08


            //G_M000_IG04:; ; offset = 0x0028
            //       mov rcx, 0x2C6C2E116D8
            //       vmovups ymm0, ymmword ptr[rcx]
            //       vmovups ymm1, ymmword ptr[rbx]
            //       vperm2i128 ymm0, ymm0, ymmword ptr[rbx], 33
            //       vpalignr ymm1, ymm1, ymm0, 15
            //       vpsrlw ymm2, ymm1, 4
            //       vmovups ymm3, ymmword ptr[reloc @RWD00]
            //       vpshufb ymm2, ymm3, ymm2
            //       vpand ymm1, ymm1, ymmword ptr[reloc @RWD32]
            //       vmovups ymm3, ymmword ptr[reloc @RWD64]
            //       vpshufb ymm1, ymm3, ymm1
            //       vpand ymm1, ymm2, ymm1
            //       vmovups ymm2, ymmword ptr[rbx]
            //       vpsrlw ymm2, ymm2, 4
            //       vmovups ymm3, ymmword ptr[reloc @RWD96]
            //       vpshufb ymm2, ymm3, ymm2
            //       vpand ymm1, ymm1, ymm2
            //       mov rcx, 0x2C6C2E11738
            //       vmovups ymm2, ymmword ptr[rcx]
            //       vmovups ymm3, ymmword ptr[rbx]
            //       vpalignr ymm3, ymm3, ymm0, 14
            //       vpsubusb ymm3, ymm3, ymmword ptr[reloc @RWD128]
            //       vmovups ymm4, ymmword ptr[rbx]
            //       vpalignr ymm0, ymm4, ymm0, 13
            //       vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD160]
            //       vpor ymm0, ymm3, ymm0
            //       vpand ymm0, ymm0, ymmword ptr[reloc @RWD192]
            //       vpxor ymm0, ymm0, ymm1
            //       vpor ymm0, ymm2, ymm0
            //       vmovups ymmword ptr[rcx], ymm0
            //       vmovups  ymm0, ymmword ptr[rbx]
            //       vpsubusw ymm0, ymm0, ymmword ptr[reloc @RWD224]
            //       mov rcx, 0x2C6C2E11708
            //       vmovups ymmword ptr[rcx], ymm0

            //G_M000_IG05:                ; ; offset = 0x00EF
            //       test     byte ptr[(reloc 0x7ff825f80950)], 1
            //       je SHORT G_M000_IG09

            //G_M000_IG06:                ; ; offset = 0x00F8
            //       vmovups ymm0, ymmword ptr[rbx]
            //       mov rcx, 0x2C6C2E116D8
            //       vmovups ymmword ptr[rcx], ymm0

            //G_M000_IG07:                ; ; offset = 0x010A
            //       vzeroupper
            //       add      rsp, 32
            //       pop rbx
            //       ret

            //G_M000_IG08:                ; ; offset = 0x0113
            //       mov rcx, 0x7FF825F80918
            //       mov edx, 8
            //       call CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
            //       jmp G_M000_IG04


            //G_M000_IG09:; ; offset = 0x012C
            //       mov rcx, 0x7FF825F80918
            //       mov edx, 8
            //       call CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
            //       jmp SHORT G_M000_IG06


            //; Total bytes of code 322

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

            // G_M000_IG01:; ; offset = 0x0000
            //       push rbx
            //       sub rsp, 32
            //       vzeroupper
            //       mov      rbx, rcx

            //G_M000_IG02:                ; ; offset = 0x000B
            //       test     byte ptr[(reloc 0x7ff822570950)], 1
            //       je G_M000_IG05


            //G_M000_IG03:; ; offset = 0x0018
            //       mov rcx, 0x24444C216D8
            //       vmovups ymm0, ymmword ptr[rcx]
            //       vmovups ymm1, ymmword ptr[rbx]
            //       vperm2i128 ymm0, ymm0, ymm1, 33
            //       vpalignr ymm2, ymm1, ymm0, 15
            //       vpsrlw ymm3, ymm2, 4
            //       vmovups ymm4, ymmword ptr[reloc @RWD00]
            //       vpshufb ymm3, ymm4, ymm3
            //       vpand ymm2, ymm2, ymmword ptr[reloc @RWD32]
            //       vmovups ymm4, ymmword ptr[reloc @RWD64]
            //       vpshufb ymm2, ymm4, ymm2
            //       vpand ymm2, ymm3, ymm2
            //       vpsrlw ymm3, ymm1, 4
            //       vmovups ymm4, ymmword ptr[reloc @RWD96]
            //       vpshufb ymm3, ymm4, ymm3
            //       vpand ymm2, ymm2, ymm3
            //       mov rcx, 0x24444C21708 <= this changes a bit
            //       vmovups ymm3, ymmword ptr[rcx]
            //       vpalignr ymm4, ymm1, ymm0, 14
            //       vpsubusb ymm4, ymm4, ymmword ptr[reloc @RWD128]
            //       vpalignr ymm0, ymm1, ymm0, 13
            //       vpsubusb ymm0, ymm0, ymmword ptr[reloc @RWD160]
            //       vpor ymm0, ymm4, ymm0
            //       vpand ymm0, ymm0, ymmword ptr[reloc @RWD192]
            //       vpxor ymm0, ymm0, ymm2
            //       vpor ymm0, ymm3, ymm0
            //       vmovups ymmword ptr[rcx], ymm0

            //G_M000_IG04:                ; ; offset = 0x00B9
            //       vzeroupper
            //       add      rsp, 32
            //       pop rbx
            //       ret

            //G_M000_IG05:                ; ; offset = 0x00C2
            //       mov rcx, 0x7FF822570918
            //       mov edx, 8
            //       call CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
            //       jmp G_M000_IG03


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


                //            G_M000_IG01:; ; offset = 0x0000
                //       sub rsp, 40
                //       vzeroupper

                //G_M000_IG02:                ; ; offset = 0x0007
                //       test     byte ptr[(reloc 0x7ffde3450950)], 1<= again this test
                //       je SHORT G_M000_IG05

                //G_M000_IG03:                ; ; offset = 0x0010
                //       mov rax, 0x2A2452616D8
                //       vmovups ymm0, ymmword ptr[rax]
                //       vptest ymm0, ymm0
                //       setne al
                //       movzx rax, al


                //G_M000_IG04:; ; offset = 0x0029
                //       vzeroupper
                //       add      rsp, 40
                //       ret

                //G_M000_IG05:                ; ; offset = 0x0031
                //       mov rcx, 0x7FFDE3450918
                //       mov edx, 8
                //       call CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE double check what this does
                //       jmp SHORT G_M000_IG03

                //; Total bytes of code 71

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


            //            G_M000_IG01:; ; offset = 0x0000
            //       sub rsp, 40
            //       vzeroupper

            //G_M000_IG02:                ; ; offset = 0x0007
            //       test     byte ptr[(reloc 0x7ffde3460950)], 1   <=Not sure why it is making a test?
            //       je SHORT G_M000_IG05

            //G_M000_IG03:                ; ; offset = 0x0010
            //       mov rcx, 0x25313F016D8
            //       vmovups ymm0, ymmword ptr[rcx]
            //       mov rdx, 0x25313F01708
            //       vpor ymm0, ymm0, ymmword ptr[rdx]
            //       vmovups ymmword ptr[rcx], ymm0

            //G_M000_IG04:                ; ; offset = 0x0030
            //       vzeroupper
            //       add      rsp, 40
            //       ret

            //G_M000_IG05:                ; ; offset = 0x0038
            //       mov rcx, 0x7FFDE3460918
            //       mov edx, 8
            //       call CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
            //       jmp SHORT G_M000_IG03

            //; Total bytes of code 78
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

