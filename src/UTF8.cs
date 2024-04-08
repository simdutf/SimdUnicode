using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;

namespace SimdUnicode
{
    public static class UTF8
    {

        public unsafe static byte* RewindAndValidateWithErrors(int howFarBack, byte* buf, int len,ref int utf16CodeUnitCountAdjustment, ref int scalarCountAdjustment)
        {

            int TempUtf16CodeUnitCountAdjustment = 0;
            int TempScalarCountAdjustment = 0;

            int extraLen = 0;
            bool foundLeadingBytes = false;

            for (int i = 0; i <= howFarBack; i++)
            {
                byte candidateByte = buf[0 - i];
                foundLeadingBytes = (candidateByte & 0b11000000) != 0b10000000;
                if (foundLeadingBytes)
                {
                    if (i == 0) {break;}
                    // Console.WriteLine("Found leading byte at:" + i + ",Byte:" + candidateByte.ToString("X2"));
                    // adjustment to avoid double counting 
                    if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                    {
                        // Console.WriteLine("Found 2 byte");
                        TempUtf16CodeUnitCountAdjustment += 1; 
                    }
                    if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                    {
                        // Console.WriteLine("Found 3 byte");
                        TempUtf16CodeUnitCountAdjustment += 2; 
                    }
                    if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                    {
                        // Console.WriteLine("Found 4 byte");
                        TempUtf16CodeUnitCountAdjustment += 2;
                        TempScalarCountAdjustment += 1;
                    }
                    break;
                }
            }


            for (int i = 0; i <= howFarBack; i++)
            {
                byte candidateByte = buf[0 - i];
                foundLeadingBytes = (candidateByte & 0b11000000) != 0b10000000;
                if (foundLeadingBytes)
                {         
                    buf -= i;
                    extraLen = i;
                    break;
                }
            }


            if (!foundLeadingBytes)
            {
                return buf - howFarBack;
            }

            utf16CodeUnitCountAdjustment += TempUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment += TempScalarCountAdjustment;

            int TailUtf16CodeUnitCountAdjustment = 0;
            int TailScalarCountAdjustment = 0;

            // Now buf points to the start of a UTF-8 sequence or the start of the buffer.
            // Validate from this new start point with the adjusted length.
            byte* invalidBytePointer = GetPointerToFirstInvalidByteScalar(buf, len + extraLen,out TailUtf16CodeUnitCountAdjustment, out TailScalarCountAdjustment);

            utf16CodeUnitCountAdjustment += TailUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment += TailScalarCountAdjustment;

            // Console.WriteLine("utf16count after rewint(Temp):" + TempUtf16CodeUnitCountAdjustment);
            // Console.WriteLine("scalarcount after rewint:" + TempScalarCountAdjustment);

            // Console.WriteLine("utf16count after rewint(Scalar):" + TailUtf16CodeUnitCountAdjustment);
            // Console.WriteLine("scalarcount after rewint:" + TailScalarCountAdjustment);

            return invalidBytePointer;
        }

        public unsafe static void AdjustForSkippedBytes(byte* pInputBuffer,// int skippedBytes,
                                                        ref int utf16CodeUnitCountAdjustment,
                                                        ref int scalarCountAdjustment,
                                                        bool shouldAdd = false)
        {
            int adjustmentFactor = shouldAdd ? 1 : -1;

            // for (int i = 0; i < skippedBytes; i++)
            for (int i = 0; i < 3; i++)
            {
                byte currentByte = *(pInputBuffer + i);
                if (currentByte >= 0xC0 && currentByte < 0xE0)
                {
                    // 2-byte sequence
                    utf16CodeUnitCountAdjustment += 1 * adjustmentFactor;
                }
                else if (currentByte >= 0xE0 && currentByte < 0xF0)
                {
                    // 3-byte sequence
                    utf16CodeUnitCountAdjustment += 2 * adjustmentFactor;
                    scalarCountAdjustment += 1 * adjustmentFactor; // Assuming each 3-byte sequence translates to one scalar.
                }
                else if (currentByte >= 0xF0)
                {
                    // 4-byte sequence
                    utf16CodeUnitCountAdjustment += 2 * adjustmentFactor; // Two UTF-16 code units for each 4-byte sequence.
                    scalarCountAdjustment += 1 * adjustmentFactor; // One scalar for each 4-byte sequence.
                }
                // Adjust for other conditions as necessary
            }
        }

