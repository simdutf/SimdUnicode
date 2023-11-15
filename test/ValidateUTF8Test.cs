public class Utf8ValidationTests
{
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
                byte* result = SimdUnicode.Utf8Utility.GetPointerToFirstInvalidByte(pInput, input.Length, out _, out _);
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
        // ... Add other sequences as needed ...
    };

    foreach (var seq in badSequences)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(seq);
        unsafe
        {
            fixed (byte* pInput = input)
            {
                byte* result = SimdUnicode.Utf8Utility.GetPointerToFirstInvalidByte(pInput, input.Length, out _, out _);
                Assert.NotEqual((IntPtr)(pInput + input.Length), (IntPtr)result); // Expecting not to reach the end
            }
        }
    }
}


}