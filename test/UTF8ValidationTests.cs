namespace tests;
using System.Text;
using SimdUnicode;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using BenchmarkDotNet.Disassemblers;
using Iced.Intel;

public unsafe class Utf8SIMDValidationTests
{


    private const int NumTrials = 100;
    private static readonly RandomUtf8 generator = new RandomUtf8(1234, 1, 1, 1, 1);
    private static readonly Random rand = new Random();

    // int[] outputLengths = { 128, 192, 256, 320, 384, 448, 512, 576, 640, 704, 768, 832, 896, 960, 1024, 1088, 1152, 1216, 1280, 1344, 1408, 1472, 1536, 1600, 1664, 1728, 1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560, 2624, 2688, 2752, 2816, 2880, 2944, 3008, 3072, 3136, 3200, 3264, 3328, 3392, 3456, 3520, 3584, 3648, 3712, 3776, 3840, 3904, 3968, 4032, 4096, 4160, 4224, 4288, 4352, 4416, 4480, 4544, 4608, 4672, 4736, 4800, 4864, 4928, 4992, 5056, 5120, 5184, 5248, 5312, 5376, 5440, 5504, 5568, 5632, 5696, 5760, 5824, 5888, 5952, 6016, 6080, 6144, 6208, 6272, 6336, 6400, 6464, 6528, 6592, 6656, 6720, 6784, 6848, 6912, 6976, 7040, 7104, 7168, 7232, 7296, 7360, 7424, 7488, 7552, 7616, 7680, 7744, 7808, 7872, 7936, 8000, 8064, 8128, 8192, 8256, 8320, 8384, 8448, 8512, 8576, 8640, 8704, 8768, 8832, 8896, 8960, 9024, 9088, 9152, 9216, 9280, 9344, 9408, 9472, 9536, 9600, 9664, 9728, 9792, 9856, 9920, 9984, 10000 };
    static int[] outputLengths = { 128, 345, 1000 }; 

    [Flags]
    public enum TestSystemRequirements
    {
        None = 0,
        Arm64 = 1,
        X64Avx512 = 2,
        X64Avx2 = 4,
        X64Sse = 8,
        // Add more as needed
    }

    private sealed class FactOnSystemRequirementAttribute : FactAttribute
    {
        private TestSystemRequirements RequiredSystems;

        public FactOnSystemRequirementAttribute(TestSystemRequirements requiredSystems)
        {
            RequiredSystems = requiredSystems;

            if (!IsSystemSupported(requiredSystems))
            {
                Skip = "Test is skipped due to not meeting system requirements.";
            }
        }

