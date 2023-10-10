namespace tests;
using System.Text;

public class AsciiTest
{
    [Fact]
    public void Test1()
    {
        // Assert.True(SimdUnicode.Ascii.IsAscii("absads12323123232131231232132132132312321321312321"));
        // Assert.False(SimdUnicode.Ascii.IsAscii("absaé12323123232131231232132132132312321321312321"));
        // Assert.True(SimdUnicode.Ascii.SIMDIsAscii("absads12323123232131231232132132132312321321312321"));
        // Assert.True(SimdUnicode.Ascii.SIMDIsAscii("12345678")); // 8 characters pass
        // Assert.True(SimdUnicode.Ascii.SIMDIsAscii("123456789")); // 9 characters fails
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("1234567890123456")); //fails
        // Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absaé12323123232131231232132132132312321321312321"));
        // Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absa12323123232131231232132132132312321321312321é")); // pass
    }

/*     [Fact]
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
            // ... (continue with all sequences)
        };

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
    } */


}