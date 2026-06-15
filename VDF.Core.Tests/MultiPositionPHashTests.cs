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

using System.Collections.Concurrent;
using System.Reflection;
using VDF.Core.pHash;

namespace VDF.Core.Tests;

public class MultiPositionPHashTests {

	static FileEntry CreateVideoEntry(ulong?[] phashes) {
		var entry = new FileEntry();
		entry._Path = $"test_{Guid.NewGuid()}.mp4";
		entry.Folder = "C:\\test";
		entry.IsImage = false;
		entry.invalid = false;
		entry.mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(100) };
		entry.grayBytes = new ConcurrentDictionary<double, byte[]?>();
		entry.PHashes = new ConcurrentDictionary<double, ulong?>();
		entry.compareGray = new byte[]?[phashes.Length];
		entry.comparePHashes = phashes;
		entry.comparePHash = phashes.Length > 0 ? phashes[0] : null;
		// Populate grayBytes and PHashes dictionaries for each position
		for (int j = 0; j < phashes.Length; j++) {
			float position = (float)(j + 1) / (phashes.Length + 1);
			double idx = entry.GetGrayBytesIndex(position);
			entry.grayBytes[idx] = new byte[1024]; // dummy 32x32 gray bytes
			if (phashes[j].HasValue)
				entry.PHashes[idx] = phashes[j];
		}
		return entry;
	}

	/// <summary>
	/// Invokes the private CheckIfDuplicate method via reflection.
	/// </summary>
	static bool InvokeCheckIfDuplicate(ScanEngine engine, FileEntry entry, byte[]?[]? overrideGray, ulong?[]? overridePHashes, FileEntry compItem, out float difference) {
		var method = typeof(ScanEngine).GetMethod("CheckIfDuplicate",
			BindingFlags.NonPublic | BindingFlags.Instance);
		if (method == null)
			throw new InvalidOperationException("CheckIfDuplicate method not found");

		var parameters = new object?[] { entry, overrideGray, overridePHashes, compItem, null };
		var result = (bool)method.Invoke(engine, parameters)!;
		difference = (float)parameters[4]!;
		return result;
	}

	ScanEngine CreateEngine(int percent = 95, bool usePHashing = true) {
		var engine = new ScanEngine();
		engine.Settings.Percent = percent;
		engine.Settings.UsePHashing = usePHashing;
		return engine;
	}

	[Fact]
	public void SinglePosition_MatchingHashes_ReturnsTrue() {
		// With ThumbnailCount=1, identical pHashes should match
		var engine = CreateEngine(percent: 95, usePHashing: true);
		ulong hash = 0xDEADBEEF_CAFEBABE;
		var entryA = CreateVideoEntry(new ulong?[] { hash });
		var entryB = CreateVideoEntry(new ulong?[] { hash });

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.True(result);
		Assert.Equal(0f, difference, precision: 2);
	}

	[Fact]
	public void SinglePosition_SimilarHashes_ReturnsTrue() {
		// 1-bit difference = 63/64 = 98.4% similarity, should pass at 95%
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { 1UL }); // 1 bit different

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.True(result);
		Assert.True(difference < 0.05f);
	}

	[Fact]
	public void SinglePosition_DifferentHashes_ReturnsFalse() {
		// Completely different hashes should not match at 95%
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { ulong.MaxValue });

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.False(result);
	}

	[Fact]
	public void MultiPosition_AllPositionsPass_ReturnsTrue() {
		// All 3 positions have similar hashes (1-bit difference each)
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL, 0UL, 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { 1UL, 1UL, 1UL }); // 1 bit different per position

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.True(result);
		Assert.True(difference < 0.05f);
	}

	[Fact]
	public void MultiPosition_OnePositionFails_ReturnsFalse() {
		// Position 0 and 1 are similar, but position 2 is completely different
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL, 0UL, 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { 1UL, 1UL, ulong.MaxValue }); // last position fails

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.False(result);
	}

	[Fact]
	public void MultiPosition_FirstPositionFails_ReturnsFalseEarly() {
		// First position fails, should return false immediately
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL, 0UL, 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { ulong.MaxValue, 1UL, 1UL }); // first position fails

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.False(result);
	}

	[Fact]
	public void MultiPosition_NullHashInOnePosition_ReturnsFalse() {
		// If any position has a null pHash, the comparison should fail
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL, null, 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { 1UL, 1UL, 1UL });

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.False(result);
	}

	[Fact]
	public void MultiPosition_FallbackToSinglePosition_WhenComparePHashesNull() {
		// When comparePHashes is null but comparePHash is set, should fall back to single-position mode
		var engine = CreateEngine(percent: 95, usePHashing: true);
		var entryA = CreateVideoEntry(new ulong?[] { 0UL });
		entryA.comparePHashes = null; // Force fallback
		entryA.comparePHash = 0UL;    // Single-position data available

		var entryB = CreateVideoEntry(new ulong?[] { 1UL });
		entryB.comparePHashes = null; // Force fallback
		entryB.comparePHash = 1UL;

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.True(result); // 1-bit difference should pass at 95%
	}

	[Fact]
	public void MultiPosition_AverageSimilarity_WhenAllPass() {
		// When all positions pass, the difference should be the average across positions
		var engine = CreateEngine(percent: 90, usePHashing: true);
		// Position 0: identical (0 diff), Position 1: 1-bit diff (1/64 diff)
		var entryA = CreateVideoEntry(new ulong?[] { 0UL, 0UL });
		var entryB = CreateVideoEntry(new ulong?[] { 0UL, 1UL });

		bool result = InvokeCheckIfDuplicate(engine, entryA, null, null, entryB, out float difference);
		Assert.True(result);
		// Average difference: (0 + 1/64) / 2 = 1/128 ≈ 0.0078
		Assert.True(difference < 0.02f);
	}

	[Fact]
	public void CreateFlippedPHashes_ProducesHashForEachPosition() {
		// CreateFlippedPHashes should produce a pHash for each flipped gray position
		var rng = new Random(42);
		var flippedGray = new byte[]?[3];
		for (int j = 0; j < 3; j++) {
			flippedGray[j] = new byte[1024];
			rng.NextBytes(flippedGray[j]!);
		}

		var method = typeof(ScanEngine).GetMethod("CreateFlippedPHashes",
			BindingFlags.NonPublic | BindingFlags.Static);
		if (method == null)
			throw new InvalidOperationException("CreateFlippedPHashes method not found");

		var result = (ulong?[])method.Invoke(null, new object[] { flippedGray, true })!;
		Assert.Equal(3, result.Length);
		Assert.All(result, h => Assert.NotNull(h));
	}

	[Fact]
	public void CreateFlippedPHashes_NotUsePHashing_ReturnsEmpty() {
		var flippedGray = new byte[]?[1] { new byte[1024] };

		var method = typeof(ScanEngine).GetMethod("CreateFlippedPHashes",
			BindingFlags.NonPublic | BindingFlags.Static);
		if (method == null)
			throw new InvalidOperationException("CreateFlippedPHashes method not found");

		var result = (ulong?[])method.Invoke(null, new object[] { flippedGray, false })!;
		Assert.Empty(result);
	}

	[Fact]
	public void FileEntry_ComparePHashesField_IsSettable() {
		var entry = new FileEntry();
		Assert.Null(entry.comparePHashes);

		var phashes = new ulong?[] { 0xDEAD, 0xBEEF };
		entry.comparePHashes = phashes;
		Assert.Equal(2, entry.comparePHashes!.Length);
		Assert.Equal((ulong?)0xDEAD, entry.comparePHashes[0]);
		Assert.Equal((ulong?)0xBEEF, entry.comparePHashes[1]);
	}

	[Fact]
	public void FileEntry_ComparePHashes_ClearedToNull() {
		var entry = new FileEntry();
		entry.comparePHashes = new ulong?[] { 0xDEAD };
		entry.comparePHashes = null;
		Assert.Null(entry.comparePHashes);
	}
}
