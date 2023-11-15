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
        List<char[]> names = new List<char[]>();
        List<byte[]> AsciiBytes = new List<byte[]>();
        List<char[]> nonAsciichars = new List<char[]>();
        public List<byte[]> nonAsciiBytes = new List<byte[]>(); // Declare at the class level

        List<bool> results = new List<bool>();
        // We don't want to create a new Random object per function call, at least
        // not one with the same seed.
        static Random rd = new Random(12345); // fixed seed

        private string[] _lines;
        private byte[][] _linesUtf8;

        public static bool RuntimeIsAsciiApproach(ReadOnlySpan<char> s)
        {

            // The runtime as of NET 8.0 has a dedicated method for this, but
            // it is not available prior to that, so let us branch.
#if NET8_0_OR_GREATER
            return System.Text.Ascii.IsValid(s);

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
            var allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";

            var chars = new char[n];
            

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

            for (var i = 0; i < n; i++)
            {
                chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
            }

            return chars;
        }



        [Params(100, 8000)]
        public uint N;

        [Params(@"data/french.utf8.txt")]
        public string FileName;


        [GlobalSetup]
        public void Setup()
        {
            names = new List<char[]>();
            nonAsciiBytes = new List<byte[]>(); // Initialize the list of byte arrays
            results = new List<bool>();
            // We reset rd so that all data is generated with the same.
            rd = new Random(12345); // fixed seed

            // for the benchmark to be meaningful, we need a lot of data.
            for (int i = 0; i < 5000; i++)
            {
                names.Add(GetRandomASCIIString(N));
                char[] nonAsciiChars = GetRandomNonASCIIString(N);
                nonAsciiBytes.Add(Encoding.UTF8.GetBytes(nonAsciiChars));  // Convert to byte array and store
                results.Add(false);
            }

            AsciiBytes = names
                .Select(name => System.Text.Encoding.ASCII.GetBytes(name))
                .ToList();

            Console.WriteLine("reading data");
            _lines = System.IO.File.ReadAllLines(FileName);
            _linesUtf8 = Array.ConvertAll(_lines, System.Text.Encoding.UTF8.GetBytes);
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

        // This seems useless:
        /*[Benchmark]
        public void StandardUnicodeIsAscii()
        {
            int count = 0;
            foreach (char[] name in names)
            {
                results[count] = SimdUnicode.Ascii.IsAscii(name);
                count += 1;
            }
        }*/

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
        public void Error_GetIndexOfFirstNonAsciiByte()
        {
            foreach (byte[] nonAsciiByte in nonAsciiBytes)  // Use nonAsciiBytes directly
            {
                unsafe
                {
                    fixed (byte* pNonAscii = nonAsciiByte)
                    {
                        nuint result = SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte(pNonAscii, (nuint)nonAsciiByte.Length);
                    }
                }
            }
        }

        [Benchmark]
        public void Error_Runtime_GetIndexOfFirstNonAsciiByte()
        {
            foreach (byte[] nonAsciiByte in nonAsciiBytes)  // Use nonAsciiBytes directly
            {
                unsafe
                {
                    fixed (byte* pNonAscii = nonAsciiByte)
                    {
                        nuint result = Competition.Ascii.GetIndexOfFirstNonAsciiByte(pNonAscii, (nuint)nonAsciiByte.Length);
                    }
                }
            }
        }

        [Benchmark]
        public nuint allAscii_GetIndexOfFirstNonAsciiByte()
        {
            nuint result = 0;
            foreach (byte[] Abyte in AsciiBytes)  // Use AsciiBytes directly
            {
               // Console.WriteLine(System.Text.Encoding.ASCII.GetString(Abyte));
                unsafe
                {
                    fixed (byte* pAllAscii = Abyte)
                    {
                        result += SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte(pAllAscii, (nuint)Abyte.Length);
                    }
                }
            }
            return result;
        }
        

        [Benchmark]
        public nuint allAscii_Runtime_GetIndexOfFirstNonAsciiByte()
        {
            nuint result = 0;
            foreach (byte[] Abyte in AsciiBytes)  // Use AsciiBytes directly
            {
                unsafe
                {
                    fixed (byte* pAllAscii = Abyte)
                    {
                        result += Competition.Ascii.GetIndexOfFirstNonAsciiByte(pAllAscii, (nuint)Abyte.Length);
                    }
                }
            }
            return result;
        }

        [Benchmark(Description = "SimDUnicodeGetIndexOfFirstNonAsciiByteRealData")]
        public void SimDUnicodeGetIndexOfFirstNonAsciiByteRealData()
        {
            foreach (var line in _linesUtf8)
            {
                unsafe
                {
                    fixed (byte* pNonAscii = line)
                    {
                        nuint result = SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte(pNonAscii, (nuint)line.Length);
                    }
                }
            }
        }

        [Benchmark(Description = "Runtime_GetIndexOfFirstNonAsciiByte_real_data")]
        public void Runtime_GetIndexOfFirstNonAsciiByte_real_data()
        {
            foreach (var line in _linesUtf8)
            {
                unsafe
                {
                    fixed (byte* pNonAscii = line)
                    {
                        nuint result = Competition.Ascii.GetIndexOfFirstNonAsciiByte(pNonAscii, (nuint)line.Length);
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