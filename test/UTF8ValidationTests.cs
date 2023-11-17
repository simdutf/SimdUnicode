namespace tests;
using System.Text;
using SimdUnicode;

public class Utf8ValidationTests
{

private const int NumTrials = 1000;
private readonly RandomUtf8 generator = new RandomUtf8(1234, 1, 1, 1, 1);
private readonly Random rand = new Random(1234);

[Fact]
public void TestGoodSequences()
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
                byte* result = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, input.Length);
                Assert.Equal((IntPtr)(pInput + input.Length), (IntPtr)result); // Expecting the end of the string
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
                byte* result = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, input.Length);
                Assert.Equal((IntPtr)(pInput + input.Length), (IntPtr)result); // Expecting not to reach the end
            }
        }
    }
}

    // Not sure why sure a simple test is there, but it was in the C++ code
    [Fact]
    public void Node48995Test()
    {
        byte[] bad = new byte[] { 0x80 };
        Assert.False(ValidateUtf8(bad)); // Asserting false because it's a bad sequence
    }

    [Fact]
    public void NoErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);
            Assert.True(ValidateUtf8(utf8));
        }
    }

// Tests to check:

    [Fact]
    public void HeaderBitsErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);
            for (int i = 0; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0b11000000) != 0b10000000) // Only process leading bytes
                {
                    byte oldByte = utf8[i];
                    utf8[i] = 0b11111000; // Forcing a header bits error
                    Assert.False(ValidateUtf8(utf8));
                    utf8[i] = oldByte; // Restore the original byte
                }
            }
        }
    }

    [Fact]
    public void TooShortErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);
            for (int i = 0; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0b11000000) == 0b10000000) // Only process continuation bytes
                {
                    byte oldByte = utf8[i];
                    utf8[i] = 0b11100000; // Forcing a too short error
                    Assert.False(ValidateUtf8(utf8));
                    utf8[i] = oldByte; // Restore the original byte
                }
            }
        }
    }

    [Fact]
    public void TooLongErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);
            for (int i = 0; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0b11000000) != 0b10000000) // Only process leading bytes
                {
                    byte oldByte = utf8[i];
                    utf8[i] = 0b10000000; // Forcing a too long error
                    Assert.False(ValidateUtf8(utf8));
                    utf8[i] = oldByte; // Restore the original byte
                }
            }
        }
    }

// 

    [Fact]
    public void OverlongErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);

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
                    else // if ((old & 0b11111000) == 0b11110000) // four-bytes case, change to a value less or equal than 0xffff
                    {
                        utf8[i] = 0b11110000;
                        utf8[i + 1] = (byte)(utf8[i + 1] & 0b11001111); 
                    }

                    Assert.False(ValidateUtf8(utf8));

                    utf8[i] = old;
                    utf8[i + 1] = secondOld;
                }
            }
        }
    }

    [Fact]
    public void TooLargeErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);

            for (int i = 0; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0b11111000) == 0b11110000) // Only in 4-bytes case
                {
                    byte old = utf8[i];
                    utf8[i] += (byte)(((utf8[i] & 0b100) == 0b100) ? 0b10 : 0b100);

                    Assert.False(ValidateUtf8(utf8));

                    utf8[i] = old;
                }
            }
        }
    }

    [Fact]
    public void SurrogateErrorTest()
    {
        for (int trial = 0; trial < NumTrials; trial++)
        {

            byte[] utf8 = generator.Generate(512);

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
                    }

                    utf8[i] = old;
                    utf8[i + 1] = secondOld;
                }
            }
        }
    }

    // We save this for when testing SIMD version, I promise to clean up later:
    // [Fact]
    // public void BruteForceTest()
    // {
    //     for (int i = 0; i < NumTrials; i++)
    //     {
    //         byte[] utf8 = generator.Generate(rand.Next(256));
    //         Assert.True(ValidateUtf8(utf8), "UTF-8 validation failed, indicating a bug.");

    //         for (int flip = 0; flip < 1000; flip++)
    //         {
    //             byte[] modifiedUtf8 = (byte[])utf8.Clone();
    //             int byteIndex = rand.Next(modifiedUtf8.Length);
    //             int bitFlip = 1 << rand.Next(8);
    //             modifiedUtf8[byteIndex] ^= (byte)bitFlip;

    //             bool isValid = ValidateUtf8(modifiedUtf8);
    //             // This condition may depend on the specific behavior of your validation method
    //             // and whether or not it should match a reference implementation.
    //             // In this example, we are simply asserting that the modified sequence is still valid.
    //             Assert.True(isValid, "Mismatch in UTF-8 validation detected, indicating a bug.");
    //         }
    //     }
    // }

    // Pseudocode for easier ChatGPT generatioN:
    // 1. Set a seed value (1234).
    // 2. Create a random UTF-8 generator with equal probabilities for 1, 2, 3, and 4-byte sequences.
    // 3. Set the total number of trials to 1000.

    // 4. For each trial (0 to total - 1):
    //    a. Generate a random UTF-8 sequence with a length between 0 and 255.
    //    b. Validate the UTF-8 sequence. If it's invalid:
    //       - Output "bug" to stderr.
    //       - Fail the test.

    //    c. For 1000 times (bit flipping loop):
    //       i. Generate a random bit position (0 to 7).
    //       ii. Flip exactly one bit at the random position in a random byte of the UTF-8 sequence.
    //       iii. Re-validate the modified UTF-8 sequence.
    //       iv. Compare the result of the validation with a reference validation method.
    //       v. If the results differ:
    //          - Output "bug" to stderr.
    //          - Fail the test.

    // 5. If all tests pass, output "OK".


    private bool ValidateUtf8(byte[] utf8)
    {
        unsafe
        {
            fixed (byte* pInput = utf8)
            {
                byte* invalidBytePointer = UTF8.GetPointerToFirstInvalidByte(pInput, utf8.Length);
                // If the pointer to the first invalid byte is at the end of the array, the UTF-8 is valid.
                return invalidBytePointer == pInput + utf8.Length;
            }
        }
    }


}