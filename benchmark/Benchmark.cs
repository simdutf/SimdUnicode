using System;
using SimdUnicode;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Filters;
using System.Text;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Columns;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;


namespace SimdUnicodeBenchmarks
{


    public class Speed : IColumn
    {
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            var ourReport = summary.Reports.First(x => x.BenchmarkCase.Equals(benchmarkCase));
            var fileName = (string)benchmarkCase.Parameters["FileName"];
            long length = new System.IO.FileInfo(fileName).Length;
            if (ourReport.ResultStatistics is null)
            {
                return "N/A";
            }
            var mean = ourReport.ResultStatistics.Mean;
            return $"{(length / ourReport.ResultStatistics.Mean):#####.00}";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;

        public string Id { get; } = nameof(Speed);
        public string ColumnName { get; } = "Speed (GB/s)";
        public bool AlwaysShow { get; } = true;
        public ColumnCategory Category { get; } = ColumnCategory.Custom;
        public int PriorityInCategory { get; } = 0;
        public bool IsNumeric { get; } = false;
        public UnitType UnitType { get; } = UnitType.Dimensionless;
        public string Legend { get; } = "The speed in gigabytes per second";
    }


    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 3)]
    [Config(typeof(Config))]
    public class RealDataBenchmark
    {

        private class Config : ManualConfig
        {
            public Config()
            {
                AddColumn(new Speed());


                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    Console.WriteLine("ARM64 system detected.");
                    AddFilter(new AnyCategoriesFilter(["arm64", "scalar", "runtime"]));

                }
                else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    if (Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX-512 support.");
                        AddFilter(new AnyCategoriesFilter(["avx512", "avx", "sse", "scalar", "runtime"]));
                    }
                    else if (Avx2.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX2 support.");
                        AddFilter(new AnyCategoriesFilter(["avx", "sse", "scalar", "runtime"]));
                    }
                    else if (Ssse3.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with Sse4.2 support.");
                        AddFilter(new AnyCategoriesFilter(["sse", "scalar", "runtime"]));
                    }
                    else
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) without relevant SIMD support.");
                        AddFilter(new AnyCategoriesFilter(["scalar", "runtime"]));
                    }
                }
                else
                {
                    AddFilter(new AnyCategoriesFilter(["scalar", "runtime"]));

                }

            }
        }
        // Parameters and variables for real data
        [Params(@"data/Arabic-Lipsum.utf8.txt",
                @"data/Hebrew-Lipsum.utf8.txt",
                @"data/Korean-Lipsum.utf8.txt",
                @"data/Chinese-Lipsum.utf8.txt",
                @"data/Hindi-Lipsum.utf8.txt",
                @"data/Latin-Lipsum.utf8.txt",
                @"data/Emoji-Lipsum.utf8.txt",
                @"data/Japanese-Lipsum.utf8.txt",
                @"data/Russian-Lipsum.utf8.txt",
                @"data/arabic.utf8.txt",
                @"data/chinese.utf8.txt",
                @"data/czech.utf8.txt",
                @"data/english.utf8.txt",
                @"data/esperanto.utf8.txt",
                @"data/french.utf8.txt",
                @"data/german.utf8.txt",
                @"data/greek.utf8.txt",
                @"data/hebrew.utf8.txt",
                @"data/hindi.utf8.txt",
                @"data/japanese.utf8.txt",
                @"data/korean.utf8.txt",
                @"data/persan.utf8.txt",
                @"data/portuguese.utf8.txt",
                @"data/russian.utf8.txt",
                @"data/thai.utf8.txt",
                @"data/turkish.utf8.txt",
                @"data/vietnamese.utf8.txt")]
        public string? FileName;
        public byte[] allLinesUtf8 = new byte[0];


        public unsafe delegate byte* Utf8ValidationFunction(byte* pUtf8, int length);
        public unsafe delegate byte* DotnetRuntimeUtf8ValidationFunction(byte* pUtf8, int length, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment);

        public void RunUtf8ValidationBenchmark(byte[] data, Utf8ValidationFunction validationFunction)
        {
            unsafe
            {
                fixed (byte* pUtf8 = data)
                {
                    var res = validationFunction(pUtf8, data.Length);
                    if (res != pUtf8 + data.Length)
                    {
                        throw new Exception("Invalid UTF-8: I expected the pointer to be at the end of the buffer.");
                    }
                }
            }
        }

        public void RunDotnetRuntimeUtf8ValidationBenchmark(byte[] data, DotnetRuntimeUtf8ValidationFunction validationFunction)
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

        [GlobalSetup]
        public void Setup()
        {
            allLinesUtf8 = FileName == null ? allLinesUtf8 : File.ReadAllBytes(FileName);
        }

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeUtf8ValidationRealData()
        {
            RunDotnetRuntimeUtf8ValidationBenchmark(allLinesUtf8, DotnetRuntime.Utf8Utility.GetPointerToFirstInvalidByte);
        }

        [Benchmark]
        [BenchmarkCategory("default")]
        public unsafe void SIMDUtf8ValidationRealData()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByte);
            }
        }

        [Benchmark]
        [BenchmarkCategory("scalar")]
        public unsafe void Utf8ValidationRealDataScalar()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
            }
        }
        [Benchmark]
        [BenchmarkCategory("arm64")]
        public unsafe void SIMDUtf8ValidationRealDataArm64()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
            }
        }
        [Benchmark]
        [BenchmarkCategory("avx")]
        public unsafe void SIMDUtf8ValidationRealDataAvx2()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
            }
        }
        [Benchmark]
        [BenchmarkCategory("sse")]
        public unsafe void SIMDUtf8ValidationRealDataSse()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByteSse);
            }
        }
        /*
        // TODO: enable this benchmark when the AVX-512 implementation is ready
        [Benchmark]
        [BenchmarkCategory("avx512")]
        public unsafe void SIMDUtf8ValidationRealDataAvx512()
        {
            if (allLinesUtf8 != null)
            {
                RunUtf8ValidationBenchmark(allLinesUtf8, SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx512);
            }
        }*/

    }
    public class Program
    {
        // TODO: adopt BenchmarkSwitcher https://benchmarkdotnet.org/articles/guides/how-to-run.html 
        /*public static void Main(string[] args)
        {
            var config = DefaultConfig.Instance.WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(100));
            BenchmarkRunner.Run<RealDataBenchmark>(config);
        }*/
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);


    }

}