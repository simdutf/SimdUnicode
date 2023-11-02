namespace tests;
using System.Text;
using SimdUnicode;

//TODO (Nick Nuon): Test UTF8 Generator works correctly

public class AsciiTest
{
    [Fact]
    public void Test1()
    {
        Assert.True(SimdUnicode.Ascii.IsAscii("absads12323123232131231232132132132312321321312321"));
        Assert.False(SimdUnicode.Ascii.IsAscii("absaé12323123232131231232132132132312321321312321"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("absads12323123232131231232132132132312321321312321"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("12345678"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("123456789"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("1234567890123456"));
        Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absaé12323123232131231232132132132312321321312321"));
        Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absa12323123232131231232132132132312321321312321é"));
    }

    [Fact]
    public void HardCodedSequencesTest()
    {
        string[] goodsequences = {
            "a",
            "abcde12345",
            "\x71",
            "\x75\x4c",
            "\x7f\x4c\x23\x3c\x3a\x6f\x5d\x44\x13\x70"
        };

        string[] badsequences = {
            "\xc3\x28",
            "\xa0\xa1",
            "\xe2\x28\xa1",
            "\xe2\x82\x28",
            "\xf0\x28\x8c\xbc",
            "\xf0\x90\x28\xbc",
            "\xf0\x28\x8c\x28",
            "\xc0\x9f",
            "\xf5\xff\xff\xff",
            "\xed\xa0\x81",
            "\xf8\x90\x80\x80\x80",
            "123456789012345\xed",
            "123456789012345\xf1",
            "123456789012345\xc2",
            "\xC2\x7F",
            "\xce",
            "\xce\xba\xe1",
            "\xce\xba\xe1\xbd",
            "\xce\xba\xe1\xbd\xb9\xcf",
            "\xce\xba\xe1\xbd\xb9\xcf\x83\xce",
            "\xce\xba\xe1\xbd\xb9\xcf\x83\xce\xbc\xce",
            "\xdf",
            "\xef\xbf",
            "\x80",
            "\x91\x85\x95\x9e",
            "\x6c\x02\x8e\x18",
            "\x25\x5b\x6e\x2c\x32\x2c\x5b\x5b\x33\x2c\x34\x2c\x05\x29\x2c\x33\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5d\x2c\x35\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x20\x01\x01\x01\x01\x01\x02\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x23\x0a\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x7e\x7e\x0a\x0a\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5d\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x2c\x37\x2e\x33\x2c\x39\x2e\x33\x2c\x37\x2e\x33\x2c\x39\x2e\x34\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x5d\x01\x01\x80\x01\x01\x01\x79\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
            "[[[[[[[[[[[[[[[\x80\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x010\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
            "\x20\x0b\x01\x01\x01\x64\x3a\x64\x3a\x64\x3a\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x5b\x30\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x80\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01\x01",
            "\x80",
            "\x90",
            "\xa1",
            "\xb2",
            "\xc3",
            "\xd4",
            "\xe5",
            "\xf6",
            "\xc3\xb1",
            "\xe2\x82\xa1",
            "\xf0\x90\x8c\xbc",
            "\xc2\x80",
            "\xf0\x90\x80\x80",
            "\xee\x80\x80",
            "\xef\xbb\xbf"};

        foreach (var sequence in goodsequences)
        {
            Assert.True(SimdUnicode.Ascii.IsAscii(sequence), "Expected valid ASCII sequence");
            Assert.True(SimdUnicode.Ascii.SIMDIsAscii(sequence), "Expected SIMDIsAscii to validate ASCII sequence");

        }

        foreach (var sequence in badsequences)
        {
            Assert.False(SimdUnicode.Ascii.IsAscii(sequence), "Expected non-valid ASCII sequence");
            Assert.False(SimdUnicode.Ascii.SIMDIsAscii(sequence), "Expected SIMDIsAscii to invalidate non-ASCII sequence");
        }
    }

    [Fact]
    public void Test_ASCII_generator()
    {
        const int NUM_TRIALS = 1000;
        const int MAX_LENGTH = 255;
        RandomUtf8 utf8Generator = new RandomUtf8(0, 100, 0, 0, 0); // Only ASCII/one-bytes

        for (int length = 1; length <= MAX_LENGTH; length++)
        {
            int validSequencesCount = 0;

            for (int i = 0; i < NUM_TRIALS; i++)
            {
                byte[] sequence = utf8Generator.Generate(length);

                if (sequence.All(b => b >= 0x00 && b <= 0x7F))
                {
                    validSequencesCount++;
                }

                // Console.WriteLine($"{length}-byte sequence: {BitConverter.ToString(sequence)}"); // Print the sequence as hex bytes
            }

            // Print the validation results
            // Console.WriteLine($"For {length}-byte sequences, {validSequencesCount * 100.0 / NUM_TRIALS}% were valid ASCII.");

            // Assertion or check to ensure all sequences were valid ASCII
            if (validSequencesCount != NUM_TRIALS)
            {
                throw new Exception($"Invalid ASCII sequences were generated for {length}-byte sequences!");
            }
        }
    }


    [Fact]
    public void TestNoErrorGetIndexOfFirstNonAsciiByte()
    {
        // Console.WriteLine("---------Testing SimdUnicode's GetIndexofFIrstNonAsciiByte: all ASCII-------------");
        // Console.WriteLine("");

        const int NUM_TRIALS = 1000;
        const int LENGTH = 512;
        RandomUtf8 utf8Generator = new RandomUtf8(0, 100, 0, 0, 0);  // Only ASCII/one-bytes

        for (int trial = 0; trial < NUM_TRIALS; trial++)
        {
            byte[] ascii = utf8Generator.Generate(LENGTH);

            // Print the generated ASCII sequence for debugging
            // Console.WriteLine("Generated ASCII sequence: " + BitConverter.ToString(ascii));
            // Console.WriteLine("");


            unsafe
            {
                fixed (byte* pAscii = ascii)
                {
                    nuint result = SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte(pAscii, (nuint)ascii.Length);
                    if (result != (nuint)ascii.Length)
                    {
                        throw new Exception($"Unexpected non-ASCII character found at index {result}");
                    }
                }
            }
        }
    }


    [Fact]
    public void TestErrorGetIndexOfFirstNonAsciiByte()
    {
        const int NUM_TRIALS = 1000;
        const int LENGTH = 512;
        RandomUtf8 utf8Generator = new RandomUtf8(0, 100, 0, 0, 0);  // Only ASCII/one-bytes

        for (int trial = 0; trial < NUM_TRIALS; trial++)
        {
            byte[] ascii = utf8Generator.Generate(LENGTH);
            // Console.WriteLine("---------Testing SimdUnicode's GetIndexofFIrstNonAsciiByte: Error-------------");
            // Console.WriteLine("");


            for (int i = 0; i < ascii.Length; i++)
            {
                ascii[i] += 0b10000000;

                unsafe
                {
                    fixed (byte* pAscii = ascii)
                    {
                        nuint result = SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte(pAscii, (nuint)ascii.Length);
                        if (result != (nuint)i)
                        {
                            // Print the generated ASCII sequence for debugging
                            // Console.WriteLine("Generated non_ASCII sequence: " + BitConverter.ToString(ascii));
                            // Console.WriteLine("");

                            throw new Exception($"Expected non-ASCII character at index {i}, but found at index {result}");
                        }
                    }
                }

                ascii[i] -= 0b10000000;
            }
        }
    }


}