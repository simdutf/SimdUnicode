using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class RandomUtf8
{
    private Random gen;
    private double[] probabilities;
    private const int maxByteLength = 4;

    public RandomUtf8(uint seed, int prob_1byte, int prob_2bytes, int prob_3bytes, int prob_4bytes)
    {
        gen = new Random((int)seed);
        probabilities = new double[maxByteLength] { prob_1byte, prob_2bytes, prob_3bytes, prob_4bytes };
    }

    public byte[] Generate(int outputBytes, int? byteCount = null)
    {
        var result = new List<byte>();
        while (result.Count < outputBytes)
        {
            int count = byteCount ?? PickRandomByteCount();
            int codePoint = GenerateCodePoint(count);
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));

            if (result.Count + utf8Bytes.Length > outputBytes)
                break;
            result.AddRange(utf8Bytes);
        }
        return result.ToArray();
    }


    private int GenerateCodePoint(int byteCount)
    {
        switch (byteCount)
        {
            case 1:
                // Generate a code point for a 1-byte UTF-8 character (ASCII)
                return gen.Next(0x0000, 0x007F + 1);// +1 because gen.Next() excludes the upper bound
            case 2:
                // Generate a code point for a 2-byte UTF-8 character (Latin)
                return gen.Next(0x0080, 0x07FF + 1);
            case 3:
                // Generate a code point for a 3-byte UTF-8 character (Asiatic)
                // Note: This range skips over the surrogate pair range U+D800 to U+DFFF
                if (gen.NextDouble() < 0.5)
                {
                    // Generate code point in U+0800 to U+D7FF range
                    return gen.Next(0x0800, 0xD7FF + 1);
                }
                else
                {
                    // Generate code point in U+E000 to U+FFFF range
                    return gen.Next(0xE000, 0xFFFF + 1);
                }
            case 4:
                // Generate a code point for a 4-byte UTF-8 character (Supplementary)
                // The +1 is factored into the ConvertFromUtf32 method
                return gen.Next(0x010000, 0x10FFFF);
            default:
                throw new InvalidOperationException($"Invalid byte count: {byteCount}");
        }
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