        public unsafe static byte* GetPointerToFirstInvalidByteScalar(byte* pInputBuffer, int inputLength,out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {


            int TempUtf16CodeUnitCountAdjustment= 0 ;
            int TempScalarCountAdjustment = 0;

            int pos = 0;
            int nextPos;
            uint codePoint = 0;
            while (pos < inputLength)
            {
                byte firstByte = pInputBuffer[pos];
                while (firstByte < 0b10000000)
                {
                    if (++pos == inputLength) { 

                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + inputLength; }
                    firstByte = pInputBuffer[pos];
                }

                if ((firstByte & 0b11100000) == 0b11000000)
                {
                    nextPos = pos + 2;
                    if (nextPos > inputLength) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00011111) << 6 | (uint)(pInputBuffer[pos + 1] & 0b00111111);
                    if ((codePoint < 0x80) || (0x7ff < codePoint)) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Overlong
                    TempUtf16CodeUnitCountAdjustment -= 1;
                }
                else if ((firstByte & 0b11110000) == 0b11100000)
                {
                    nextPos = pos + 3;
                    if (nextPos > inputLength) { 
                        
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00001111) << 12 |
                                 (uint)(pInputBuffer[pos + 1] & 0b00111111) << 6 |
                                 (uint)(pInputBuffer[pos + 2] & 0b00111111);
                    // Either overlong or too large:
                    if ((codePoint < 0x800) || (0xffff < codePoint) ||
                        (0xd7ff < codePoint && codePoint < 0xe000))
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    }
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; } // Too short
                    // if (pInputBuffer[pos + 3] < 0b10000000) { 
                    //     TempUtf16CodeUnitCountAdjustment -= 1;
                    // } else {
                    //     TempUtf16CodeUnitCountAdjustment -= 2;
                    // }
                    TempUtf16CodeUnitCountAdjustment -= 2;
                }
                else if ((firstByte & 0b11111000) == 0b11110000)
                { // 0b11110000

                    nextPos = pos + 4;
                    if (nextPos > inputLength) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 3] & 0b11000000) != 0b10000000) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; }
                    // range check
                    codePoint =
                        (uint)(firstByte & 0b00000111) << 18 | (uint)(pInputBuffer[pos + 1] & 0b00111111) << 12 |
                        (uint)(pInputBuffer[pos + 2] & 0b00111111) << 6 | (uint)(pInputBuffer[pos + 3] & 0b00111111);
                    if (codePoint <= 0xffff || 0x10ffff < codePoint) { 
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos; }
                    TempUtf16CodeUnitCountAdjustment -= 2;
                    TempScalarCountAdjustment -= 1;



                }
                else
                {
                    // we may have a continuation
                    utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                    scalarCountAdjustment = TempScalarCountAdjustment;
                    return pInputBuffer + pos;
                }
                pos = nextPos;
            }
            utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment = TempScalarCountAdjustment;
            return pInputBuffer + inputLength;
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

        public unsafe static byte* GetPointerToFirstInvalidByteSse(byte* pInputBuffer, int inputLength)
        {

            int processedLength = 0;
            int TempUtf16CodeUnitCountAdjustment= 0 ;
            int TempScalarCountAdjustment = 0;

            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for(; asciirun + 64 <= inputLength; asciirun += 64)
                {
                    Vector128<byte> block1 = Avx.LoadVector128(pInputBuffer + asciirun);
                    Vector128<byte> block2 = Avx.LoadVector128(pInputBuffer + asciirun + 16);
                    Vector128<byte> block3 = Avx.LoadVector128(pInputBuffer + asciirun + 32);
                    Vector128<byte> block4 = Avx.LoadVector128(pInputBuffer + asciirun + 48);

                    Vector128<byte> or = Sse2.Or(Sse2.Or(block1, block2), Sse2.Or(block3, block4));
                    if (Sse2.MoveMask(or) != 0)
                    {
                        break;
                    }
                }
                processedLength = asciirun;

                if (processedLength + 16 < inputLength)
                {
                    // We still have work to do!
                    Vector128<byte> prevInputBlock = Vector128<byte>.Zero;

                    Vector128<byte> maxValue = Vector128.Create(
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                    Vector128<byte> prevIncomplete = Sse2.SubtractSaturate(prevInputBlock, maxValue);


                    Vector128<byte> shuf1 = Vector128.Create(TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                            TOO_SHORT | OVERLONG_2,
                            TOO_SHORT,
                            TOO_SHORT | OVERLONG_3 | SURROGATE,
                            TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4);

                    Vector128<byte> shuf2 = Vector128.Create(CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
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
                    Vector128<byte> shuf3 = Vector128.Create(TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT);

                    Vector128<byte> thirdByte = Vector128.Create((byte)(0b11100000u - 0x80));
                    Vector128<byte> fourthByte = Vector128.Create((byte)(0b11110000u - 0x80));
                    Vector128<byte> v0f = Vector128.Create((byte)0x0F);
                    Vector128<byte> v80 = Vector128.Create((byte)0x80);

                    for (; processedLength + 16 <= inputLength; processedLength += 16)
                    {

                        Vector128<byte> currentBlock = Sse2.LoadVector128(pInputBuffer + processedLength);

                        int mask = Sse2.MoveMask(currentBlock);
                        if (mask == 0)
                        {
                                // Console.WriteLine("ascii");

                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (Sse2.MoveMask(prevIncomplete) != 0)
                            {
                               // return pInputBuffer + processedLength;

                              //  Console.WriteLine("not ascii");
                               return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength,ref TempUtf16CodeUnitCountAdjustment,ref TempScalarCountAdjustment);
                            }
                            prevIncomplete = Vector128<byte>.Zero;
                        }
                        else
                        {
                            // Contains non-ASCII characters, we need to do non-trivial processing
                            Vector128<byte> prev1 = Ssse3.AlignRight(currentBlock, prevInputBlock, (byte)(16 - 1));
                            Vector128<byte> byte_1_high = Ssse3.Shuffle(shuf1, Sse2.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> byte_1_low = Ssse3.Shuffle(shuf2, (prev1 & v0f));
                            Vector128<byte> byte_2_high = Ssse3.Shuffle(shuf3, Sse2.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> sc = Sse2.And(Sse2.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector128<byte> prev2 = Ssse3.AlignRight (currentBlock, prevInputBlock, (byte)(16 - 2));
                            Vector128<byte> prev3 = Ssse3.AlignRight (currentBlock, prevInputBlock, (byte)(16 - 3));
                            prevInputBlock = currentBlock;
                            Vector128<byte> isThirdByte = Sse2.SubtractSaturate(prev2, thirdByte);
                            Vector128<byte> isFourthByte = Sse2.SubtractSaturate(prev3, fourthByte);
                            Vector128<byte> must23 = Sse2.Or(isThirdByte, isFourthByte);
                            Vector128<byte> must23As80 = Sse2.And(must23, v80);
                            Vector128<byte> error = Sse2.Xor(must23As80, sc);
                            if (Sse2.MoveMask(error) != 0)
                            {
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength,ref TempUtf16CodeUnitCountAdjustment,ref TempScalarCountAdjustment);
                            }
                            prevIncomplete = Sse2.SubtractSaturate(currentBlock, maxValue);
                        }
                    }
                }
            }
            // We have processed all the blocks using SIMD, we need to process the remaining bytes.

            // Process the remaining bytes with the scalar function
            if (processedLength < inputLength)
            {
                // We need to possibly backtrack to the start of the last code point
                // worst possible case is 4 bytes, where we need to backtrack 3 bytes
                // 11110xxxx 10xxxxxx 10xxxxxx 10xxxxxx <== we might be pointing at the last byte
                if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                {
                    processedLength -= 1;
                    if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                    {
                        processedLength -= 1;
                        if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                        {
                            processedLength -= 1;
                        }
                    }
                }
                int TailScalarCodeUnitCountAdjustment = 0;
                int TailUtf16CodeUnitCountAdjustment = 0;
                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

            return pInputBuffer + inputLength;
        }


        public unsafe static byte* GetPointerToFirstInvalidByteAvx2(byte* pInputBuffer, int inputLength,out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            // Console.WriteLine("--------------------------Calling function----------------------------------");
            int processedLength = 0;
            int TempUtf16CodeUnitCountAdjustment= 0 ;
            int TempScalarCountAdjustment = 0;

            int TailScalarCodeUnitCountAdjustment = 0;
            int TailUtf16CodeUnitCountAdjustment = 0;

            if (pInputBuffer == null || inputLength <= 0)
            {
                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                scalarCountAdjustment = TempScalarCountAdjustment;
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for(; asciirun + 64 <= inputLength; asciirun += 64)
                {
                    Vector256<byte> block1 = Avx.LoadVector256(pInputBuffer + asciirun);
                    Vector256<byte> block2 = Avx.LoadVector256(pInputBuffer + asciirun + 32);
                    Vector256<byte> or = Avx2.Or(block1, block2);
                    if (Avx2.MoveMask(or) != 0)
                    {
                        break;
                    }

                }
                processedLength = asciirun;

                

                if (processedLength + 32 < inputLength)
                {
                    // We still have work to do!
                    Vector256<byte> prevInputBlock = Vector256<byte>.Zero;

                    Vector256<byte> maxValue = Vector256.Create(255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                    Vector256<byte> prevIncomplete = Avx2.SubtractSaturate(prevInputBlock, maxValue);


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

                    Vector256<byte> secondByte = Vector256.Create((byte)(0b11000000u - 0x80));
                    Vector256<byte> thirdByte = Vector256.Create((byte)(0b11100000u - 0x80));
                    Vector256<byte> fourthByte = Vector256.Create((byte)(0b11110000u - 0x80));

                    // // Mask for the lower and upper parts of the vector
                    // Vector128<byte> lowerMask = Vector128.Create(
                    //     0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                    //     0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF).AsByte();

                    // Vector128<byte> upperMask = Vector128.Create(
                    //     0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                    //     0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00).AsByte();

                    // // Combine lower and upper masks into a Vector256<byte>
                    // Vector256<byte> mask = Vector256.Create(lowerMask, upperMask);

                    // // Apply the mask to zero out the last 3 bytes of each vector
                    // Vector256<byte> secondByteMasked = Avx2.And(secondByte, mask);
                    // Vector256<byte> thirdByteMasked = Avx2.And(thirdByte, mask);
                    // Vector256<byte> fourthByteMasked = Avx2.And(fourthByte, mask);


                    Vector256<byte> v0f = Vector256.Create((byte)0x0F);
                    Vector256<byte> v80 = Vector256.Create((byte)0x80);

                                                                    // Vector to identify bytes right before the start of a 4-byte sequence in UTF-8.
                        // Vector256<byte> beforeFourByteMarker = Vector256.Create((byte)(0xF0 - 1));
                        // // Vector to identify bytes right before the start of a 3-byte sequence in UTF-8.
                        // Vector256<byte> beforeThreeByteMarker = Vector256.Create((byte)(0xE0 - 1));
                        // // Vector to identify bytes right before the start of a 2-byte sequence in UTF-8.
                        // Vector256<byte> beforeTwoByteMarker = Vector256.Create((byte)(0xC0 - 1));



                    for (; processedLength + 32 <= inputLength; processedLength += 32)
                    {
                        Vector256<byte> currentBlock = Avx.LoadVector256(pInputBuffer + processedLength);

                        int mask = Avx2.MoveMask(currentBlock);
                        if (mask == 0)
                        {
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (!Avx2.TestZ(prevIncomplete, prevIncomplete))
                            {

                            // TODO/think about : this path iss not explicitly tested
                            // Console.WriteLine("----Checkpoint 1:All ASCII need rewind");
                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment;

                                // int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                int off = processedLength;

                                if (processedLength >= 32 + 3){
                                    off = processedLength -32 - 3;
                                    int overlapCount =3;

                                    for(int k = 0; k < overlapCount; k++)
                                    {
                                        
                                        int candidateByte = pInputBuffer[processedLength + k];
                                        if ((candidateByte & 0b11000000) == 0b11000000)
                                        {
                                            if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                                            {
                                                TempUtf16CodeUnitCountAdjustment += 1; 
                                            }
                                            if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                                            {
                                                TempUtf16CodeUnitCountAdjustment += 2; 
                                            }
                                            if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                                            {
                                                TempUtf16CodeUnitCountAdjustment += 2;
                                                TempScalarCountAdjustment += 1;
                                            }
                                        }
                                    }
                                }
                                // else{ off = processedLength;}

                                // return SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off);
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off, ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                            }
                            prevIncomplete = Vector256<byte>.Zero;
                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                                // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.

                                // Detect start of 4-byte sequences.
                                Vector256<byte> isStartOf4ByteSequence = Avx2.SubtractSaturate(currentBlock, fourthByte);
                                uint fourByteCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf4ByteSequence));

                                // Detect start of 3-byte sequences (including those that start 4-byte sequences).
                                Vector256<byte> isStartOf3OrMoreByteSequence = Avx2.SubtractSaturate(currentBlock, thirdByte);
                                uint threeBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf3OrMoreByteSequence));

                                // Detect start of 2-byte sequences (including those that start 3-byte and 4-byte sequences).
                                Vector256<byte> isStartOf2OrMoreByteSequence = Avx2.SubtractSaturate(currentBlock, secondByte);
                                uint twoBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf2OrMoreByteSequence));

                                // Calculate counts by isolating each type.
                                uint threeByteCount = threeBytePlusCount - fourByteCount; // Isolate 3-byte starts by subtracting 4-byte starts.
                                uint twoByteCount = twoBytePlusCount - threeBytePlusCount; // Isolate 2-byte starts by subtracting 3-byte and 4-byte starts.



                            Vector256<byte> shuffled = Avx2.Permute2x128(prevInputBlock, currentBlock, 0x21);
                            prevInputBlock = currentBlock;
                            Vector256<byte> prev1 = Avx2.AlignRight(prevInputBlock, shuffled, (byte)(16 - 1));
                            // Vector256.Shuffle vs Avx2.Shuffle
                            // https://github.com/dotnet/runtime/blob/1400c1e7a888ea1e710e5c08d55c800e0b04bf8a/docs/coding-guidelines/vectorization-guidelines.md#vector256shuffle-vs-avx2shuffle
                            Vector256<byte> byte_1_high = Avx2.Shuffle(shuf1, Avx2.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);
                            Vector256<byte> byte_1_low = Avx2.Shuffle(shuf2, (prev1 & v0f));
                            Vector256<byte> byte_2_high = Avx2.Shuffle(shuf3, Avx2.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f);
                            Vector256<byte> sc = Avx2.And(Avx2.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector256<byte> prev2 = Avx2.AlignRight(prevInputBlock, shuffled, (byte)(16 - 2));
                            Vector256<byte> prev3 = Avx2.AlignRight(prevInputBlock, shuffled, (byte)(16 - 3));
                            Vector256<byte> isThirdByte = Avx2.SubtractSaturate(prev2, thirdByte);
                            Vector256<byte> isFourthByte = Avx2.SubtractSaturate(prev3, fourthByte);
                            Vector256<byte> must23 = Avx2.Or(isThirdByte, isFourthByte);
                            Vector256<byte> must23As80 = Avx2.And(must23, v80);
                            Vector256<byte> error = Avx2.Xor(must23As80, sc);
                            if (!Avx2.TestZ(error, error)) //context: we are dealing with a 32 bit 
                            {
                                // Console.WriteLine("-----Error path!!");
                                TailScalarCodeUnitCountAdjustment =0;
                                TailUtf16CodeUnitCountAdjustment =0;


                                int off = processedLength >= 32 ? processedLength : processedLength;

                                // Console.WriteLine("This is off :" + off);
                                // return SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off);
                                // byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off, ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                                byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - processedLength, ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);

                                // byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer,processedLength,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                                // Adjustments not to double count
                                // TempUtf16CodeUnitCountAdjustment += (int)fourByteCount * 2; 
                                // TempUtf16CodeUnitCountAdjustment += (int)twoByteCount; 
                                // TempUtf16CodeUnitCountAdjustment += (int)threeByteCount *2; 
                                // TempScalarCountAdjustment += (int)fourByteCount; 

                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment +TailUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;


 
                                return invalidBytePointer;

                            }
                                // Adjustments
                                TempUtf16CodeUnitCountAdjustment -= (int)fourByteCount * 2; 
                                TempUtf16CodeUnitCountAdjustment -= (int)twoByteCount; 
                                TempUtf16CodeUnitCountAdjustment -= (int)threeByteCount *2; 
                                TempScalarCountAdjustment -= (int)fourByteCount; 

                            prevIncomplete = Avx2.SubtractSaturate(currentBlock, maxValue);
                        }
                    }

                    if (!Avx2.TestZ(prevIncomplete, prevIncomplete))
                    {

                        // Console.WriteLine("----Checkpoint 2:SIMD rewind");
                        // We have an unterminated sequence.
                        processedLength -= 3;
                        for(int k = 0; k < 3; k++)
                        {
                            
                            int candidateByte = pInputBuffer[processedLength + k];
                            if ((candidateByte & 0b11000000) == 0b11000000)
                            {
                                if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                                {
                                    TempUtf16CodeUnitCountAdjustment += 1; 
                                }
                                if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                                {
                                    TempUtf16CodeUnitCountAdjustment += 2; 
                                }
                                if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                                {
                                    TempUtf16CodeUnitCountAdjustment += 2;
                                    TempScalarCountAdjustment += 1;
                                }
                            }
                        }
                    }
                }
            }

            // We have processed all the blocks using SIMD, we need to process the remaining bytes.
            // Process the remaining bytes with the scalar function
            if (processedLength < inputLength)
            {

                // Console.WriteLine("----Process remaining Scalar");
                int overlapCount = 0;

                // // We need to possibly backtrack to the start of the last code point
                while (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                {
                    processedLength -= 1;
                    overlapCount +=1;
                }

                for(int k = 0; k < overlapCount; k++)
                {
                    
                    int candidateByte = pInputBuffer[processedLength + k];
                    if ((candidateByte & 0b11000000) == 0b11000000)
                    {
                        if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                        {
                            TempUtf16CodeUnitCountAdjustment += 1; 
                        }
                        if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                        {
                            TempUtf16CodeUnitCountAdjustment += 2; 
                        }
                        if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                        {
                            TempUtf16CodeUnitCountAdjustment += 2;
                            TempScalarCountAdjustment += 1;
                        }

                        // processedLength += k;
                        break;
                    }
                }

                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment + TailUtf16CodeUnitCountAdjustment;
                    scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;

                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

            utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment + TailUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;

            return pInputBuffer + inputLength;
        }

        public unsafe static byte* GetPointerToFirstInvalidByteArm64(byte* pInputBuffer, int inputLength)
        {
            int processedLength = 0;

            int TempUtf16CodeUnitCountAdjustment= 0 ;
            int TempScalarCountAdjustment = 0;

            int utf16CodeUnitCountAdjustment=0, scalarCountAdjustment=0;

            if (pInputBuffer == null || inputLength <= 0)
            {
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for(; asciirun + 64 <= inputLength; asciirun += 64)
                {
                    Vector128<byte> block1 = AdvSimd.LoadVector128(pInputBuffer + asciirun);
                    Vector128<byte> block2 = AdvSimd.LoadVector128(pInputBuffer + asciirun + 16);
                    Vector128<byte> block3 = AdvSimd.LoadVector128(pInputBuffer + asciirun + 32);
                    Vector128<byte> block4 = AdvSimd.LoadVector128(pInputBuffer + asciirun + 48);
                    Vector128<byte> or = AdvSimd.Or(AdvSimd.Or(block1, block2), AdvSimd.Or(block3, block4));
                    if (AdvSimd.Arm64.MaxAcross(or).ToScalar() > 127)
                    {
                        break;
                    }
                }
                processedLength = asciirun;

                if (processedLength + 32 < inputLength)
                {
                    // We still have work to do!
                    Vector128<byte> prevInputBlock = Vector128<byte>.Zero;

                    Vector128<byte> maxValue = Vector128.Create(
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                    Vector128<byte> prevIncomplete = AdvSimd.SubtractSaturate(prevInputBlock, maxValue);


                    Vector128<byte> shuf1 = Vector128.Create(TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                            TOO_SHORT | OVERLONG_2,
                            TOO_SHORT,
                            TOO_SHORT | OVERLONG_3 | SURROGATE,
                            TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4);

                    Vector128<byte> shuf2 = Vector128.Create(CARRY | OVERLONG_3 | OVERLONG_2 | OVERLONG_4,
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
                    Vector128<byte> shuf3 = Vector128.Create(TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE_1000 | OVERLONG_4,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | OVERLONG_3 | TOO_LARGE,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                            TOO_LONG | OVERLONG_2 | TWO_CONTS | SURROGATE | TOO_LARGE,
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT);

                    Vector128<byte> thirdByte = Vector128.Create((byte)(0b11100000u - 0x80));
                    Vector128<byte> fourthByte = Vector128.Create((byte)(0b11110000u - 0x80));
                    Vector128<byte> v0f = Vector128.Create((byte)0x0F);
                    Vector128<byte> v80 = Vector128.Create((byte)0x80);
                    // Performance note: we could process 64 bytes at a time for better speed in some cases.
                    for (; processedLength + 16 <= inputLength; processedLength += 16)
                    {

                        Vector128<byte> currentBlock = AdvSimd.LoadVector128(pInputBuffer + processedLength);

                        if (AdvSimd.Arm64.MaxAcross(currentBlock).ToScalar() > 127)
                        {
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (AdvSimd.Arm64.MaxAcross(prevIncomplete).ToScalar() != 0)
                            {
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength, ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                            }
                            prevIncomplete = Vector128<byte>.Zero;
                        }
                        else
                        {
                            // Contains non-ASCII characters, we need to do non-trivial processing
                            Vector128<byte> prev1 = AdvSimd.ExtractVector128(prevInputBlock, currentBlock, (byte)(16 - 1));
                            Vector128<byte> byte_1_high = Vector128.Shuffle(shuf1, AdvSimd.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> byte_1_low = Vector128.Shuffle(shuf2, (prev1 & v0f));
                            Vector128<byte> byte_2_high = Vector128.Shuffle(shuf3, AdvSimd.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> sc = AdvSimd.And(AdvSimd.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector128<byte> prev2 = AdvSimd.ExtractVector128 (prevInputBlock, currentBlock, (byte)(16 - 2));
                            Vector128<byte> prev3 = AdvSimd.ExtractVector128 (prevInputBlock, currentBlock, (byte)(16 - 3));
                            prevInputBlock = currentBlock;
                            Vector128<byte> isThirdByte = AdvSimd.SubtractSaturate(prev2, thirdByte);
                            Vector128<byte> isFourthByte = AdvSimd.SubtractSaturate(prev3, fourthByte);
                            Vector128<byte> must23 = AdvSimd.Or(isThirdByte, isFourthByte);
                            Vector128<byte> must23As80 = AdvSimd.And(must23, v80);
                            Vector128<byte> error = AdvSimd.Xor(must23As80, sc);
                            if (AdvSimd.Arm64.MaxAcross(error).ToScalar() != 0)
                            {
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength,ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                            }
                            prevIncomplete = AdvSimd.SubtractSaturate(currentBlock, maxValue);
                        }
                    }
                }
            }
            // We have processed all the blocks using SIMD, we need to process the remaining bytes.

            // Process the remaining bytes with the scalar function
            if (processedLength < inputLength)
            {
                // We need to possibly backtrack to the start of the last code point
                // worst possible case is 4 bytes, where we need to backtrack 3 bytes
                // 11110xxxx 10xxxxxx 10xxxxxx 10xxxxxx <== we might be pointing at the last byte
                if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                {
                    processedLength -= 1;
                    if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                    {
                        processedLength -= 1;
                        if (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                        {
                            processedLength -= 1;
                        }
                    }
                }
                int TailScalarCodeUnitCountAdjustment = 0;
                int TailUtf16CodeUnitCountAdjustment = 0;
                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

            return pInputBuffer + inputLength;
        }
        public unsafe static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength,out int Utf16CodeUnitCountAdjustment,out int ScalarCodeUnitCountAdjustment)
        {

            // if (AdvSimd.Arm64.IsSupported)
            // {
            //     return GetPointerToFirstInvalidByteArm64(pInputBuffer, inputLength);
            // }
            if (Avx2.IsSupported)
            {
                return GetPointerToFirstInvalidByteAvx2(pInputBuffer, inputLength,out Utf16CodeUnitCountAdjustment,out ScalarCodeUnitCountAdjustment);
            }
            /*if (Vector512.IsHardwareAccelerated && Avx512Vbmi2.IsSupported)
            {
                return GetPointerToFirstInvalidByteAvx512(pInputBuffer, inputLength);
            }*/
            // if (Ssse3.IsSupported)
            // {
            //     return GetPointerToFirstInvalidByteSse(pInputBuffer, inputLength);
            // }
            // return GetPointerToFirstInvalidByteScalar(pInputBuffer, inputLength);

            return GetPointerToFirstInvalidByteScalar(pInputBuffer, inputLength,out Utf16CodeUnitCountAdjustment,out ScalarCodeUnitCountAdjustment);

        }

    }
}
