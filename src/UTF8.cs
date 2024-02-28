using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.CompilerServices;

namespace SimdUnicode
{
    public static class UTF8
    {

        public unsafe static byte* RewindAndValidateWithErrors(int priorBytes, byte* buf, int len)
        {
            int howFarBack = priorBytes;
            int extraLen = 0;
            bool foundLeadingBytes = false;
            for (int i = 0; i <= howFarBack; i++)
            {
                byte b = buf[0 - i];
                foundLeadingBytes = ((b & 0b11000000) != 0b10000000);
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


            // Now buf points to the start of a UTF-8 sequence or the start of the buffer.
            // Validate from this new start point with the adjusted length.
            byte* invalidByte = GetPointerToFirstInvalidByteScalar(buf, len + extraLen);

            return invalidByte;
        }

        public unsafe static byte* GetPointerToFirstInvalidByteScalar(byte* pInputBuffer, int inputLength)
        {

            int pos = 0;
            int nextPos;
            uint codePoint = 0;
            while (pos < inputLength)
            {
                // If the next  16 bytes are ascii, we can skip them.
                nextPos = pos + 16;
                if (nextPos <= inputLength)
                { // if it is safe to read 16 more bytes, check that they are ascii
                    ulong v1 = *(ulong*)pInputBuffer;
                    ulong v2 = *(ulong*)(pInputBuffer + 8);
                    ulong v = v1 | v2;

                    if ((v & 0x8080808080808080) == 0)
                    {
                        pos = nextPos;
                        continue;
                    }

                }
                byte firstByte = pInputBuffer[pos];
                while (firstByte < 0b10000000)
                {
                    if (++pos == inputLength) { return pInputBuffer + inputLength; }
                    firstByte = pInputBuffer[pos];
                }

                if ((firstByte & 0b11100000) == 0b11000000)
                {
                    nextPos = pos + 2;
                    if (nextPos > inputLength) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00011111) << 6 | (uint)(pInputBuffer[pos + 1] & 0b00111111);
                    if ((codePoint < 0x80) || (0x7ff < codePoint)) { return pInputBuffer + pos; } // Overlong
                }
                else if ((firstByte & 0b11110000) == 0b11100000)
                {
                    nextPos = pos + 3;
                    if (nextPos > inputLength) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00001111) << 12 |
                                 (uint)(pInputBuffer[pos + 1] & 0b00111111) << 6 |
                                 (uint)(pInputBuffer[pos + 2] & 0b00111111);
                    // Either overlong or too large:
                    if ((codePoint < 0x800) || (0xffff < codePoint) ||
                        (0xd7ff < codePoint && codePoint < 0xe000))
                    {
                        return pInputBuffer + pos;
                    }
                }
                else if ((firstByte & 0b11111000) == 0b11110000)
                { // 0b11110000
                    nextPos = pos + 4;
                    if (nextPos > inputLength) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 3] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    // range check
                    codePoint =
                        (uint)(firstByte & 0b00000111) << 18 | (uint)(pInputBuffer[pos + 1] & 0b00111111) << 12 |
                        (uint)(pInputBuffer[pos + 2] & 0b00111111) << 6 | (uint)(pInputBuffer[pos + 3] & 0b00111111);
                    if (codePoint <= 0xffff || 0x10ffff < codePoint) { return pInputBuffer + pos; }
                }
                else
                {
                    // we may have a continuation
                    return pInputBuffer + pos;
                }
                pos = nextPos;
            }
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

        public unsafe static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)
        {
            int processedLength = 0;

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
                            if (Avx2.MoveMask(prevIncomplete) != 0)
                            {
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength);
                            }
                            prevIncomplete = Vector256<byte>.Zero;
                        }
                        else
                        {
                            // Contains non-ASCII characters, we need to do non-trivial processing
                            Vector256<byte> shuffled = Avx2.Permute2x128(prevInputBlock, currentBlock, 0x21);
                            prevInputBlock = currentBlock;
                            Vector256<byte> prev1 = Avx2.AlignRight(prevInputBlock, shuffled, (byte)(16 - 1));
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
                            if (Avx2.MoveMask(error) != 0)
                            {
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(processedLength, pInputBuffer + processedLength, inputLength - processedLength);
                            }
                            prevIncomplete = Avx2.SubtractSaturate(currentBlock, maxValue);
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
                byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
            }

            return pInputBuffer + inputLength;
        }
    }


}
