using System;

// Ideally, we would want to implement something that looks like
// https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding?view=net-7.0
//
// See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Text/UTF8Encoding.cs
namespace SimdUnicode
{
    public static class UTF8
    {
        public unsafe static byte* GetPointerToFirstInvalidByte(byte* pInputBuffer, int inputLength)
        {

            int pos = 0;
            int next_pos;
            uint code_point = 0;
            while (pos < inputLength)
            {
                // If the next  16 bytes are ascii, we can skip them.
                next_pos = pos + 16;
                if (next_pos <= inputLength)
                { // if it is safe to read 16 more bytes, check that they are ascii
                    ulong v1 = *(ulong*)pInputBuffer;
                    ulong v2 = *(ulong*)(pInputBuffer + 8);
                    ulong v = v1 | v2;

                    if ((v & 0x8080808080808080) == 0)
                    {
                        pos = next_pos;
                        continue;
                    }

                }
                byte first_byte = pInputBuffer[pos];
                while (first_byte < 0b10000000)
                {
                    if (++pos == inputLength) { return pInputBuffer + inputLength; }
                    first_byte = pInputBuffer[pos];
                }

                if ((first_byte & 0b11100000) == 0b11000000)
                {
                    next_pos = pos + 2;
                    if (next_pos > inputLength) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    // range check
                    code_point = (uint)(first_byte & 0b00011111) << 6 | (uint)(pInputBuffer[pos + 1] & 0b00111111); 
                    if ((code_point < 0x80) || (0x7ff < code_point)) { return pInputBuffer + pos; } // Overlong
                }
                else if ((first_byte & 0b11110000) == 0b11100000)
                {
                    next_pos = pos + 3;
                    if (next_pos > inputLength) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; } // Too short
                    // range check
                    code_point = (uint)(first_byte & 0b00001111) << 12 |
                                 (uint)(pInputBuffer[pos + 1] & 0b00111111) << 6 |
                                 (uint)(pInputBuffer[pos + 2] & 0b00111111);
                    // Either overlong or too large:
                    if ((code_point < 0x800) || (0xffff < code_point) ||
                        (0xd7ff < code_point && code_point < 0xe000))
                    {
                        return pInputBuffer + pos;
                    }
                }
                else if ((first_byte & 0b11111000) == 0b11110000)
                { // 0b11110000
                    next_pos = pos + 4;
                    if (next_pos > inputLength) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 1] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 2] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    if ((pInputBuffer[pos + 3] & 0b11000000) != 0b10000000) { return pInputBuffer + pos; }
                    // range check
                    code_point =
                        (uint)(first_byte & 0b00000111) << 18 | (uint)(pInputBuffer[pos + 1] & 0b00111111) << 12 |
                        (uint)(pInputBuffer[pos + 2] & 0b00111111) << 6 | (uint)(pInputBuffer[pos + 3] & 0b00111111);
                    if (code_point <= 0xffff || 0x10ffff < code_point) { return pInputBuffer + pos; }
                }
                else
                {
                    // we may have a continuation
                    return pInputBuffer + pos;
                }
                pos = next_pos;
            }
            return pInputBuffer + inputLength;
        }
    }
}