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

    // public byte[] Generate(int howManyUnits, int? byteCountInUnit = null)
    // {
    //     var result = new List<byte>();
    //     while (result.Count < howManyUnits)
    //     {
    //         int count = byteCountInUnit ?? PickRandomByteCount();
    //         int codePoint = GenerateCodePoint(count);
    //         byte[] utf8Bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));

    //         result.AddRange(utf8Bytes);
    //         if (result.Count + utf8Bytes.Length > howManyUnits)
    //             break;
    //     }
    //     return result.ToArray();
    // }

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

    //     public List<byte> Generate(int howManyUnits, int? byteCountInUnit = null)
    // {
    //     var result = new List<byte>();
    //     var unitsAdded = 0; // Track the number of characters added.

    //     while (unitsAdded < howManyUnits)
    //     {
    //         int count = byteCountInUnit ?? PickRandomByteCount();
    //         int codePoint = GenerateCodePoint(count);
    //         byte[] utf8Bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));

    //         // Ensure adding the new character won't exceed the howManyUnits limit.
    //         if (unitsAdded + 1 > howManyUnits)
    //             break;

    //         result.AddRange(utf8Bytes);
    //         unitsAdded++; // Increment the units (characters) count.
    //     }

    //     return result;
    // }


    //     public object Generate(int howManyUnits, int? byteCountInUnit = null, bool returnAsList = false)
    // {
    //     var result = new List<byte>();
    //     while (result.Count < howManyUnits)
    //     {
    //         int count = byteCountInUnit ?? PickRandomByteCount();
    //         int codePoint = GenerateCodePoint(count);
    //         byte[] utf8Bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));

    //         if (result.Count + utf8Bytes.Length > howManyUnits)
    //             break;

    //         result.AddRange(utf8Bytes);
    //     }

    //     if (returnAsList)
    //     {
    //         return result;
    //     }
    //     else
    //     {
    //         return result.ToArray();
    //     }
    // }

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

    public List<byte> AppendContinuationByte(List<byte> utf8Bytes) => 
                            utf8Bytes.Concat(new byte[] {(byte)gen.Next(0x80, 0xBF + 1)}).ToList();




    public void ReplaceEndOfArray(byte[] original, byte[] replacement)//, int startIndex)
    {
        // // Check if the startIndex is within the bounds of the original array
        // if (startIndex < 0 || startIndex > original.Length)
        // {
        //     throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index is out of the range of the original array.");
        // }

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
