﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Quickenshtein.Benchmarks
{
	public static class LevenshteinBaseline
	{
		public static int GetDistance(string source, string target)
		{
			var costMatrix = Enumerable
			  .Range(0, source.Length + 1)
			  .Select(line => new int[target.Length + 1])
			  .ToArray();

			for (var i = 1; i <= source.Length; ++i)
			{
				costMatrix[i][0] = i;
			}

			for (var i = 1; i <= target.Length; ++i)
			{
				costMatrix[0][i] = i;
			}

			for (var i = 1; i <= source.Length; ++i)
			{
				for (var j = 1; j <= target.Length; ++j)
				{
					var insertion = costMatrix[i][j - 1] + 1;
					var deletion = costMatrix[i - 1][j] + 1;
					var substitution = costMatrix[i - 1][j - 1] + (source[i - 1] == target[j - 1] ? 0 : 1);

					costMatrix[i][j] = Math.Min(Math.Min(insertion, deletion), substitution);
				}
			}

			return costMatrix[source.Length][target.Length];
		}
	}
}
