namespace tests;

public class AsciiTest
{
    [Fact]
    public void Test1()
    {
        Assert.True(SimdUnicode.Ascii.IsAscii("absads"));
        Assert.False(SimdUnicode.Ascii.IsAscii("absaé"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("absads"));
        Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absaé"));
    }
}