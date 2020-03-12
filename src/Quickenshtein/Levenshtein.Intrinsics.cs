﻿#if NETCOREAPP3_0
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quickenshtein
{
	public static partial class Levenshtein
	{
		private const byte VECTOR256_NUMBER_OF_CHARACTERS = 16;
		private const sbyte VECTOR256_COMPARISON_ALL_EQUAL = -1;

		private const int VECTOR256_FILL_SIZE = 8;
		private static readonly Vector256<int> VECTOR256_SEQUENCE = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8);

		private const int VECTOR128_FILL_SIZE = 4;
		private static readonly Vector128<int> VECTOR128_SEQUENCE = Vector128.Create(1, 2, 3, 4);

		/// <summary>
		/// Using AVX2, calculates the trim offsets at the start and end of the source and target spans where characters are equal.
		/// AVX2 instructions allow for a maximum comparison rate of 16 characters.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="target"></param>
		/// <param name="startIndex"></param>
		/// <param name="sourceEnd"></param>
		/// <param name="targetEnd"></param>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static unsafe void TrimInput_Avx2(ReadOnlySpan<char> source, ReadOnlySpan<char> target, ref int startIndex, ref int sourceEnd, ref int targetEnd)
		{
			var charactersAvailableToTrim = Math.Min(sourceEnd, targetEnd);
			if (charactersAvailableToTrim >= VECTOR256_NUMBER_OF_CHARACTERS)
			{
				fixed (char* sourcePtr = source)
				fixed (char* targetPtr = target)
				{
					var sourceUShortPtr = (ushort*)sourcePtr;
					var targetUShortPtr = (ushort*)targetPtr;

					while (charactersAvailableToTrim >= VECTOR256_NUMBER_OF_CHARACTERS)
					{
						var sectionEquality = Avx2.MoveMask(
							Avx2.CompareEqual(
								Avx.LoadDquVector256(sourceUShortPtr + startIndex),
								Avx.LoadDquVector256(targetUShortPtr + startIndex)
							).AsByte()
						);

						if (sectionEquality != VECTOR256_COMPARISON_ALL_EQUAL)
						{
							break;
						}

						startIndex += VECTOR256_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR256_NUMBER_OF_CHARACTERS;
					}

					while (charactersAvailableToTrim >= VECTOR256_NUMBER_OF_CHARACTERS)
					{
						var sectionEquality = Avx2.MoveMask(
							Avx2.CompareEqual(
								Avx.LoadDquVector256(sourceUShortPtr + (sourceEnd - VECTOR256_NUMBER_OF_CHARACTERS + 1)),
								Avx.LoadDquVector256(targetUShortPtr + (targetEnd - VECTOR256_NUMBER_OF_CHARACTERS + 1))
							).AsByte()
						);

						if (sectionEquality != VECTOR256_COMPARISON_ALL_EQUAL)
						{
							break;
						}

						sourceEnd -= VECTOR256_NUMBER_OF_CHARACTERS;
						targetEnd -= VECTOR256_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR256_NUMBER_OF_CHARACTERS;
					}
				}
			}

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

		/// <summary>
		/// Using AVX2, fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// AVX2 instructions allow for a maximum fill rate of 8 values at once.
		/// </summary>
		/// <param name="previousRow"></param>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static unsafe void FillRow_Avx2(Span<int> previousRow)
		{
			var columnIndex = 0;
			var columnsRemaining = previousRow.Length;

			fixed (int* previousRowPtr = previousRow)
			{
				var lastVector256 = VECTOR256_SEQUENCE;
				var shiftVector256 = Vector256.Create(VECTOR256_FILL_SIZE);

				while (columnsRemaining >= VECTOR256_FILL_SIZE)
				{
					columnsRemaining -= VECTOR256_FILL_SIZE;
					Avx.Store(previousRowPtr + columnIndex, lastVector256);
					lastVector256 = Avx2.Add(lastVector256, shiftVector256);
					columnIndex += VECTOR256_FILL_SIZE;
				}

				if (columnsRemaining > 4)
				{
					columnsRemaining -= 4;
					previousRowPtr[columnIndex] = ++columnIndex;
					previousRowPtr[columnIndex] = ++columnIndex;
					previousRowPtr[columnIndex] = ++columnIndex;
					previousRowPtr[columnIndex] = ++columnIndex;
				}

				while (columnsRemaining > 0)
				{
					columnsRemaining--;
					previousRowPtr[columnIndex] = ++columnIndex;
				}
			}
		}

		/// <summary>
		/// Using SSE2, fills <paramref name="previousRow"/> with a number sequence from 1 to the length of the row.
		/// SSE2 instructions provide a maximum fill rate of 4 values at once.
		/// </summary>
		/// <param name="previousRow"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void FillRow_Sse2(Span<int> previousRow)
		{
			var columnIndex = 0;
			var columnsRemaining = previousRow.Length;

			fixed (int* previousRowPtr = previousRow)
			{
				var lastVector128 = VECTOR128_SEQUENCE;
				var shiftVector128 = Vector128.Create(VECTOR128_FILL_SIZE);

				while (columnsRemaining >= VECTOR128_FILL_SIZE)
				{
					columnsRemaining -= VECTOR128_FILL_SIZE;
					Sse2.Store(previousRowPtr + columnIndex, lastVector128);
					lastVector128 = Sse2.Add(lastVector128, shiftVector128);
					columnIndex += VECTOR128_FILL_SIZE;
				}

				while (columnsRemaining > 0)
				{
					columnsRemaining--;
					previousRowPtr[columnIndex] = ++columnIndex;
				}
			}
		}

		/// <summary>
		/// Using SSE4.1, calculates the costs for an entire row of the virtual matrix.
		/// </summary>
		/// <param name="previousRowPtr"></param>
		/// <param name="targetPtr"></param>
		/// <param name="targetLength"></param>
		/// <param name="sourcePrevChar"></param>
		/// <param name="lastInsertionCost"></param>
		/// <param name="lastSubstitutionCost"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void CalculateRow_Sse41(int* previousRowPtr, char* targetPtr, int targetLength, char sourcePrevChar, int lastInsertionCost, int lastSubstitutionCost)
		{
			var columnIndex = 0;
			var rowColumnsRemaining = targetLength;

			var allOnesVector = Vector128.Create(1);
			var lastInsertionCostVector = Vector128.Create(lastInsertionCost);
			var lastSubstitutionCostVector = Vector128.Create(lastSubstitutionCost);
			var lastDeletionCostVector = Vector128<int>.Zero;

			//Levenshtein Distance inner loop unrolling inspired by CoreLib SpanHelpers
			//https://github.com/dotnet/runtime/blob/4f9ae42d861fcb4be2fcd5d3d55d5f227d30e723/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.T.cs#L62-L118
			while (rowColumnsRemaining >= 8)
			{
				rowColumnsRemaining -= 8;
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
			}

			if (rowColumnsRemaining > 4)
			{
				rowColumnsRemaining -= 4;
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
			}

			while (rowColumnsRemaining > 0)
			{
				rowColumnsRemaining--;
				CalculateColumn_Sse41(previousRowPtr, targetPtr, sourcePrevChar, ref lastSubstitutionCostVector, ref lastInsertionCostVector, ref lastDeletionCostVector, ref allOnesVector, ref columnIndex);
			}
		}

		/// <summary>
		/// Using SSE4.1, calculates the cost for an individual cell in the virtual matrix.
		/// SSE4.1 instructions allow a virtually branchless minimum value computation when the source and target characters don't match.
		/// </summary>
		/// <param name="previousRowPtr"></param>
		/// <param name="targetPtr"></param>
		/// <param name="sourcePrevChar"></param>
		/// <param name="lastSubstitutionCostVector"></param>
		/// <param name="lastInsertionCostVector"></param>
		/// <param name="lastDeletionCostVector"></param>
		/// <param name="allOnesVector"></param>
		/// <param name="columnIndex"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void CalculateColumn_Sse41(
			int* previousRowPtr,
			char* targetPtr,
			char sourcePrevChar,
			ref Vector128<int> lastSubstitutionCostVector,
			ref Vector128<int> lastInsertionCostVector,
			ref Vector128<int> lastDeletionCostVector,
			ref Vector128<int> allOnesVector,
			ref int columnIndex
		)
		{
			var localCostVector = lastSubstitutionCostVector;
			lastDeletionCostVector = Vector128.Create(previousRowPtr[columnIndex]);
			if (sourcePrevChar != targetPtr[columnIndex])
			{
				localCostVector = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							lastInsertionCostVector,
							localCostVector
						),
						lastDeletionCostVector
					),
					allOnesVector
				);
			}
			lastInsertionCostVector = localCostVector;
			previousRowPtr[columnIndex++] = localCostVector.GetElement(0);
			lastSubstitutionCostVector = lastDeletionCostVector;
		}

		/// <summary>
		/// Using SSE4.1, calculates the costs for the virtual matrix.
		/// This performs a 4x outer loop unrolling allowing fewer lookups of target character and deletion cost data across the rows.
		/// </summary>
		/// <param name="previousRowPtr"></param>
		/// <param name="source"></param>
		/// <param name="rowIndex"></param>
		/// <param name="targetPtr"></param>
		/// <param name="targetLength"></param>
		private static unsafe void CalculateRows_4Rows_Sse41(int* previousRowPtr, ReadOnlySpan<char> source, ref int rowIndex, char* targetPtr, int targetLength)
		{
			var acceptableRowCount = source.Length - 3;

			Vector128<int> row1Costs, row2Costs, row3Costs, row4Costs, row5Costs;
			char sourceChar1, sourceChar2, sourceChar3, sourceChar4;
			var allOnesVector = Vector128.Create(1);

			for (; rowIndex < acceptableRowCount; rowIndex += 4)
			{
				sourceChar1 = source[rowIndex];
				sourceChar2 = source[rowIndex + 1];
				sourceChar3 = source[rowIndex + 2];
				sourceChar4 = source[rowIndex + 3];
				row1Costs = Vector128.Create(rowIndex); //Sub
				row2Costs = Sse2.Add(row1Costs, allOnesVector); //Insert, Sub
				row3Costs = Sse2.Add(row2Costs, allOnesVector); //Insert, Sub
				row4Costs = Sse2.Add(row3Costs, allOnesVector); //Insert, Sub
				row5Costs = Sse2.Add(row4Costs, allOnesVector); //Insert

				var columnIndex = 0;
				var rowColumnsRemaining = targetLength;

				while (rowColumnsRemaining >= 8)
				{
					rowColumnsRemaining -= 8;
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
				}

				if (rowColumnsRemaining >= 4)
				{
					rowColumnsRemaining -= 4;
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
				}

				while (rowColumnsRemaining > 0)
				{
					rowColumnsRemaining--;
					CalculateColumn_4Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, ref columnIndex);
				}
			}
		}

		/// <summary>
		/// Using SSE4.1, calculates the cost for 4 vertically adjacent cells in the virtual matrix.
		/// Comparing 4 vertically adjacent cells prevents 3 target character lookups, 3 deletion cost lookups and 3 saves of the deletion cost.
		/// SSE4.1 instructions allow a virtually branchless minimum value computation when the source and target characters don't match.
		/// </summary>
		/// <param name="targetPtr"></param>
		/// <param name="previousRowPtr"></param>
		/// <param name="row1Costs"></param>
		/// <param name="row2Costs"></param>
		/// <param name="row3Costs"></param>
		/// <param name="row4Costs"></param>
		/// <param name="row5Costs"></param>
		/// <param name="allOnesVector"></param>
		/// <param name="sourceChar1"></param>
		/// <param name="sourceChar2"></param>
		/// <param name="sourceChar3"></param>
		/// <param name="sourceChar4"></param>
		/// <param name="columnIndex"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void CalculateColumn_4Rows_Sse41(
			char* targetPtr,
			int* previousRowPtr,
			ref Vector128<int> row1Costs,
			ref Vector128<int> row2Costs,
			ref Vector128<int> row3Costs,
			ref Vector128<int> row4Costs,
			ref Vector128<int> row5Costs,
			ref Vector128<int> allOnesVector,
			char sourceChar1,
			char sourceChar2,
			char sourceChar3,
			char sourceChar4,
			ref int columnIndex
		)
		{
			var targetChar = targetPtr[columnIndex];
			var lastDeletionCost = Vector128.Create(previousRowPtr[columnIndex]);
			var localCost = row1Costs;
			if (sourceChar1 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row2Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row1Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row2Costs;
			if (sourceChar2 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row3Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row2Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row3Costs;
			if (sourceChar3 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row4Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row3Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row4Costs;
			if (sourceChar4 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row5Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row4Costs = lastDeletionCost;
			row5Costs = localCost;
			previousRowPtr[columnIndex++] = row5Costs.GetElement(0);
		}


		/// <summary>
		/// Using SSE4.1, calculates the costs for the virtual matrix.
		/// This performs a 8x outer loop unrolling allowing fewer lookups of target character and deletion cost data across the rows.
		/// </summary>
		/// <param name="previousRowPtr"></param>
		/// <param name="source"></param>
		/// <param name="rowIndex"></param>
		/// <param name="targetPtr"></param>
		/// <param name="targetLength"></param>
		private static unsafe void CalculateRows_8Rows_Sse41(int* previousRowPtr, ReadOnlySpan<char> source, ref int rowIndex, char* targetPtr, int targetLength)
		{
			var acceptableRowCount = source.Length - 7;

			Vector128<int> row1Costs, row2Costs, row3Costs, row4Costs, row5Costs, row6Costs, row7Costs, row8Costs, row9Costs;
			char sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8;
			var allOnesVector = Vector128.Create(1);

			for (; rowIndex < acceptableRowCount; rowIndex += 8)
			{
				sourceChar1 = source[rowIndex];
				sourceChar2 = source[rowIndex + 1];
				sourceChar3 = source[rowIndex + 2];
				sourceChar4 = source[rowIndex + 3];
				sourceChar5 = source[rowIndex + 4];
				sourceChar6 = source[rowIndex + 5];
				sourceChar7 = source[rowIndex + 6];
				sourceChar8 = source[rowIndex + 7];
				row1Costs = Vector128.Create(rowIndex); //Sub
				row2Costs = Sse2.Add(row1Costs, allOnesVector); //Insert, Sub
				row3Costs = Sse2.Add(row2Costs, allOnesVector); //Insert, Sub
				row4Costs = Sse2.Add(row3Costs, allOnesVector); //Insert, Sub
				row5Costs = Sse2.Add(row4Costs, allOnesVector); //Insert, Sub
				row6Costs = Sse2.Add(row5Costs, allOnesVector); //Insert, Sub
				row7Costs = Sse2.Add(row6Costs, allOnesVector); //Insert, Sub
				row8Costs = Sse2.Add(row7Costs, allOnesVector); //Insert, Sub
				row9Costs = Sse2.Add(row8Costs, allOnesVector); //Insert

				var columnIndex = 0;
				var rowColumnsRemaining = targetLength;

				while (rowColumnsRemaining >= 8)
				{
					rowColumnsRemaining -= 8;
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
				}

				if (rowColumnsRemaining >= 4)
				{
					rowColumnsRemaining -= 4;
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
				}

				while (rowColumnsRemaining > 0)
				{
					rowColumnsRemaining--;
					CalculateColumn_8Rows_Sse41(targetPtr, previousRowPtr, ref row1Costs, ref row2Costs, ref row3Costs, ref row4Costs, ref row5Costs, ref row6Costs, ref row7Costs, ref row8Costs, ref row9Costs, ref allOnesVector, sourceChar1, sourceChar2, sourceChar3, sourceChar4, sourceChar5, sourceChar6, sourceChar7, sourceChar8, ref columnIndex);
				}
			}
		}

		/// <summary>
		/// Using SSE4.1, calculates the cost for 8 vertically adjacent cells in the virtual matrix.
		/// Comparing 8 vertically adjacent cells prevents 7 target character lookups, 7 deletion cost lookups and 7 saves of the deletion cost.
		/// SSE4.1 instructions allow a virtually branchless minimum value computation when the source and target characters don't match.
		/// </summary>
		/// <param name="targetPtr"></param>
		/// <param name="previousRowPtr"></param>
		/// <param name="row1Costs"></param>
		/// <param name="row2Costs"></param>
		/// <param name="row3Costs"></param>
		/// <param name="row4Costs"></param>
		/// <param name="row5Costs"></param>
		/// <param name="allOnesVector"></param>
		/// <param name="sourceChar1"></param>
		/// <param name="sourceChar2"></param>
		/// <param name="sourceChar3"></param>
		/// <param name="sourceChar4"></param>
		/// <param name="columnIndex"></param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe void CalculateColumn_8Rows_Sse41(
			char* targetPtr,
			int* previousRowPtr,
			ref Vector128<int> row1Costs,
			ref Vector128<int> row2Costs,
			ref Vector128<int> row3Costs,
			ref Vector128<int> row4Costs,
			ref Vector128<int> row5Costs,
			ref Vector128<int> row6Costs,
			ref Vector128<int> row7Costs,
			ref Vector128<int> row8Costs,
			ref Vector128<int> row9Costs,
			ref Vector128<int> allOnesVector,
			char sourceChar1,
			char sourceChar2,
			char sourceChar3,
			char sourceChar4,
			char sourceChar5,
			char sourceChar6,
			char sourceChar7,
			char sourceChar8,
			ref int columnIndex
		)
		{
			var targetChar = targetPtr[columnIndex];
			var lastDeletionCost = Vector128.Create(previousRowPtr[columnIndex]);
			var localCost = row1Costs;
			if (sourceChar1 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row2Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row1Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row2Costs;
			if (sourceChar2 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row3Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row2Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row3Costs;
			if (sourceChar3 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row4Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row3Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row4Costs;
			if (sourceChar4 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row5Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row4Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row5Costs;
			if (sourceChar5 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row6Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row5Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row6Costs;
			if (sourceChar6 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row7Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row6Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row7Costs;
			if (sourceChar7 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row8Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row7Costs = lastDeletionCost;
			lastDeletionCost = localCost;
			localCost = row8Costs;
			if (sourceChar8 != targetChar)
			{
				localCost = Sse2.Add(
					Sse41.Min(
						Sse41.Min(
							row9Costs,
							localCost
						),
						lastDeletionCost
					),
					allOnesVector
				);
			}
			row8Costs = lastDeletionCost;
			row9Costs = localCost;
			previousRowPtr[columnIndex++] = row9Costs.GetElement(0);
		}
	}
}
#endif