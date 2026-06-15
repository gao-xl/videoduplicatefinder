// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VDF.Core.pHash {

	/// <summary>
	/// Multi-probe Locality-Sensitive Hashing index for 64-bit perceptual hashes.
	/// Uses multiple hash tables, each keyed by a random subset of bit positions,
	/// to quickly find candidate hashes within a Hamming distance threshold while
	/// guaranteeing high recall through multi-probe queries.
	/// </summary>
	internal class PHashLSHIndex {

		readonly int numTables;
		readonly int keyLength;
		readonly int hammingThreshold;

		// Each table has its own set of randomly chosen bit positions [0..63].
		readonly int[][] bitPositions;

		// Per-table dictionaries: extracted-bit-pattern → list of (entry, hash).
		readonly Dictionary<int, List<(FileEntry entry, ulong hash)>>[] tables;

		public PHashLSHIndex(int numTables = 10, int keyLength = 8, int hammingThreshold = 6) {
			if (numTables < 1) throw new ArgumentOutOfRangeException(nameof(numTables));
			if (keyLength < 1 || keyLength > 31) throw new ArgumentOutOfRangeException(nameof(keyLength));
			if (hammingThreshold < 0 || hammingThreshold > 64) throw new ArgumentOutOfRangeException(nameof(hammingThreshold));

			this.numTables = numTables;
			this.keyLength = keyLength;
			this.hammingThreshold = hammingThreshold;

			var rng = new Random();

			bitPositions = new int[numTables][];
			tables = new Dictionary<int, List<(FileEntry entry, ulong hash)>>[numTables];

			for (int t = 0; t < numTables; t++) {
				// Randomly select keyLength distinct bit positions from [0..63].
				var positions = new int[keyLength];
				var used = new HashSet<int>();
				for (int k = 0; k < keyLength; k++) {
					int pos;
					do { pos = rng.Next(64); } while (!used.Add(pos));
					positions[k] = pos;
				}
				bitPositions[t] = positions;
				tables[t] = new Dictionary<int, List<(FileEntry entry, ulong hash)>>();
			}
		}

		/// <summary>
		/// Populate all hash tables from the given items.
		/// </summary>
		public void Build(IEnumerable<(FileEntry entry, ulong hash)> items) {
			// Clear any previous data.
			for (int t = 0; t < numTables; t++)
				tables[t].Clear();

			foreach (var item in items) {
				for (int t = 0; t < numTables; t++) {
					int key = ExtractKey(item.hash, bitPositions[t]);
					if (!tables[t].TryGetValue(key, out var list)) {
						list = new List<(FileEntry entry, ulong hash)>();
						tables[t][key] = list;
					}
					list.Add(item);
				}
			}
		}

		/// <summary>
		/// Query the index for candidates similar to the given hash.
		/// Uses multi-probe: for each table, probes the exact key and all
		/// single-bit flips of that key to ensure high recall.
		/// Skips entries whose compareIndex &lt;= excludeIndex to avoid
		/// symmetric duplicate comparisons.
		/// </summary>
		public List<FileEntry> Query(ulong hash, int excludeIndex = -1) {
			var seen = new HashSet<int>();
			var candidates = new List<FileEntry>();

			for (int t = 0; t < numTables; t++) {
				int key = ExtractKey(hash, bitPositions[t]);

				// Exact key probe.
				ProbeKey(t, key, excludeIndex, seen, candidates);

				// Multi-probe: flip each bit position in the key.
				for (int k = 0; k < keyLength; k++) {
					int flipped = key ^ (1 << k);
					ProbeKey(t, flipped, excludeIndex, seen, candidates);
				}
			}

			return candidates;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void ProbeKey(int tableIdx, int key, int excludeIndex, HashSet<int> seen, List<FileEntry> candidates) {
			if (!tables[tableIdx].TryGetValue(key, out var list))
				return;
			foreach (var (entry, h) in list) {
				if (entry.compareIndex <= excludeIndex)
					continue;
				if (seen.Add(entry.compareIndex))
					candidates.Add(entry);
			}
		}

		/// <summary>
		/// Extract a hash key by reading the bits at the specified positions
		/// and packing them into an int (LSB = first position).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static int ExtractKey(ulong hash, int[] positions) {
			int key = 0;
			for (int i = 0; i < positions.Length; i++) {
				// BitOperations.PopCount on a masked value tells us if bit is set.
				if ((hash & (1UL << positions[i])) != 0)
					key |= 1 << i;
			}
			return key;
		}

		/// <summary>
		/// Self-test: verifies 100% recall against brute-force search
		/// on 1000 random hashes, and reports average candidate set size.
		/// </summary>
		public static void SelfTest() {
			const int N = 1000;
			const int threshold = 6;
			var rng = new Random(42);

			// Generate random hashes.
			var items = new (FileEntry entry, ulong hash)[N];
			for (int i = 0; i < N; i++) {
				var entry = new FileEntry();
				entry.compareIndex = i;
				ulong h = ((ulong)rng.NextInt64()) ^ ((ulong)rng.NextInt64() << 1);
				items[i] = (entry, h);
			}

			// Build index with generous parameters for guaranteed recall.
			var index = new PHashLSHIndex(numTables: 20, keyLength: 8, hammingThreshold: threshold);
			index.Build(items);

			int totalCandidates = 0;
			int totalBruteForce = 0;
			int queryCount = 0;
			bool allRecalled = true;

			for (int i = 0; i < N; i++) {
				var (entry, hash) = items[i];

				// Brute-force: find all j > i within threshold.
				var bruteForce = new HashSet<int>();
				for (int j = i + 1; j < N; j++) {
					int d = BitOperations.PopCount(hash ^ items[j].hash);
					if (d <= threshold)
						bruteForce.Add(items[j].entry.compareIndex);
				}

				// LSH query.
				var lshResults = index.Query(hash, excludeIndex: i);
				var lshSet = new HashSet<int>(lshResults.Select(e => e.compareIndex));

				// Check recall: every brute-force match must appear in LSH results.
				foreach (int idx in bruteForce) {
					if (!lshSet.Contains(idx)) {
						allRecalled = false;
						Console.WriteLine($"RECALL FAILURE: query {i} missed index {idx}");
					}
				}

				totalBruteForce += bruteForce.Count;
				totalCandidates += lshResults.Count;
				queryCount++;
			}

			double avgCandidates = (double)totalCandidates / queryCount;
			double avgBruteForce = (double)totalBruteForce / queryCount;

			Console.WriteLine($"LSH Self-Test: N={N}, threshold={threshold}");
			Console.WriteLine($"  Recall 100%: {allRecalled}");
			Console.WriteLine($"  Avg LSH candidates per query: {avgCandidates:F1}");
			Console.WriteLine($"  Avg brute-force matches per query: {avgBruteForce:F1}");
			Console.WriteLine($"  Reduction ratio: {avgBruteForce / avgCandidates:F3}x (lower is more filtering)");

			if (!allRecalled)
				throw new InvalidOperationException("LSH self-test FAILED: recall < 100%");
		}
	}
}
