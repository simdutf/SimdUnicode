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

        // Returns &inputBuffer[inputLength] if the input buffer is valid.
        /// <summary>
        /// Given an input buffer <paramref name="pInputBuffer"/> of byte length <paramref name="inputLength"/>,
        /// returns a pointer to where the first invalid data appears in <paramref name="pInputBuffer"/>.
        /// The parameter <paramref name="Utf16CodeUnitCountAdjustment"/> is set according to the content of the valid UTF-8 characters encountered, counting -1 for each 2-byte character, -2 for each 3-byte and 4-byte characters.
        /// The parameter <paramref name="ScalarCodeUnitCountAdjustment"/> is set according to the content of the valid UTF-8 characters encountered, counting -1 for each 4-byte character.
        /// </summary>
        /// <remarks>
        /// Returns a pointer to the end of <paramref name="pInputBuffer"/> if the buffer is well-formed.
        /// </remarks>
        public unsafe static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength, out int Utf16CodeUnitCountAdjustment, out int ScalarCodeUnitCountAdjustment)
        {

            if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
            {
                return GetPointerToFirstInvalidByteArm64(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);
            }
            if (Vector512.IsHardwareAccelerated && Avx512Vbmi.IsSupported)
            {
                return GetPointerToFirstInvalidByteAvx512(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);
            }
            if (Avx2.IsSupported)
            {
                return GetPointerToFirstInvalidByteAvx2(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);
            }
            if (Ssse3.IsSupported)
            {
                return GetPointerToFirstInvalidByteSse(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);
            }

            return GetPointerToFirstInvalidByteScalar(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);

        }
        // prevents double counting in case there is a toolong error on the edge
        private static (int utfAdjust, int scalarAdjust) GetFinalScalarUtfAdjustments(byte headerByte)
        {
            // Check if the header byte belongs to a 2-byte UTF-8 character
            if ((headerByte & 0b11100000) == 0b11000000)
            {
                return (1, 0);
            }
            // Check if the header byte belongs to a 3-byte UTF-8 character
            else if ((headerByte & 0b11110000) == 0b11100000)
            {
                return (2, 0);
            }
            // Check if the header byte belongs to a 4-byte UTF-8 character
            else if ((headerByte & 0b11111000) == 0b11110000)
            {
                return (2, 1);
            }
            // Otherwise, it's a 1-byte character or continuation byte
            return (0, 0);
        }

        // We scan the input from buf to len, possibly going back howFarBack bytes, to find the end of
        // a valid UTF-8 sequence. We return buf + len if the buffer is valid, otherwise we return the
        // pointer to the first invalid byte.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static byte* SimpleRewindAndValidateWithErrors(int howFarBack, byte* buf, int len)
        {
            int extraLen = 0;
            bool foundLeadingBytes = false;

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
            int pos = 0;
            int nextPos;
            uint codePoint = 0;

            len += extraLen;

            while (pos < len)
            {

                byte firstByte = buf[pos];

                while (firstByte < 0b10000000)
                {
                    if (++pos == len)
                    {
                        return buf + len;
                    }
                    firstByte = buf[pos];
                }

                if ((firstByte & 0b11100000) == 0b11000000)
                {
                    nextPos = pos + 2;
                    if (nextPos > len)
                    {
                        return buf + pos;
                    } // Too short
                    if ((buf[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00011111) << 6 | (uint)(buf[pos + 1] & 0b00111111);
                    if ((codePoint < 0x80) || (0x7ff < codePoint))
                    {
                        return buf + pos;
                    } // Overlong
                }
                else if ((firstByte & 0b11110000) == 0b11100000)
                {
                    nextPos = pos + 3;
                    if (nextPos > len)
                    {
                        return buf + pos;
                    } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00001111) << 12 |
                                 (uint)(buf[pos + 1] & 0b00111111) << 6 |
                                 (uint)(buf[pos + 2] & 0b00111111);
                    // Either overlong or too large:
                    if ((codePoint < 0x800) || (0xffff < codePoint) ||
                        (0xd7ff < codePoint && codePoint < 0xe000))
                    {
                        return buf + pos;
                    }
                    if ((buf[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    } // Too short
                    if ((buf[pos + 2] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    } // Too short
                }
                else if ((firstByte & 0b11111000) == 0b11110000)
                {
                    nextPos = pos + 4;
                    if (nextPos > len)
                    {
                        return buf + pos;
                    }
                    if ((buf[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    }
                    if ((buf[pos + 2] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    }
                    if ((buf[pos + 3] & 0b11000000) != 0b10000000)
                    {
                        return buf + pos;
                    }
                    // range check
                    codePoint =
                        (uint)(firstByte & 0b00000111) << 18 | (uint)(buf[pos + 1] & 0b00111111) << 12 |
                        (uint)(buf[pos + 2] & 0b00111111) << 6 | (uint)(buf[pos + 3] & 0b00111111);
                    if (codePoint <= 0xffff || 0x10ffff < codePoint)
                    {
                        return buf + pos;
                    }
                }
                else
                {
                    // we may have a continuation/too long error
                    return buf + pos;
                }
                pos = nextPos;
            }

            return buf + len; // no error
        }


        public unsafe static byte* GetPointerToFirstInvalidByteScalar(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {

            int TempUtf16CodeUnitCountAdjustment = 0;
            int TempScalarCountAdjustment = 0;

            int pos = 0;
            int nextPos;
            uint codePoint = 0;

            while (pos < inputLength)
            {

                byte firstByte = pInputBuffer[pos];
                while (firstByte < 0b10000000)
                {
                    if (++pos == inputLength)
                    {

                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + inputLength;
                    }
                    firstByte = pInputBuffer[pos];
                }

                if ((firstByte & 0b11100000) == 0b11000000)
                {
                    nextPos = pos + 2;
                    if (nextPos > inputLength)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Too short
                    // range check
                    codePoint = (uint)(firstByte & 0b00011111) << 6 | (uint)(pInputBuffer[pos + 1] & 0b00111111);
                    if ((codePoint < 0x80) || (0x7ff < codePoint))
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Overlong
                    TempUtf16CodeUnitCountAdjustment -= 1;
                }
                else if ((firstByte & 0b11110000) == 0b11100000)
                {
                    nextPos = pos + 3;
                    if (nextPos > inputLength)
                    {

                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Too short
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
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Too short
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    } // Too short
                    TempUtf16CodeUnitCountAdjustment -= 2;
                }
                else if ((firstByte & 0b11111000) == 0b11110000)
                {
                    nextPos = pos + 4;
                    if (nextPos > inputLength)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment; return pInputBuffer + pos;
                    }
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    }
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    }
                    if ((pInputBuffer[pos + 3] & 0b11000000) != 0b10000000)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    }
                    // range check
                    codePoint =
                        (uint)(firstByte & 0b00000111) << 18 | (uint)(pInputBuffer[pos + 1] & 0b00111111) << 12 |
                        (uint)(pInputBuffer[pos + 2] & 0b00111111) << 6 | (uint)(pInputBuffer[pos + 3] & 0b00111111);
                    if (codePoint <= 0xffff || 0x10ffff < codePoint)
                    {
                        utf16CodeUnitCountAdjustment = TempUtf16CodeUnitCountAdjustment;
                        scalarCountAdjustment = TempScalarCountAdjustment;
                        return pInputBuffer + pos;
                    }
                    TempUtf16CodeUnitCountAdjustment -= 2;
                    TempScalarCountAdjustment -= 1;
                }
                else
                {
                    // we may have a continuation/too long error
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
        // computes how many bytes are needed  to complete the last character. also counts the number of n4, n2 and ascii affected
        // This will return 1, 2, 3. If the whole byte sequence is valid UTF-8,
        // and this function returns returnedvalue>0, then the bytes at pInputBuffer[0], 
        // ... pInputBuffer[returnedvalue - 1] should be continuation bytes.
        // Note that this function is unsafe, and it is the caller's responsibility
        // to ensure that we can read at least 4 bytes before pInputBuffer.
        private unsafe static (int totalbyteadjustment, int backedupByHowMuch, int ascii, int contbyte, int n4) adjustmentFactor(byte* pInputBuffer)
        {
            // Find the first non-continuation byte, working backward.
            int i = 1;
            int contbyteadjust = 0;
            for (; i <= 4; i++)
            {
                if ((pInputBuffer[-i] & 0b11000000) != 0b10000000)
                {
                    break;
                }
                contbyteadjust -= 1;
            }
            if ((pInputBuffer[-i] & 0b10000000) == 0)
            {
                return (0, i, -1, contbyteadjust, 0); // We must have that i == 1
            }
            if ((pInputBuffer[-i] & 0b11100000) == 0b11000000)
            {
                return (2 - i, i, 0, contbyteadjust, 0); // We have that i == 1 or i == 2, if i == 1, we are missing one byte.
            }
            if ((pInputBuffer[-i] & 0b11110000) == 0b11100000)
            {
                return (3 - i, i, 0, contbyteadjust, 0); // We have that i == 1 or i == 2 or i == 3, if i == 1, we are missing two bytes, if i == 2, we are missing one byte.
            }
            // We must have that (pInputBuffer[-i] & 0b11111000) == 0b11110000
            return (4 - i, i, 0, contbyteadjust, -1); // We have that i == 1 or i == 2 or i == 3 or i == 4, if i == 1, we are missing three bytes, if i == 2, we are missing two bytes, if i == 3, we are missing one byte.
        }

        private static (int utfadjust, int scalaradjust) CalculateN2N3FinalSIMDAdjustments(int n4, int contbytes)
        {
            int n3 = -2 * n4 + 2 * contbytes;
            int n2 = n4 - 3 * contbytes;
            int utfadjust = -2 * n4 - 2 * n3 - n2;
            int scalaradjust = -n4;

            return (utfadjust, scalaradjust);
        }

        private unsafe static (int utfadjust, int scalaradjust) calculateErrorPathadjust(int start_point, int processedLength, byte* pInputBuffer, int n4, int contbytes)
        {
            // Calculate the total bytes from start_point to processedLength
            int totalbyte = processedLength - start_point;
            int adjusttotalbyte = 0, backedupByHowMuch = 0, adjustascii = 0, adjustcont = 0, adjustn4 = 0;

            // Adjust the length to include a complete character, if necessary
            if (totalbyte > 0)
            {
                (adjusttotalbyte, backedupByHowMuch, adjustascii, adjustcont, adjustn4) = adjustmentFactor(pInputBuffer + processedLength);
            }
            var (utfadjust, scalaradjust) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
            return (utfadjust, scalaradjust);
        }

        public unsafe static byte* GetPointerToFirstInvalidByteSse(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            int processedLength = 0;
            if (pInputBuffer == null || inputLength <= 0)
            {
                utf16CodeUnitCountAdjustment = 0;
                scalarCountAdjustment = 0;
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for (; asciirun + 64 <= inputLength; asciirun += 64)
                {
                    Vector128<byte> block1 = Sse2.LoadVector128(pInputBuffer + asciirun);
                    Vector128<byte> block2 = Sse2.LoadVector128(pInputBuffer + asciirun + 16);
                    Vector128<byte> block3 = Sse2.LoadVector128(pInputBuffer + asciirun + 32);
                    Vector128<byte> block4 = Sse2.LoadVector128(pInputBuffer + asciirun + 48);

                    Vector128<byte> or = Sse2.Or(Sse2.Or(block1, block2), Sse2.Or(block3, block4));
                    if (Sse2.MoveMask(or) != 0)
                    {
                        break;
                    }
                }
                processedLength = asciirun;

                if (processedLength + 16 < inputLength)
                {
                    Vector128<byte> prevInputBlock = Vector128<byte>.Zero;

                    Vector128<byte> maxValue = Vector128.Create(
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                    Vector128<byte> prevIncomplete = Sse3.SubtractSaturate(prevInputBlock, maxValue);

                    Vector128<byte> shuf1 = Vector128.Create(
                            TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
                            TWO_CONTS, TWO_CONTS, TWO_CONTS, TWO_CONTS,
                            TOO_SHORT | OVERLONG_2,
                            TOO_SHORT,
                            TOO_SHORT | OVERLONG_3 | SURROGATE,
                            TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4);

                    Vector128<byte> shuf2 = Vector128.Create(
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
                    Vector128<byte> shuf3 = Vector128.Create(
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
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
                    * But we need even less because we compute
                    * utfadjust = -2 * n4 - 2 * n3 - n2
                    * so n1 and length cancel out in the end. Thus we only need to compute
                    * n3' =  - 2 * n4 + 2 * ncon
                    * n2' = n4 - 4 * ncon
                    */
                    ////////////
                    // The *block* here is what begins at processedLength and ends
                    // at processedLength/16*16 or when an error occurs.
                    ///////////
                    int start_point = processedLength;

                    // The block goes from processedLength to processedLength/16*16.
                    int contbytes = 0; // number of continuation bytes in the block
                    int n4 = 0; // number of 4-byte sequences that start in this block        
                    for (; processedLength + 16 <= inputLength; processedLength += 16)
                    {

                        Vector128<byte> currentBlock = Sse2.LoadVector128(pInputBuffer + processedLength);
                        int mask = Sse42.MoveMask(currentBlock);
                        if (mask == 0)
                        {
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            // 

                            if (!Sse41.TestZ(prevIncomplete, prevIncomplete))
                            {
                                int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                byte* invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(16 - 3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                // So the code is correct up to invalidBytePointer
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int totalbyteasciierror = processedLength - start_point;
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }
                            prevIncomplete = Vector128<byte>.Zero;

                            // Often, we have a lot of ASCII characters in a row.
                            int localasciirun = 16;
                            if (processedLength + localasciirun + 64 <= inputLength)
                            {
                                for (; processedLength + localasciirun + 64 <= inputLength; localasciirun += 64)
                                {
                                    Vector128<byte> block1 = Sse2.LoadVector128(pInputBuffer + processedLength + localasciirun);
                                    Vector128<byte> block2 = Sse2.LoadVector128(pInputBuffer + processedLength + localasciirun + 16);
                                    Vector128<byte> block3 = Sse2.LoadVector128(pInputBuffer + processedLength + localasciirun + 32);
                                    Vector128<byte> block4 = Sse2.LoadVector128(pInputBuffer + processedLength + localasciirun + 48);

                                    Vector128<byte> or = Sse2.Or(Sse2.Or(block1, block2), Sse2.Or(block3, block4));
                                    if (Sse2.MoveMask(or) != 0)
                                    {
                                        break;
                                    }
                                }
                                processedLength += localasciirun - 16;
                            }
                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                            // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.
                            // Contains non-ASCII characters, we need to do non-trivial processing
                            Vector128<byte> prev1 = Ssse3.AlignRight(currentBlock, prevInputBlock, (byte)(16 - 1));
                            Vector128<byte> byte_1_high = Ssse3.Shuffle(shuf1, Sse2.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> byte_1_low = Ssse3.Shuffle(shuf2, (prev1 & v0f));
                            Vector128<byte> byte_2_high = Ssse3.Shuffle(shuf3, Sse2.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> sc = Sse2.And(Sse2.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector128<byte> prev2 = Ssse3.AlignRight(currentBlock, prevInputBlock, (byte)(16 - 2));
                            Vector128<byte> prev3 = Ssse3.AlignRight(currentBlock, prevInputBlock, (byte)(16 - 3));
                            prevInputBlock = currentBlock;

                            Vector128<byte> isThirdByte = Sse2.SubtractSaturate(prev2, thirdByte);
                            Vector128<byte> isFourthByte = Sse2.SubtractSaturate(prev3, fourthByte);
                            Vector128<byte> must23 = Sse2.Or(isThirdByte, isFourthByte);
                            Vector128<byte> must23As80 = Sse2.And(must23, v80);
                            Vector128<byte> error = Sse2.Xor(must23As80, sc);

                            if (!Sse42.TestZ(error, error))
                            {

                                byte* invalidBytePointer;
                                if (processedLength == 0)
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                                }
                                else
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                }
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }

                            prevIncomplete = Sse3.SubtractSaturate(currentBlock, maxValue);

                            contbytes += (int)Popcnt.PopCount((uint)Sse42.MoveMask(byte_2_high));
                            // We use two instructions (SubtractSaturate and MoveMask) to update n4, with one arithmetic operation.
                            n4 += (int)Popcnt.PopCount((uint)Sse42.MoveMask(Sse42.SubtractSaturate(currentBlock, fourthByte)));
                        }

                    }


                    // We may still have an error.
                    bool hasIncompete = !Sse42.TestZ(prevIncomplete, prevIncomplete);
                    if (processedLength < inputLength || hasIncompete)
                    {
                        byte* invalidBytePointer;
                        if (processedLength == 0 || !hasIncompete)
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                        }
                        else
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);

                        }
                        if (invalidBytePointer != pInputBuffer + inputLength)
                        {
                            if (invalidBytePointer < pInputBuffer + processedLength)
                            {
                                removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                            }
                            else
                            {
                                addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                            }
                            int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                            (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                            return invalidBytePointer;
                        }
                        else
                        {
                            addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                        }
                    }
                    int final_total_bytes_processed = inputLength - start_point;
                    (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                    return pInputBuffer + inputLength;
                }
            }
            return GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
        }

        public unsafe static byte* GetPointerToFirstInvalidByteAvx2(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            int processedLength = 0;
            if (pInputBuffer == null || inputLength <= 0)
            {
                utf16CodeUnitCountAdjustment = 0;
                scalarCountAdjustment = 0;
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for (; asciirun + 64 <= inputLength; asciirun += 64)
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
                    * But we need even less because we compute
                    * utfadjust = -2 * n4 - 2 * n3 - n2
                    * so n1 and length cancel out in the end. Thus we only need to compute
                    * n3' =  - 2 * n4 + 2 * ncon
                    * n2' = n4 - 4 * ncon
                    */
                    ////////////
                    // The *block* here is what begins at processedLength and ends
                    // at processedLength/16*16 or when an error occurs.
                    ///////////
                    int start_point = processedLength;

                    // The block goes from processedLength to processedLength/16*16.
                    int contbytes = 0; // number of continuation bytes in the block
                    int n4 = 0; // number of 4-byte sequences that start in this block        
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
                                int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                byte* invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(32 - 3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                // So the code is correct up to invalidBytePointer
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int totalbyteasciierror = processedLength - start_point;
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }
                            prevIncomplete = Vector256<byte>.Zero;

                            // Often, we have a lot of ASCII characters in a row.
                            int localasciirun = 32;
                            if (processedLength + localasciirun + 64 <= inputLength)
                            {
                                for (; processedLength + localasciirun + 64 <= inputLength; localasciirun += 64)
                                {
                                    Vector256<byte> block1 = Avx.LoadVector256(pInputBuffer + processedLength + localasciirun);
                                    Vector256<byte> block2 = Avx.LoadVector256(pInputBuffer + processedLength + localasciirun + 32);
                                    Vector256<byte> or = Avx2.Or(block1, block2);
                                    if (Avx2.MoveMask(or) != 0)
                                    {
                                        break;
                                    }
                                }
                                processedLength += localasciirun - 32;
                            }

                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                            // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.
                            Vector256<byte> shuffled = Avx2.Permute2x128(prevInputBlock, currentBlock, 0x21);
                            prevInputBlock = currentBlock;
                            Vector256<byte> prev1 = Avx2.AlignRight(prevInputBlock, shuffled, (byte)(16 - 1));
                            // Vector256.Shuffle vs Avx2.Shuffle
                            // https://github.com/dotnet/runtime/blob/1400c1e7a888ea1e710e5c08d55c800e0b04bf8a/docs/coding-guidelines/vectorization-guidelines.md#vector256shuffle-vs-avx2shuffle
                            Vector256<byte> byte_1_high = Avx2.Shuffle(shuf1, Avx2.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);// takes the XXXX 0000 part of the previous byte
                            Vector256<byte> byte_1_low = Avx2.Shuffle(shuf2, (prev1 & v0f)); // takes the 0000 XXXX part of the previous part
                            Vector256<byte> byte_2_high = Avx2.Shuffle(shuf3, Avx2.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f); // takes the XXXX 0000 part of the current byte
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
                                byte* invalidBytePointer;
                                if (processedLength == 0)
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                                }
                                else
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                }
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }

                            prevIncomplete = Avx2.SubtractSaturate(currentBlock, maxValue);
                            contbytes += (int)Popcnt.PopCount((uint)Avx2.MoveMask(byte_2_high));
                            // We use two instructions (SubtractSaturate and MoveMask) to update n4, with one arithmetic operation.
                            n4 += (int)Popcnt.PopCount((uint)Avx2.MoveMask(Avx2.SubtractSaturate(currentBlock, fourthByte)));
                        }
                    }
                    // We may still have an error.
                    bool hasIncompete = !Avx2.TestZ(prevIncomplete, prevIncomplete);
                    if (processedLength < inputLength || hasIncompete)
                    {
                        byte* invalidBytePointer;
                        if (processedLength == 0 || !hasIncompete)
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                        }
                        else
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                        }
                        if (invalidBytePointer != pInputBuffer + inputLength)
                        {
                            if (invalidBytePointer < pInputBuffer + processedLength)
                            {
                                removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                            }
                            else
                            {
                                addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                            }
                            int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                            (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                            return invalidBytePointer;
                        }
                        else
                        {
                            addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                        }
                    }
                    int final_total_bytes_processed = inputLength - start_point;
                    (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                    return pInputBuffer + inputLength;
                }
            }
            return GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
        }

        public unsafe static byte* GetPointerToFirstInvalidByteAvx512(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            int processedLength = 0;
            if (pInputBuffer == null || inputLength <= 0)
            {
                utf16CodeUnitCountAdjustment = 0;
                scalarCountAdjustment = 0;
                return pInputBuffer;
            }

            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                // We intentionally use AVX2 instead of AVX-512.
                int asciirun = 0;
                for (; asciirun + 64 <= inputLength; asciirun += 64)
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

                if (processedLength + 64 < inputLength)
                {

                    Vector512<byte> prevInputBlock = Vector512<byte>.Zero;

                    Vector512<byte> maxValue = Vector512.Create(
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 255, 255, 255,
                            255, 255, 255, 255, 255, 0b11110000 - 1, 0b11100000 - 1, 0b11000000 - 1);
                    Vector512<byte> prevIncomplete = Avx512BW.SubtractSaturate(prevInputBlock, maxValue);


                    Vector512<byte> shuf1 = Vector512.Create(TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
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
                            TOO_SHORT | TOO_LARGE | TOO_LARGE_1000 | OVERLONG_4,
                            TOO_LONG, TOO_LONG, TOO_LONG, TOO_LONG,
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

                    Vector512<byte> shuf2 = Vector512.Create(
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
                    Vector512<byte> shuf3 = Vector512.Create(TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
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
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
                            TOO_SHORT, TOO_SHORT, TOO_SHORT, TOO_SHORT,
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

                    Vector512<byte> thirdByte = Vector512.Create((byte)(0b11100000u - 0x80));
                    Vector512<byte> fourthByte = Vector512.Create((byte)(0b11110000u - 0x80));
                    Vector512<byte> v0f = Vector512.Create((byte)0x0F);
                    Vector512<byte> v80 = Vector512.Create((byte)0x80);
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
                    * But we need even less because we compute
                    * utfadjust = -2 * n4 - 2 * n3 - n2
                    * so n1 and length cancel out in the end. Thus we only need to compute
                    * n3' =  - 2 * n4 + 2 * ncon
                    * n2' = n4 - 4 * ncon
                    */
                    ////////////
                    // The *block* here is what begins at processedLength and ends
                    // at processedLength/16*16 or when an error occurs.
                    ///////////
                    int start_point = processedLength;

                    // The block goes from processedLength to processedLength/16*16.
                    int contbytes = 0; // number of continuation bytes in the block
                    int n4 = 0; // number of 4-byte sequences that start in this block        
                    for (; processedLength + 64 <= inputLength; processedLength += 64)
                    {

                        Vector512<byte> currentBlock = Avx512F.LoadVector512(pInputBuffer + processedLength);
                        ulong mask = currentBlock.ExtractMostSignificantBits();
                        if (mask == 0)
                        {
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (Avx512BW.CompareGreaterThan(prevIncomplete, Vector512<byte>.Zero).ExtractMostSignificantBits() != 0)
                            {
                                int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                byte* invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(16 - 3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                // So the code is correct up to invalidBytePointer
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int totalbyteasciierror = processedLength - start_point;
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }
                            prevIncomplete = Vector512<byte>.Zero;

                            // Often, we have a lot of ASCII characters in a row.
                            int localasciirun = 64;
                            if (processedLength + localasciirun + 64 <= inputLength)
                            {
                                for (; processedLength + localasciirun + 64 <= inputLength; localasciirun += 64)
                                {
                                    Vector512<byte> block = Avx512F.LoadVector512(pInputBuffer + processedLength + localasciirun);
                                    if (block.ExtractMostSignificantBits() != 0)
                                    {
                                        break;
                                    }
                                }
                                processedLength += localasciirun - 64;
                            }
                        }
                        else // Contains non-ASCII characters, we need to do non-trivial processing
                        {
                            // Use SubtractSaturate to effectively compare if bytes in block are greater than markers.
                            Vector512<int> movemask = Vector512.Create(28, 29, 30, 31, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                            Vector512<byte> shuffled = Avx512F.PermuteVar16x32x2(currentBlock.AsInt32(), movemask, prevInputBlock.AsInt32()).AsByte();
                            prevInputBlock = currentBlock;

                            Vector512<byte> prev1 = Avx512BW.AlignRight(prevInputBlock, shuffled, (byte)(16 - 1));
                            Vector512<byte> byte_1_high = Avx512BW.Shuffle(shuf1, Avx512BW.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);// takes the XXXX 0000 part of the previous byte
                            Vector512<byte> byte_1_low = Avx512BW.Shuffle(shuf2, (prev1 & v0f)); // takes the 0000 XXXX part of the previous part
                            Vector512<byte> byte_2_high = Avx512BW.Shuffle(shuf3, Avx512BW.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f); // takes the XXXX 0000 part of the current byte
                            Vector512<byte> sc = Avx512F.And(Avx512F.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector512<byte> prev2 = Avx512BW.AlignRight(prevInputBlock, shuffled, (byte)(16 - 2));
                            Vector512<byte> prev3 = Avx512BW.AlignRight(prevInputBlock, shuffled, (byte)(16 - 3));
                            Vector512<byte> isThirdByte = Avx512BW.SubtractSaturate(prev2, thirdByte);
                            Vector512<byte> isFourthByte = Avx512BW.SubtractSaturate(prev3, fourthByte);
                            Vector512<byte> must23 = Avx512F.Or(isThirdByte, isFourthByte);
                            Vector512<byte> must23As80 = Avx512F.And(must23, v80);
                            Vector512<byte> error = Avx512F.Xor(must23As80, sc);

                            if (Avx512BW.CompareGreaterThan(error, Vector512<byte>.Zero).ExtractMostSignificantBits() != 0)
                            {
                                byte* invalidBytePointer;
                                if (processedLength == 0)
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                                }
                                else
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                }
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }

                            prevIncomplete = Avx512BW.SubtractSaturate(currentBlock, maxValue);
                            contbytes += (int)Popcnt.X64.PopCount(byte_2_high.ExtractMostSignificantBits());
                            // We use two instructions (SubtractSaturate and ExtractMostSignificantBits) to update n4, with one arithmetic operation.
                            n4 += (int)Popcnt.X64.PopCount(Avx512BW.SubtractSaturate(currentBlock, fourthByte).ExtractMostSignificantBits());
                        }
                    }
                    // We may still have an error.
                    bool hasIncompete = Avx512BW.CompareGreaterThan(prevIncomplete, Vector512<byte>.Zero).ExtractMostSignificantBits() != 0;
                    if (processedLength < inputLength || hasIncompete)
                    {
                        byte* invalidBytePointer;
                        if (processedLength == 0 || !hasIncompete)
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                        }
                        else
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);

                        }
                        if (invalidBytePointer != pInputBuffer + inputLength)
                        {
                            if (invalidBytePointer < pInputBuffer + processedLength)
                            {
                                removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                            }
                            else
                            {
                                addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                            }
                            int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                            (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                            return invalidBytePointer;
                        }
                        else
                        {
                            addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                        }
                    }
                    int final_total_bytes_processed = inputLength - start_point;
                    (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                    return pInputBuffer + inputLength;
                }
            }
            return GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
        }

        public unsafe static byte* GetPointerToFirstInvalidByteArm64(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment)
        {
            int processedLength = 0;
            if (pInputBuffer == null || inputLength <= 0)
            {
                utf16CodeUnitCountAdjustment = 0;
                scalarCountAdjustment = 0;
                return pInputBuffer;
            }
            if (inputLength > 128)
            {
                // We skip any ASCII characters at the start of the buffer
                int asciirun = 0;
                for (; asciirun + 64 <= inputLength; asciirun += 64)
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
                    Vector128<byte> fourthByteMinusOne = Vector128.Create((byte)(0b11110000u - 1));
                    Vector128<sbyte> largestcont = Vector128.Create((sbyte)-65); // -65 => 0b10111111
                    // Performance note: we could process 64 bytes at a time for better speed in some cases.
                    int start_point = processedLength;

                    // The block goes from processedLength to processedLength/16*16.
                    int contbytes = 0; // number of continuation bytes in the block
                    int n4 = 0; // number of 4-byte sequences that start in this block
                    for (; processedLength + 16 <= inputLength; processedLength += 16)
                    {

                        Vector128<byte> currentBlock = AdvSimd.LoadVector128(pInputBuffer + processedLength);
                        if (AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(AdvSimd.And(currentBlock, v80))).ToScalar() == 0)
                        // We could also use (AdvSimd.Arm64.MaxAcross(currentBlock).ToScalar() <= 127) but it is slower on some
                        // hardware.
                        {
                            // We have an ASCII block, no need to process it, but
                            // we need to check if the previous block was incomplete.
                            if (AdvSimd.Arm64.MaxAcross(prevIncomplete).ToScalar() != 0)
                            {
                                int off = processedLength >= 3 ? processedLength - 3 : processedLength;
                                byte* invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(16 - 3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                // So the code is correct up to invalidBytePointer
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int totalbyteasciierror = processedLength - start_point;
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }
                            prevIncomplete = Vector128<byte>.Zero;
                            // Often, we have a lot of ASCII characters in a row.
                            int localasciirun = 16;
                            if (processedLength + localasciirun + 16 <= inputLength)
                            {
                                Vector128<byte> block = AdvSimd.LoadVector128(pInputBuffer + processedLength + localasciirun);
                                if (AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(AdvSimd.And(block, v80))).ToScalar() == 0)
                                {
                                    localasciirun += 16;
                                    for (; processedLength + localasciirun + 64 <= inputLength; localasciirun += 64)
                                    {
                                        Vector128<byte> block1 = AdvSimd.LoadVector128(pInputBuffer + processedLength + localasciirun);
                                        Vector128<byte> block2 = AdvSimd.LoadVector128(pInputBuffer + processedLength + localasciirun + 16);
                                        Vector128<byte> block3 = AdvSimd.LoadVector128(pInputBuffer + processedLength + localasciirun + 32);
                                        Vector128<byte> block4 = AdvSimd.LoadVector128(pInputBuffer + processedLength + localasciirun + 48);
                                        Vector128<byte> or = AdvSimd.Or(AdvSimd.Or(block1, block2), AdvSimd.Or(block3, block4));

                                        if (AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(AdvSimd.And(or, v80))).ToScalar() != 0)
                                        {
                                            break;
                                        }
                                    }

                                }

                                processedLength += localasciirun - 16;
                            }
                        }
                        else
                        {
                            // Contains non-ASCII characters, we need to do non-trivial processing
                            Vector128<byte> prev1 = AdvSimd.ExtractVector128(prevInputBlock, currentBlock, (byte)(16 - 1));
                            // Vector128.Shuffle vs AdvSimd.Arm64.VectorTableLookup: prefer the latter!!!
                            Vector128<byte> byte_1_high = AdvSimd.Arm64.VectorTableLookup(shuf1, AdvSimd.ShiftRightLogical(prev1.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> byte_1_low = AdvSimd.Arm64.VectorTableLookup(shuf2, (prev1 & v0f));
                            Vector128<byte> byte_2_high = AdvSimd.Arm64.VectorTableLookup(shuf3, AdvSimd.ShiftRightLogical(currentBlock.AsUInt16(), 4).AsByte() & v0f);
                            Vector128<byte> sc = AdvSimd.And(AdvSimd.And(byte_1_high, byte_1_low), byte_2_high);
                            Vector128<byte> prev2 = AdvSimd.ExtractVector128(prevInputBlock, currentBlock, (byte)(16 - 2));
                            Vector128<byte> prev3 = AdvSimd.ExtractVector128(prevInputBlock, currentBlock, (byte)(16 - 3));
                            prevInputBlock = currentBlock;
                            Vector128<byte> isThirdByte = AdvSimd.SubtractSaturate(prev2, thirdByte);
                            Vector128<byte> isFourthByte = AdvSimd.SubtractSaturate(prev3, fourthByte);
                            Vector128<byte> must23 = AdvSimd.Or(isThirdByte, isFourthByte);
                            Vector128<byte> must23As80 = AdvSimd.And(must23, v80);
                            Vector128<byte> error = AdvSimd.Xor(must23As80, sc);
                            // AdvSimd.Arm64.MaxAcross(error) works, but it might be slower
                            // than AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(error)) on some
                            // hardware:
                            if (AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(error)).ToScalar() != 0)
                            {
                                byte* invalidBytePointer;
                                if (processedLength == 0)
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                                }
                                else
                                {
                                    invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                                }
                                if (invalidBytePointer < pInputBuffer + processedLength)
                                {
                                    removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                                }
                                else
                                {
                                    addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                                }
                                int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                                (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                                return invalidBytePointer;
                            }
                            prevIncomplete = AdvSimd.SubtractSaturate(currentBlock, maxValue);
                            contbytes += -AdvSimd.Arm64.AddAcross(AdvSimd.CompareLessThanOrEqual(Vector128.AsSByte(currentBlock), largestcont)).ToScalar();
                            Vector128<byte> largerthan0f = AdvSimd.CompareGreaterThan(currentBlock, fourthByteMinusOne);
                            ulong n4marker = AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(largerthan0f)).ToScalar();
                            if (n4marker != 0)
                            {
                                byte n4add = (byte)AdvSimd.Arm64.AddAcross(largerthan0f).ToScalar();
                                int negn4add = (int)(byte)-n4add;
                                n4 += negn4add;
                            }
                        }
                    }
                    bool hasIncompete = AdvSimd.Arm64.MaxAcross(Vector128.AsUInt32(prevIncomplete)).ToScalar() != 0;
                    if (processedLength < inputLength || hasIncompete)
                    {
                        byte* invalidBytePointer;
                        if (processedLength == 0 || !hasIncompete)
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(0, pInputBuffer + processedLength, inputLength - processedLength);
                        }
                        else
                        {
                            invalidBytePointer = SimdUnicode.UTF8.SimpleRewindAndValidateWithErrors(3, pInputBuffer + processedLength - 3, inputLength - processedLength + 3);
                        }
                        if (invalidBytePointer != pInputBuffer + inputLength)
                        {
                            if (invalidBytePointer < pInputBuffer + processedLength)
                            {
                                removeCounters(invalidBytePointer, pInputBuffer + processedLength, ref n4, ref contbytes);
                            }
                            else
                            {
                                addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                            }
                            int total_bytes_processed = (int)(invalidBytePointer - (pInputBuffer + start_point));
                            (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                            return invalidBytePointer;
                        }
                        else
                        {
                            addCounters(pInputBuffer + processedLength, invalidBytePointer, ref n4, ref contbytes);
                        }
                    }
                    int final_total_bytes_processed = inputLength - start_point;
                    (utf16CodeUnitCountAdjustment, scalarCountAdjustment) = CalculateN2N3FinalSIMDAdjustments(n4, contbytes);
                    return pInputBuffer + inputLength;
                }
            }
            return GetPointerToFirstInvalidByteScalar(pInputBuffer + processedLength, inputLength - processedLength, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void removeCounters(byte* start, byte* end, ref int n4, ref int contbytes)
        {
            for (byte* p = start; p < end; p++)
            {
                if ((*p & 0b11000000) == 0b10000000)
                {
                    contbytes -= 1;
                }
                if ((*p & 0b11110000) == 0b11110000)
                {
                    n4 -= 1;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void addCounters(byte* start, byte* end, ref int n4, ref int contbytes)
        {
            for (byte* p = start; p < end; p++)
            {
                if ((*p & 0b11000000) == 0b10000000)
                {
                    contbytes += 1;
                }
                if ((*p & 0b11110000) == 0b11110000)
                {
                    n4 += 1;
                }
            }
        }

    }
}
