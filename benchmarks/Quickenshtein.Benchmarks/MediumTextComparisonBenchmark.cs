using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Quickenshtein.Benchmarks
{
	[MemoryDiagnoser]
	//	[SimpleJob(RuntimeMoniker.NetCoreApp31, 3, 1, 3, 10)]

	//	[SimpleJob(RuntimeMoniker.Net472)]
	[SimpleJob]
//	[DryJob]

	public class MediumTextComparisonBenchmark
	{
		[ParamsSource(nameof(GetComparisonStrings))]
		public string StringA;

		[ParamsSource(nameof(GetComparisonStrings2))]
		public string StringB;

		public static IEnumerable<string> GetComparisonStrings()
		{
			//			yield return string.Empty;
			yield return Utilities.BuildString("aababbadebaaebebb", 400);
//			yield return Utilities.BuildString("bbebeaabedabbabaa", 400);
		}

		public static IEnumerable<string> GetComparisonStrings2()
		{
			yield return string.Empty;
			yield return Utilities.BuildString("bbebeaabedabbabaa", 400);
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

		public int BaseLine()
		{
			return LevenshteinBaseline.GetDistance(StringA, StringB);
		}

		[Benchmark]
		public int Quickenshtein()
		{
			return global::Quickenshtein.Levenshtein.GetDistance(StringA, StringB);
		}
	}
}
