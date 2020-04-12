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
	public static class LevenshteinSimd
	{

		[MethodImpl(MethodImplOptions.NoInlining)]
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

				for (; rowIndex < sourceLength - 3; rowIndex += 4)
				{
					var diag = Vector128.Create(rowIndex);
					var left = Vector128.Create(rowIndex + 1);

					var sourceV = Sse42.ConvertToVector128Int32((short*)(srcPtr + rowIndex));
					var targetV = Vector128<int>.Zero;
					var one = Vector128.Create(1);

					// First 3  iterations fills the vector
					var shift = Vector128.CreateScalar(-1);
					for (int columnIndex = 0; columnIndex < 4; columnIndex++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 4);
						targetV = Sse42.Insert(targetV, (short)targetPtr[columnIndex], 0);

						//left = Sse42.Insert(left, rowIndex + columnIndex + 1, (byte)columnIndex);
						var leftValue = Vector128.Create(rowIndex + columnIndex + 1);
						left = Sse42.Or(Sse42.And(shift, leftValue), left);
						shift = Sse42.ShiftLeftLogical128BitLane(shift, 4);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse.CompareNotEqual(sourceV.AsSingle(), targetV.AsSingle());
						var next = Sse42.Subtract(diag, match.AsInt32());

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 4);
						up = Sse42.Insert(up, previousRowPtr[columnIndex], 0);

						var tmp = Sse42.Add(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;
					}

					previousRowPtr[0] = Sse42.Extract(left, 3);
					for (int columnIndex = 4; columnIndex < targetLength; columnIndex++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 4);
						targetV = Sse42.Insert(targetV, (short)targetPtr[columnIndex], 0);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse42.CompareNotEqual(sourceV.AsSingle(), targetV.AsSingle());
						var next = Sse42.Subtract(diag, match.AsInt32());

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 4);
						up = Sse42.Insert(up, previousRowPtr[columnIndex], 0);

						var tmp = Sse42.Add(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;

						// Store one value
						previousRowPtr[columnIndex - 3] = Sse42.Extract(next, 3);
					}

					// Finish with last 3 items, dont read any more chars just extract them
					for (int i = targetLength - 3; i < targetLength; i++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 4);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse.CompareNotEqual(sourceV.AsSingle(), targetV.AsSingle());
						var next = Sse42.Subtract(diag, match.AsInt32());

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 4);

						var tmp = Sse42.Add(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;
						// Store one value
						previousRowPtr[i] = Sse42.Extract(next, 3);
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
		private static unsafe void FillRow(int* previousRow, int length)
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
			var rowColumnsRemaining = targetLength;

			Vector128<int> one = Vector128.CreateScalar(1);
			Vector128<int> lastSubstition = Vector128.CreateScalar(lastSubstitutionCost);
			Vector128<int> lastInsertion = Vector128.CreateScalar(lastInsertionCost);
			Vector128<int> localCost;
			Vector128<int> lastDeletion;

			while (rowColumnsRemaining > 0)
			{
				rowColumnsRemaining--;

				localCost = lastSubstition;
				lastDeletion = Vector128.CreateScalar(previousRowPtr[columnIndex]);
				if (sourcePrevChar != targetPtr[columnIndex])
				{
					localCost = Sse2.Add(one,
						Sse41.Min(localCost,
						Sse41.Min(lastInsertion, lastDeletion)));
				}
				lastInsertion = localCost;
				previousRowPtr[columnIndex++] = localCost.GetElement(0);
				lastSubstition = lastDeletion;
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
