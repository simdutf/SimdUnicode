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
using BenchmarkDotNet.Engines; // Correct namespace for HardwareCounter



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
                                break; // Just introduce one surrogate error // TODO: having a loop that breaks immediately does not make much sense !!!!!!!!!!!!!!!!!!!!!!
                            }
                        }
                        break;
                }
            }
        }


    }


//    [MemoryDiagnoser]
    // [HardwareCounters(
    //     HardwareCounter.BranchInstructions,
    //     HardwareCounter.BranchMispredictions,
    //     HardwareCounter.CacheMisses,
    //     HardwareCounter.TotalCycles,
    //     HardwareCounter.TotalInstructions,
    //     HardwareCounter.L1CacheMisses,
    //     HardwareCounter.L2CacheMisses,
    //     HardwareCounter.L3CacheMisses,
    //     HardwareCounter.InstructionRetired
    // )]



//[MemoryDiagnoser]

    public class RealDataBenchmark : BenchmarkBase
    {

        // Parameters and variables for real data
        [Params(//@"data/french.utf8.txt",
                @"data/arabic.utf8.txt"
                //@"data/chinese.utf8.txt",
                //@"data/english.utf8.txt",
                //@"data/turkish.utf8.txt",
                //@"data/german.utf8.txt",
                //@"data/japanese.utf8.txt"
                )]
        public string? FileName;

        private string[] _lines = Array.Empty<string>();
        private byte[][] _linesUtf8 = Array.Empty<byte[]>();
        private byte[][] _linesUtf8WithErrors = Array.Empty<byte[]>();

        private byte[] _allLinesUtf8 = Array.Empty<byte>();
        private byte[] _allLinesUtf8WithErrors = Array.Empty<byte>();


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
/*
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
        }*/

        [Benchmark]
        public unsafe void SIMDUtf8ValidationRealData()
        {
            RunUtf8ValidationBenchmark(_allLinesUtf8, Utf8Utility.GetPointerToFirstInvalidByte);
        }

/*        [Benchmark]
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
*/
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
            var config = DefaultConfig.Instance.With(summaryStyle: SummaryStyle.Default.WithMaxParameterColumnWidth(100));

            // Check if a specific argument (e.g., "runall") is provided
            //if (args.Length > 0 && args[0] == "runall")
           // {
                // Run all benchmarks directly with the custom config
               // BenchmarkRunner.Run<SyntheticBenchmark>(config);
                BenchmarkRunner.Run<RealDataBenchmark>(config);
          //  }
            //else
           // {
                // Use the interactive BenchmarkSwitcher with the custom config
               // var switcher = new BenchmarkSwitcher(new[] { typeof(SyntheticBenchmark), typeof(RealDataBenchmark) });
               // switcher.Run(args, config);
           // }
        }

    }

}