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

using System.Text.Json;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Services;

public class SettingsTests {
	// ── Round-trip serialization ────────────────────────────────────────────

	[Fact]
	public void Settings_RoundTrip_PreservesAllFields() {
		var original = new Settings {
			IgnoreReadOnlyFolders = true,
			IgnoreReparsePoints = true,
			ExcludeHardLinks = true,
			GeneratePreviewThumbnails = true,
			UseNativeFfmpegBinding = true,
			IncludeSubDirectories = false,
			IncludeImages = false,
			ExtendedFFToolsLogging = true,
			LogExcludedFiles = true,
			AlwaysRetryFailedSampling = true,
			IgnoreBlackPixels = true,
			IgnoreWhitePixels = true,
			CompareHorizontallyFlipped = true,
			IncludeNonExistingFiles = true,
			ScanAgainstEntireDatabase = true,
			FolderMatchMode = FolderMatchMode.DifferentFolderOnly,
			SameFolderDepth = 3,
			UsePHashing = true,
			UseExifCreationDate = true,
			LanguageCode = "de",
			ShowWelcomeGuide = false,
			HardwareAccelerationMode = FFTools.FFHardwareAccelerationMode.cuda,
			AutoDetectHardwareAcceleration = false,
			Threshhold = 7,
			Percent = 98f,
			PercentDurationDifference = 15d,
			DurationDifferenceMinSeconds = 2d,
			DurationDifferenceMaxSeconds = 8d,
			MaxSamplingDurationSeconds = 45d,
			ThumbnailCount = 5,
			ThumbnailMaxWidth = 200,
			MaxDegreeOfParallelism = 4,
			CustomFFArguments = "-preset fast",
			CustomDatabaseFolder = "/tmp/db",
			FilterByFilePathContains = true,
			FilterByFilePathNotContains = true,
			FilterByFileSize = true,
			MaximumFileSize = 5000,
			MinimumFileSize = 100,
			FileSizeTolerancePercent = 25d,
			EnableResolutionPreFilter = false,
			EnablePartialClipDetection = true,
			PartialClipMinRatio = 0.25,
			PartialClipSimilarityThreshold = 0.90,
			PartialClipRequireVisualMatch = false,
			PartialClipVisualThreshold = 0.75,
			NetworkPathTimeoutSeconds = 60,
			NetworkRetryCount = 5,
			DatabaseCheckpointIntervalMinutes = 10,
			TestAutoSerializeField = "auto-serialized-value",
		};
		original.IncludeList.Add("C:/include1");
		original.IncludeList.Add("D:/include2");
		original.BlackList.Add("C:/exclude1");
		original.FilePathContainsTexts.Add("keyword1");
		original.FilePathNotContainsTexts.Add("blocked");

		var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.Settings);
		var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!;

