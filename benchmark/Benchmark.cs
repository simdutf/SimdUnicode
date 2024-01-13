using System;
using SimdUnicode;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using System.Text;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO;
using System.Collections.Generic;
using System.Linq;


namespace SimdUnicodeBenchmarks
{

    // See https://github.com/dotnet/performance/blob/cea924dd0639057c1062444a642a470deef96158/src/benchmarks/micro/libraries/System.Text.Encoding/Perf.Ascii.cs#L38
    // for a standard benchmark
    // This might provide an alternative way to organize the code: https://benchmarkdotnet.org/articles/samples/IntroParamsSource.html but I felt it was simpler to divide everything into classes(Nick Nuon)
    public abstract class BenchmarkBase
    {

        // Common to both classes
        // We don't want to create a new Random object per function call, at least
        // not one with the same seed.
        // static Random rd = new Random(12345); // fixed seed
        protected static Random rd = new Random(12345); // Fixed seed

        protected void IntroduceError(byte[] utf8, Random random)
        {

            bool errorIntroduced = false;

            while (!errorIntroduced)
            {
                int errorType = random.Next(5); // Randomly select an error type (0-4)
                int position = random.Next(utf8.Length); // Random position in the byte array

                switch (errorType)
                {
                    case 0: // Header Bits Error
                        if ((utf8[position] & 0b11000000) != 0b10000000)
                        {
                            utf8[position] = 0b11111000;
                            errorIntroduced = true;
                        }
                        break;

                    case 1: // Too Short Error
                        if ((utf8[position] & 0b11000000) == 0b10000000)
                        {
                            utf8[position] = 0b11100000;
                            errorIntroduced = true;
                        }
                        break;

                    case 2: // Too Long Error
                        if ((utf8[position] & 0b11000000) != 0b10000000)
                        {
                            utf8[position] = 0b10000000;
                            errorIntroduced = true;
                        }
                        break;

                    case 3: // Overlong Error
                        if (utf8[position] >= 0b11000000)
                        {
                            if ((utf8[position] & 0b11100000) == 0b11000000)
                            {
                                utf8[position] = 0b11000000;
                            }
                            else if ((utf8[position] & 0b11110000) == 0b11100000)
                            {
                                utf8[position] = 0b11100000;
                                utf8[position + 1] = (byte)(utf8[position + 1] & 0b11011111);
                            }
                            else if ((utf8[position] & 0b11111000) == 0b11110000)
                            {
                                utf8[position] = 0b11110000;
                                utf8[position + 1] = (byte)(utf8[position + 1] & 0b11001111);
                            }
                            errorIntroduced = true;
                        }
                        break;

                    case 4: // Surrogate Error
                        if ((utf8[position] & 0b11110000) == 0b11100000)
                        {
                            utf8[position] = 0b11101101; // Leading byte for surrogate
                            for (int s = 0x8; s < 0xf; s++)
                            {
                                utf8[position + 1] = (byte)((utf8[position + 1] & 0b11000011) | (s << 2));
                                errorIntroduced = true;
                                break; // Just introduce one surrogate error
                            }
                        }
                        break;
                }
            }
        }


    }

    public class SyntheticBenchmark : BenchmarkBase
    {

        [Params(100, 8000)]
        public uint N;

        // For synthetic benchmarks
        List<char[]> AsciiChars = new List<char[]>();
        List<byte[]> AsciiBytes = new List<byte[]>();
        List<char[]> nonAsciichars = new List<char[]>();
        public List<byte[]> nonAsciiBytes = new List<byte[]>(); // Declare at the class level
        private List<byte[]> SyntheticUtf8Strings = new List<byte[]>(); // For testing UTF-8 validation
        private List<byte[]> SynthethicUtf8ErrorStrings = new List<byte[]>(); // For testing UTF-8 validation

        List<bool> results = new List<bool>();

        // Synthetic functions
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

