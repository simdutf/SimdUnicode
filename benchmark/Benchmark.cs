using System;
using SimdUnicode;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;
using System.Runtime;
using System.Runtime.InteropServices;
namespace SimdUnicodeBenchmarks
{
    public class Checker
    {
        List<char[]> names;
        List<bool> results;

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


        [Params(100, 200, 500)]
        public uint N;

        [GlobalSetup]
        public void Setup()
        {
            names = new List<char[]>();
            results = new List<bool>();

            for (int i = 0; i < 100; i++)
            {
                names.Add(GetRandomASCIIString(N));
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
                results[count] = (Encoding.ASCII.GetByteCount(name) == name.Length);
                count += 1;
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
            Console.WriteLine("The RuntimeIsAscii is too fast. The execution time does not depend on the string length.");
            Console.WriteLine("It is assuredly cheating..");

        }
    }
}