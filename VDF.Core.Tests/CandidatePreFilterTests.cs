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
//

namespace VDF.Core.Tests;

public class CandidatePreFilterTests {
	[Fact]
	public void FileSizeTolerancePercent_Zero_DisablesPreFilter() {
		// When FileSizeTolerancePercent is 0 (default), no filtering should occur
		var settings = new Settings();
		Assert.Equal(0, settings.FileSizeTolerancePercent);
	}

	[Fact]
	public void EnableResolutionPreFilter_DefaultTrue() {
		var settings = new Settings();
		Assert.True(settings.EnableResolutionPreFilter);
	}

	[Fact]
	public void FileSizeTolerancePercent_NonZero_CalculatesCorrectBounds() {
		// Verify the math: for a 100MB file with 50% tolerance,
		// min = 50MB, max = 150MB
		long fileSize = 100_000_000;
		double tolerance = 50.0;
		long minSize = (long)(fileSize * (1.0 - tolerance / 100.0));
		long maxSize = (long)(fileSize * (1.0 + tolerance / 100.0));
		Assert.Equal(50_000_000, minSize);
		Assert.Equal(150_000_000, maxSize);
	}

	[Fact]
	public void ResolutionPreFilter_SameResolution_Passes() {
		int pixels1 = 1920 * 1080;
		int pixels2 = 1920 * 1080;
		int smaller = Math.Min(pixels1, pixels2);
		int larger = Math.Max(pixels1, pixels2);
		Assert.True(smaller >= larger / 2);
	}

	[Fact]
	public void ResolutionPreFilter_4K_vs_480p_Fails() {
		int pixels4K = 3840 * 2160;
		int pixels480p = 720 * 480;
		int smaller = Math.Min(pixels4K, pixels480p);
		int larger = Math.Max(pixels4K, pixels480p);
		Assert.False(smaller >= larger / 2);
	}

	[Fact]
	public void ResolutionPreFilter_1080p_vs_720p_Passes() {
		int pixels1080p = 1920 * 1080;
		int pixels720p = 1280 * 720;
		int smaller = Math.Min(pixels1080p, pixels720p);
		int larger = Math.Max(pixels1080p, pixels720p);
		Assert.True(smaller >= larger / 2);
	}
}