		Assert.Equal(original.IgnoreReadOnlyFolders, restored.IgnoreReadOnlyFolders);
		Assert.Equal(original.IgnoreReparsePoints, restored.IgnoreReparsePoints);
		Assert.Equal(original.ExcludeHardLinks, restored.ExcludeHardLinks);
		Assert.Equal(original.GeneratePreviewThumbnails, restored.GeneratePreviewThumbnails);
		Assert.Equal(original.UseNativeFfmpegBinding, restored.UseNativeFfmpegBinding);
		Assert.Equal(original.IncludeSubDirectories, restored.IncludeSubDirectories);
		Assert.Equal(original.IncludeImages, restored.IncludeImages);
		Assert.Equal(original.ExtendedFFToolsLogging, restored.ExtendedFFToolsLogging);
		Assert.Equal(original.LogExcludedFiles, restored.LogExcludedFiles);
		Assert.Equal(original.AlwaysRetryFailedSampling, restored.AlwaysRetryFailedSampling);
		Assert.Equal(original.IgnoreBlackPixels, restored.IgnoreBlackPixels);
		Assert.Equal(original.IgnoreWhitePixels, restored.IgnoreWhitePixels);
		Assert.Equal(original.CompareHorizontallyFlipped, restored.CompareHorizontallyFlipped);
		Assert.Equal(original.IncludeNonExistingFiles, restored.IncludeNonExistingFiles);
		Assert.Equal(original.ScanAgainstEntireDatabase, restored.ScanAgainstEntireDatabase);
		Assert.Equal(original.FolderMatchMode, restored.FolderMatchMode);
		Assert.Equal(original.SameFolderDepth, restored.SameFolderDepth);
		Assert.Equal(original.UsePHashing, restored.UsePHashing);
		Assert.Equal(original.UseExifCreationDate, restored.UseExifCreationDate);
		Assert.Equal(original.LanguageCode, restored.LanguageCode);
		Assert.Equal(original.ShowWelcomeGuide, restored.ShowWelcomeGuide);
		Assert.Equal(original.HardwareAccelerationMode, restored.HardwareAccelerationMode);
		Assert.Equal(original.AutoDetectHardwareAcceleration, restored.AutoDetectHardwareAcceleration);
		Assert.Equal(original.Threshhold, restored.Threshhold);
		Assert.Equal(original.Percent, restored.Percent);
		Assert.Equal(original.PercentDurationDifference, restored.PercentDurationDifference);
		Assert.Equal(original.DurationDifferenceMinSeconds, restored.DurationDifferenceMinSeconds);
		Assert.Equal(original.DurationDifferenceMaxSeconds, restored.DurationDifferenceMaxSeconds);
		Assert.Equal(original.MaxSamplingDurationSeconds, restored.MaxSamplingDurationSeconds);
		Assert.Equal(original.ThumbnailCount, restored.ThumbnailCount);
		Assert.Equal(original.ThumbnailMaxWidth, restored.ThumbnailMaxWidth);
		Assert.Equal(original.MaxDegreeOfParallelism, restored.MaxDegreeOfParallelism);
		Assert.Equal(original.CustomFFArguments, restored.CustomFFArguments);
		Assert.Equal(original.CustomDatabaseFolder, restored.CustomDatabaseFolder);
		Assert.Equal(original.FilterByFilePathContains, restored.FilterByFilePathContains);
		Assert.Equal(original.FilterByFilePathNotContains, restored.FilterByFilePathNotContains);
		Assert.Equal(original.FilterByFileSize, restored.FilterByFileSize);
		Assert.Equal(original.MaximumFileSize, restored.MaximumFileSize);
		Assert.Equal(original.MinimumFileSize, restored.MinimumFileSize);
		Assert.Equal(original.FileSizeTolerancePercent, restored.FileSizeTolerancePercent);
		Assert.Equal(original.EnableResolutionPreFilter, restored.EnableResolutionPreFilter);
		Assert.Equal(original.EnablePartialClipDetection, restored.EnablePartialClipDetection);
		Assert.Equal(original.PartialClipMinRatio, restored.PartialClipMinRatio);
		Assert.Equal(original.PartialClipSimilarityThreshold, restored.PartialClipSimilarityThreshold);
		Assert.Equal(original.PartialClipRequireVisualMatch, restored.PartialClipRequireVisualMatch);
		Assert.Equal(original.PartialClipVisualThreshold, restored.PartialClipVisualThreshold);
		Assert.Equal(original.NetworkPathTimeoutSeconds, restored.NetworkPathTimeoutSeconds);
		Assert.Equal(original.NetworkRetryCount, restored.NetworkRetryCount);
		Assert.Equal(original.DatabaseCheckpointIntervalMinutes, restored.DatabaseCheckpointIntervalMinutes);
		Assert.Equal(original.IncludeList, restored.IncludeList);
		Assert.Equal(original.BlackList, restored.BlackList);
		Assert.Equal(original.FilePathContainsTexts, restored.FilePathContainsTexts);
		Assert.Equal(original.FilePathNotContainsTexts, restored.FilePathNotContainsTexts);
	}

	/// <summary>
	/// Verifies that a newly added field (<see cref="Settings.TestAutoSerializeField"/>)
	/// is automatically serialized without any manual sync code — the core guarantee
	/// of the composition-based settings approach.
	/// </summary>
	[Fact]
	public void Settings_NewField_AutoSerialized() {
		var original = new Settings { TestAutoSerializeField = "hello-from-test" };
		var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.Settings);
		var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!;
		Assert.Equal("hello-from-test", restored.TestAutoSerializeField);
		// Also verify the field appears in the JSON output
		Assert.Contains("TestAutoSerializeField", json);
		Assert.Contains("hello-from-test", json);
	}

	// ── Validation clamping ─────────────────────────────────────────────────

	[Fact]
	public void Validator_ClampsPercent() {
		var s = new Settings { Percent = 150f };
		SettingsValidator.Validate(s);
		Assert.Equal(100f, s.Percent);

		s.Percent = -10f;
		SettingsValidator.Validate(s);
		Assert.Equal(0f, s.Percent);
	}

	[Fact]
	public void Validator_ClampsPercentDurationDifference() {
		var s = new Settings { PercentDurationDifference = 200d };
		SettingsValidator.Validate(s);
		Assert.Equal(100d, s.PercentDurationDifference);

		s.PercentDurationDifference = -5d;
		SettingsValidator.Validate(s);
		Assert.Equal(0d, s.PercentDurationDifference);
	}

	[Fact]
	public void Validator_ClampsMaxDegreeOfParallelism() {
		var s = new Settings { MaxDegreeOfParallelism = int.MaxValue };
		SettingsValidator.Validate(s);
		Assert.Equal(Environment.ProcessorCount * 2, s.MaxDegreeOfParallelism);
	}

	[Fact]
	public void Validator_ClampsThumbnailCount() {
		var s = new Settings { ThumbnailCount = 100 };
		SettingsValidator.Validate(s);
		Assert.Equal(20, s.ThumbnailCount);

		s.ThumbnailCount = -5;
		SettingsValidator.Validate(s);
		Assert.Equal(0, s.ThumbnailCount);
	}

	[Fact]
	public void Validator_ClampsThumbnailMaxWidth() {
		var s = new Settings { ThumbnailMaxWidth = 5000 };
		SettingsValidator.Validate(s);
		Assert.Equal(960, s.ThumbnailMaxWidth);

		s.ThumbnailMaxWidth = 10;
		SettingsValidator.Validate(s);
		Assert.Equal(48, s.ThumbnailMaxWidth);
	}

	[Fact]
	public void Validator_ClampsPartialClipRatios() {
		var s = new Settings {
			PartialClipMinRatio = 1.5,
			PartialClipSimilarityThreshold = -0.5,
			PartialClipVisualThreshold = 2.0,
		};
		SettingsValidator.Validate(s);
		Assert.Equal(1.0, s.PartialClipMinRatio);
		Assert.Equal(0.0, s.PartialClipSimilarityThreshold);
		Assert.Equal(1.0, s.PartialClipVisualThreshold);
	}

	[Fact]
	public void Validator_EnforcesFileSizeOrdering() {
		var s = new Settings { MinimumFileSize = 500, MaximumFileSize = 100 };
		SettingsValidator.Validate(s);
		Assert.Equal(500, s.MinimumFileSize);
		Assert.Equal(500, s.MaximumFileSize); // Max raised to Min
	}

	[Fact]
	public void Validator_ClampsThreshhold() {
		var s = new Settings { Threshhold = 20 };
		SettingsValidator.Validate(s);
		Assert.Equal((byte)10, s.Threshhold);
	}

	[Fact]
	public void Validator_NonNegativeCheckpointInterval() {
		var s = new Settings { DatabaseCheckpointIntervalMinutes = -3 };
		SettingsValidator.Validate(s);
		Assert.Equal(0, s.DatabaseCheckpointIntervalMinutes);
	}
}
