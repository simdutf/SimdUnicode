using System;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using SimdUnicode;

namespace SimdUnicodeBenchmarks
{
    // Speed in GB/s of UTF-16 input (2 bytes per code unit).
    public class Utf16Speed : IColumn
    {
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            if (summary is null || benchmarkCase is null || benchmarkCase.Parameters is null)
            {
                return "N/A";
            }
            var ourReport = summary.Reports.First(x => x.BenchmarkCase.Equals(benchmarkCase));
            if (ourReport is null || ourReport.ResultStatistics is null)
            {
                return "N/A";
            }
            var fileName = (string)benchmarkCase.Parameters["FileName"];
            var errors = (int)benchmarkCase.Parameters["ErrorsPerMillion"];
            long bytes = 2L * UTF16WellFormedBenchmark.Load(fileName, errors).Length;
            return $"{(bytes / ourReport.ResultStatistics.Mean):#####.00}";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;

        public string Id { get; } = nameof(Utf16Speed);
        public string ColumnName { get; } = "Speed (GB/s)";
        public bool AlwaysShow { get; } = true;
        public ColumnCategory Category { get; } = ColumnCategory.Custom;
        public int PriorityInCategory { get; }
        public bool IsNumeric { get; }
        public UnitType UnitType { get; } = UnitType.Dimensionless;
        public string Legend { get; } = "The speed in gigabytes per second (UTF-16 input)";
    }

    // Replacing lone surrogates by U+FFFD (JavaScript's toWellFormed).
    // Run with: dotnet run -c Release --filter "*UTF16WellFormed*"
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    [Config(typeof(Config))]
    public class UTF16WellFormedBenchmark
    {
#pragma warning disable CA1812
        private sealed class Config : ManualConfig
        {
            public Config()
            {
                AddColumn(new Utf16Speed());
            }
        }

        [Params(@"data/twitter.json",
                @"data/Arabic-Lipsum.utf8.txt",
                @"data/Chinese-Lipsum.utf8.txt",
                @"data/Emoji-Lipsum.utf8.txt",
                @"data/Hebrew-Lipsum.utf8.txt",
                @"data/Hindi-Lipsum.utf8.txt",
                @"data/Japanese-Lipsum.utf8.txt",
                @"data/Korean-Lipsum.utf8.txt",
                @"data/Latin-Lipsum.utf8.txt",
                @"data/Russian-Lipsum.utf8.txt")]
#pragma warning disable CA1051
        public string FileName = "";

        // 0: well-formed input (the common case). Otherwise, about this many code units
        // per million are overwritten with lone surrogates.
        [Params(0, 100)]
        public int ErrorsPerMillion;

        private string input = "";
        private char[] output = Array.Empty<char>();
        // The same content, cut into short strings of 1 to 64 code units.
        private string[] shortStrings = Array.Empty<string>();

        public static string Load(string fileName, int errorsPerMillion)
        {
            char[] chars = Encoding.UTF8.GetString(File.ReadAllBytes(fileName)).ToCharArray();
            if (errorsPerMillion > 0)
            {
                var rand = new Random(1234);
                long count = (long)chars.Length * errorsPerMillion / 1_000_000 + 1;
                for (long k = 0; k < count; k++)
                {
                    int pos = rand.Next(chars.Length);
                    chars[pos] = (char)(rand.Next(2) == 0 ? 0xD800 + rand.Next(0x400) : 0xDC00 + rand.Next(0x400));
                }
            }
            return new string(chars);
        }

        [GlobalSetup]
        public void Setup()
        {
            input = Load(FileName, ErrorsPerMillion);
            output = new char[input.Length];
            var rand = new Random(4321);
            var pieces = new System.Collections.Generic.List<string>();
            for (int i = 0; i < input.Length;)
            {
                int len = Math.Min(rand.Next(1, 65), input.Length - i);
                pieces.Add(input.Substring(i, len));
                i += len;
            }
            shortStrings = pieces.ToArray();
            string expected = RunesToWellFormed(input);
            if (UTF16.ToWellFormed(input) != expected || IndexOfAnyToWellFormed(input) != expected)
            {
                throw new InvalidOperationException("Mismatch between implementations.");
            }
        }

