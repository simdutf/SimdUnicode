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

        static Func<byte, string> byteToBinaryString = b => Convert.ToString(b, 2).PadLeft(8, '0');//for debugging

        public unsafe static byte* RewindAndValidateWithErrors(int howFarBack, byte* buf, int len,ref int utf16CodeUnitCountAdjustment, ref int scalarCountAdjustment)
        {
            // Console.WriteLine("--Rewind Validate with Errors");
            // Console.WriteLine("current Byte:" + Convert.ToString(buf[0], 2).PadLeft(8, '0'));

            int TempUtf16CodeUnitCountAdjustment = 0;
            int TempScalarCountAdjustment = 0;

            int extraLen = 0;
            bool foundLeadingBytes = false;

            for (int i = 0; i <= howFarBack; i++)
            {
                // Console.WriteLine("Activiting main backup:" + i);
                byte candidateByte = buf[0 - i];
                foundLeadingBytes = (candidateByte & 0b11000000) != 0b10000000;
                if (foundLeadingBytes)
                {         
                    buf -= i;
                    // extraLen = i; // a measure of how far we've backed up, only useful for debugging
                    // Console.WriteLine(howFarBack);
                    // Console.WriteLine("Found leading byte at:" + i + ",Byte:" + Convert.ToString(candidateByte, 2).PadLeft(8, '0'));

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

            byte* invalidBytePointer = GetPointerToFirstInvalidByteScalar(buf, len + extraLen,out TailUtf16CodeUnitCountAdjustment, out TailScalarCountAdjustment);

            utf16CodeUnitCountAdjustment += TailUtf16CodeUnitCountAdjustment;
            scalarCountAdjustment += TailScalarCountAdjustment;

            // Console.WriteLine("rewind utf16 Doublecount adjustment(Temp):" + TempUtf16CodeUnitCountAdjustment);
            // Console.WriteLine("scalarcount adjstment after rewind:" + TempScalarCountAdjustment);
            // Console.WriteLine(" ");
            // Console.WriteLine("rewinds utf16 count(done by GetPointerToFirstInvalidByteScalar):" + TailUtf16CodeUnitCountAdjustment);
            // Console.WriteLine("scalarcount after rewind(ditto):" + TailScalarCountAdjustment);

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

        // Assuming that a valid UTF-8 sequence ends at pInputBuffer,
        // computes how many bytes are needed (eg what type of byte) to complete the last character. also counts the number of n4, n2 and ascii affected
        // This will return 1, 2, 3. If the whole byte sequence is valid UTF-8,
        // and this function returns returnedvalue>0, then the bytes at pInputBuffer[0], 
        // ... pInputBuffer[returnedvalue - 1] should be continuation bytes.
        // Note that this function is unsafe, and it is the caller's responsibility
        // to ensure that we can read at least 4 bytes before pInputBuffer.
        // (Nick Nuon added 7th may) there is an addenum labeled important in the mock PR however I think we can treat unterminated as 
        public unsafe static (int totalbyteadjustment,int backedupByHowMuch,int ascii,int n2,int n4) adjustmentFactor(byte* pInputBuffer) {
            // Find the first non-continuation byte, working backward.
            int i = 1;
            for (; i <= 4; i++)
            {
                if ((pInputBuffer[-i] & 0b11000000) != 0b10000000)
                {
                    break;
                }
            }
            if ((pInputBuffer[-i] & 0b10000000) == 0) {
                return (0,i,-1,0,0); // We must have that i == 1
            }
            if ((pInputBuffer[-i] & 0b11100000) == 0b11000000) {
                return (2 - i,i,0,-1,0); // We have that i == 1 or i == 2, if i == 1, we are missing one byte.
            }
            if ((pInputBuffer[-i] & 0b11110000) == 0b11100000) {
                return (3 - i,i,0,0,0); // We have that i == 1 or i == 2 or i == 3, if i == 1, we are missing two bytes, if i == 2, we are missing one byte.
            }
            // We must have that (pInputBuffer[-i] & 0b11111000) == 0b11110000
            return (4 - i,i,0,0,-1); // We have that i == 1 or i == 2 or i == 3 or i == 4, if i == 1, we are missing three bytes, if i == 2, we are missing two bytes, if i == 3, we are missing one byte.
        }

        public static (int utfadjust, int scalaradjust) CalculateN2N3FinalSIMDAdjustments(int asciibytes, int n4, int contbytes, int totalbyte)
        {
            // Calculate n3 based on the provided formula
            int n3 = asciibytes - 2 * n4 + 2 * contbytes - totalbyte;

            // Calculate n2 based on the provided formula
            int n2 = -2 * asciibytes + n4 - 3 * contbytes + 2 * totalbyte;

            // Calculate utfadjust by adding them all up
            int utfadjust = -2 * n4 - 2 * n3 - n2;

            // Calculate scalaradjust based on n4
            int scalaradjust = -n4;

            // Return the calculated utfadjust and scalaradjust
            return (utfadjust, scalaradjust);
        }



        

        public unsafe static (int utfadjust, int scalaradjust) calculateErrorPathadjust(int start_point, int processedLength, byte* pInputBuffer, int asciibytes, int n4, int n2, int contbytes)
        {
            // Calculate the total bytes from start_point to processedLength
            int totalbyte = processedLength - start_point;
            int adjusttotalbyte = 0, backedupByHowMuch = 0, adjustascii = 0, adjustn2 = 0, adjustn4 = 0;

            // Adjust the length to include a complete character, if necessary
            if (totalbyte > 0)
            {
                (adjusttotalbyte, backedupByHowMuch ,adjustascii, adjustn2, adjustn4) = adjustmentFactor(pInputBuffer + processedLength);
            }

            var (utfadjust,scalaradjust) = CalculateN2N3FinalSIMDAdjustments( asciibytes + adjustascii, n4 + adjustn4, contbytes + adjustn2, totalbyte + adjusttotalbyte);

            // Return the calculated n2 and n3
            return (utfadjust, scalaradjust);
        }


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
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (Sse2.MoveMask(prevIncomplete) != 0)
                            {
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
            // Console.WriteLine("Length: " + inputLength);
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
                // Console.WriteLine("asciirun bytes: ", asciirun); // debugging
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
                    /****
                    * So we want to count the number of 4-byte sequences,
                    * the number of 4-byte sequences, 3-byte sequences, and
                    * the number of 2-byte sequences.
                    * We can do it indirectly. We know how many bytes in total
                    * we have (length). Let us assume that the length covers
                    * only complete sequences (we need to adjust otherwise).
                    * We have that
                    *   length = 4 * n4 + 3 * n3 + 2 * n2 + n1
                    * where n1 is the number of 1-byte sequences (ASCII),
                    * n2 is the number of 2-byte sequences, n3 is the number
                    * of 3-byte sequences, and n4 is the number of 4-byte sequences.
                    *
                    * Let ncon be the number of continuation bytes, then we have
                    *  length =  n4 + n3 + n2 + ncon + n1
                    *
                    * We can solve for n2 and n3 in terms of the other variables:
                    * n3 = n1 - 2 * n4 + 2 * ncon - length
                    * n2 = -2 * n1 + n4 - 4 * ncon + 2 * length
                    * Thus we only need to count the number of continuation bytes,
                    * the number of ASCII bytes and the number of 4-byte sequences.
                    */
                    ////////////
                    // The *block* here is what begins at processedLength and ends
                    // at processedLength/16*16 or when an error occurs.
                    ///////////
                    int start_point = processedLength;
                    
                    // The block goes from processedLength to processedLength/16*16.
                    int asciibytes = 0; // number of ascii bytes in the block (could also be called n1)
                    int contbytes = 0; // number of continuation bytes in the block
                    int n4 = 0; // number of 4-byte sequences that start in this block
                    // int totalbyte = 0, n3 = 0, n2 = 0;



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
                            // TODO? : this path iss not explicitly tested
                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment;

                                int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                return SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + off, inputLength - off, ref utf16CodeUnitCountAdjustment,ref scalarCountAdjustment);
                            }
                            prevIncomplete = Vector256<byte>.Zero;
                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                            Console.WriteLine("--Found non-ascii:triggering SIMD routine at " + processedLength + "bytes"); //debug
                            // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.
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
                            if (!Avx2.TestZ(error, error))
                            {
                                Console.WriteLine("-----Error path!!");
                                TailScalarCodeUnitCountAdjustment =0;
                                TailUtf16CodeUnitCountAdjustment =0;

                                int off = processedLength >= 32 ? processedLength - 32 : processedLength;
                                byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(off, pInputBuffer + processedLength, inputLength - processedLength, ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);

                                utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment +TailUtf16CodeUnitCountAdjustment;
                                scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;

                                // Console.WriteLine("--------"); //debug
                                // Console.WriteLine("TempUTF16 after error rewind:"+ utf16CodeUnitCountAdjustment);
                                // Console.WriteLine("TempScalar '' '' '':"+ scalarCountAdjustment);

                                return invalidBytePointer;
                            }

                            // Console.WriteLine("Doublecount(Temp) after SIMD processing:" + TempUtf16CodeUnitCountAdjustment); debug
                            // Console.WriteLine("Scalarcount after SIMD processing:" + TempScalarCountAdjustment);
                            prevIncomplete = Avx2.SubtractSaturate(currentBlock, maxValue);

                            if (!Avx2.TestZ(prevIncomplete, prevIncomplete))
                            {
                                // We have an unterminated sequence.
                                Console.WriteLine("---Unterminated seq--- at " + processedLength + "bytes");
                                // processedLength -= 3;

                                // Console.WriteLine("incomplete utf16 count", incompleteUtf16CodeUnitPreventDoubleCounting);
                                // int backedup= 0;

                                // int currentByte = pInputBuffer[processedLength];
                                // Console.WriteLine("CurrentByte:" + Convert.ToString(currentByte, 2).PadLeft(8, '0'));

                                var (totalbyteadjustment, i,tempascii, tempn2, tempn4) = adjustmentFactor(pInputBuffer + processedLength + 32);
                                processedLength -= i;
                                // totalbyte -= totalbyteadjustment;
                                asciibytes +=tempascii;
                                n4 += tempn4;
                                contbytes +=tempn2;

                            //     // Console.WriteLine("Backed up " + backedup +" bytes");
                            //     // Console.WriteLine("TempUTF16:"+ TempUtf16CodeUnitCountAdjustment);
                            //     // Console.WriteLine("TempScalar:"+ TempScalarCountAdjustment);
                            //     // Console.WriteLine("-----------------");

                            }

                                                        // We use one instruction (MoveMask) to update ncon, plus one arithmetic operation.
                            contbytes += Avx2.MoveMask(sc);

                            // We use two instructions (SubtractSaturate and MoveMask) to update n4, with one arithmetic operation.
                            n4 += Avx2.MoveMask(Avx2.SubtractSaturate(currentBlock, fourthByte));
                        }
                    }
                    // There are 2 possible scenarios here : either  
                    //  A)  it arrives flush en the border. eg it doesnt need to be processed further
                    //  B)  There is some bytes remaining in which case we need to call the scalar functien
                    // Either way we need to calculate n2,n3 and update the utf16adjust and scalar adjust
                    int totalbyte = processedLength - start_point;
                    var (utf16adjust, scalaradjust) = CalculateN2N3FinalSIMDAdjustments( asciibytes,  n4,  contbytes, totalbyte);

                    utf16CodeUnitCountAdjustment = utf16adjust;
                    scalarCountAdjustment = scalaradjust;
                }


            }
            // Console.WriteLine("-Done with SIMD part!"); //debug
            // We have processed all the blocks using SIMD, we need to process the remaining bytes.
            // Process the remaining bytes with the scalar function


            // worst possible case is 4 bytes, where we need to backtrack 3 bytes
            // 11110xxxx 10xxxxxx 10xxxxxx 10xxxxxx <== we might be pointing at the last byte
            if (processedLength < inputLength)
            {
                Console.WriteLine("----Process remaining Scalar @ "  + processedLength + "bytes");
                // int overlapCount = 0;
                // Console.WriteLine("processed length after backtrack:" + processedLength);
                // Console.WriteLine("TempUTF16 before tail remaining check:"+ TempUtf16CodeUnitCountAdjustment);
                // Console.WriteLine("TempScalar '' '' '':"+ TempScalarCountAdjustment);
                byte* invalidBytePointer = SimdUnicode.UTF8.RewindAndValidateWithErrors(32,pInputBuffer + processedLength, inputLength - processedLength,ref TailUtf16CodeUnitCountAdjustment,ref TailScalarCodeUnitCountAdjustment);
                if (invalidBytePointer != pInputBuffer + inputLength)
                {
                    utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment + TailUtf16CodeUnitCountAdjustment;
                    scalarCountAdjustment = TempScalarCountAdjustment + TailScalarCodeUnitCountAdjustment;
                    // An invalid byte was found by the scalar function
                    return invalidBytePointer;
                }
                // Console.WriteLine("TempUTF16 after tail remaining check:"+ TempUtf16CodeUnitCountAdjustment);
                // Console.WriteLine("TempScalar '' '' '':"+ TempScalarCountAdjustment);
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