        private bool IsSystemSupported(TestSystemRequirements requiredSystems)
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.Arm64:
                    return requiredSystems.HasFlag(TestSystemRequirements.Arm64);
                case Architecture.X64:
                    return (requiredSystems.HasFlag(TestSystemRequirements.X64Avx512) && Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512F.IsSupported) ||
                        (requiredSystems.HasFlag(TestSystemRequirements.X64Avx2) && System.Runtime.Intrinsics.X86.Avx2.IsSupported) ||
                        (requiredSystems.HasFlag(TestSystemRequirements.X64Sse) && System.Runtime.Intrinsics.X86.Sse.IsSupported);
                default:
                    return false; // If architecture is not covered above, the test is not supported.
            }
        }
    }


    public sealed class TestIfCondition : FactAttribute
    {
        public TestIfCondition(Func<bool> condition, string skipReason)
        {
            // Only set the Skip property if the condition evaluates to false
            if (!condition.Invoke())
            {
                Skip = skipReason;
            }
        }
    }


    
    private void simpleGoodSequences(Utf8ValidationDelegate utf8ValidationDelegate)
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

        foreach (var seq in goodSequences)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(seq);
            unsafe
            {
                fixed (byte* pInput = input)
                {
                    Assert.True(ValidateUtf8(input,utf8ValidationDelegate),
                                    $"Failure in Scalar function: SimdUnicode.UTF8.GetPointerToFirstInvalidByte.Sequence: {seq}");

                    Assert.True(ValidateCount(input,utf8ValidationDelegate));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void simpleGoodSequencesScalar()
    {
        simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void simpleGoodSequencesAvx2()
    {
        simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void simpleGoodSequencesArm64()
    {
        simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void BadSequences(Utf8ValidationDelegate utf8ValidationDelegate)
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
                    ValidateUtf8(input,utf8ValidationDelegate);
                    Assert.True(ValidateCount(input,utf8ValidationDelegate));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void BadSequencesScalar()
    {
        BadSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void BadSequencesAvx2()
    {
        BadSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void BadSequencesArm64()
    {
        BadSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    // this was in the C++ code
    private void Node48995Test(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        byte[] bad = new byte[] { 0x80 };
        Assert.False(ValidateUtf8(bad,utf8ValidationDelegate));
    }

    private void NoError(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                byte[] utf8 = generator.Generate(outputLength).ToArray();
                bool isValidUtf8 = ValidateUtf8(utf8,utf8ValidationDelegate);
                string utf8HexString = BitConverter.ToString(utf8).Replace("-", " ");
                try
                {
                    Assert.True(isValidUtf8, $"Failure NoErrorTest. Sequence: {utf8HexString}");
                    Assert.True(InvalidateUtf8(utf8, utf8.Length,utf8ValidationDelegate));
                    Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                }
                catch (Xunit.Sdk.XunitException)
                {
                    PrintHexAndBinary(utf8);
                    throw; // Rethrow the exception to fail the test.
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void NoErrorScalar()
    {
        NoError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void NoErrorAvx2()
    {
        NoError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void NoErrorArm64()
    {
        NoError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void NoErrorSpecificByteCount(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        RunTestForByteLength(1,utf8ValidationDelegate);
        RunTestForByteLength(2,utf8ValidationDelegate);
        RunTestForByteLength(3,utf8ValidationDelegate);
        RunTestForByteLength(4,utf8ValidationDelegate);
    }

    private void RunTestForByteLength(int byteLength,Utf8ValidationDelegate utf8ValidationDelegate)
    {
        // int[] outputLengths = { 128, 256, 512, 1024, 1000 }; // Example lengths
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {
                byte[] utf8 = generator.Generate(outputLength, byteLength).ToArray();
                bool isValidUtf8 = ValidateUtf8(utf8,utf8ValidationDelegate);
                try
                {
                    Assert.True(isValidUtf8, $"Failure NoErrorTest. ");
                    Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                }
                catch (Xunit.Sdk.XunitException)
                {
                    Console.WriteLine($"Test failed for {byteLength}-byte unit ");
                    PrintHexAndBinary(utf8);
                    throw; // Rethrow the exception to fail the test.
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void NoErrorSpecificByteCountScalar()
    {
        NoErrorSpecificByteCount(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void NoErrorSpecificByteCountAvx2()
    {
        NoErrorSpecificByteCount(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void NoErrorSpecificByteCountArm64()
    {
        NoErrorSpecificByteCount(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }
private void NoErrorIncompleteThenASCII(Utf8ValidationDelegate utf8ValidationDelegate)
{
    foreach (int outputLength in outputLengths){
        for (int trial = 0; trial < NumTrials; trial++)
        {
            var allAscii = new List<byte>(Enumerable.Repeat((byte)0, outputLength));
            int firstCodeLength = rand.Next(2, 5);
            List<byte> singleBytes = generator.Generate(1, firstCodeLength);
            
            int incompleteLocation = 128 - rand.Next(1, firstCodeLength - 1);
            allAscii.InsertRange(incompleteLocation, singleBytes);
            
            var utf8 = allAscii.ToArray();
            int cutOffLength = 128;//utf8.Length - rand.Next(1, firstCodeLength);
            cutOffLength = Math.Min(cutOffLength, outputLength); // Ensure it doesn't exceed the length of truncatedUtf8
            byte[] truncatedUtf8 = new byte[outputLength]; // Initialized to zero

            Array.Copy(utf8, 0, truncatedUtf8, 0, cutOffLength);

            bool isValidUtf8 = ValidateUtf8(truncatedUtf8, utf8ValidationDelegate);
            // string utf8HexString = BitConverter.ToString(truncatedUtf8).Replace("-", " ");
            try
            {
                Assert.False(isValidUtf8);
                Assert.True(InvalidateUtf8(truncatedUtf8, truncatedUtf8.Length, utf8ValidationDelegate));
                Assert.True(ValidateCount(truncatedUtf8, utf8ValidationDelegate));
            }
            catch (Xunit.Sdk.XunitException)
            {
                PrintHexAndBinary(truncatedUtf8, incompleteLocation);
                throw;
            }
        }
    }
}


        [Fact]
    [Trait("Category", "scalar")]
    public void NoErrorIncompleteThenASCIIScalar()
    {
        NoErrorIncompleteThenASCII(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void NoErrorIncompleteThenASCIIAvx2()
    {
        NoErrorIncompleteThenASCII(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }


    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void NoErrorIncompleteThenASCIIArm64()
    {
        NoErrorIncompleteThenASCII(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void NoErrorIncompleteAt256Vector(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        foreach (int outputLength in outputLengths)
        {
            for (int trial = 0; trial < NumTrials; trial++)
            {

                
                // var allAscii = generator.Generate(outputLength,1);
                var allAscii = new List<byte>(Enumerable.Repeat((byte)0, 256));
                int firstcodeLength = rand.Next(2,5);
                List<byte> singlebytes = generator.Generate(1,firstcodeLength);//recall:generate a utf8 code between 2 and 4 bytes
                int incompleteLocation = 128 - rand.Next(1,firstcodeLength - 1);
                allAscii.InsertRange(incompleteLocation,singlebytes);

                var utf8 = allAscii.ToArray();

                bool isValidUtf8 = ValidateUtf8(utf8,utf8ValidationDelegate);
                string utf8HexString = BitConverter.ToString(utf8).Replace("-", " ");
                try
                {
                    Assert.True(isValidUtf8, $"Failure NoErrorTest. Sequence: {utf8HexString}");
                    Assert.True(InvalidateUtf8(utf8, utf8.Length,utf8ValidationDelegate));
                    Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                }
                catch (Xunit.Sdk.XunitException)
                {
                    PrintHexAndBinary(utf8,incompleteLocation);
                    throw; // Rethrow the exception to fail the test.
                }
            }
        }
    }


    [Fact]
    [Trait("Category", "scalar")]
    public void NoErrorIncompleteAt256VectorScalar()
    {
        NoErrorIncompleteAt256Vector(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void NoErrorIncompleteAt256VectorAvx2()
    {
        NoErrorIncompleteAt256Vector(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void NoErrorIncompleteAt256VectorArm64()
    {
        NoErrorIncompleteAt256Vector(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void BadHeaderBits(Utf8ValidationDelegate utf8ValidationDelegate)
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
                        try
                        {
                            Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                            Assert.True(InvalidateUtf8(utf8, i,utf8ValidationDelegate));
                            Assert.True(ValidateCount(utf8,utf8ValidationDelegate)); 
                        }
                        catch (Xunit.Sdk.XunitException)
                        {
                            Console.WriteLine($"Assertion failed at index: {i}");
                            PrintHexAndBinary(utf8, i);
                            throw; // Rethrow the exception to fail the test.
                        }

                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
    }


    [Fact]
    [Trait("Category", "scalar")]
    public void BadHeaderBitsScalar()
    {
        BadHeaderBits(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void BadHeaderBitsAvx2()
    {
        BadHeaderBits(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void BadHeaderBitsArm64()
    {
        BadHeaderBits(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void TooShortError(Utf8ValidationDelegate utf8ValidationDelegate)
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
                    try
                    {
                        Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                        Assert.True(InvalidateUtf8(utf8, i,utf8ValidationDelegate));
                        Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                    }
                    catch (Xunit.Sdk.XunitException)
                    {
                        Console.WriteLine($"Assertion failed at index: {i}");
                        PrintHexAndBinary(utf8, i);
                        throw; // Rethrow the exception to fail the test.
                    }
                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
        
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TooShortErrorScalar()
    {
        TooShortError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void TooShortErrorAvx2()
    {
        TooShortError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TooShortErrorArm64()
    {
        TooShortError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void TooLongError(Utf8ValidationDelegate utf8ValidationDelegate)
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
                        utf8[i] = 0b10000000; // Forcing a too long error
                        try
                        {
                            Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                            Assert.True(InvalidateUtf8(utf8, i,utf8ValidationDelegate));
                            Assert.True(ValidateCount(utf8,utf8ValidationDelegate)); 
                        }
                        catch (Xunit.Sdk.XunitException)
                        {
                            Console.WriteLine($"Assertion failed at index: {i}");
                            PrintHexAndBinary(utf8, i);
                            throw; // Rethrow the exception to fail the test.
                        }
                        utf8[i] = oldByte; // Restore the original byte
                    }
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TooLongErrorScalar()
    {
        TooLongError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void TooLongErrorAvx2()
    {
        TooLongError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TooLongErrorArm64()
    {
        TooLongError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void OverlongError(Utf8ValidationDelegate utf8ValidationDelegate)
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

                        Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                        Assert.True(InvalidateUtf8(utf8, i,utf8ValidationDelegate));
                        Assert.True(ValidateCount(utf8,utf8ValidationDelegate));

                        utf8[i] = old;
                        utf8[i + 1] = secondOld;
                    }
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void OverlongErrorScalar()
    {
        OverlongError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void OverlongErrorArm64()
    {
        OverlongError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }


    private void TooShortErrorAtEnd(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {
            foreach (int outputLength in outputLengths)
            {
                byte[] utf8 = generator.Generate(outputLength,byteCountInUnit: 1).ToArray();        
                
                unsafe
                {
                    fixed (byte* pInput = utf8)
                    {

                        for (int i = 0; i < utf8.Length; i++)
                            {
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

                            Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                            }

                    }    
                }
            }
        }
    }


    [Fact]
    [Trait("Category", "scalar")]
    public void TooShortErrorAtEndScalar()
    {
        TooShortErrorAtEnd(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }


    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void TooShortErrorAtEndAvx2()
    {
        TooShortErrorAtEnd(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TooShortErrorAtEndArm64()
    {
        TooShortErrorAtEnd(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    //corresponds to condition 5.4.1 in the paper
    private void Invalid0xf50xff(Utf8ValidationDelegate utf8ValidationDelegate)
    {

        var invalidBytes = Enumerable.Range(0xF5, 0x100 - 0xF5).Select(i => (byte)i).ToArray(); // 0xF5 to 0xFF
        foreach (var length in outputLengths)
        {
            byte[] utf8 = generator.Generate(length).ToArray();
            for (int position = 0; position < utf8.Length; position++)
            {
                foreach (var invalidByte in invalidBytes)
                {
                    utf8[position] = invalidByte;
                    Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate)); // Expect the validation to fail due to the invalid byte
                    Assert.True(InvalidateUtf8(utf8,position,utf8ValidationDelegate));
                    Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void Invalid0xf50xffScalar()
    {
        Invalid0xf50xff(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }


    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void Invalid0xf50xffAvx2()
    {
        Invalid0xf50xff(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }


    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void Invalid0xf50xffArm64()
    {
        Invalid0xf50xff(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

// helper function for debugging: it prints a green byte every 32 bytes and a red byte at a given index 
static void PrintHexAndBinary(byte[] bytes, int highlightIndex = -1)
{
    int chunkSize = 16; // 128 bits = 16 bytes

    // Process each chunk for hexadecimal
    Console.Write("Hex: ");
    for (int i = 0; i < bytes.Length; i++)
    {
        if (i > 0 && i % chunkSize == 0)
            Console.WriteLine(); // New line after every 16 bytes
        
        if (i == highlightIndex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{bytes[i]:X2} ");
            Console.ResetColor();
        }
        else if (i % (chunkSize * 2) == 0) // print green every 256 bytes
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{bytes[i]:X2} ");
            Console.ResetColor();
        }
        else
        {
            Console.Write($"{bytes[i]:X2} ");
        }

        if ((i + 1) % chunkSize != 0) Console.Write(" "); // Add space between bytes but not at the end of the line
    }
    Console.WriteLine("\n"); // New line for readability and to separate hex from binary

    // Process each chunk for binary
    Console.Write("Binary: ");
    for (int i = 0; i < bytes.Length; i++)
    {
        if (i > 0 && i % chunkSize == 0)
            Console.WriteLine(); // New line after every 16 bytes

        string binaryString = Convert.ToString(bytes[i], 2).PadLeft(8, '0');
        if (i == highlightIndex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{binaryString} ");
            Console.ResetColor();
        }
        else if (i % (chunkSize * 2) == 0) // print green every 256 bytes
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{binaryString} ");
            Console.ResetColor();
        }
        else
        {
            Console.Write($"{binaryString} ");
        }

        if ((i + 1) % chunkSize != 0) Console.Write(" "); // Add space between bytes but not at the end of the line
    }
    Console.WriteLine(); // New line for readability
}


    private void TooLargeError(Utf8ValidationDelegate utf8ValidationDelegate)
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

                        Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                        Assert.True(InvalidateUtf8(utf8, i+1,utf8ValidationDelegate));
                        Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                        utf8[i] = old;
                    }
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TooLargeErrorScalar()
    {
        TooLargeError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void TooLargeErrorAvx()
    {
        TooLargeError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }


    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TooLargeErrorArm64()
    {
        TooLargeError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void AsciiPlusContinuationAtEndError(Utf8ValidationDelegate utf8ValidationDelegate)
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

                    Assert.False(ValidateUtf8(filler,utf8ValidationDelegate));
                    Assert.True(InvalidateUtf8(filler, filler.Length - 1,utf8ValidationDelegate));
                    Assert.True(ValidateCount(filler,utf8ValidationDelegate));
                }


            }
        }
    }
    
    [Fact]
    [Trait("Category", "scalar")]
    public void AsciiPlusContinuationAtEndErrorScalar()
    {
        AsciiPlusContinuationAtEndError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void AsciiPlusContinuationAtEndErrorArm64()
    {
        AsciiPlusContinuationAtEndError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void AsciiPlusContinuationAtEndErrorAvx2()
    {
        AsciiPlusContinuationAtEndError(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    private void SurrogateErrorTest(Utf8ValidationDelegate utf8ValidationDelegate)
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

                            Assert.False(ValidateUtf8(utf8,utf8ValidationDelegate));
                            Assert.True(InvalidateUtf8(utf8, i,utf8ValidationDelegate));
                            Assert.True(ValidateCount(utf8,utf8ValidationDelegate));
                        }

                        utf8[i] = old;
                        utf8[i + 1] = secondOld;
                    }
                }
            }
        }
    }


    [Fact]
    [Trait("Category", "scalar")]
    public void SurrogateErrorTestScalar()
    {
        SurrogateErrorTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void SurrogateErrorTestAvx2()
    {
        SurrogateErrorTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void SurrogateErrorTestArm64()
    {
        SurrogateErrorTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    private void BruteForceTest(Utf8ValidationDelegate utf8ValidationDelegate)
    {
        foreach (int outputLength in outputLengths)
        {
            for (int i = 0; i < NumTrials; i++)
            {

                // Generate random UTF-8 sequence
                byte[] utf8 = generator.Generate(rand.Next(outputLength)).ToArray();

                Assert.True(ValidateUtf8(utf8,utf8ValidationDelegate), "Initial UTF-8 validation (primary) failed.");

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
                    bool isValidPrimary = ValidateUtf8(modifiedUtf8,utf8ValidationDelegate);
                    bool isValidFuschia = ValidateUtf8Fuschia(modifiedUtf8);

                    // Ensure both methods agree on the validation result
                    try{ Assert.Equal(isValidPrimary, isValidFuschia);
                        Assert.True(ValidateCount(modifiedUtf8,utf8ValidationDelegate));
                        }
                        catch (Xunit.Sdk.XunitException)
                        {
                            Console.WriteLine($"Assertion failed. Byte randomly changed at index: {byteIndex}");
                            PrintHexAndBinary(utf8, byteIndex);
                            throw; // Rethrow the exception to fail the test.
                        }
                    
                }
            }
        }
    }

        [Fact]
    [Trait("Category", "scalar")]
    public void BruteForceTestScalar()
    {
        BruteForceTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void BruteForceTestAvx2()
    {
        BruteForceTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }


    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void BruteForceTestArm64()
    {
        BruteForceTest(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

    // credit: based on code from Google Fuchsia (Apache Licensed)
    public static bool ValidateUtf8Fuschia(byte[] data)
    {
        if(data == null) return false;
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
    private bool InvalidateUtf8(byte[] utf8, int badindex,Utf8ValidationDelegate utf8ValidationDelegate)
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
                byte* simdResult = utf8ValidationDelegate(pInput, utf8.Length,out SIMDUtf16CodeUnitCountAdjustment,out SIMDScalarCountAdjustment);
                int simdOffset = (int)(simdResult - pInput);

                int utf16CodeUnitCountAdjustment, scalarCountAdjustment;
                byte* dotnetResult = DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8.Length, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
                int dotnetOffset = (int)(dotnetResult - pInput);
                var message = "Suprisingly, scalarResult != simdResult, scalarResult is {0} != simdResult is {1}, badindex = {2}, length = {3}";
                if (scalarOffset != simdOffset)
                {
                    Console.WriteLine(message, scalarOffset, simdOffset, badindex, utf8.Length);
                }
                if (dotnetOffset != simdOffset)
                {
                    Console.WriteLine(message, dotnetOffset, simdOffset, badindex, utf8.Length);
                }
                return (scalarResult == simdResult) && (simdResult == dotnetResult);
            }
        }
    }
    private bool ValidateUtf8(byte[] utf8,Utf8ValidationDelegate utf8ValidationDelegate, Range range = default)
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

                byte* simdResult = utf8ValidationDelegate(startPtr, length, out SimdUnicodeUtf16Adjustment, out SimdUnicodeScalarCountAdjustment);
                if (simdResult != startPtr + length)
                {
                    // PrintDebugInfo(simdResult, startPtr, utf8, "Our result fails to return the correct invalid position");
                    return false;
                }
                return true;
            }

        }
    }

        // Helper method to calculate the actual offset and length from a Range
    private static (int offset, int length) GetOffsetAndLength(int totalLength, Range range)
    {
        var start = range.Start.GetOffset(totalLength);
        var end = range.End.GetOffset(totalLength);
        var length = end - start;
        return (start, length);
    }


// Define a delegate that matches the signature of the methods you want to test
    public unsafe delegate byte* Utf8ValidationDelegate(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment);

public bool ValidateCount(byte[] utf8, Utf8ValidationDelegate utf8ValidationDelegate, Range range = default)
{
    int dotnetUtf16Adjustment, dotnetScalarCountAdjustment;
    int simdUnicodeUtf16Adjustment, simdUnicodeScalarCountAdjustment;
    if(utf8 == null || utf8ValidationDelegate == null)
    {
        return false;
    }

    var isDefaultRange = range.Equals(default(Range));
    var (offset, length) = isDefaultRange ? (0, utf8.Length) : GetOffsetAndLength(utf8.Length, range);

    unsafe
    {
        fixed (byte* pInput = utf8)
        {
            byte* startPtr = pInput + offset;

            // Initialize adjustments
            dotnetUtf16Adjustment = 0;
            dotnetScalarCountAdjustment = 0;
            DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte(pInput, length, out dotnetUtf16Adjustment, out dotnetScalarCountAdjustment);

            simdUnicodeUtf16Adjustment = 0;
            simdUnicodeScalarCountAdjustment = 0;
            byte* simdResult = utf8ValidationDelegate(pInput, length, out simdUnicodeUtf16Adjustment, out simdUnicodeScalarCountAdjustment);

            // Check for discrepancies and report them in one combined message
            bool adjustmentsMatch = true;
            string errorMessage = "Error: Adjustments mismatch - ";

            if (dotnetScalarCountAdjustment != simdUnicodeScalarCountAdjustment)
            {
                errorMessage += $"Expected Scalar Count Adjustment: {dotnetScalarCountAdjustment}, but got: {simdUnicodeScalarCountAdjustment}. ";
                adjustmentsMatch = false;
            }

            if (dotnetUtf16Adjustment != simdUnicodeUtf16Adjustment)
            {
                errorMessage += $"Expected UTF16 Adjustment: {dotnetUtf16Adjustment}, but got: {simdUnicodeUtf16Adjustment}.";
                adjustmentsMatch = false;
            }

            if (!adjustmentsMatch)
            {
                Console.WriteLine(errorMessage);
                return false;
            }

            return true;
        }
    }
}


}