        // The idiomatic version: decode rune by rune.
        private static string RunesToWellFormed(string s) => string.Concat(s.EnumerateRunes());

        // The best version we know that uses only public .NET APIs: return the input
        // when possible and skip non-surrogates with the vectorized IndexOfAnyInRange.
        private static string IndexOfAnyToWellFormed(string s)
        {
            int first = NextError(s, 0);
            if (first < 0)
            {
                return s;
            }
            return string.Create(s.Length, (s, first), static (dst, state) =>
            {
                var (src, i) = state;
                src.AsSpan().CopyTo(dst);
                do
                {
                    dst[i] = '\uFFFD';
                    i = NextError(src, i + 1);
                } while (i >= 0);
            });
        }

        private static int NextError(ReadOnlySpan<char> s, int start)
        {
            int i = start;
            while (true)
            {
                int k = s.Slice(i).IndexOfAnyInRange('\uD800', '\uDFFF');
                if (k < 0)
                {
                    return -1;
                }
                i += k;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i += 2;
                }
                else
                {
                    return i;
                }
            }
        }

        [Benchmark]
        [BenchmarkCategory("string")]
        public string StringRunes() => RunesToWellFormed(input);

        [Benchmark]
        [BenchmarkCategory("string")]
        public string StringIndexOfAnyInRange() => IndexOfAnyToWellFormed(input);

        // Encoding.Unicode replaces lone surrogates by U+FFFD when encoding.
        [Benchmark]
        [BenchmarkCategory("string")]
        public string StringEncodingRoundTrip() => Encoding.Unicode.GetString(Encoding.Unicode.GetBytes(input));

        [Benchmark]
        [BenchmarkCategory("string")]
        public string StringSimdUnicode() => UTF16.ToWellFormed(input);

        // Short strings (1 to 64 code units), one call per string.
        [Benchmark]
        [BenchmarkCategory("short")]
        public int ShortIndexOfAnyInRange()
        {
            int count = 0;
            foreach (string s in shortStrings)
            {
                count += IndexOfAnyToWellFormed(s).Length;
            }
            return count;
        }

        [Benchmark]
        [BenchmarkCategory("short")]
        public int ShortSimdUnicode()
        {
            int count = 0;
            foreach (string s in shortStrings)
            {
                count += UTF16.ToWellFormed(s).Length;
            }
            return count;
        }

        // Buffer to buffer: always writes the whole output.
        // A plain copy, as a reference for the best we can hope for.
        [Benchmark]
        [BenchmarkCategory("buffer")]
        public void BufferCopyOnly() => input.AsSpan().CopyTo(output);

        [Benchmark]
        [BenchmarkCategory("buffer")]
        public unsafe void BufferScalar()
        {
            fixed (char* pIn = input)
            fixed (char* pOut = output)
            {
                UTF16.ToWellFormedScalar(pIn, input.Length, pOut);
            }
        }

        [Benchmark]
        [BenchmarkCategory("buffer")]
        public void BufferCopyThenIndexOfAnyInRange()
        {
            input.AsSpan().CopyTo(output);
            Span<char> dst = output;
            int i = NextError(dst, 0);
            while (i >= 0)
            {
                dst[i] = '\uFFFD';
                i = NextError(dst, i + 1);
            }
        }

        [Benchmark]
        [BenchmarkCategory("buffer")]
        public unsafe void BufferSimdUnicode()
        {
            fixed (char* pIn = input)
            fixed (char* pOut = output)
            {
                UTF16.ToWellFormed(pIn, input.Length, pOut);
            }
        }

        // Validation only (isWellFormed).
        [Benchmark]
        [BenchmarkCategory("validate")]
        public bool ValidateIndexOfAnyInRange() => NextError(input, 0) < 0;

        [Benchmark]
        [BenchmarkCategory("validate")]
        public bool ValidateSimdUnicode() => UTF16.IsWellFormed(input);
    }
}
