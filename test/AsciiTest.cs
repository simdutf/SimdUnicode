namespace tests;
using System.Text;

public class AsciiTest
{
    [Fact]
    public void Test1()
    {
        Assert.True(SimdUnicode.Ascii.IsAscii("absads12323123232131231232132132132312321321312321"));
        Assert.False(SimdUnicode.Ascii.IsAscii("absaé12323123232131231232132132132312321321312321"));
        Assert.True(SimdUnicode.Ascii.SIMDIsAscii("absads12323123232131231232132132132312321321312321"));
        Assert.False(SimdUnicode.Ascii.SIMDIsAscii("absaé12323123232131231232132132132312321321312321"));
    }
}