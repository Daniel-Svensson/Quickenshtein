using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Intrinsics;

#if !NET472
using System.Runtime.Intrinsics.X86;
#endif

namespace Quickenshtein.Benchmarks
{
	public static class LevenshteinSimdAvx16
	{
		public static int GetDistance(string source, string target)
		{
			//Shortcut any processing if either string is empty
			if (source == null || source.Length == 0)
			{
				return target?.Length ?? 0;
			}
			if (target == null || target.Length == 0)
			{
				return source.Length;
			}

			//Identify and trim any common prefix or suffix between the strings
			TrimInput_NetFramework(source, target, out var startIndex, out var sourceEnd, out var targetEnd);

			var sourceLength = sourceEnd - startIndex;
			var targetLength = targetEnd - startIndex;

			//Check the trimmed values are not empty
			if (sourceLength == 0)
			{
				return targetLength;
			}
			if (targetLength == 0)
			{
				return sourceLength;
			}

			//Switch around variables so outer loop has fewer iterations
			if (targetLength < sourceLength)
			{
				var tempSource = source;
				source = target;
				target = tempSource;

				var tempSourceLength = sourceLength;
				sourceLength = targetLength;
				targetLength = tempSourceLength;
			}


			return CalculateDistance(source, sourceLength, target, targetLength, startIndex);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe int CalculateDistance(string sourceString, int sourceLength, string targetString, int targetLength, int startIndex)
		{
			var arrayPool = ArrayPool<int>.Shared;
			var pooledArray = arrayPool.Rent(targetLength);
			Span<int> previousRow = pooledArray;
			ReadOnlySpan<char> source = sourceString.AsSpan().Slice(startIndex, sourceLength);
			ReadOnlySpan<char> target = targetString.AsSpan().Slice(startIndex, targetLength);

			//ArrayPool values are sometimes bigger than allocated, let's trim our span to exactly what we use
			previousRow = previousRow.Slice(0, targetLength);

			fixed (char* targetPtr = target)
			fixed (char* srcPtr = source)
			fixed (int* previousRowPtr = previousRow)
			{
				FillRow(previousRowPtr, targetLength);

				var rowIndex = 0;

				//var sourceV = Vector128<short>.Zero;
				const int VECTOR_LENGTH = 16;
				for (; rowIndex < sourceLength - VECTOR_LENGTH-1; rowIndex += VECTOR_LENGTH)
				{
					// todo max
					var temp = Vector128.Create(rowIndex);
					var diag = Sse42.PackUnsignedSaturate(temp, temp).ToVector256();
					var one = Vector256.Create((ushort)1);
					var left = Avx2.AddSaturate(diag, one);

					var sourceV = Avx2.LoadVector256((ushort*)(srcPtr + rowIndex));
					var targetV = Vector256<ushort>.Zero;

					var shift = Vector256.CreateScalar(ushort.MaxValue);
					// First 3  iterations fills the vector
					for (int columnIndex = 0; columnIndex < VECTOR_LENGTH-1; columnIndex++)
					{
						// Shift in the next character
						targetV = ShiftLeft(targetV);

						//targetV = Avx2.Insert(targetV, (ushort)targetPtr[columnIndex], 0);
						targetV = Avx2.Or(targetV, Vector256.CreateScalar((ushort)targetPtr[columnIndex]));

						// Insert "(rowIndex + columnIndex + 1)" from the left
						var leftValue = Vector256.Create(rowIndex + columnIndex + 1);
						left = Avx2.Or(Avx2.And(shift, Avx2.PackUnsignedSaturate(leftValue, leftValue)), left);
						shift = ShiftLeft(shift);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Avx2.CompareEqual(sourceV, targetV);
						var add = Avx2.AndNot(match, one);
						var next = Avx2.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = ShiftLeft(left);
						//up = Sse42.Insert(up, (ushort)previousRowPtr[columnIndex], 0);
						up = Avx2.Or(up, Vector256.CreateScalar((ushort)previousRowPtr[columnIndex]));

						var tmp = Avx2.AddSaturate(Avx2.Min(left, up), one);
						next = Avx2.Min(next, tmp);

						left = next;
						diag = up;
					}

					var writePtr = previousRowPtr;
					*writePtr = left.GetElement(VECTOR_LENGTH-1);
					writePtr++;
					for (int columnIndex = VECTOR_LENGTH; columnIndex < targetLength; columnIndex++)
					{
						// Shift in the next character
						targetV = ShiftLeft(targetV);
						//targetV = Avx2.Insert(targetV, (ushort)targetPtr[columnIndex], 0);
						targetV = Avx2.Or(targetV, Vector256.CreateScalar((ushort)targetPtr[columnIndex]));

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Avx2.CompareEqual(sourceV, targetV);
						var add = Avx2.AndNot(match, one);
						var next = Avx2.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = ShiftLeft(left);
						//up = Sse42.Insert(up, (ushort)previousRowPtr[columnIndex], 0);
						up = Avx2.Or(up, Vector256.CreateScalar((ushort)previousRowPtr[columnIndex]));

						var tmp = Avx2.AddSaturate(Avx2.Min(left, up), one);
						next = Avx2.Min(next, tmp);

						left = next;
						diag = up;

						// Store one value
						*writePtr = next.GetElement(VECTOR_LENGTH - 1);
						writePtr++;
					}

					// Finish with last 3 items, dont read any more chars just extract them
					for (int i = targetLength - (VECTOR_LENGTH - 1); i < previousRow.Length; i++)
					{
						// Shift in the next character
						targetV = ShiftLeft(targetV);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Avx2.CompareEqual(sourceV, targetV);
						var add = Avx2.AndNot(match, one);
						var next = Avx2.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = ShiftLeft(left);

						var tmp = Avx2.AddSaturate(Avx2.Min(left, up), one);
						next = Avx2.Min(next, tmp);

						left = next;
						diag = up;
						// Store one value
						previousRowPtr[i] = left.GetElement(VECTOR_LENGTH - 1);
				//		writePtr++;
					}

#if DEBUG
					if (true)
					{
						Console.Write("prev values for row {0}:", rowIndex);
						for (int i = 0; i < targetLength; ++i)
							Console.Write("{0} ", previousRow[i]);
						Console.WriteLine();
					}
#endif
				}

				//Calculate Single Rows
				for (; rowIndex < sourceLength; rowIndex++)
				{
					var lastSubstitutionCost = rowIndex;
					var lastInsertionCost = rowIndex + 1;
					var sourcePrevChar = source[rowIndex];
#if DEBUG
					Console.Write("prev values for row {0}:", rowIndex);
					for (int i = 0; i < targetLength; ++i)
						Console.Write("{0} ", previousRow[i]);
					Console.WriteLine();
#endif

					CalculateRow(previousRowPtr, targetPtr, targetLength, sourcePrevChar, lastInsertionCost, lastSubstitutionCost);
				}
			}

			var result = previousRow[targetLength - 1];
			arrayPool.Return(pooledArray);
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static Vector256<ushort> ShiftLeft(Vector256<ushort> targetV)
		{
			// Borde vara 0000_0001 ??
			// Blir nu kanske low128 0000
			var mask = Avx2.Permute2x128(targetV, targetV, 0x08);

			// r1 = _mm256_permute2f128_si256(r, r, 0x08);
			return Avx2.AlignRight(targetV, mask, /*(16 - N) */14);
		}


		/// <summary>
		/// Fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// </summary>
		private static unsafe void FillRow(int* previousRow, int length)
		{
			for (int i = 0; i < length; ++i)
			{
				previousRow[i] = i + 1;
			}
		}

		/// <summary>
		/// Fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// </summary>
		private static unsafe void FillRow(ushort* previousRow, int length)
		{
			int end = Min(length, ushort.MaxValue - 1);
			for (ushort i = 0; i < end;)
			{
				previousRow[i] = (ushort)++i;
			}

			for (int i = ushort.MaxValue; i < length;)
			{
				previousRow[i] = ushort.MaxValue;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]

		private static ushort Min(ushort a, ushort b)
		{
			return Sse41.Min(Vector128.CreateScalar(a),
							Vector128.CreateScalar(b))
					.GetElement(0);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]

		private static int Min(int a, int b)
		{
			return Sse41.Min(Vector128.CreateScalar(a),
							Vector128.CreateScalar(b))
					.GetElement(0);
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ushort ToUshort(int a)
		{
			var xmm = Vector128.CreateScalar(a);
			return Sse41.PackUnsignedSaturate(xmm, xmm)
					.GetElement(0);
		}

		private static unsafe void FillRow2(ushort* previousRow, int length)
		{
			var one = Vector128.CreateScalar((ushort)1);
			var j = one;
			for (int i = 0; i < length;)
			{
				previousRow[i] = j.GetElement(0);
				j = Sse42.AddSaturate(j, one);
			}
		}

		private static unsafe void FillRow3(ushort* previousRow, int length)
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
		/// Calculates the costs for an entire row of the virtual matrix.
		/// </summary>
		/// <param name="previousRowPtr"></param>
		/// <param name="targetPtr"></param>
		/// <param name="targetLength"></param>
		/// <param name="sourcePrevChar"></param>
		/// <param name="lastInsertionCost"></param>
		/// <param name="lastSubstitutionCost"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void CalculateRow(int* previousRowPtr, char* targetPtr, int targetLength, char sourcePrevChar, int lastInsertionCost, int lastSubstitutionCost)
		{
			var columnIndex = 0;
			int lastDeletionCost;
			int localCost;

			var rowColumnsRemaining = targetLength;

			while (rowColumnsRemaining > 0)
			{
				rowColumnsRemaining--;

				localCost = lastSubstitutionCost;
				lastDeletionCost = previousRowPtr[columnIndex];
				if (sourcePrevChar != targetPtr[columnIndex])
				{
					localCost = Sse41.Min(
					Vector128.CreateScalar(localCost),
						Sse41.Min(Vector128.CreateScalar(lastInsertionCost),
							Vector128.CreateScalar(lastDeletionCost)))
					.GetElement(0)
						;
					localCost++;
				}
				lastInsertionCost = localCost;
				previousRowPtr[columnIndex++] = localCost;
				lastSubstitutionCost = lastDeletionCost;
			}
		}

		/// <summary>
		/// Specifically used for <see cref="GetDistance(string, string)"/> - helps defer `AsSpan` overhead on .NET Framework
		/// </summary>
		/// <param name="source"></param>
		/// <param name="target"></param>
		/// <param name="startIndex"></param>
		/// <param name="sourceEnd"></param>
		/// <param name="targetEnd"></param>
		private static unsafe void TrimInput_NetFramework(string source, string target, out int startIndex, out int sourceEnd, out int targetEnd)
		{
			sourceEnd = source.Length;
			targetEnd = target.Length;
			startIndex = 0;

			var charactersAvailableToTrim = Math.Min(sourceEnd, targetEnd);

			while (charactersAvailableToTrim > 0 && source[startIndex] == target[startIndex])
			{
				charactersAvailableToTrim--;
				startIndex++;
			}

			while (charactersAvailableToTrim > 0 && source[sourceEnd - 1] == target[targetEnd - 1])
			{
				charactersAvailableToTrim--;
				sourceEnd--;
				targetEnd--;
			}
		}
	}
}
