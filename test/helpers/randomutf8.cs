namespace tests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class RandomUtf8
{
    private Random gen;
    private double[] probabilities;
    private const int maxByteLength = 4;

    public RandomUtf8(uint seed, int prob1byte, int prob2bytes, int prob3bytes, int prob4bytes)
    {
        gen = new Random((int)seed);
        probabilities = new double[maxByteLength] { prob1byte, prob2bytes, prob3bytes, prob4bytes };
    }

#pragma warning disable CA1002
    public List<byte> Generate(int howManyUnits, int? byteCountInUnit = null)
    {
        var result = new List<byte>();
        while (result.Count < howManyUnits)
        {
            int count = byteCountInUnit ?? PickRandomByteCount();
            int codePoint = GenerateCodePoint(count);
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));

            result.AddRange(utf8Bytes);
            if (result.Count + utf8Bytes.Length > howManyUnits)
                break;
        }
        return result;
    }

    private int GenerateCodePoint(int byteCount)
    {
        switch (byteCount)
        {
            case 1:
                // Generate a code point for a 1-byte UTF-8 character (ASCII)
#pragma warning disable CA5394
                return gen.Next(0x0000, 0x007F + 1);// +1 because gen.Next() excludes the upper bound
            case 2:
                // Generate a code point for a 2-byte UTF-8 character (Latin)
#pragma warning disable CA5394
                return gen.Next(0x0080, 0x07FF + 1);
            case 3:
                // Generate a code point for a 3-byte UTF-8 character (Asiatic)
                // Note: This range skips over the surrogate pair range U+D800 to U+DFFF
#pragma warning disable CA5394
                if (gen.NextDouble() < 0.5)
                {
                    // Generate code point in U+0800 to U+D7FF range
                    return gen.Next(0x0800, 0xD7FF + 1);
                }
                else
                {
                    // Generate code point in U+E000 to U+FFFF range
#pragma warning disable CA5394
                    return gen.Next(0xE000, 0xFFFF + 1);
                }
            case 4:
                // Generate a code point for a 4-byte UTF-8 character (Supplementary)
                // The +1 is factored into the ConvertFromUtf32 method
#pragma warning disable CA5394
                return gen.Next(0x010000, 0x10FFFF);
            default:
                throw new InvalidOperationException($"Invalid byte count: {byteCount}");
        }
    }

#pragma warning disable CA1002
    public List<byte> AppendContinuationByte(List<byte> utf8Bytes) =>
                            utf8Bytes.Concat(new byte[] { (byte)gen.Next(0x80, 0xBF + 1) }).ToList();




    public static void ReplaceEndOfArray(byte[] original, byte[] replacement)//, int startIndex)
    {
        // Calculate the start index for replacement
        int startIndex = original.Length - replacement.Length;

        // Copy the replacement array into the original starting at startIndex
        Array.Copy(replacement, 0, original, startIndex, Math.Min(replacement.Length, original.Length - startIndex));
    }




    private int PickRandomByteCount()
    {
        double randomValue = gen.NextDouble() * probabilities.Sum();
        double cumulative = 0.0;
        for (int i = 0; i < maxByteLength; i++)
        {
            cumulative += probabilities[i];
            if (randomValue <= cumulative)
                return i + 1;
        }
        return maxByteLength;
    }
}