        public static List<byte[]> GenerateUtf8Strings(int count, uint length)
        {
            var strings = new List<byte[]>();
            var randomUtf8Generator = new RandomUtf8(12345, 1, 1, 1, 1);

            for (int i = 0; i < count; i++)
            {
                strings.Add(randomUtf8Generator.Generate((int)length));
            }

            return strings;
        }


        [GlobalSetup]
        public void Setup()
        {

            // Synthetic setup
            // for the benchmark to be meaningful, we need a lot of data.
            for (int i = 0; i < 5000; i++)
            {
                AsciiChars.Add(GetRandomASCIIString(N));
                char[] nonAsciiChars = GetRandomNonASCIIString(N);
                nonAsciiBytes.Add(Encoding.UTF8.GetBytes(nonAsciiChars));  // Convert to byte array and store
                results.Add(false);
            }

            AsciiBytes = AsciiChars
                .Select(name => System.Text.Encoding.ASCII.GetBytes(name))
                .ToList();


            SyntheticUtf8Strings = GenerateUtf8Strings(1000, N); // Generate 1000 UTF-8 strings of length N

            // Introduce errors to synthetic UTF-8 data
            foreach (var utf8String in SyntheticUtf8Strings)
            {
                byte[] modifiedString = new byte[utf8String.Length];
                Array.Copy(utf8String, modifiedString, utf8String.Length);
                IntroduceError(modifiedString, rd); // Method to introduce errors into UTF-8 strings
                SynthethicUtf8ErrorStrings.Add(modifiedString);
            }
        }

        // Synthetic benchmarks
        [Benchmark]
        [BenchmarkCategory("Ascii", "SIMD")]
        public void FastUnicodeIsAscii()
        {
            int count = 0;
            foreach (char[] name in AsciiChars)
            {
                results[count] = SimdUnicode.Ascii.SIMDIsAscii(name);
                count += 1;
            }
        }

