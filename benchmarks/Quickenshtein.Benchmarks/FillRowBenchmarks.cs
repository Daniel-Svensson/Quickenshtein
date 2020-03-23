using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Quickenshtein.Benchmarks
{
	[BenchmarkDotNet.

		Attributes.ShortRunJob]
	public class FillRowBenchmarks
	{
		[Params(40, 400, 4000)]
		public int ArrayCount { get; set; } = 400;
		int[] _array;
		ushort[] _array2;

		[IterationSetup]
		public void Setup()
		{
			_array = new int[ArrayCount];
			_array2 = new ushort[ArrayCount];
		}

		[Benchmark(Baseline = true)]
		public void Simple()
		{
			FillRow(_array);
		}

	//	[Benchmark]
		public unsafe void Pointer()
		{
			fixed (int* data = _array)
				FillRowP(data, _array.Length);
		}

		[Benchmark]
		public unsafe void QuickensteinUnroll()
		{
			Quickenstein(_array.AsSpan());
		}

		[Benchmark]
		public unsafe void PointerUnroll()
		{
			fixed (int* data = _array)
				FillRowUnroll(data, _array.Length);
		}

		[Benchmark]
		public unsafe void Simd()
		{
			FillRowSimd(_array);
		}

		[Benchmark]
		public unsafe void Sse()
		{
			fixed (ushort* data = _array2)
				FillRowSSe(data, _array2.Length);
		}

		[Benchmark]
		public unsafe void SSeVector()
		{
			fixed (ushort* data = _array2)
				FillRowSSeVector(data, _array2.Length);
		}

		[Benchmark]
		public unsafe void QuickensteingAvx()
		{
			Quickenshtein.Levenshtein.FillRow_Avx2(_array);
		}

		[Benchmark]
		public unsafe void QuickensteingSSe()
		{
			Quickenshtein.Levenshtein.FillRow_Sse2(_array);
		}


		private static unsafe void FillRowUnroll(int* previousRow, int lenght)
		{
			int i = 0;
			for (; i < lenght - 4; i += 4)
			{
				previousRow[i] = i;
				previousRow[i+1] = i + 1;
				previousRow[i + 2] = i + 2;
				previousRow[i + 3] = i + 3;
			}

			for (; i < lenght; ++i)
			{
				previousRow[i] = i;
			}
		}
		private static unsafe void FillRowP(int* previousRow, int lenght)
		{
			int i = 0;

			for (; i < lenght; ++i)
			{
				previousRow[i] = i;
			}
		}


		private static unsafe void FillRow(int[] previousRow)
		{
			int i = 0;


			for (; i < previousRow.Length; ++i)
			{
				previousRow[i] = i;
			}
		}

		private static unsafe void FillRowSimd(int[] previousRow)
		{
			// First 
			int i = 0;
			int count = System.Numerics.Vector<int>.Count;
			//int initialCount = Math.Min(count, previousRow.Length);
			for (i = 0; i < previousRow.Length && i < count; ++i)
			{
				previousRow[i] = i;
			}

			var a = new System.Numerics.Vector<int>(previousRow);
			var countV = new System.Numerics.Vector<int>(count);
			for (; i < previousRow.Length - count; i += count)
			{
				a = a + countV;
				a.CopyTo(previousRow, i);
			}

			for (; i < previousRow.Length; ++i)
			{
				previousRow[i] = i;
			}
		}


		private static unsafe void FillRowSSe(ushort* previousRow, int length)
		{
			var one = Vector128.CreateScalar((ushort)1);
			var j = one;
			for (int i = 0; i < length;)
			{
				previousRow[i] = j.GetElement(0);
				j = Sse42.AddSaturate(j, one);
			}
		}

		private static unsafe void FillRowSSeVector(ushort* previousRow, int length)
		{
			ushort* bytes = stackalloc ushort[] { 1, 2, 3, 4, 5, 6, 7, 8 };
			var counter = Sse41.LoadVector128(bytes);
			var step = Vector128.Create((ushort)8);

			int i = 0;
			for (; i < (length & 7); i += 8)
			{
				Sse42.Store(previousRow + i, counter);
				counter = Sse42.AddSaturate(counter, step);
			}
			//step = Sse42.ShiftRightArithmetic
			step = Vector128.Create((ushort)1);
			for (; i < length; ++i)
			{
				previousRow[i] = counter.GetElement(0);
				counter = Sse42.AddSaturate(counter, step);
			}
		}

		/// <summary>
		/// Fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// </summary>
		/// <param name="previousRow"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void Quickenstein(Span<int> previousRow)
		{
			var columnIndex = 0;
			var columnsRemaining = previousRow.Length;

			while (columnsRemaining >= 8)
			{
				columnsRemaining -= 8;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
			}

			if (columnsRemaining > 4)
			{
				columnsRemaining -= 4;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
				previousRow[columnIndex] = ++columnIndex;
			}

			while (columnsRemaining > 0)
			{
				columnsRemaining--;
				previousRow[columnIndex] = ++columnIndex;
			}
		}
	}
}
