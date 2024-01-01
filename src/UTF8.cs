using System;

// Ideally, we would want to implement something that looks like
// https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding?view=net-7.0
//
// See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Text/UTF8Encoding.cs
namespace SimdUnicode
{
    public static class UTF8
    {
        
        //         |                             Method |               FileName |    N |      Mean |    Error |   StdDev |    Median |
        // |----------------------------------- |----------------------- |----- |----------:|---------:|---------:|----------:|
        // |  SimDUnicodeUtf8ValidationRealData |   data/arabic.utf8.txt |  100 | 443.13 us | 2.262 us | 2.005 us | 442.34 us |
        // |  CompetitionUtf8ValidationRealData |   data/arabic.utf8.txt |  100 | 253.63 us | 0.708 us | 0.591 us | 253.87 us |
        // |      ScalarUtf8ValidationErrorData |   data/arabic.utf8.txt |  100 | 443.36 us | 4.688 us | 4.385 us | 441.68 us |
        // | CompetitionUtf8ValidationErrorData |   data/arabic.utf8.txt |  100 | 260.56 us | 4.439 us | 4.152 us | 260.63 us |
        // |  SimDUnicodeUtf8ValidationRealData |   data/arabic.utf8.txt | 8000 | 458.02 us | 8.067 us | 7.923 us | 457.11 us |
        // |  CompetitionUtf8ValidationRealData |   data/arabic.utf8.txt | 8000 | 258.85 us | 1.542 us | 1.287 us | 259.10 us |
        // |      ScalarUtf8ValidationErrorData |   data/arabic.utf8.txt | 8000 | 446.58 us | 3.803 us | 2.969 us | 446.82 us |
        // | CompetitionUtf8ValidationErrorData |   data/arabic.utf8.txt | 8000 | 263.95 us | 1.851 us | 1.732 us | 264.42 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/chinese.utf8.txt |  100 | 117.03 us | 1.605 us | 1.423 us | 116.98 us |
        // |  CompetitionUtf8ValidationRealData |  data/chinese.utf8.txt |  100 |  42.87 us | 0.887 us | 2.601 us |  42.90 us |
        // |      ScalarUtf8ValidationErrorData |  data/chinese.utf8.txt |  100 | 114.73 us | 0.935 us | 0.875 us | 114.58 us |
        // | CompetitionUtf8ValidationErrorData |  data/chinese.utf8.txt |  100 |  42.40 us | 0.923 us | 2.720 us |  42.45 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/chinese.utf8.txt | 8000 | 114.89 us | 1.475 us | 1.307 us | 114.87 us |
        // |  CompetitionUtf8ValidationRealData |  data/chinese.utf8.txt | 8000 |  41.35 us | 0.861 us | 2.538 us |  41.37 us |
        // |      ScalarUtf8ValidationErrorData |  data/chinese.utf8.txt | 8000 | 115.67 us | 1.261 us | 1.179 us | 115.33 us |
        // | CompetitionUtf8ValidationErrorData |  data/chinese.utf8.txt | 8000 |  40.94 us | 0.925 us | 2.728 us |  40.25 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/english.utf8.txt |  100 |  70.52 us | 0.438 us | 0.410 us |  70.68 us |
        // |  CompetitionUtf8ValidationRealData |  data/english.utf8.txt |  100 |  56.48 us | 1.008 us | 0.943 us |  56.29 us |
        // |      ScalarUtf8ValidationErrorData |  data/english.utf8.txt |  100 |  70.73 us | 0.567 us | 0.530 us |  70.82 us |
        // | CompetitionUtf8ValidationErrorData |  data/english.utf8.txt |  100 |  54.61 us | 0.551 us | 0.460 us |  54.67 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/english.utf8.txt | 8000 |  70.17 us | 0.136 us | 0.127 us |  70.18 us |
        // |  CompetitionUtf8ValidationRealData |  data/english.utf8.txt | 8000 |  55.72 us | 0.450 us | 0.399 us |  55.79 us |
        // |      ScalarUtf8ValidationErrorData |  data/english.utf8.txt | 8000 |  70.11 us | 0.445 us | 0.417 us |  69.96 us |
        // | CompetitionUtf8ValidationErrorData |  data/english.utf8.txt | 8000 |  56.88 us | 0.658 us | 0.616 us |  56.99 us |
        // |  SimDUnicodeUtf8ValidationRealData |   data/french.utf8.txt |  100 | 156.65 us | 1.576 us | 1.474 us | 156.17 us |
        // |  CompetitionUtf8ValidationRealData |   data/french.utf8.txt |  100 | 170.88 us | 0.920 us | 0.816 us | 170.91 us |
        // |      ScalarUtf8ValidationErrorData |   data/french.utf8.txt |  100 | 159.67 us | 2.057 us | 1.925 us | 159.29 us |
        // | CompetitionUtf8ValidationErrorData |   data/french.utf8.txt |  100 | 171.70 us | 0.434 us | 0.406 us | 171.64 us |
        // |  SimDUnicodeUtf8ValidationRealData |   data/french.utf8.txt | 8000 | 157.04 us | 2.294 us | 2.034 us | 156.19 us |
        // |  CompetitionUtf8ValidationRealData |   data/french.utf8.txt | 8000 | 169.17 us | 0.912 us | 0.853 us | 168.88 us |
        // |      ScalarUtf8ValidationErrorData |   data/french.utf8.txt | 8000 | 161.08 us | 1.114 us | 0.988 us | 161.24 us |
        // | CompetitionUtf8ValidationErrorData |   data/french.utf8.txt | 8000 | 172.82 us | 1.335 us | 1.042 us | 172.79 us |
        // |  SimDUnicodeUtf8ValidationRealData |   data/german.utf8.txt |  100 |  53.99 us | 1.050 us | 1.031 us |  53.85 us |
        // |  CompetitionUtf8ValidationRealData |   data/german.utf8.txt |  100 |  32.52 us | 0.647 us | 0.605 us |  32.33 us |
        // |      ScalarUtf8ValidationErrorData |   data/german.utf8.txt |  100 |  54.06 us | 1.011 us | 0.946 us |  53.89 us |
        // | CompetitionUtf8ValidationErrorData |   data/german.utf8.txt |  100 |  31.88 us | 0.550 us | 0.459 us |  31.88 us |
        // |  SimDUnicodeUtf8ValidationRealData |   data/german.utf8.txt | 8000 |  54.14 us | 1.065 us | 1.140 us |  54.22 us |
        // |  CompetitionUtf8ValidationRealData |   data/german.utf8.txt | 8000 |  32.55 us | 0.205 us | 0.192 us |  32.61 us |
        // |      ScalarUtf8ValidationErrorData |   data/german.utf8.txt | 8000 |  54.15 us | 0.336 us | 0.314 us |  54.16 us |
        // | CompetitionUtf8ValidationErrorData |   data/german.utf8.txt | 8000 |  30.86 us | 0.166 us | 0.130 us |  30.84 us |
        // |  SimDUnicodeUtf8ValidationRealData | data/japanese.utf8.txt |  100 | 108.53 us | 1.117 us | 0.990 us | 108.31 us |
        // |  CompetitionUtf8ValidationRealData | data/japanese.utf8.txt |  100 |  36.15 us | 0.841 us | 2.481 us |  35.94 us |
        // |      ScalarUtf8ValidationErrorData | data/japanese.utf8.txt |  100 | 109.98 us | 1.486 us | 1.241 us | 109.67 us |
        // | CompetitionUtf8ValidationErrorData | data/japanese.utf8.txt |  100 |  38.03 us | 1.288 us | 3.797 us |  37.92 us |
        // |  SimDUnicodeUtf8ValidationRealData | data/japanese.utf8.txt | 8000 | 118.41 us | 2.359 us | 3.383 us | 118.22 us |
        // |  CompetitionUtf8ValidationRealData | data/japanese.utf8.txt | 8000 |  36.51 us | 0.942 us | 2.776 us |  35.62 us |
        // |      ScalarUtf8ValidationErrorData | data/japanese.utf8.txt | 8000 | 112.78 us | 0.836 us | 0.741 us | 112.55 us |
        // | CompetitionUtf8ValidationErrorData | data/japanese.utf8.txt | 8000 |  36.27 us | 0.887 us | 2.601 us |  35.68 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/turkish.utf8.txt |  100 | 102.64 us | 2.021 us | 2.406 us | 102.38 us |
        // |  CompetitionUtf8ValidationRealData |  data/turkish.utf8.txt |  100 |  66.69 us | 1.302 us | 1.278 us |  66.99 us |
        // |      ScalarUtf8ValidationErrorData |  data/turkish.utf8.txt |  100 | 105.90 us | 2.071 us | 3.787 us | 105.64 us |
        // | CompetitionUtf8ValidationErrorData |  data/turkish.utf8.txt |  100 |  70.23 us | 1.398 us | 2.336 us |  69.86 us |
        // |  SimDUnicodeUtf8ValidationRealData |  data/turkish.utf8.txt | 8000 | 100.58 us | 1.493 us | 1.397 us | 100.20 us |
        // |  CompetitionUtf8ValidationRealData |  data/turkish.utf8.txt | 8000 |  68.05 us | 1.314 us | 1.290 us |  68.21 us |
        // |      ScalarUtf8ValidationErrorData |  data/turkish.utf8.txt | 8000 |  99.07 us | 1.146 us | 1.016 us |  99.03 us |
        // | CompetitionUtf8ValidationErrorData |  data/turkish.utf8.txt | 8000 |  64.67 us | 0.981 us | 0.917 us |  64.80 us |
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