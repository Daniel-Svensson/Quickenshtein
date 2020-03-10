using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

#if !NET472
using System.Runtime.Intrinsics.X86;
#endif

namespace Quickenshtein.Benchmarks
{
	public static class LevenshteinScalar
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

		private static unsafe int CalculateDistance(string sourceString, int sourceLength, string targetString, int targetLength, int startIndex )
		{
			var arrayPool = ArrayPool<int>.Shared;
			var pooledArray = arrayPool.Rent(targetLength);
			Span<int> previousRow = pooledArray;
			ReadOnlySpan<char> source = sourceString.AsSpan().Slice(startIndex, sourceLength);
			ReadOnlySpan<char> target = targetString.AsSpan().Slice(startIndex, targetLength);

			//ArrayPool values are sometimes bigger than allocated, let's trim our span to exactly what we use
			previousRow = previousRow.Slice(0, targetLength);

			fixed (char* targetPtr = target)
			fixed (int* previousRowPtr = previousRow)
			{
				FillRow(previousRowPtr, targetLength);

				//Calculate Single Rows
				for (int rowIndex = 0; rowIndex < source.Length; rowIndex++)
				{
					var lastSubstitutionCost = rowIndex;
					var lastInsertionCost = rowIndex + 1;

					var sourcePrevChar = source[rowIndex];


#if DEBUG
					{
					Console.Write("prev values for row {0}:", rowIndex);
						for (int i = 0; i < targetLength; ++i)
							Console.Write("{0} ", previousRow[i]);
						Console.WriteLine();
					}
#endif

					CalculateRow(previousRowPtr, targetPtr, targetLength, sourcePrevChar, lastInsertionCost, lastSubstitutionCost);
				}
			}

			var result = previousRow[targetLength - 1];
			arrayPool.Return(pooledArray);
			return result;
		}


		/// <summary>
		/// Fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// </summary>
		/// <param name="previousRow"></param>
		private static unsafe void FillRow(int* previousRow, int length)
		{
			for (int i = 0; i < length; ++i)
			{
				previousRow[i] = i + 1;
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
			for (int columnIndex=0; columnIndex < targetLength; ++ columnIndex)
			{
				int localCost = lastSubstitutionCost;
				int lastDeletionCost = previousRowPtr[columnIndex];
				if (sourcePrevChar != targetPtr[columnIndex])
				{
					localCost = Math.Min(lastInsertionCost, localCost);
					localCost = Math.Min(lastDeletionCost, localCost);
					localCost++;
				}
				lastInsertionCost = localCost;
				previousRowPtr[columnIndex] = localCost;
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
