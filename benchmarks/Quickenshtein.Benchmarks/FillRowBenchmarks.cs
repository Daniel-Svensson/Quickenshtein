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

		Attributes.LongRunJob]
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
		public unsafe void PointerUnroll2()
		{
			fixed (int* data = _array)
				FillRowUnroll2(data, _array.Length);
		}


		[Benchmark]
		public unsafe void PointerUnroll3()
		{
			fixed (int* data = _array)
				FillRowUnroll3(data, _array.Length);
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
		public unsafe void SSeVector16()
		{
			fixed (ushort* data = _array2)
				FillRowSSeVector16(data, _array2.Length);
		}

		[Benchmark]
		public unsafe void SSeVector16v2()
		{
			fixed (ushort* data = _array2)
				FillRowSSeVector16v2(data, _array2.Length);
		}

		[Benchmark]
		public unsafe void SSeVector()
		{
			fixed (int* data = _array)
				FillRowSSeVector(data, _array.Length);
		}

		[Benchmark]
		public unsafe void SSeVector2()
		{
			fixed (int* data = _array)
				FillRowSSeVector(data, _array.Length);
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



		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowUnroll(int* previousRow, int lenght)
		{
			int i = 0;
			for (; i < lenght - 4; i += 4)
			{
				previousRow[i] = i;
				previousRow[i + 1] = i + 1;
				previousRow[i + 2] = i + 2;
				previousRow[i + 3] = i + 3;
			}

			for (; i < lenght; ++i)
			{
				previousRow[i] = i;
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowUnroll2(int* previousRow, int lenght)
		{
			int* pEnd = previousRow + lenght - 3;
			int i = 0;
			for (; previousRow < pEnd; previousRow += 4, i += 4)
			{
				previousRow[0] = i;
				previousRow[1] = i + 1;
				previousRow[2] = i + 2;
				previousRow[3] = i + 3;
			}

			pEnd += 3;
			for (; previousRow < pEnd; ++i, ++previousRow)
			{
				*previousRow = i;
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowUnroll3(int* previousRow, int lenght)
		{
			int* pEnd = previousRow + lenght - 3;
			int i = 0;
			for (; previousRow < pEnd; previousRow += 4, i += 4)
			{
				previousRow[0] = ++i;
				previousRow[1] = ++i;
				previousRow[2] = ++i;
				previousRow[3] = ++i;
			}

			pEnd += 3;
			for (; previousRow < pEnd; ++previousRow)
			{
				*previousRow = ++i;
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


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRow(int[] previousRow)
		{
			int i = 0;


			for (; i < previousRow.Length; ++i)
			{
				previousRow[i] = i;
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowSSeVector16(ushort* previousRow, int length)
		{
			const int LENGHT = 8;

			int i = 0;
			//int initialCount = Math.Min(count, previousRow.Length);
			for (i = 0; i < LENGHT;)
			{
				previousRow[i] = (ushort)++i;
			}

			var counter1 = Sse2.LoadVector128(previousRow);
			var step = Vector128.Create((ushort)i);

			ushort* pDest = previousRow + i;
			for (; i < (length - (LENGHT - 1)); i += LENGHT)
			{
				counter1 = Sse2.Add(counter1, step);

				Sse2.Store(pDest, counter1);
				pDest += LENGHT;
			}

			for (; i < length;)
			{
				previousRow[i] = (ushort)++i;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowSSeVector16v2(ushort* previousRow, int length)
		{
			const int LENGHT = 8;

			int i = 0;
			//int initialCount = Math.Min(count, previousRow.Length);
			for (i = 0; i < LENGHT;)
			{
				previousRow[i] = (ushort)++i;
			}

			var counter1 = Sse2.LoadVector128(previousRow);
			var step = Vector128.Create((ushort)i);

			ushort* pDest = previousRow + i;
			for (; i < (length - (LENGHT - 1)); i += LENGHT)
			{
				counter1 = Sse2.AddSaturate(counter1, step);

				Sse2.Store(pDest, counter1);
				pDest += LENGHT;
			}

			for (; i < length;)
			{
				previousRow[i] = (ushort)++i;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowSSeVector(int* previousRow, int length)
		{
			const int LENGHT = 4;

			int i = 0;
			//int initialCount = Math.Min(count, previousRow.Length);
			for (i = 0; i < LENGHT;)
			{
				previousRow[i] = ++i;
			}

			var counter1 = Sse2.LoadVector128(previousRow);
			var step = Vector128.Create(i);

			int* pDest = previousRow + i;
			for (; i < (length - (LENGHT - 1)); i += LENGHT)
			{
				counter1 = Sse2.Add(counter1, step);

				Sse2.Store(pDest, counter1);
				pDest += LENGHT;
			}

			for (; i < length;)
			{
				previousRow[i] = ++i;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowSSeVector2(int* previousRow, int length)
		{
			const int LENGHT = 4;

			int i = 0;
			//int initialCount = Math.Min(count, previousRow.Length);
			for (i = 0; i < 2 * LENGHT;)
			{
				previousRow[i] = ++i;
			}

			var counter1 = Sse41.LoadVector128(previousRow);
			var counter2 = Sse41.LoadVector128(previousRow + LENGHT);
			var step = Vector128.Create(i);

			int* pDest = previousRow + i;
			for (; i < (length - (2 * LENGHT-1)); i += 2 * LENGHT)
			{
				counter1 = Sse42.Add(counter1, step);
				counter2 = Sse42.Add(counter2, step);

				Sse42.Store(pDest, counter1);
				Sse42.Store(pDest + LENGHT, counter2);
				pDest += 2 * LENGHT;
			}

			if (i < (length - (LENGHT -1)))
			{
				counter1 = Sse42.Add(counter1, step);
				Sse42.Store(previousRow + i, counter1);
				i += LENGHT;
			}

			for (; i < length;)
			{
				previousRow[i] = ++i;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRowSSe(ushort* previousRow, int length)
		{
			var one = Vector128.CreateScalar((ushort)1);
			var j = one;
			for (int i = 0; i < length; ++i)
			{
				previousRow[i] = j.GetElement(0);
				j = Sse42.AddSaturate(j, one);
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
