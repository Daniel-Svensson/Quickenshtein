﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Quickenshtein.Benchmarks
{
	public enum Direction { Forward, Backwards, Both };

	//[BenchmarkDotNet.Attributes.ShortRunJob]
	public class TrimBenchmarks
	{
		public string StringA;
		public string StringB;

		[Params(20, 40, 400, 4000)]
		public int ArrayCount { get; set; } = 400;

		[Params(Direction.Forward, Direction.Backwards, Direction.Both)]
		public Direction Direction { get; set; }

		[GlobalSetup]
		public void GlobalSetup()
		{
			StringB = StringA = Utilities.BuildString("aababbadebaaebebb", ArrayCount);
			switch (Direction)
			{
				case Direction.Forward:
					StringA = StringA + "D";
					break;
				case Direction.Backwards:
					StringA = "D" + StringA;
					break;
				case Direction.Both:
					StringA = StringA.Substring(0, ArrayCount/2) + "D" + StringA.Substring(ArrayCount/2);
					break;
			}
		}

		[Benchmark]
		public unsafe (int, int, int) QuickensteinNetframework()
		{
			int  end1 = StringA.Length, end2 = StringB.Length;

			fixed (char* sourcePtr = StringA)
			fixed (char* targetPtr = StringB)
			{
				int start = Quickenshtein.Internal.DataHelper.GetIndexOfFirstNonMatchingCharacter(sourcePtr, targetPtr, end1, end2);
				Quickenshtein.Internal.DataHelper.TrimLengthOfMatchingCharacters(sourcePtr, targetPtr, ref end1, ref end2);

				return (start, end1, end2);
			}
		}



		[Benchmark]
		public (int, int, int) TrimInputSSe()
		{
			TrimInput_SSe(StringA, StringB, out int start, out int end1, out int end2);
			return (start, end1, end2);
		}

		[Benchmark]
		public (int, int, int) TrimInputAvx()
		{
			TrimInput_Avx(StringA, StringB, out int start, out int end1, out int end2);
			return (start, end1, end2);
		}

		public static unsafe void TrimInput_Simple(string source, string target, out int start, out int end1, out int end2)
		{
			int sourceEnd = source.Length;
			int targetEnd = target.Length;
			int startIndex = 0;

			int charactersAvailableToTrim = Math.Min(sourceEnd, targetEnd);

			fixed (char* sourcePtr = source)
			fixed (char* targetPtr = target)
			{
				ushort* sourceUShortPtr = (ushort*)sourcePtr;
				ushort* targetUShortPtr = (ushort*)targetPtr;

				while (charactersAvailableToTrim > 0 && sourceUShortPtr[startIndex] == targetUShortPtr[startIndex])
				{
					charactersAvailableToTrim--;
					startIndex++;
				}

				while (charactersAvailableToTrim > 0 && sourceUShortPtr[sourceEnd - 1] == targetUShortPtr[targetEnd - 1])
				{
					charactersAvailableToTrim--;
					sourceEnd--;
					targetEnd--;
				}
			}
			start = startIndex;
			end1 = sourceEnd;
			end2 = targetEnd;
		}

		public static unsafe void TrimInput_Avx(string source, string target, out int startIndex, out int sourceEnd, out int targetEnd)
		{
			const int VECTOR256_NUMBER_OF_CHARACTERS = 16;
			const int ALL_EQUAL = -1;
			sourceEnd = source.Length;
			targetEnd = target.Length;
			startIndex = 0;

			int charactersAvailableToTrim = Math.Min(sourceEnd, targetEnd);

			fixed (char* sourcePtr = source)
			fixed (char* targetPtr = target)
			{
				ushort* sourceUShortPtr = (ushort*)sourcePtr;
				ushort* targetUShortPtr = (ushort*)targetPtr;

				if (charactersAvailableToTrim >= VECTOR256_NUMBER_OF_CHARACTERS)
				{
					while (true)
					{
						int sectionEquality = Avx2.MoveMask(
							Avx2.CompareEqual(
								Avx.LoadVector256(sourceUShortPtr + startIndex),
								Avx.LoadVector256(targetUShortPtr + startIndex)
							).AsByte()
						);

						if (sectionEquality != ALL_EQUAL)
						{
							int index = BitOperations.TrailingZeroCount((uint)sectionEquality ^ ALL_EQUAL) >> 1;
							startIndex += index;
							charactersAvailableToTrim -= index;
							break;
						}

						startIndex += VECTOR256_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR256_NUMBER_OF_CHARACTERS;

						if (charactersAvailableToTrim < VECTOR256_NUMBER_OF_CHARACTERS)
						{
							while (charactersAvailableToTrim > 0
								&& sourceUShortPtr[startIndex] == targetUShortPtr[startIndex])
							{
								charactersAvailableToTrim--;
								startIndex++;
							}
							break;
						}
					}

					while (true)
					{
						if (charactersAvailableToTrim < VECTOR256_NUMBER_OF_CHARACTERS)
						{
							while (charactersAvailableToTrim > 0
								&& sourceUShortPtr[sourceEnd - 1] == targetUShortPtr[targetEnd - 1])
							{
								charactersAvailableToTrim--;
								sourceEnd--;
								targetEnd--;
							}
							break;
						}

						int sectionEquality = Avx2.MoveMask(
							Avx2.CompareEqual(
								Avx.LoadVector256(sourceUShortPtr + (sourceEnd - VECTOR256_NUMBER_OF_CHARACTERS + 1)),
								Avx.LoadVector256(targetUShortPtr + (targetEnd - VECTOR256_NUMBER_OF_CHARACTERS + 1))
							).AsByte()
						);

						if (sectionEquality != ALL_EQUAL)
						{
							int index = BitOperations.LeadingZeroCount((uint)(sectionEquality ^ ALL_EQUAL)) >> 1;
							sourceEnd -= index;
							targetEnd -= index;
							charactersAvailableToTrim -= index;
							break;
						}

						sourceEnd -= VECTOR256_NUMBER_OF_CHARACTERS;
						targetEnd -= VECTOR256_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR256_NUMBER_OF_CHARACTERS;
					}
				}
			}
		}


		public static unsafe void TrimInput_SSe(string source, string target, out int startIndex, out int sourceEnd, out int targetEnd)
		{
			const int VECTOR128_NUMBER_OF_CHARACTERS = 8;
			const int ALL_EQUAL = ushort.MaxValue;
			sourceEnd = source.Length;
			targetEnd = target.Length;
			startIndex = 0;

			int charactersAvailableToTrim = Math.Min(sourceEnd, targetEnd);

			fixed (char* sourcePtr = source)
			fixed (char* targetPtr = target)
			{
				ushort* sourceUShortPtr = (ushort*)sourcePtr;
				ushort* targetUShortPtr = (ushort*)targetPtr;

				if (charactersAvailableToTrim >= VECTOR128_NUMBER_OF_CHARACTERS)
				{
					while (true)
					{
						int sectionEquality = Sse2.MoveMask(
							Sse2.CompareEqual(
								Sse2.LoadVector128(sourceUShortPtr + startIndex),
								Sse2.LoadVector128(targetUShortPtr + startIndex)
							).AsByte()
						);

						if (sectionEquality != ALL_EQUAL)
						{
							int index = BitOperations.TrailingZeroCount((uint)sectionEquality ^ ALL_EQUAL) >> 1;
							startIndex += index;
							charactersAvailableToTrim -= index;
							break;
						}

						startIndex += VECTOR128_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR128_NUMBER_OF_CHARACTERS;

						if (charactersAvailableToTrim < VECTOR128_NUMBER_OF_CHARACTERS)
						{
							while (charactersAvailableToTrim > 0
								&& sourceUShortPtr[startIndex] == targetUShortPtr[startIndex])
							{
								charactersAvailableToTrim--;
								startIndex++;
							}
							break;
						}
					}

					while (true)
					{
						if (charactersAvailableToTrim < VECTOR128_NUMBER_OF_CHARACTERS)
						{
							while (charactersAvailableToTrim > 0
								&& sourceUShortPtr[sourceEnd - 1] == targetUShortPtr[targetEnd - 1])
							{
								charactersAvailableToTrim--;
								sourceEnd--;
								targetEnd--;
							}
							break;
						}

						int sectionEquality = Sse2.MoveMask(
							Sse2.CompareEqual(
								Sse2.LoadVector128(sourceUShortPtr + (sourceEnd - VECTOR128_NUMBER_OF_CHARACTERS + 1)),
								Sse2.LoadVector128(targetUShortPtr + (targetEnd - VECTOR128_NUMBER_OF_CHARACTERS + 1))
							).AsByte()
						);

						if (sectionEquality != ALL_EQUAL)
						{
							int index = (BitOperations.LeadingZeroCount((uint)sectionEquality ^ ALL_EQUAL) - 16) >> 1;
							sourceEnd -= index;
							targetEnd -= index;
							charactersAvailableToTrim -= index;
							break;
						}

						sourceEnd -= VECTOR128_NUMBER_OF_CHARACTERS;
						targetEnd -= VECTOR128_NUMBER_OF_CHARACTERS;
						charactersAvailableToTrim -= VECTOR128_NUMBER_OF_CHARACTERS;
					}
				}
			}
		}
	}
}
