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

// |                      Method |               FileName |      Mean |     Error |    StdDev | Allocated |
// |---------------------------- |----------------------- |----------:|----------:|----------:|----------:|
// |  SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 31.509 us | 0.2234 us | 0.2089 us |         - |
// | SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 28.280 us | 0.2042 us | 0.1810 us |         - |
// |  SIMDUtf8ValidationRealData |  data/chinese.utf8.txt |  6.682 us | 0.0400 us | 0.0354 us |         - |
// | SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt |  6.750 us | 0.1294 us | 0.1080 us |         - |
// |  SIMDUtf8ValidationRealData |  data/english.utf8.txt |  9.291 us | 0.0345 us | 0.0323 us |         - |
// | SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  9.483 us | 0.0486 us | 0.0454 us |         - |
// |  SIMDUtf8ValidationRealData |   data/french.utf8.txt | 19.547 us | 0.3349 us | 0.3132 us |         - |
// | SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 18.264 us | 0.2890 us | 0.2703 us |         - |
// |  SIMDUtf8ValidationRealData |   data/german.utf8.txt |  4.972 us | 0.0402 us | 0.0357 us |         - |
// | SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  4.936 us | 0.0468 us | 0.0438 us |         - |
// |  SIMDUtf8ValidationRealData | data/japanese.utf8.txt |  6.039 us | 0.0680 us | 0.0636 us |         - |
// | SIMDUtf8ValidationErrorData | data/japanese.utf8.txt |  5.683 us | 0.0970 us | 0.0907 us |         - |
// |  SIMDUtf8ValidationRealData |  data/turkish.utf8.txt |  6.054 us | 0.1161 us | 0.1627 us |         - |
// | SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt |  5.909 us | 0.0483 us | 0.0452 us |         - |
            // scalar results:
        // if (processedLength < inputLength)
        // {
        //     byte* invalidBytePointer = UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength, inputLength - processedLength);
        //     // This makes little difference
        //     if (invalidBytePointer != pInputBuffer + inputLength)
        //     {
        //         // An invalid byte was found. Adjust error handling as needed.
        //         error = Vector256.Create((byte)1);
        //     }
        //     processedLength += (int)(invalidBytePointer - (pInputBuffer + processedLength));
        // }


// ThreadStaticAttribute approach is buggy
        // if (processedLength < inputLength)
        // {

        //     // int mask = Avx2.MoveMask(prev_incomplete.AsSByte());
        //     // int index = BitOperations.TrailingZeroCount(mask);


        //     // byte* invalidBytePointer = UTF8.RewindAndValidateWithErrors(pInputBuffer + processedLength, inputLength - processedLength);
        //     // // This makes little difference
        //     // if (invalidBytePointer != pInputBuffer + inputLength)
        //     // {
        //     //     // An invalid byte was found. Adjust error handling as needed.
        //     //     error = Vector256.Create((byte)1);
        //     // }

        //         // Find the position of the first set bit in incompleteMask, indicating the start of an incomplete sequence.
        //     int incompleteMask = Avx2.MoveMask(prev_incomplete.AsSByte());
        //     int firstIncompletePos = BitOperations.LeadingZeroCount((uint)incompleteMask);

        //     // Calculate the pointer adjustment based on the position of the incomplete sequence.
        //     byte* startPtrForScalarValidation = pInputBuffer + processedLength + firstIncompletePos;

        //     // Ensure startPtrForScalarValidation does not precede pInputBuffer.
        //     // startPtrForScalarValidation = Math.Max(pInputBuffer, startPtrForScalarValidation);

        //     // Now, ensure startPtrForScalarValidation points to a leading byte by backtracking if it's pointing to a continuation byte.
        //     // while (startPtrForScalarValidation > pInputBuffer && (*startPtrForScalarValidation & 0xC0) == 0x80) {
        //     //     startPtrForScalarValidation--;
        //     // }

        //     // Invoke scalar validation from the identified leading byte position.
        //     byte* invalidBytePointer = UTF8.GetPointerToFirstInvalidByte(startPtrForScalarValidation, inputLength - (int)(startPtrForScalarValidation - pInputBuffer));
        //     if (invalidBytePointer != pInputBuffer + inputLength)
        //     {
        //         // An invalid byte was found. Adjust error handling as needed.
        //         error = Vector256.Create((byte)1);
        //     }
        //     processedLength += (int)(invalidBytePointer - (pInputBuffer + processedLength));
        // }



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


            if (processedLength < inputLength)
            {

                Span<byte> remainingBytes = stackalloc byte[32];
                for (int i = 0; i < inputLength - processedLength; i++)
                {
                    remainingBytes[i] = pInputBuffer[processedLength + i];
                }

                ReadOnlySpan<Byte> remainingBytesReadOnly = remainingBytes;
                Vector256<byte> remainingBlock = Vector256.Create(remainingBytesReadOnly);
                Utf8Validation.utf8_checker.CheckNextInput(remainingBlock, ref prev_input_block, ref prev_incomplete, ref error);
                processedLength += inputLength - processedLength;

            }




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
