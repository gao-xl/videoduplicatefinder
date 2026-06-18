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

namespace VDF.Core {
	/// <summary>
	/// Single source of truth for clamping/validating a <see cref="Settings"/> instance.
	/// Both the Web PUT endpoint and <c>WebSettingsService.Load</c> call this so that
	/// validation rules can never drift apart again.
	/// </summary>
	public static class SettingsValidator {
		/// <summary>
		/// Clamps the numeric fields of <paramref name="s"/> in place to their
		/// valid ranges.  Non-numeric fields (booleans, enums, strings, collections)
		/// are left untouched.
		/// </summary>
		public static void Validate(Settings s) {
			s.Percent = Math.Clamp(s.Percent, 0f, 100f);
			s.PercentDurationDifference = Math.Clamp(s.PercentDurationDifference, 0d, 100d);
			s.MaxDegreeOfParallelism = Math.Clamp(s.MaxDegreeOfParallelism, 0, Environment.ProcessorCount * 2);
			s.ThumbnailCount = Math.Clamp(s.ThumbnailCount, 0, 20);
			s.ThumbnailMaxWidth = Math.Clamp(s.ThumbnailMaxWidth, 48, 960);
			s.Threshhold = Math.Clamp(s.Threshhold, (byte)0, (byte)10);
			s.SameFolderDepth = Math.Max(0, s.SameFolderDepth);
			s.DurationDifferenceMinSeconds = Math.Max(0d, s.DurationDifferenceMinSeconds);
			s.DurationDifferenceMaxSeconds = Math.Max(0d, s.DurationDifferenceMaxSeconds);
			s.MaxSamplingDurationSeconds = Math.Max(0d, s.MaxSamplingDurationSeconds);
			s.MinimumFileSize = Math.Max(0, s.MinimumFileSize);
			s.MaximumFileSize = Math.Max(s.MinimumFileSize, s.MaximumFileSize);
			s.DatabaseCheckpointIntervalMinutes = Math.Max(0, s.DatabaseCheckpointIntervalMinutes);
			s.PartialClipMinRatio = Math.Clamp(s.PartialClipMinRatio, 0d, 1d);
			s.PartialClipSimilarityThreshold = Math.Clamp(s.PartialClipSimilarityThreshold, 0d, 1d);
			s.PartialClipVisualThreshold = Math.Clamp(s.PartialClipVisualThreshold, 0d, 1d);
			s.FileSizeTolerancePercent = Math.Max(0d, s.FileSizeTolerancePercent);
			s.NetworkPathTimeoutSeconds = Math.Max(0, s.NetworkPathTimeoutSeconds);
			s.NetworkRetryCount = Math.Max(0, s.NetworkRetryCount);
		}
	}
}
