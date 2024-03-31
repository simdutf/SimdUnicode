namespace tests;
using System.Text;
using SimdUnicode;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;


public unsafe class Utf8SIMDValidationTests
{


    private const int NumTrials = 1000;
    private static readonly RandomUtf8 generator = new RandomUtf8(1234, 1, 1, 1, 1);
    private static readonly Random rand = new Random();

    // int[] outputLengths = { 128, 192, 256, 320, 384, 448, 512, 576, 640, 704, 768, 832, 896, 960, 1024, 1088, 1152, 1216, 1280, 1344, 1408, 1472, 1536, 1600, 1664, 1728, 1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560, 2624, 2688, 2752, 2816, 2880, 2944, 3008, 3072, 3136, 3200, 3264, 3328, 3392, 3456, 3520, 3584, 3648, 3712, 3776, 3840, 3904, 3968, 4032, 4096, 4160, 4224, 4288, 4352, 4416, 4480, 4544, 4608, 4672, 4736, 4800, 4864, 4928, 4992, 5056, 5120, 5184, 5248, 5312, 5376, 5440, 5504, 5568, 5632, 5696, 5760, 5824, 5888, 5952, 6016, 6080, 6144, 6208, 6272, 6336, 6400, 6464, 6528, 6592, 6656, 6720, 6784, 6848, 6912, 6976, 7040, 7104, 7168, 7232, 7296, 7360, 7424, 7488, 7552, 7616, 7680, 7744, 7808, 7872, 7936, 8000, 8064, 8128, 8192, 8256, 8320, 8384, 8448, 8512, 8576, 8640, 8704, 8768, 8832, 8896, 8960, 9024, 9088, 9152, 9216, 9280, 9344, 9408, 9472, 9536, 9600, 9664, 9728, 9792, 9856, 9920, 9984, 10000 };
    static int[] outputLengths = { 128, 256,345, 512,968, 1024, 1000 }; // Example lengths

    
    public void TestGoodSequences(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        string[] goodSequences = {
        "a",
        "\xC3\xB1",
        "\xE2\x82\xA1",
        "\xF0\x90\x8C\xBC",
        "\xC2\x80",
        "\xF0\x90\x80\x80",
        "\xEE\x80\x80",
        "\xEF\xBB\xBF"
    };

    int SimdUnicodeUtf16Adjustment, SimdUnicodeScalarCountAdjustment;


        foreach (var seq in goodSequences)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(seq);
            unsafe
            {
                fixed (byte* pInput = input)
                {
                    // int TailScalarCodeUnitCountAdjustment = 0;
                    // int TailUtf16CodeUnitCountAdjustment = 0;
                    // byte* scalarResult = DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, input.Length,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                    // Assert.True((IntPtr)(pInput + input.Length) == (IntPtr)scalarResult,
                    //             $"Failure in Scalar function: SimdUnicode.UTF8.GetPointerToFirstInvalidByte.Sequence: {seq}");

                    // byte* SIMDResult = utf8ValidationDelegate(pInput, input.Length);
                    // Assert.True((IntPtr)(pInput + input.Length) == (IntPtr)SIMDResult,
                    //             $"Failure in SIMD function: Utf8Utility.GetPointerToFirstInvalidByte.Sequence: {seq}");
                    ValidateUtf8(input);
                }
            }
        }
    }

    [Fact]
    public void TestBadSequences()
    {
        string[] badSequences = {
        "\xC3\x28",
        "\xA0\xA1",
        "\xE2\x28\xA1",
        "\xE2\x82\x28",
        "\xF0\x28\x8C\xBC",
        "\xF0\x90\x28\xBC",
        "\xF0\x28\x8C\x28",
        "\xC0\x9F",
        "\xF5\xFF\xFF\xFF",
        "\xED\xA0\x81",
        "\xF8\x90\x80\x80\x80",
        "123456789012345\xED",
        "123456789012345\xF1",
        "123456789012345\xC2",
        "\xC2\x7F",
        "\xCE",
        "\xCE\xBA\xE1",
        "\xCE\xBA\xE1\xBD",
        "\xCE\xBA\xE1\xBD\xB9\xCF",
        "\xCE\xBA\xE1\xBD\xB9\xCF\x83\xCE",
        "\xCE\xBA\xE1\xBD\xB9\xCF\x83\xCE\xBC\xCE",
        "\xDF",
        "\xEF\xBF",
        "\x80",
        "\x91\x85\x95\x9E",
        "\x6C\x02\x8E\x18",
        "\x25\x5b\x6e\x2c\x32\x2c\x5b\x5b\x33\x2c\x34\x2c\x05\x29\x2c\x33\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5d\x2c\x35\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x20\x01\x01\x01\x01\x01\x02\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x23\x0a\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x7e\x7e\x0a\x0a\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5d\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x01\x01\x80\x01\x01\x01\x79\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
        "[[[[[[[[[[[[[[[\x80\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x010\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
        "\x20\x0b\x01\x01\x01\x64\x3a\x64\x3a\x64\x3a\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x30\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x80\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
    };

        foreach (var seq in badSequences)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(seq);
            unsafe
            {
                fixed (byte* pInput = input)
                {
                    // int TailScalarCodeUnitCountAdjustment = 0;
                    // int TailUtf16CodeUnitCountAdjustment = 0;
                    // byte* scalarResult = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInput, input.Length,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                    //                     Assert.True((IntPtr)(pInput + input.Length) == (IntPtr)scalarResult,
                    //             $"Failure in Scalar function: SimdUnicode.UTF8.GetPointerToFirstInvalidByte.Sequence: {seq}");

                    // byte* SIMDResult = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, input.Length);
                    // Assert.True((IntPtr)(pInput + input.Length) == (IntPtr)SIMDResult,
                    //             $"Failure in SIMD function: Utf8Utility.GetPointerToFirstInvalidByte.Sequence: {seq}");
                    ValidateUtf8(input);

                }
            }
        }
    }

    [Fact]
    public void Node48995Test()
    {
        byte[] bad = new byte[] { 0x80 };
        Assert.False(ValidateUtf8(bad));
    }

    [Fact]
    public void NoErrorTest()
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                byte[] utf8 = generator.Generate(outputLength).ToArray();
                bool isValidUtf8 = ValidateUtf8(utf8);
                string utf8HexString = BitConverter.ToString(utf8).Replace("-", " ");
                Assert.True(isValidUtf8, $"Failure NoErrorTest. Sequence: {utf8HexString}");
            }
        }
    }

    [Fact]
    public void NoErrorTestASCII()
    {
        RunTestForByteLength(1);
    }

    [Fact]
    public void NoErrorTest1Byte()
    {
        RunTestForByteLength(1);
    }

    [Fact]
    public void NoErrorTest2Bytes()
    {
        RunTestForByteLength(2);
    }

    [Fact]
    public void NoErrorTest3Bytes()
    {
        RunTestForByteLength(3);
    }

    [Fact]
    public void NoErrorTest4Bytes()
    {
        RunTestForByteLength(4);
    }

    private void RunTestForByteLength(int byteLength)
    {
        // int[] outputLengths = { 128, 256, 512, 1024, 1000 }; // Example lengths
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                byte[] utf8 = generator.Generate(outputLength, byteLength).ToArray();
                bool isValidUtf8 = ValidateUtf8(utf8);
                Assert.True(isValidUtf8, $"Failure for {byteLength}-byte UTF8 of length {outputLength} in trial {trial}");
            }
        }
    }

    [Fact]
    public void HeaderBitsErrorTest()
    {
        foreach (int outputLength in outputLengths)
            {
            for (int trial = 0; trial < NumTrials; trial++)
            {

                byte[] utf8 = generator.Generate(outputLength).ToArray();
                for (int i = 0; i < utf8.Length; i++)
                {
                    if ((utf8[i] & 0b11000000) != 0b10000000) // Only process leading bytes
                    {
                        byte oldByte = utf8[i];
                        utf8[i] = 0b11111000; // Forcing a header bits error
                        Assert.False(ValidateUtf8(utf8));
                        Assert.True(InvalidateUtf8(utf8, i));
                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
    }

    [Fact]
    public void TooShortErrorTest()
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
            byte[] utf8 = generator.Generate(outputLength).ToArray();

                for (int i = 0; i < utf8.Length; i++)
                {
                    if ((utf8[i] & 0b11000000) == 0b10000000) // Only process continuation bytes
                    {
                        byte oldByte = utf8[i];
                        utf8[i] = 0b11100000; // Forcing a too short error
                        Assert.False(ValidateUtf8(utf8));
                        Assert.True(InvalidateUtf8(utf8, i));
                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
        
    }

    [Fact]
    public void TooLongErrorTest()
    {

        int[] outputLengths = { 128, 256, 512, 1024 }; // Example lengths

        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                byte[] utf8 = generator.Generate(outputLength).ToArray();

                for (int i = 0; i < utf8.Length; i++)
                {
                    if ((utf8[i] & 0b11000000) != 0b10000000) // Only process leading bytes
                    {
                        byte oldByte = utf8[i];
                        utf8[i] = 0b10000000; // Forcing a too long error
                        Assert.False(ValidateUtf8(utf8));
                        Assert.True(InvalidateUtf8(utf8, i));
                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
    }

    [Fact]
    public void OverlongErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {
            foreach (int outputLength in outputLengths)
            {
                byte[] utf8 = generator.Generate(outputLength).ToArray();


                for (int i = 0; i < utf8.Length; i++)
                {
                    if (utf8[i] >= 0b11000000) // Only non-ASCII leading bytes can be overlong
                    {
                        byte old = utf8[i];
                        byte secondOld = utf8[i + 1];

                        if ((old & 0b11100000) == 0b11000000) // two-bytes case, change to a value less or equal than 0x7f
                        {
                            utf8[i] = 0b11000000;
                        }
                        else if ((old & 0b11110000) == 0b11100000) // three-bytes case, change to a value less or equal than 0x7ff
                        {
                            utf8[i] = 0b11100000;
                            utf8[i + 1] = (byte)(utf8[i + 1] & 0b11011111);
                        }
                        else if ((old & 0b11111000) == 0b11110000) // four-bytes case, change to a value less or equal than 0xffff
                        {
                            utf8[i] = 0b11110000;
                            utf8[i + 1] = (byte)(utf8[i + 1] & 0b11001111);
                        }

                        Assert.False(ValidateUtf8(utf8));
                        Assert.True(InvalidateUtf8(utf8, i));

                        utf8[i] = old;
                        utf8[i + 1] = secondOld;
                    }
                }
            }
        }
    }




    public void TooShortTest(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {
            foreach (int outputLength in outputLengths)
            {
                // List<byte> oneUTFunit = generator.Generate( howManyUnits:1 ,byteCountInUnit: 2);            
                byte[] utf8 = generator.Generate(outputLength,byteCountInUnit: 1).ToArray();        
                
                unsafe
                {
                    fixed (byte* pInput = utf8)
                    {

                        for (int i = 0; i < utf8.Length; i++)
                            {
                            // int DotnetUtf16Adjustment, DotnetScalarCountAdjustment;
                                int SimdUnicodeUtf16Adjustment, SimdUnicodeScalarCountAdjustment;
                                byte currentByte = utf8[i];
                                int offset = 0;

                            if ((currentByte & 0b11100000) == 0b11000000) { // This is a header byte of a 2-byte sequence
                                offset = 0;
                            } 
                            if ((currentByte & 0b11110000) == 0b11100000) {
                                // This is a header byte of a 3-byte sequence
                    
                                offset = rand.Next(0, 3);
                            } 
                            if ((currentByte & 0b11111000) == 0b11110000) {
                                // This is a header byte of a 4-byte sequence
                                offset = rand.Next(0, 4);
                            }

                            byte* ThisResult = utf8ValidationDelegate(pInput, i + offset, out SimdUnicodeUtf16Adjustment, out SimdUnicodeScalarCountAdjustment);
                            Assert.True(ThisResult == pInput + i + offset);

                            byte* dotnetResult = DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, i + offset, out SimdUnicodeUtf16Adjustment, out SimdUnicodeScalarCountAdjustment);
                            Assert.True(dotnetResult == pInput + i + offset);
                            }

                    }    
                }
            }
        }
    }

    [Fact]
    public void TooShortTestAvx2()
    {
        TooShortTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }
    public static IEnumerable<object[]> InvalidTestData()
{

    var invalidBytes = Enumerable.Range(0xF5, 0x100 - 0xF5).Select(i => (byte)i).ToArray(); // 0xF5 to 0xFF
    foreach (var length in outputLengths)
    {
        byte[] utf8 = generator.Generate(length).ToArray();
        for (int position = 0; position < utf8.Length; position++)
        {
            foreach (var invalidByte in invalidBytes)
            {
                yield return new object[] { length, position, invalidByte ,utf8 };
            }
        }
    }
}


    //corresponds to condition 5.4.1 in the paper
    [Theory]
    [MemberData(nameof(InvalidTestData))]
    public void Invalid0xf50xff(int outputLength, int position, byte invalidByte,byte[] utf8)
    {

        // Initialize utf8 with some valid data, if necessary
        // Array.Fill(utf8, (byte)0x20); // Filling with spaces for simplicity

        utf8[position] = invalidByte; // Inject an invalid byte at a random position

        // PrintHexAndBinary(utf8);

        Assert.False(ValidateUtf8(utf8)); // Expect the validation to fail due to the invalid byte
        Assert.True(InvalidateUtf8(utf8,position));
    }


            // Prints both hexadecimal and binary representations of a byte array
    static void PrintHexAndBinary(byte[] bytes)
    {
        // Convert to hexadecimal
        string hexRepresentation = BitConverter.ToString(bytes).Replace("-", " ");
        Console.WriteLine($"Hex: {hexRepresentation}");

        // Convert to binary
        string binaryRepresentation = string.Join(" ", Array.ConvertAll(bytes, byteValue => Convert.ToString(byteValue, 2).PadLeft(8, '0')));
        Console.WriteLine($"Binary: {binaryRepresentation}");
    }


    [Fact]
    public void TooLargeErrorTest()
    {
        foreach (int outputLength in outputLengths)
        {

            for (int trial = 0; trial < NumTrials; trial++)
            {

                byte[] utf8 = generator.Generate(outputLength).ToArray();

                for (int i = 0; i < utf8.Length; i++)
                {
                    if ((utf8[i] & 0b11111000) == 0b11110000) // Only in 4-bytes case
                    {
                        byte old = utf8[i];
                        utf8[i] += (byte)(((utf8[i] & 0b100) == 0b100) ? 0b10 : 0b100);

                        Assert.False(ValidateUtf8(utf8));
                        Assert.True(InvalidateUtf8(utf8, i));
                        utf8[i] = old;
                    }
                }
            }
        }
    }

        [Fact]
    public void TooLargeErrorTestEnd()
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                for (int i = 1; i <= 4; i++)
             {
                byte[] filler = generator.Generate(outputLength,byteCountInUnit:1).ToArray();
                byte[] toolong = generator.AppendContinuationByte(generator.Generate(1,i)).ToArray();

                generator.ReplaceEndOfArray(filler,toolong); 

                Assert.False(ValidateUtf8(filler ));
                Assert.True(InvalidateUtf8(filler, outputLength -1));
             }


            }
        }
    }
    

    [Fact]
    public void SurrogateErrorTest()
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {

                byte[] utf8 = generator.Generate(outputLength).ToArray();

                for (int i = 0; i < utf8.Length; i++)
                {
                    if ((utf8[i] & 0b11110000) == 0b11100000) // Only in 3-bytes case
                    {
                        byte old = utf8[i];
                        byte secondOld = utf8[i + 1];

                        utf8[i] = 0b11101101; // Leading byte for surrogate
                        for (int s = 0x8; s < 0xf; s++)
                        {
                            utf8[i + 1] = (byte)((utf8[i + 1] & 0b11000011) | (s << 2));

                            Assert.False(ValidateUtf8(utf8));
                            Assert.True(InvalidateUtf8(utf8, i));
                        }

                        utf8[i] = old;
                        utf8[i + 1] = secondOld;
                    }
                }
            }
        }
    }

    [Fact]
    public void BruteForceTest()
    {
        foreach (int outputLength in outputLengths)
        {
            for (int i = 0; i < NumTrials; i++)
            {

                // Generate random UTF-8 sequence
                byte[] utf8 = generator.Generate(rand.Next(outputLength)).ToArray();

                Assert.True(ValidateUtf8(utf8), "Initial UTF-8 validation (primary) failed.");

                Assert.True(ValidateUtf8Fuschia(utf8), "Initial UTF-8 validation (Fuschia) failed.");

                // Perform random bit flips
                for (int flip = 0; flip < 1000; flip++)
                {
                    if (utf8.Length == 0)
                    {
                        break;
                    }

                    byte[] modifiedUtf8 = (byte[])utf8.Clone();
                    int byteIndex = rand.Next(modifiedUtf8.Length);
                    int bitFlip = 1 << rand.Next(8);
                    modifiedUtf8[byteIndex] ^= (byte)bitFlip;

                    // Validate the modified sequence with both methods
                    bool isValidPrimary = ValidateUtf8(modifiedUtf8);
                    bool isValidFuschia = ValidateUtf8Fuschia(modifiedUtf8);

                    // Ensure both methods agree on the validation result
                    Assert.Equal(isValidPrimary, isValidFuschia);
                }
            }
        }
    }

    // credit: based on code from Google Fuchsia (Apache Licensed)
    public static bool ValidateUtf8Fuschia(byte[] data)
    {
        int pos = 0;
        int len = data.Length;
        uint codePoint;

        while (pos < len)
        {
            byte byte1 = data[pos];
            if (byte1 < 0b10000000)
            {
                pos++;
                continue;
            }
            else if ((byte1 & 0b11100000) == 0b11000000)
            {
                if (pos + 2 > len) return false;
                if ((data[pos + 1] & 0b11000000) != 0b10000000) return false;

                codePoint = (uint)((byte1 & 0b00011111) << 6 | (data[pos + 1] & 0b00111111));
                if (codePoint < 0x80 || 0x7ff < codePoint) return false;
                pos += 2;
            }
            else if ((byte1 & 0b11110000) == 0b11100000)
            {
                if (pos + 3 > len) return false;
                if ((data[pos + 1] & 0b11000000) != 0b10000000) return false;
                if ((data[pos + 2] & 0b11000000) != 0b10000000) return false;

                codePoint = (uint)((byte1 & 0b00001111) << 12 | (data[pos + 1] & 0b00111111) << 6 | (data[pos + 2] & 0b00111111));
                if (codePoint < 0x800 || 0xffff < codePoint || (0xd7ff < codePoint && codePoint < 0xe000)) return false;
                pos += 3;
            }
            else if ((byte1 & 0b11111000) == 0b11110000)
            {
                if (pos + 4 > len) return false;
                if ((data[pos + 1] & 0b11000000) != 0b10000000) return false;
                if ((data[pos + 2] & 0b11000000) != 0b10000000) return false;
                if ((data[pos + 3] & 0b11000000) != 0b10000000) return false;

                codePoint = (uint)((byte1 & 0b00000111) << 18 | (data[pos + 1] & 0b00111111) << 12 | (data[pos + 2] & 0b00111111) << 6 | (data[pos + 3] & 0b00111111));
                if (codePoint < 0x10000 || 0x10ffff < codePoint) return false;
                pos += 4;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    // Check that all functions agree on the result when the input might be invalid.
    private bool InvalidateUtf8(byte[] utf8, int badindex)
    {
        unsafe
        {
            fixed (byte* pInput = utf8)
            {
                int TailScalarCodeUnitCountAdjustment = 0;
                int TailUtf16CodeUnitCountAdjustment = 0;
                int SIMDUtf16CodeUnitCountAdjustment, SIMDScalarCountAdjustment;

                byte* scalarResult = SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar(pInput, utf8.Length,out TailUtf16CodeUnitCountAdjustment,out TailScalarCodeUnitCountAdjustment);
                int scalarOffset = (int)(scalarResult - pInput);
                byte* simdResult = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, utf8.Length,out SIMDUtf16CodeUnitCountAdjustment,out SIMDScalarCountAdjustment);
                int simdOffset = (int)(simdResult - pInput);

                int utf16CodeUnitCountAdjustment, scalarCountAdjustment;
                byte* dotnetResult = DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8.Length, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
                int dotnetOffset = (int)(dotnetResult - pInput);
                if (scalarOffset != simdOffset)
                {
                    Console.WriteLine("Suprisingly, scalarResult != simdResult {0} != {1}, badindex = {2}, length = {3}", scalarOffset, simdOffset, badindex, utf8.Length);
                }
                if (dotnetOffset != simdOffset)
                {
                    Console.WriteLine("Suprisingly, dotnetOffset != simdResult {0} != {1}, badindex = {2}, length = {3}", dotnetOffset, simdOffset, badindex, utf8.Length);
                }
                return (scalarResult == simdResult) && (simdResult == dotnetResult);
            }
        }
    }
    private bool ValidateUtf8(byte[] utf8, Range range = default)
    {


        // Adjusted check for default Range
        var isDefaultRange = range.Equals(default(Range));
        var (offset, length) = isDefaultRange ? (0, utf8.Length) : GetOffsetAndLength(utf8.Length, range);

        unsafe
        {
            fixed (byte* pInput = utf8)
            {
                int DotnetUtf16Adjustment, DotnetScalarCountAdjustment;
                int SimdUnicodeUtf16Adjustment, SimdUnicodeScalarCountAdjustment;

                byte* startPtr = pInput + offset;
                byte* dotnetResult = DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(startPtr, length, out DotnetUtf16Adjustment, out DotnetScalarCountAdjustment);

                if (dotnetResult != startPtr + length)
                {
                    // PrintDebugInfo(dotnetResult, startPtr, utf8, "DotnetRuntime fails to return the correct invalid position");
                    return false;
                }

                byte* simdResult = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(startPtr, length, out SimdUnicodeUtf16Adjustment, out SimdUnicodeScalarCountAdjustment);
                if (simdResult != startPtr + length)
                {
                    // PrintDebugInfo(simdResult, startPtr, utf8, "Our result fails to return the correct invalid position");
                    return false;
                }

                bool utf16AdjustmentsMatch = DotnetUtf16Adjustment == SimdUnicodeUtf16Adjustment;
                // bool scalarCountAdjustmentsMatch = DotnetScalarCountAdjustment == SimdUnicodeScalarCountAdjustment;

                // if (!utf16AdjustmentsMatch)
                // {
                //     Console.WriteLine($"UTF16 Adjustment mismatch: Expected {DotnetUtf16Adjustment}, but got: {SimdUnicodeUtf16Adjustment}.");
                // }

                // if (!scalarCountAdjustmentsMatch)
                // {
                //     Console.WriteLine($"Scalar Count Adjustment mismatch: Expected {DotnetScalarCountAdjustment}, but got: {SimdUnicodeScalarCountAdjustment}.");
                // }

                // return utf16AdjustmentsMatch && scalarCountAdjustmentsMatch;
                return true;
            }

        }
    }

    void PrintDebugInfo(byte* failedByte, byte* startPtr, byte[] utf8, string source)
{
    int failedIndex = (int)(failedByte - startPtr);
    byte failedByteValue = *failedByte;
    Console.WriteLine($"Failure in {source}: Index {failedIndex}, Byte {failedByteValue:X2}");

    // Print surrounding sequence, assuming 5 bytes context around the failure point
    int contextRadius = 5;
    int startContext = Math.Max(0, failedIndex - contextRadius);
    int endContext = Math.Min(utf8.Length, failedIndex + contextRadius + 1); // Include the failed byte and some after
    Console.Write("Sequence around failure point: ");
    for (int i = startContext; i < endContext; i++)
    {
        Console.Write($"{utf8[i]:X2} ");
    }
    Console.WriteLine();
}

    

        // Helper method to calculate the actual offset and length from a Range
    private (int offset, int length) GetOffsetAndLength(int totalLength, Range range)
    {
        var start = range.Start.GetOffset(totalLength);
        var end = range.End.GetOffset(totalLength);
        var length = end - start;
        return (start, length);
    }


// Define a delegate that matches the signature of the methods you want to test
    public unsafe delegate byte* Utf8ValidationDelegate(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment);



    
        public void UTF16CountTest(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        // int[] outputLengths = { 10, 15, 11,12 ,15,15,1, 3, 5, 8, 10, 12, 15, 18 };

        int DotnetUtf16Adjustment, DotnetScalarCountAdjustment;
        int SimdUnicodeUtf16Adjustment, SimdUnicodeScalarCountAdjustment;

        foreach (int outputLength in outputLengths)
        {
            // Generate a UTF-8 sequence with 3 units, each 2 bytes long, presumed to be valid.
            // byte[] utf8 = generator.Generate(howManyUnits: 11, byteCountInUnit: 3).ToArray();
            byte[] utf8 = generator.Generate(howManyUnits: outputLength).ToArray();
            PrintHexAndBinary(utf8);
            var (offset, length) = (0, utf8.Length);

            unsafe
            {
                fixed (byte* pInput = utf8)
                {
                    byte* startPtr = pInput + offset;
                    // Invoke the method under test.

                    DotnetUtf16Adjustment= 0; 
                    DotnetScalarCountAdjustment= 0;
                    DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, length, out DotnetUtf16Adjustment, out DotnetScalarCountAdjustment);

                    SimdUnicodeUtf16Adjustment= 0; 
                    SimdUnicodeScalarCountAdjustment= 0;
                    utf8ValidationDelegate(pInput, length, out SimdUnicodeUtf16Adjustment, out SimdUnicodeScalarCountAdjustment);



                    // Console.WriteLine("DotnetScalar:" + DotnetScalarCountAdjustment);
                    // Console.WriteLine("OurScalar:" + SimdUnicodeScalarCountAdjustment);

                    Console.WriteLine("Lenght:" + utf8.Length);
                    // Console.WriteLine("Dotnetutf16:" + DotnetUtf16Adjustment);
                    // Console.WriteLine("Ourutf16:" + SimdUnicodeUtf16Adjustment);
                    Console.WriteLine("___________________________________________________");


                    Assert.True(DotnetUtf16Adjustment == SimdUnicodeUtf16Adjustment, $"Expected UTF16 Adjustment: {DotnetUtf16Adjustment}, but got: {SimdUnicodeUtf16Adjustment}.");
                    Assert.True(DotnetScalarCountAdjustment == SimdUnicodeScalarCountAdjustment, $"Expected Scalar Count Adjustment: {DotnetScalarCountAdjustment}, but got: {SimdUnicodeScalarCountAdjustment}.");




                    // If your generator creates specific patterns or the utility calculates these adjustments differently,
                    // you'll need to adjust the expected values accordingly.
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "Scalar")]
    public void ScalarUTF16CountTest()
    {
        UTF16CountTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Fact]
    [Trait("Category", "Avx")]
    public void AvxUTF16CountTest()
    {
        UTF16CountTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }


}