        [Benchmark]
        [BenchmarkCategory("Ascii", "Runtime")]
        public void RuntimeIsAscii()
        {
            int count = 0;
            foreach (char[] name in AsciiChars)
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
        public nuint allAsciiGetIndexOfFirstNonAsciiByte()
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
        public nuint AllAsciiRuntimeGetIndexOfFirstNonAsciiByte()
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


        [Benchmark]
        public void ScalarUtf8ValidationValidUtf8()
        {
            foreach (var utf8String in SyntheticUtf8Strings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, utf8String.Length);
                    }
                }
            }
        }

        [Benchmark]
        public void CompetitionUtf8ValidationValidUtf8()
        {
            foreach (var utf8String in SyntheticUtf8Strings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        int utf16CodeUnitCountAdjustment, scalarCountAdjustment;
                        byte* invalidBytePointer = Competition.Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8String.Length, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
                    }
                }
            }
        }

        [Benchmark]
        public void SIMDUtf8ValidationValidUtf8()
        {
            foreach (var utf8String in SyntheticUtf8Strings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        byte* invalidBytePointer = Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8String.Length);
                    }
                }
            }
        }

        [Benchmark]
        public void ScalarUtf8ValidationErrorUtf8()
        {
            foreach (var utf8String in SynthethicUtf8ErrorStrings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        byte* invalidBytePointer = SimdUnicode.UTF8.GetPointerToFirstInvalidByte(pInput, utf8String.Length);
                    }
                }
            }
        }

        [Benchmark]
        public void CompetitionUtf8ValidationErrorUtf8()
        {
            foreach (var utf8String in SynthethicUtf8ErrorStrings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        int utf16CodeUnitCountAdjustment, scalarCountAdjustment;
                        byte* invalidBytePointer = Competition.Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8String.Length, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
                    }
                }
            }
        }

        [Benchmark]
        public void SIMDUtf8ValidationErrorUtf8()
        {
            foreach (var utf8String in SynthethicUtf8ErrorStrings)
            {
                unsafe
                {
                    fixed (byte* pInput = utf8String)
                    {
                        byte* invalidBytePointer = Utf8Utility.GetPointerToFirstInvalidByte(pInput, utf8String.Length);
                    }
                }
            }
        }


    }

    public class RealDataBenchmark : BenchmarkBase
    {

        // Parameters and variables for real data
        [Params(@"data/french.utf8.txt",
                @"data/arabic.utf8.txt",
                @"data/chinese.utf8.txt",
                @"data/english.utf8.txt",
                @"data/turkish.utf8.txt",
                @"data/german.utf8.txt",
                @"data/japanese.utf8.txt")]
        public string FileName;

        private string[] _lines;
        private byte[][] _linesUtf8;
        private byte[][] _linesUtf8WithErrors;

        private byte[] _allLinesUtf8;
        private byte[] _allLinesUtf8WithErrors;


        public unsafe delegate byte* Utf8ValidationFunction(byte* pUtf8, int length);
        public unsafe delegate byte* CompetitionUtf8ValidationFunction(byte* pUtf8, int length, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment);
        public unsafe delegate nuint ASCIIValidationFunction(byte* pUtf8, nuint length);




        // public void RunUtf8ValidationBenchmark(byte[][] data, Utf8ValidationFunction validationFunction)
        // {
        //     foreach (var line in data)
        //     {
        //         unsafe
        //         {
        //             fixed (byte* pUtf8 = line)
        //             {
        //                 validationFunction(pUtf8, line.Length);
        //             }
        //         }
        //     }
        // }

        public void RunUtf8ValidationBenchmark(byte[] data, Utf8ValidationFunction validationFunction)
        {
            unsafe
            {
                fixed (byte* pUtf8 = data)
                {
                    validationFunction(pUtf8, data.Length);
                }
            }
        }

        public void RunCompetitionUtf8ValidationBenchmark(byte[] data, CompetitionUtf8ValidationFunction validationFunction)
        {
            unsafe
            {
                fixed (byte* pUtf8 = data)
                {
                    int utf16CodeUnitCountAdjustment, scalarCountAdjustment;
                    validationFunction(pUtf8, data.Length, out utf16CodeUnitCountAdjustment, out scalarCountAdjustment);
                }
            }
        }

        public void RunAsciiValidationBenchmark(byte[] data, ASCIIValidationFunction validationFunction)
        {
            unsafe
            {
                fixed (byte* pUtf8 = data)
                {
                    nuint result = validationFunction(pUtf8, (nuint)data.Length);
                }
            }
        }


        [GlobalSetup]
        public void Setup()
        {
            // Common setup
            // We reset rd so that all data is generated with the same.
            // rd = new Random(12345); // fixed seed

            // For real data only:
            Console.WriteLine("reading data");
            _lines = System.IO.File.ReadAllLines(FileName);
            _linesUtf8 = Array.ConvertAll(_lines, System.Text.Encoding.UTF8.GetBytes);

            // Introduce errors to UTF-8 data
            _linesUtf8WithErrors = new byte[_linesUtf8.Length][];
            for (int i = 0; i < _linesUtf8.Length; i++)
            {
                // Only process lines that are at least 2 bytes long
                if (_linesUtf8[i].Length > 1)
                {
                    byte[] modifiedLine = new byte[_linesUtf8[i].Length];
                    Array.Copy(_linesUtf8[i], modifiedLine, _linesUtf8[i].Length);
                    IntroduceError(modifiedLine, rd); // Assuming IntroduceError modifies the array in-place
                    _linesUtf8WithErrors[i] = modifiedLine;
                }
                else
                {
                    // For lines that are too short, just copy them as is
                    _linesUtf8WithErrors[i] = _linesUtf8[i];
                }
            }

            _allLinesUtf8 = _linesUtf8.SelectMany(line => line).ToArray();

            _allLinesUtf8WithErrors = new byte[_allLinesUtf8.Length];
            Array.Copy(_allLinesUtf8, _allLinesUtf8WithErrors, _allLinesUtf8.Length);
            IntroduceError(_allLinesUtf8WithErrors, rd);


        }

        // Synthetic Benchmarks:
        //      |                                     Method |    N |         Mean |      Error |     StdDev |
        // |------------------------------------------- |----- |-------------:|-----------:|-----------:|
        // |          Error_GetIndexOfFirstNonAsciiByte |  100 |     13.47 us |   0.269 us |   0.377 us |
        // |  Error_Runtime_GetIndexOfFirstNonAsciiByte |  100 |     24.37 us |   0.471 us |   0.463 us |
        // |        allAsciiGetIndexOfFirstNonAsciiByte |  100 |     17.55 us |   0.306 us |   0.286 us |
        // | AllAsciiRuntimeGetIndexOfFirstNonAsciiByte |  100 |     22.06 us |   0.286 us |   0.267 us |
        // |              ScalarUtf8ValidationValidUtf8 |  100 |    230.54 us |   2.784 us |   2.604 us |
        // |         CompetitionUtf8ValidationValidUtf8 |  100 |    175.41 us |   0.447 us |   0.396 us |
        // |                SIMDUtf8ValidationValidUtf8 |  100 |    205.84 us |   1.259 us |   1.178 us |
        // |              ScalarUtf8ValidationErrorUtf8 |  100 |    108.72 us |   1.088 us |   1.018 us |
        // |         CompetitionUtf8ValidationErrorUtf8 |  100 |     84.13 us |   0.833 us |   0.780 us |
        // |                SIMDUtf8ValidationErrorUtf8 |  100 |    206.34 us |   2.080 us |   1.844 us |
        // |          Error_GetIndexOfFirstNonAsciiByte | 8000 |     18.82 us |   0.330 us |   0.324 us |
        // |  Error_Runtime_GetIndexOfFirstNonAsciiByte | 8000 |     32.85 us |   0.476 us |   0.445 us |
        // |        allAsciiGetIndexOfFirstNonAsciiByte | 8000 |  1,740.73 us |  34.648 us |  51.859 us |
        // | AllAsciiRuntimeGetIndexOfFirstNonAsciiByte | 8000 |  1,756.78 us |  34.450 us |  52.608 us |
        // |              ScalarUtf8ValidationValidUtf8 | 8000 | 20,267.28 us | 141.247 us | 125.212 us |
        // |         CompetitionUtf8ValidationValidUtf8 | 8000 | 14,469.73 us | 142.729 us | 133.509 us |
        // |                SIMDUtf8ValidationValidUtf8 | 8000 | 10,803.09 us |  72.169 us |  63.976 us |
        // |              ScalarUtf8ValidationErrorUtf8 | 8000 |  9,977.49 us |  10.584 us |   9.383 us |
        // |         CompetitionUtf8ValidationErrorUtf8 | 8000 |  7,125.99 us |  31.785 us |  28.177 us |
        // |                SIMDUtf8ValidationErrorUtf8 | 8000 | 11,066.96 us |  46.763 us |  41.454 us |
        // |                             RuntimeIsAscii |  100 |     26.05 us |   0.392 us |   0.367 us |
        // |                             RuntimeIsAscii | 8000 |  3,087.97 us |  54.602 us |  51.075 us |
        // |                         FastUnicodeIsAscii |  100 |     38.20 us |   0.718 us |   0.769 us |
        // |                         FastUnicodeIsAscii | 8000 |  3,936.35 us |  35.294 us |  29.472 us |



        // Real data Benchmarks:
        // |                                         Method |               FileName |           Mean |         Error |        StdDev |         Median |
        // |----------------------------------------------- |----------------------- |---------------:|--------------:|--------------:|---------------:|
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |   data/arabic.utf8.txt |       2.680 ns |     0.0768 ns |     0.1365 ns |       2.607 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |   data/arabic.utf8.txt |       3.018 ns |     0.0143 ns |     0.0134 ns |       3.013 ns |
        // |              CompetitionUtf8ValidationRealData |   data/arabic.utf8.txt | 197,648.829 ns |   812.2495 ns |   720.0380 ns | 197,508.363 ns |
        // |                   ScalarUtf8ValidationRealData |   data/arabic.utf8.txt | 443,182.471 ns |   798.9335 ns |   708.2337 ns | 442,962.399 ns |
        // |                     SIMDUtf8ValidationRealData |   data/arabic.utf8.txt | 485,398.969 ns | 9,210.3219 ns | 8,615.3408 ns | 484,244.760 ns |
        // |                  ScalarUtf8ValidationErrorData |   data/arabic.utf8.txt | 273,304.429 ns | 3,837.0198 ns | 3,401.4181 ns | 271,987.183 ns |
        // |                    SIMDUtf8ValidationErrorData |   data/arabic.utf8.txt | 463,201.056 ns | 4,938.9873 ns | 4,378.2836 ns | 462,211.430 ns |
        // |             CompetitionUtf8ValidationErrorData |   data/arabic.utf8.txt | 132,324.360 ns | 1,319.6822 ns | 1,169.8639 ns | 132,072.994 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |  data/chinese.utf8.txt |       2.673 ns |     0.0331 ns |     0.0293 ns |       2.669 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |  data/chinese.utf8.txt |       3.015 ns |     0.0310 ns |     0.0259 ns |       3.005 ns |
        // |              CompetitionUtf8ValidationRealData |  data/chinese.utf8.txt |  28,828.583 ns |   138.0568 ns |   107.7857 ns |  28,830.839 ns |
        // |                   ScalarUtf8ValidationRealData |  data/chinese.utf8.txt | 108,065.561 ns |   988.9390 ns |   772.0985 ns | 108,121.367 ns |
        // |                     SIMDUtf8ValidationRealData |  data/chinese.utf8.txt | 169,712.953 ns |   350.0369 ns |   292.2967 ns | 169,786.395 ns |
        // |                  ScalarUtf8ValidationErrorData |  data/chinese.utf8.txt |  16,007.430 ns |   311.2779 ns |   275.9398 ns |  16,021.233 ns |
        // |                    SIMDUtf8ValidationErrorData |  data/chinese.utf8.txt | 173,279.029 ns | 1,738.2233 ns | 1,625.9351 ns | 172,349.532 ns |
        // |             CompetitionUtf8ValidationErrorData |  data/chinese.utf8.txt |   5,248.688 ns |    52.9922 ns |    49.5690 ns |   5,232.658 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |  data/english.utf8.txt |      21.936 ns |     0.2246 ns |     0.2101 ns |      21.951 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |  data/english.utf8.txt |      21.349 ns |     0.2400 ns |     0.2245 ns |      21.348 ns |
        // |              CompetitionUtf8ValidationRealData |  data/english.utf8.txt |  15,377.374 ns |   306.9761 ns |   459.4674 ns |  15,229.054 ns |
        // |                   ScalarUtf8ValidationRealData |  data/english.utf8.txt |  11,242.030 ns |   220.0386 ns |   195.0585 ns |  11,309.270 ns |
        // |                     SIMDUtf8ValidationRealData |  data/english.utf8.txt |  43,552.394 ns |   468.8692 ns |   415.6404 ns |  43,438.472 ns |
        // |                  ScalarUtf8ValidationErrorData |  data/english.utf8.txt |  11,055.396 ns |   212.5761 ns |   188.4432 ns |  10,954.137 ns |
        // |                    SIMDUtf8ValidationErrorData |  data/english.utf8.txt |  42,467.983 ns |   128.7759 ns |   100.5397 ns |  42,466.673 ns |
        // |             CompetitionUtf8ValidationErrorData |  data/english.utf8.txt |  11,059.229 ns |   152.4759 ns |   135.1659 ns |  11,046.243 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |   data/french.utf8.txt |       3.027 ns |     0.0417 ns |     0.0369 ns |       3.013 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |   data/french.utf8.txt |       4.198 ns |     0.0214 ns |     0.0189 ns |       4.202 ns |
        // |              CompetitionUtf8ValidationRealData |   data/french.utf8.txt |  72,845.829 ns | 1,404.7485 ns | 1,379.6509 ns |  72,385.557 ns |
        // |                   ScalarUtf8ValidationRealData |   data/french.utf8.txt |  12,878.832 ns |   256.9766 ns |   263.8961 ns |  12,801.300 ns |
        // |                     SIMDUtf8ValidationRealData |   data/french.utf8.txt | 292,669.021 ns | 2,573.6879 ns | 2,149.1460 ns | 293,155.206 ns |
        // |                  ScalarUtf8ValidationErrorData |   data/french.utf8.txt |  12,857.911 ns |   159.8981 ns |   141.7455 ns |  12,823.008 ns |
        // |                    SIMDUtf8ValidationErrorData |   data/french.utf8.txt | 330,673.690 ns | 4,954.0357 ns | 4,634.0080 ns | 329,306.929 ns |
        // |             CompetitionUtf8ValidationErrorData |   data/french.utf8.txt |  22,421.218 ns |   221.1434 ns |   206.8577 ns |  22,429.289 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |   data/german.utf8.txt |       4.474 ns |     0.1098 ns |     0.1027 ns |       4.426 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |   data/german.utf8.txt |       5.333 ns |     0.1129 ns |     0.1000 ns |       5.311 ns |
        // |              CompetitionUtf8ValidationRealData |   data/german.utf8.txt |  14,724.289 ns |   249.0448 ns |   232.9566 ns |  14,730.711 ns |
        // |                   ScalarUtf8ValidationRealData |   data/german.utf8.txt |   5,727.147 ns |    25.8582 ns |    21.5928 ns |   5,722.029 ns |
        // |                     SIMDUtf8ValidationRealData |   data/german.utf8.txt |  89,132.695 ns |   261.7445 ns |   218.5685 ns |  89,043.322 ns |
        // |                  ScalarUtf8ValidationErrorData |   data/german.utf8.txt |   5,746.750 ns |    27.6930 ns |    24.5491 ns |   5,742.815 ns |
        // |                    SIMDUtf8ValidationErrorData |   data/german.utf8.txt |  91,011.017 ns |   327.1932 ns |   273.2212 ns |  90,989.386 ns |
        // |             CompetitionUtf8ValidationErrorData |   data/german.utf8.txt |   6,824.207 ns |     6.1107 ns |     5.4170 ns |   6,823.704 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData | data/japanese.utf8.txt |       2.558 ns |     0.0228 ns |     0.0213 ns |       2.550 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData | data/japanese.utf8.txt |       3.092 ns |     0.0135 ns |     0.0119 ns |       3.091 ns |
        // |              CompetitionUtf8ValidationRealData | data/japanese.utf8.txt |  24,311.020 ns |    33.7536 ns |    31.5731 ns |  24,302.439 ns |
        // |                   ScalarUtf8ValidationRealData | data/japanese.utf8.txt | 129,296.970 ns |   632.0247 ns |   560.2734 ns | 129,028.961 ns |
        // |                     SIMDUtf8ValidationRealData | data/japanese.utf8.txt | 157,432.763 ns | 2,518.6585 ns | 2,694.9364 ns | 156,458.796 ns |
        // |                  ScalarUtf8ValidationErrorData | data/japanese.utf8.txt |  73,996.641 ns | 1,169.4449 ns | 1,093.8995 ns |  74,231.917 ns |
        // |                    SIMDUtf8ValidationErrorData | data/japanese.utf8.txt | 154,621.956 ns | 1,465.6442 ns | 1,370.9645 ns | 154,842.774 ns |
        // |             CompetitionUtf8ValidationErrorData | data/japanese.utf8.txt |  17,296.737 ns |   209.9211 ns |   196.3604 ns |  17,240.434 ns |
        // | SimDUnicodeGetIndexOfFirstNonAsciiByteRealData |  data/turkish.utf8.txt |       2.605 ns |     0.0329 ns |     0.0308 ns |       2.594 ns |
        // |     RuntimeGetIndexOfFirstNonAsciiByteRealData |  data/turkish.utf8.txt |       3.005 ns |     0.0227 ns |     0.0201 ns |       3.000 ns |
        // |              CompetitionUtf8ValidationRealData |  data/turkish.utf8.txt |  24,644.066 ns |   416.0595 ns |   368.8259 ns |  24,680.862 ns |
        // |                   ScalarUtf8ValidationRealData |  data/turkish.utf8.txt | 125,255.323 ns | 1,989.3579 ns | 1,860.8466 ns | 125,058.628 ns |
        // |                     SIMDUtf8ValidationRealData |  data/turkish.utf8.txt | 141,474.291 ns | 1,416.7977 ns | 1,255.9542 ns | 140,999.616 ns |
        // |                  ScalarUtf8ValidationErrorData |  data/turkish.utf8.txt |  79,372.679 ns |   990.3425 ns |   877.9128 ns |  79,329.234 ns |
        // |                    SIMDUtf8ValidationErrorData |  data/turkish.utf8.txt | 145,464.801 ns | 2,676.6019 ns | 2,503.6951 ns | 145,141.191 ns |
        // |             CompetitionUtf8ValidationErrorData |  data/turkish.utf8.txt |  21,998.925 ns |   138.4730 ns |   129.5277 ns |  21,939.048 ns |

        [Benchmark]
        public unsafe void SimDUnicodeGetIndexOfFirstNonAsciiByteRealData()
        {
            RunAsciiValidationBenchmark(_allLinesUtf8, SimdUnicode.Ascii.GetIndexOfFirstNonAsciiByte);
        }

        [Benchmark]
        public unsafe void RuntimeGetIndexOfFirstNonAsciiByteRealData()
        {
            RunAsciiValidationBenchmark(_allLinesUtf8, Competition.Ascii.GetIndexOfFirstNonAsciiByte);
        }

        [Benchmark]
        public unsafe void CompetitionUtf8ValidationRealData()
        {
            RunCompetitionUtf8ValidationBenchmark(_allLinesUtf8, Competition.Utf8Utility.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        public unsafe void ScalarUtf8ValidationRealData()
        {
            RunUtf8ValidationBenchmark(_allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        public unsafe void SIMDUtf8ValidationRealData()
        {
            RunUtf8ValidationBenchmark(_allLinesUtf8, Utf8Utility.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        public unsafe void ScalarUtf8ValidationErrorData()
        {
            RunUtf8ValidationBenchmark(_allLinesUtf8WithErrors, SimdUnicode.UTF8.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        public unsafe void SIMDUtf8ValidationErrorData()
        {
            RunUtf8ValidationBenchmark(_allLinesUtf8WithErrors, Utf8Utility.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        public unsafe void CompetitionUtf8ValidationErrorData()
        {
            RunCompetitionUtf8ValidationBenchmark(_allLinesUtf8WithErrors, Competition.Utf8Utility.GetPointerToFirstInvalidByte);
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

            // Create a BenchmarkDotNet config with a custom maximum parameter column width
            var config = DefaultConfig.Instance.With(SummaryStyle.Default.WithMaxParameterColumnWidth(100));

            // Check if a specific argument (e.g., "runall") is provided
            if (args.Length > 0 && args[0] == "runall")
            {
                // Run all benchmarks directly with the custom config
                BenchmarkRunner.Run<SyntheticBenchmark>(config);
                BenchmarkRunner.Run<RealDataBenchmark>(config);
            }
            else
            {
                // Use the interactive BenchmarkSwitcher with the custom config
                var switcher = new BenchmarkSwitcher(new[] { typeof(SyntheticBenchmark), typeof(RealDataBenchmark) });
                switcher.Run(args, config);
            }
        }

    }

}