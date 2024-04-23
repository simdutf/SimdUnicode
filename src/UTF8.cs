using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SimdUnicode
{
    public static class UTF8
    {

        static Func<byte, string> byteToBinaryString = b => Convert.ToString(b, 2).PadLeft(8, '0');


        public unsafe static byte* RewindAndValidateWithErrors(int howFarBack, byte* buf, int len,ref int utf16CodeUnitCountAdjustment, ref int scalarCountAdjustment)
        {
            Console.WriteLine("--Rewind Validate with Errors");
            Console.WriteLine("current Byte:" + Convert.ToString(buf[0], 2).PadLeft(8, '0'));

            int TempUtf16CodeUnitCountAdjustment = 0;
            int TempScalarCountAdjustment = 0;

            int extraLen = 0;
            bool foundLeadingBytes = false;

            // TODO: adjust for double counting iff there is an error eg invalidpointerbyte != length
            // Even with no errors, it sometime double counts, why.. ? because it goes back even further
            // even though the scalar doesnt thread 
            // adjust for  double counting
            // for (int i = 0; i <= howFarBack; i++) 
            for (int i = 0; i <= howFarBack; i++) 
            {
                if (i==0){continue;};// we dont want to miss out on counting the current byte, only to avoid double counting what may have been counted prior
                byte candidateByte = buf[0 - i];
                foundLeadingBytes = (candidateByte & 0b11000000) != 0b10000000;
                if (foundLeadingBytes)
                {

                    // Console.WriteLine("Found leading byte at:" + i + ",Byte:" + candidateByte.ToString("X2"));
                    Console.WriteLine("Found leading byte at:" + i + ",Byte:" + Convert.ToString(candidateByte, 2).PadLeft(8, '0'));

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
                Console.WriteLine("backup stat:" + i);
                byte candidateByte = buf[0 - i];
                foundLeadingBytes = (candidateByte & 0b11000000) != 0b10000000;
                if (foundLeadingBytes)
                {         
                    buf -= i;
                    extraLen = i;
                    Console.WriteLine(howFarBack);
                    Console.WriteLine("Found leading byte at:" + i + ",Byte:" + Convert.ToString(candidateByte, 2).PadLeft(8, '0'));


                    // Console.WriteLine("Backed up " + extraLen + 1 + " bytes");
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

            // TODO:figure out why calling SIMD here breaks the tests filter.This just breaks stuff?!?!?!
            byte* invalidBytePointer = GetPointerToFirstInvalidByteScalar(buf, len + extraLen,out TailUtf16CodeUnitCountAdjustment, out TailScalarCountAdjustment);

            utf16CodeUnitCountAdjustment += TailUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment += TailScalarCountAdjustment;

            Console.WriteLine("rewind utf16 Doublecount adjustment(Temp):" + TempUtf16CodeUnitCountAdjustment);
            Console.WriteLine("scalarcount adjstment after rewind:" + TempScalarCountAdjustment);
            Console.WriteLine(" ");
            Console.WriteLine("rewinds utf16 count(done by GetPointerToFirstInvalidByteScalar):" + TailUtf16CodeUnitCountAdjustment);
            Console.WriteLine("scalarcount after rewind(ditto):" + TailScalarCountAdjustment);

            return invalidBytePointer;
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
                    TempUtf16CodeUnitCountAdjustment -= 2;
                }
                else if ((firstByte & 0b11111000) == 0b11110000)
                { 
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
            Console.WriteLine("--------------------------Calling function----------------------------------");
            Console.WriteLine("Length: " + inputLength);
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
                Console.WriteLine("asciirun bytes: ", asciirun);
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

                    Vector256<byte> v0f = Vector256.Create((byte)0x0F);
                    Vector256<byte> v80 = Vector256.Create((byte)0x80);

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
                            Console.WriteLine("----All ASCII need rewind");
                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment;

                                // int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                int off = processedLength;


                                // No need to count

                                // if (processedLength >= 32 + 3){
                                //     off = processedLength -32 - 3;
                                //     int overlapCount =3;

                                //     for(int k = 0; k < overlapCount; k++)
                                //     {
                                        
                                //         int candidateByte = pInputBuffer[processedLength + k];
                                //         if ((candidateByte & 0b11000000) == 0b11000000)
                                //         {
                                //             if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                                //             {
                                //                 TempUtf16CodeUnitCountAdjustment += 1; 
                                //             }
                                //             if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                                //             {
                                //                 TempUtf16CodeUnitCountAdjustment += 2; 
                                //             }
                                //             if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                                //             {
                                //                 TempUtf16CodeUnitCountAdjustment += 2;
                                //                 TempScalarCountAdjustment += 1;
                                //             }
                                //         }
                                //     }
                                // }

                                
                            // TODO this needs S
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off, ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                            }
                            prevIncomplete = Vector256<byte>.Zero;
                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                            Console.WriteLine("--Found non-ascii:triggering SIMD routine at " + processedLength + "bytes");

                            // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.
                            // TODO:integrate this better with the rest of the code
                            // Vector256<byte> isStartOf4ByteSequence = Avx2.SubtractSaturate(currentBlock, fourthByte);
                            // Vector256<byte> isStartOf3OrMoreByteSequence = Avx2.SubtractSaturate(currentBlock, thirdByte);
                            // Vector256<byte> isStartOf2OrMoreByteSequence = Avx2.SubtractSaturate(currentBlock, secondByte);

                            // uint twoBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf2OrMoreByteSequence));
                            // uint threeBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf3OrMoreByteSequence));
                            // uint fourByteCount = Popcnt.PopCount((uint)Avx2.MoveMask(isStartOf4ByteSequence));

                            // Calculate counts by isolating each type.
                            // uint threeByteCount = threeBytePlusCount - fourByteCount; // Isolate 3-byte starts by subtracting 4-byte starts.
                            // uint twoByteCount = twoBytePlusCount - threeBytePlusCount; // Isolate 2-byte starts by subtracting 3-byte and 4-byte starts.



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

                            Vector256<byte> isSecondByte = Avx2.SubtractSaturate(prev3, secondByte);
                            Vector256<byte> isThirdByte = Avx2.SubtractSaturate(prev2, thirdByte);
                            
                            Vector256<byte> isThirdByteAdjustment = Avx2.SubtractSaturate(prev3, thirdByte);

                            Vector256<byte> isFourthByte = Avx2.SubtractSaturate(prev3, fourthByte);

                            uint twoBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isSecondByte));
                            uint threeBytePlusCount = Popcnt.PopCount((uint)Avx2.MoveMask(isThirdByteAdjustment));
                            uint fourByteCount = Popcnt.PopCount((uint)Avx2.MoveMask(isFourthByte));

                            uint threeByteCount = threeBytePlusCount - fourByteCount; // Isolate 3-byte starts by subtracting 4-byte starts.
                            uint twoByteCount = twoBytePlusCount - threeBytePlusCount; // Isolate 2-byte starts by subtracting 3-byte and 4-byte starts.

                            Vector256<byte> must23 = Avx2.Or(isThirdByte, isFourthByte);
                            Vector256<byte> must23As80 = Avx2.And(must23, v80);
                            Vector256<byte> error = Avx2.Xor(must23As80, sc);
                            if (!Avx2.TestZ(error, error)) //context: we are dealing with a 32 bit 
                            {
                                Console.WriteLine("-----Error path!!");
                                TailScalarCodeUnitCountAdjustment =0;
                                TailUtf16CodeUnitCountAdjustment =0;
                                
                            // TODO     :I cant remember why I pu an off that does the same thing here but look intit
                                // int off = processedLength >= 32 ? processedLength: processedLength;
                                // byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off, ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);
                                byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(3, pInputBuffer + processedLength, inputLength - processedLength, ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);

                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment +TailUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;


                                Console.WriteLine("--------");
                                Console.WriteLine("TempUTF16 after error rewind:"+ utf16CodeUnitCountAdjustment);
                                Console.WriteLine("TempScalar '' '' '':"+ scalarCountAdjustment);

                                return invalidBytePointer;
                            }
                            // Adjustments
                            TempUtf16CodeUnitCountAdjustment -= (int)fourByteCount * 2; 
                            TempUtf16CodeUnitCountAdjustment -= (int)twoByteCount; 
                            TempUtf16CodeUnitCountAdjustment -= (int)threeByteCount *2; 
                            TempScalarCountAdjustment -= (int)fourByteCount; 

                            Console.WriteLine("Doublecount(Temp) after SIMD processing:" + TempUtf16CodeUnitCountAdjustment);
                            Console.WriteLine("Scalarcount after SIMD processing:" + TempScalarCountAdjustment);


                            prevIncomplete = Avx2.SubtractSaturate(currentBlock, maxValue);

                            if (!Avx2.TestZ(prevIncomplete, prevIncomplete))
                            {
                                // We have an unterminated sequence.
                                Console.WriteLine("---Unterminated seq--- at " + processedLength + "bytes");
                                processedLength -= 3;

                                // int incompleteScalarCodeUnitCountAdjustmentPreventDoubleCounting = 0;
                                // int incompleteUtf16CodeUnitPreventDoubleCounting = 0;

                                // SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, 3,out incompleteUtf16CodeUnitPreventDoubleCounting,out incompleteScalarCodeUnitCountAdjustmentPreventDoubleCounting);

                                // incompleteScalarCodeUnitCountAdjustmentPreventDoubleCounting *= -1;
                                // incompleteUtf16CodeUnitPreventDoubleCounting *= -1;
                                
                                // TempUtf16CodeUnitCountAdjustment+= incompleteUtf16CodeUnitPreventDoubleCounting;
                                // TempScalarCountAdjustment+= incompleteScalarCodeUnitCountAdjustmentPreventDoubleCounting;



                                // Console.WriteLine("incomplete utf16 count", incompleteUtf16CodeUnitPreventDoubleCounting);
                                int backedup= 0;

                                int currentByte = pInputBuffer[processedLength];
                                Console.WriteLine("CurrentByte:" + Convert.ToString(currentByte, 2).PadLeft(8, '0'));

                                for(int k = 0; k < 3; k++)
                                {
                                    int candidateByte = pInputBuffer[processedLength + k];
                                    Console.WriteLine("Backing up " + k +" bytes");
                                    Console.WriteLine("CurrentByte after backing up:" + Convert.ToString(candidateByte, 2).PadLeft(8, '0'));

                                    backedup = 3-k +1;
                                    // TODO: 
                                    // the weird + 1 is so I dont have to put an else to the conditional below
                                    // less readable, there might be  a more elegant way to rewrite it but I am taking the path of convenience for now

                                    if ((candidateByte & 0b11000000) == 0b11000000)
                                    {
                                        // Whatever you do, do not delete this
                                        processedLength += k;
                                        break;
                                    }
                                }

                                Console.WriteLine("Backed up " + backedup +" bytes");

                                for(int k = backedup; k < 3 ; k++)
                                {
                                    int candidateByte = pInputBuffer[processedLength - k];
                                    if ((candidateByte & 0b11000000) == 0b11000000)
                                    {
                                        // TODO:The SIMD code backs up by at most 3, but recall that the count is now done on prev3
                                        // This has the advantage that we KNOW that the bytes here are valid instead of having to take into account the error 
                                        // if (k != 0)
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
                                                Console.WriteLine("Found 4-byte");
                                                TempUtf16CodeUnitCountAdjustment += 2;
                                                TempScalarCountAdjustment += 1;
                                            }
                                            // break;

                                        }
                                    }
                                }

                                
                                Console.WriteLine("TempUTF16:"+ TempUtf16CodeUnitCountAdjustment);
                                Console.WriteLine("TempScalar:"+ TempScalarCountAdjustment);
                                Console.WriteLine("-----------------");
                            }
                        }
                    }
                }
            }

            Console.WriteLine("-Done with SIMD part!");

            // We have processed all the blocks using SIMD, we need to process the remaining bytes.
            // Process the remaining bytes with the scalar function
            // worst possible case is 4 bytes, where we need to backtrack 3 bytes
            // 11110xxxx 10xxxxxx 10xxxxxx 10xxxxxx <== we might be pointing at the last byte
            if (processedLength < inputLength)
            {

                Console.WriteLine("----Process remaining Scalar");
                Console.WriteLine("processed length before:" + processedLength);
                int overlapCount = 0;

                // // We need to possibly backtrack to the start of the last code point
                while (processedLength > 0 && (sbyte)pInputBuffer[processedLength] <= -65)
                {
                    processedLength -= 1;
                    overlapCount +=1;
                }                
                
                Console.WriteLine("processed length after backtrack:" + processedLength);


                // TOCHECK:See if rewind is better here
                // for(int k = 0; k < overlapCount; k++)
                // {
                // // There is no error here hence the loop is straigthforward and we avoid double counting every byte                     
                //     int candidateByte = pInputBuffer[processedLength + k];
                //     if ((candidateByte & 0b11000000) == 0b11000000)
                //     {
                //         if ((candidateByte & 0b11100000) == 0b11000000) // Start of a 2-byte sequence
                //         {
                //             TempUtf16CodeUnitCountAdjustment += 1; 
                //         }
                //         if ((candidateByte & 0b11110000) == 0b11100000) // Start of a 3-byte sequence
                //         {
                //             TempUtf16CodeUnitCountAdjustment += 2; 
                //         }
                //         if ((candidateByte & 0b11111000) == 0b11110000) // Start of a 4-byte sequence
                //         {
                //             TempUtf16CodeUnitCountAdjustment += 2;
                //             TempScalarCountAdjustment += 1;
                //         }
                //         break;
                //     }
                // }

                Console.WriteLine("TempUTF16 before tail remaining check:"+ TempUtf16CodeUnitCountAdjustment);
                Console.WriteLine("TempScalar '' '' '':"+ TempScalarCountAdjustment);


                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                // byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(3,pInputBuffer + processedLength, inputLength - processedLength,ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment + TailUtf16CodeUnitCountAdjustment;
                    scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;

                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }

                Console.WriteLine("TempUTF16 after tail remaining check:"+ TempUtf16CodeUnitCountAdjustment);
                Console.WriteLine("TempScalar '' '' '':"+ TempScalarCountAdjustment);

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
