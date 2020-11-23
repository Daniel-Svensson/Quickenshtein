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
	public static class LevenshteinSimd16ushort
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
			var arrayPool = ArrayPool<ushort>.Shared;
			var pooledArray = arrayPool.Rent(targetLength);
			Span<ushort> previousRow = pooledArray;
			ReadOnlySpan<char> source = sourceString.AsSpan().Slice(startIndex, sourceLength);
			ReadOnlySpan<char> target = targetString.AsSpan().Slice(startIndex, targetLength);

			//ArrayPool values are sometimes bigger than allocated, let's trim our span to exactly what we use
			previousRow = previousRow.Slice(0, targetLength);

			fixed (char* targetPtr = target)
			fixed (char* srcPtr = source)
			fixed (ushort* previousRowPtr = previousRow)
			{
				FillRow(previousRowPtr, targetLength);

				var rowIndex = 0;

				for (; rowIndex < sourceLength - 7; rowIndex += 8)
				{
					// todo max
					var temp = Vector128.Create(rowIndex);
					var diag = Sse42.PackUnsignedSaturate(temp, temp);
					var one = Vector128.Create((ushort)1);
					var left = Sse42.AddSaturate(diag, one);

					var sourceV = Sse42.LoadVector128((ushort*)(srcPtr + rowIndex));
					var targetV = Vector128<ushort>.Zero;

					var shift = Vector128.CreateScalar(ushort.MaxValue);
					// First 3  iterations fills the vector
					for (int columnIndex = 0; columnIndex < 7; columnIndex++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 2);
						targetV = Sse42.Insert(targetV, (ushort)targetPtr[columnIndex], 0);

						// Insert "(rowIndex + columnIndex + 1)" from the left
						var leftValue = Vector128.Create(rowIndex + columnIndex + 1);
						left = Sse42.Or(Sse42.And(shift, Sse42.PackUnsignedSaturate(leftValue, leftValue)), left);
						shift = Sse42.ShiftLeftLogical128BitLane(shift, 2);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse42.CompareEqual(sourceV, targetV);
						var add = Sse42.AndNot(match, one);
						var next = Sse42.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 2);
						up = Sse42.Insert(up, (ushort)previousRowPtr[columnIndex], 0);

						var tmp = Sse42.AddSaturate(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;
					}

					previousRowPtr[0] = Sse42.Extract(left, 7);
					var writePtr = previousRowPtr + 1;
					for (int columnIndex = 8; columnIndex < targetLength; columnIndex++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 2);
						targetV = Sse42.Insert(targetV, (ushort)targetPtr[columnIndex], 0);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse42.CompareEqual(sourceV, targetV);
						var add = Sse42.AndNot(match, one);
						var next = Sse42.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 2);
						up = Sse42.Insert(up, (ushort)previousRowPtr[columnIndex], 0);

						var tmp = Sse42.AddSaturate(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;

						// Store one value
						*writePtr = Sse42.Extract(next, 7);
						writePtr = writePtr + 1;

						// Store one value
						//previousRowPtr[columnIndex - 7] = Sse42.Extract(next, 7);
					}

					// Finish with last 3 items, dont read any more chars just extract them
					for (int i = targetLength - 7; i < previousRow.Length; i++)
					{
						// Shift in the next character
						targetV = Sse42.ShiftLeftLogical128BitLane(targetV, 2);

						// compare source to target
						// alternativ, compare equal and OR with One
						var match = Sse42.CompareEqual(sourceV, targetV);
						var add = Sse42.AndNot(match, one);
						var next = Sse42.AddSaturate(diag, add);

						// Create next diag which is current up
						var up = Sse42.ShiftLeftLogical128BitLane(left, 2);

						var tmp = Sse42.AddSaturate(Sse42.Min(left, up), one);
						next = Sse42.Min(next, tmp);

						left = next;
						diag = up;
						// Store one value
						previousRowPtr[i] = Sse42.Extract(next, 7);
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


		/// <summary>
		/// Fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void FillRow(ushort* previousRow, int length)
		{
			int i = 0;
			//int initialCount = Math.Min(count, previousRow.Length);

			// Jit will do loop onrolling due to loop 0..Vector128<ushort>.Count-1
			for (i = 0; i < Vector128<ushort>.Count; ++i)
			{
				previousRow[i] = (ushort)(i+1);
			}

			var counter1 = Sse2.LoadVector128(previousRow);
			var step = Vector128.Create((ushort)i);

			ushort* pDest = previousRow + i;
			for (; i < (length - (Vector128<ushort>.Count - 1)); i += Vector128<ushort>.Count)
			{
				counter1 = Sse2.AddSaturate(counter1, step);
				Sse2.Store(pDest, counter1);
				pDest += Vector128<ushort>.Count;
			}

			for (; i < length;)
			{
				previousRow[i] = (ushort)++i;
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
		private static unsafe void CalculateRow(ushort* previousRowPtr, char* targetPtr, int targetLength, char sourcePrevChar, int lastInsertionCost, int lastSubstitutionCost)
		{
			static Vector128<ushort> ToUshortScalar(int i)
			{
				var xmm = Vector128.Create(i);
				return Sse41.PackUnsignedSaturate(xmm, xmm);
			}
			var columnIndex = 0;
			var rowColumnsRemaining = targetLength;

			Vector128<ushort> one = Vector128.CreateScalar((ushort)1);
			Vector128<ushort> lastSubstition = ToUshortScalar(lastSubstitutionCost);
			Vector128<ushort> lastInsertion = ToUshortScalar(lastInsertionCost);
			Vector128<ushort> localCost;
			Vector128<ushort> lastDeletion;

			while (rowColumnsRemaining > 0)
			{
				rowColumnsRemaining--;

				localCost = lastSubstition;
				lastDeletion = Vector128.CreateScalar(previousRowPtr[columnIndex]);
				if (sourcePrevChar != targetPtr[columnIndex])
				{
					localCost = Sse2.AddSaturate(one,
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
		[MethodImpl(MethodImplOptions.NoInlining)]
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
