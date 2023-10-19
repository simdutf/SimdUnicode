using System;
using SimdUnicode;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Buffers;

namespace SimdUnicodeBenchmarks
{

    // See https://github.com/dotnet/performance/blob/cea924dd0639057c1062444a642a470deef96158/src/benchmarks/micro/libraries/System.Text.Encoding/Perf.Ascii.cs#L38
    // for a standard benchmark
    public class Checker
    {
        List<char[]> names;
        List<char[]> nonAsciichars;
        public List<byte[]> nonAsciiByteArrays; // Declare at the class level

        List<bool> results;

        public static bool RuntimeIsAsciiApproach(ReadOnlySpan<char> s)
        {
            // The runtime as of NET 8.0 has a dedicated method for this, but
            // it is not available prior to that, so let us branch.
#if NET8_0_OR_GREATER
            return Ascii.IsValid(s);
#else
            foreach (char c in s)
            {
                if (c >= 128)
                {
                    return false;
                }
            }

            return true;
#endif
        }
        public static char[] GetRandomASCIIString(uint n)
        {
            var allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ01234567é89";

            var chars = new char[n];
            var rd = new Random(12345); // fixed seed

            for (var i = 0; i < n; i++)
            {
                chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
            }

            return chars;
        }

        public static char[] GetRandomNonASCIIString(uint n)
        {
            // Chose a few Latin Extended-A and Latin Extended-B characters alongside ASCII chars
            var allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ01234567é89šžŸũŭůűųŷŹźŻżŽ";

            var chars = new char[n];
            var rd = new Random(12345); // fixed seed

            for (var i = 0; i < n; i++)
            {
                chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
            }

            return chars;
        }



        [Params(100, 200, 500, 1000, 2000)]
        public uint N;


        [GlobalSetup]
        public void Setup()
        {
            names = new List<char[]>();
            nonAsciiByteArrays = new List<byte[]>(); // Initialize the list of byte arrays
            results = new List<bool>();

            for (int i = 0; i < 100; i++)
            {
                names.Add(GetRandomASCIIString(N));
                char[] nonAsciiChars = GetRandomNonASCIIString(N);
                nonAsciiByteArrays.Add(Encoding.UTF8.GetBytes(nonAsciiChars));  // Convert to byte array and store
                results.Add(false);
            }
        }


        [Benchmark]
        public void FastUnicodeIsAscii()
        {
            int count = 0;
            foreach (char[] name in names)
            {
                results[count] = SimdUnicode.Ascii.SIMDIsAscii(name);
                count += 1;
            }
        }

        [Benchmark]
        public void StandardUnicodeIsAscii()
        {
            int count = 0;
            foreach (char[] name in names)
            {
                results[count] = SimdUnicode.Ascii.IsAscii(name);
                count += 1;
            }
        }

        [Benchmark]
        public void RuntimeIsAscii()
        {
            int count = 0;
            foreach (char[] name in names)
            {
                results[count] = RuntimeIsAsciiApproach(name);
                count += 1;
            }
        }
        [Benchmark]
        public void TestErrorGetIndexOfFirstNonAsciiByteBenchmark()
        {
            foreach (byte[] nonAsciiBytes in nonAsciiByteArrays)  // Use nonAsciiByteArrays directly
            {
                unsafe
                {
                    fixed (byte* pNonAscii = nonAsciiBytes)
                    {
                        nuint result = Ascii.GetIndexOfFirstNonAsciiByte(pNonAscii, (nuint)nonAsciiBytes.Length);
                    }
                }
            }
        }



    }

    public class Program
    {
        public static void Main(string[] args)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                Console.WriteLine("ARM64 system detected.");
            }
            else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                Console.WriteLine("X64 system detected (Intel, AMD,...).");

            }
            else
            {
                Console.WriteLine("Unrecognized system.");

            }
            var summary = BenchmarkRunner.Run<Checker>();
        }
    }
}