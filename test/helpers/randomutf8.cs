using System;
using System.Collections.Generic;
using System.Linq;

public class RandomUtf8
{
    // Internal random number generator
    private Random gen;

    // Array of probabilities for each UTF-8 byte count (1-byte, 2-bytes, etc.)
    private double[] probabilities;

    // Maximum number of bytes a UTF-8 character can be (based on the standard)
    private const int maxByteLength = 4;

    // Constructor initializing the generator with seed and probabilities
    public RandomUtf8(uint seed, int prob_1byte, int prob_2bytes, int prob_3bytes, int prob_4bytes)
    {
        gen = new Random((int)seed);
        probabilities = new double[maxByteLength] { prob_1byte, prob_2bytes, prob_3bytes, prob_4bytes };
    }

    // Generates a byte array of random UTF-8 sequences of specified length
    public byte[] Generate(int outputBytes)
    {
        List<byte> result = new List<byte>(outputBytes);
        while (result.Count < outputBytes)
        {
            uint codePoint = GenerateCodePoint();
            byte[] utf8Bytes = EncodeToUTF8(codePoint);

            // Ensure we don't exceed the desired length
            if (result.Count + utf8Bytes.Length > outputBytes)
                break;

            result.AddRange(utf8Bytes);
        }
        return result.ToArray();
    }

    // Generates a byte array of random UTF-8 sequences and returns it along with its length
    public (byte[] utf8, int count) GenerateCounted(int outputBytes)
    {
        var utf8 = Generate(outputBytes);
        return (utf8, utf8.Length);
    }

    // Overload to regenerate the byte sequence with a new seed
    public byte[] Generate(int outputBytes, long seed)
    {
        gen = new Random((int)seed);
        return Generate(outputBytes);
    }

    // Generate a random UTF-8 code point based on probabilities
    private uint GenerateCodePoint()
    {
        int byteCount = PickRandomByteCount();

        // Depending on the byte count, generate an appropriate UTF-8 sequence
        switch (byteCount)
        {
            // Each case follows UTF-8 encoding rules for 1-byte, 2-byte, 3-byte, and 4-byte sequences
            case 1: return (uint)gen.Next(0x00, 0x80); // 1-byte sequence
            case 2: return (uint)((gen.Next(0xC2, 0xDF) << 8) | (0x80 | gen.Next(0x00, 0x40)));
            case 3: return (uint)((gen.Next(0xE0, 0xEF) << 16) | ((0x80 | gen.Next(0x00, 0x40)) << 8) | (0x80 | gen.Next(0x00, 0x40)));
            case 4: return (uint)((gen.Next(0xF0, 0xF4) << 24) | ((0x80 | gen.Next(0x00, 0x40)) << 16) | ((0x80 | gen.Next(0x00, 0x40)) << 8) | (0x80 | gen.Next(0x00, 0x40)));
            default: throw new InvalidOperationException($"Invalid byte count: {byteCount}"); // Guard clause for invalid byte count
        }
    }

    // Pick a random byte count based on the given probabilities
    private int PickRandomByteCount()
    {
        double randomValue = gen.NextDouble() * probabilities.Sum();
        double cumulative = 0.0;

        // Check each cumulative probability until the random value is less than the cumulative sum
        for (int i = 0; i < maxByteLength; i++)
        {
            cumulative += probabilities[i];
            if (randomValue <= cumulative)
                return i + 1; // Return the byte count
        }

        return maxByteLength; // Default to max byte length
    }

    // Convert the generated code point into a valid UTF-8 sequence
    private byte[] EncodeToUTF8(uint codePoint)
    {
        var result = new List<byte>();

        // Break the code point into its constituent bytes
        while (codePoint != 0)
        {
            result.Add((byte)(codePoint & 0xFF));
            codePoint >>= 8;
        }

        result.Reverse(); // Reverse to get the bytes in the correct order
        return result.ToArray();
    }
}
