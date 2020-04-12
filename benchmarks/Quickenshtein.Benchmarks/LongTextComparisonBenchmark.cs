using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Quickenshtein.Benchmarks
{
	public class LongTextComparisonBenchmark
	{
		[ParamsSource(nameof(GetComparisonStrings))]
		public string StringA;

		[ParamsSource(nameof(GetComparisonStrings))]
		public string StringB;

		public static IEnumerable<string> GetComparisonStrings()
		{
			yield return string.Empty;
			yield return Utilities.BuildString("aabcdecbaabcadbab", 8000);
			yield return Utilities.BuildString("babdacbaabcedcbaa", 8000);
		}

		//[Benchmark(Baseline = true)]
		public int Baseline()
		{
			return LevenshteinBaseline.GetDistance(StringA, StringB);
		}

		[Benchmark]
		public int Quickenshtein()
		{
			return global::Quickenshtein.Levenshtein.GetDistance(StringA, StringB);
		}

		[Benchmark(Baseline = true)]
		public int Scalar()
		{
			return LevenshteinScalar.GetDistance(StringA, StringB);
		}


		[Benchmark()]
		public int Simd()
		{
			return LevenshteinSimd.GetDistance(StringA, StringB);
		}

		[Benchmark()]
		public int Simd16()
		{
			return LevenshteinSimd16.GetDistance(StringA, StringB);
		}

		[Benchmark()]
		public int Simd16ushort()
		{
			return LevenshteinSimd16ushort.GetDistance(StringA, StringB);
		}

		[Benchmark()]
		public int Simd16Avx()
		{
			return LevenshteinSimdAvx16.GetDistance(StringA, StringB);
		}

		//[Benchmark]
		public int Fastenshtein()
		{
			return global::Fastenshtein.Levenshtein.Distance(StringA, StringB);
		}
	}
}
