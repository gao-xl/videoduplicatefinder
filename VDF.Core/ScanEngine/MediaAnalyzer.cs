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
//

using System.Linq;
using VDF.Core.FFTools;
using VDF.Core.Utils;

namespace VDF.Core;

/// <summary>
/// Handles media information gathering: metadata extraction, gray bytes sampling,
/// and audio fingerprint extraction. Extracted from ScanEngine to improve separation of concerns.
/// </summary>
internal sealed class MediaAnalyzer {
	readonly Settings _settings;
	readonly List<float> _positionList;

	public MediaAnalyzer(Settings settings, List<float> positionList) {
		_settings = settings;
		_positionList = positionList;
	}

	/// <summary>
	/// Gathers media information for a file entry including metadata, gray bytes, and audio fingerprint.
	/// Returns true if the entry is valid, false if it should be skipped.
	/// </summary>
	public bool GatherInfo(FileEntry entry, Action<string, string, int, int>? reportStage, CancellationToken cancellationToken) {
		// Ensure heavy fields are loaded
		DatabaseUtils.EnsureHeavyFieldsLoaded(entry);

		// Skip if all information is already cached (for IncludeNonExistingFiles mode)
		if (_settings.IncludeNonExistingFiles && entry.grayBytes?.Count > 0) {
			bool hasAllInformation = entry.IsImage;
			if (!hasAllInformation) {
				hasAllInformation = true;
				for (int i = 0; i < _positionList.Count; i++) {
					if (entry.grayBytes.ContainsKey(GetGrayBytesIndex(entry, _positionList[i])))
						continue;
					hasAllInformation = false;
					break;
				}
			}
			if (hasAllInformation) {
				// Extract audio fingerprint if needed
				if (_settings.EnablePartialClipDetection &&
					!entry.IsImage &&
					!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
					!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
					!entry.Flags.Has(EntryFlags.SilentAudioTrack) &&
					entry.AudioFingerprint == null) {
					string audioStageLabel = "Extracting audio fingerprint";
					reportStage?.Invoke(entry.Path, audioStageLabel, 0, 100);
					ExtractAudioFingerprint(entry, cancellationToken,
						onProgress: p => reportStage?.Invoke(entry.Path, audioStageLabel, (int)(p * 100), 100));
				}
				return true;
			}
		}

		// Extract media info if not already available
		if (entry.mediaInfo == null && !entry.IsImage) {
			reportStage?.Invoke(entry.Path, "Probing media info", 0, 1);
			MediaInfo? info = FFProbeEngine.GetMediaInfo(entry.Path, _settings.ExtendedFFToolsLogging);
			if (info == null) {
				entry.invalid = true;
				entry.Flags.Set(EntryFlags.MetadataError);
				entry.dirty = true;
				return false;
			}
			entry.mediaInfo = info;
			entry.dirty = true;
		}

		// Initialize gray bytes and PHash dictionaries
		entry.grayBytes ??= new System.Collections.Concurrent.ConcurrentDictionary<double, byte[]?>();
		entry.PHashes ??= new System.Collections.Concurrent.ConcurrentDictionary<double, ulong?>();

		// Extract gray bytes
		if (entry.IsImage && entry.grayBytes.Count == 0) {
			if (!GetGrayBytesFromImage(entry, _settings.UseExifCreationDate, _settings.ExtendedFFToolsLogging)) {
				entry.invalid = true;
				entry.dirty = true;
				return false;
			}
			entry.dirty = true;
		}
		else if (!entry.IsImage) {
			string samplingLabel = "Sampling frames";
			reportStage?.Invoke(entry.Path, samplingLabel, 0, _positionList.Count);
			if (!FfmpegEngine.GetGrayBytesFromVideo(entry, _positionList, _settings.MaxSamplingDurationSeconds,
					_settings.ExtendedFFToolsLogging,
					onSampleComplete: (done) => reportStage?.Invoke(entry.Path, samplingLabel, done, _positionList.Count))) {
				entry.invalid = true;
				entry.dirty = true;
				return false;
			}
		}

		// Extract audio fingerprint (videos only, when enabled)
		if (_settings.EnablePartialClipDetection &&
			!entry.IsImage &&
			!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
			!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
			!entry.Flags.Has(EntryFlags.SilentAudioTrack) &&
			entry.AudioFingerprint == null) {
			string audioLabel = "Extracting audio fingerprint";
			reportStage?.Invoke(entry.Path, audioLabel, 0, 100);
			ExtractAudioFingerprint(entry, cancellationToken,
				onProgress: p => reportStage?.Invoke(entry.Path, audioLabel, (int)(p * 100), 100));
		}

		return true;
	}

	/// <summary>
	/// Extracts audio fingerprint from a file entry.
	/// </summary>
	public static void ExtractAudioFingerprint(FileEntry entry, CancellationToken ct = default, Action<double>? onProgress = null) {
		uint[]? fp = FFTools.ChromaprintEngine.ExtractFingerprint(entry.Path, false, ct, onProgress);
		if (fp == null) {
			entry.Flags.Set(EntryFlags.AudioFingerprintError);
			entry.AudioFingerprint = Array.Empty<uint>();
			entry.dirty = true;
		}
		else if (fp.Length == 0) {
			entry.Flags.Set(EntryFlags.NoAudioTrack);
			entry.AudioFingerprint = Array.Empty<uint>();
			entry.dirty = true;
		}
		else if (IsSilentFingerprint(fp)) {
			entry.Flags.Set(EntryFlags.SilentAudioTrack);
			entry.AudioFingerprint = Array.Empty<uint>();
			entry.dirty = true;
		}
		else {
			entry.AudioFingerprint = fp;
			entry.dirty = true;
		}
	}

	/// <summary>
	/// Checks if a fingerprint is silent (all zeros).
	/// </summary>
	public static bool IsSilentFingerprint(uint[] fp) {
		if (fp.Length == 0) return false;
		for (int i = 0; i < fp.Length; i++)
			if (fp[i] != 0u) return false;
		return true;
	}

	/// <summary>
	/// Gets the gray bytes index for a given position.
	/// </summary>
	public static double GetGrayBytesIndex(FileEntry entry, float position) =>
		entry.mediaInfo?.Duration.TotalSeconds * position ?? 0;

	/// <summary>
	/// Extracts gray bytes from an image file using FFmpeg.
	/// </summary>
	public static bool GetGrayBytesFromImage(FileEntry imageFile, bool useExifIfAvailable, bool extendedLogging) {
		try {
			byte[]? grayBytes;
			int width, height;
			if (!FfmpegEngine.TryGetImageInfoAndGrayBytes(imageFile.Path, out grayBytes, out width, out height, extendedLogging)) {
				// CLI fallback: dimensions via ffprobe, gray bytes via an FFmpeg process.
				MediaInfo? info = FFProbeEngine.GetMediaInfo(imageFile.Path, extendedLogging);
				var stream = info?.Streams?.FirstOrDefault(s => s.Width > 0 && s.Height > 0);
				width = stream?.Width ?? 0;
				height = stream?.Height ?? 0;
				grayBytes = FfmpegEngine.GetThumbnail(new FfmpegSettings {
					File = imageFile.Path,
					Position = TimeSpan.Zero,
					GrayScale = 1,
					SoftwareDecodeOnly = true,
				}, extendedLogging);
			}

			if (grayBytes == null) {
				imageFile.Flags.Set(EntryFlags.ThumbnailError);
				return false;
			}

			imageFile.mediaInfo = new MediaInfo {
				Streams = new[] {
						new MediaInfo.StreamInfo {Height = height, Width = width}
					}
			};

			// Extract EXIF capture date if enabled
			if (useExifIfAvailable) {
				if (ExifReader.TryGetDateTaken(imageFile.Path, out DateTime exifDate)) {
					imageFile.DateCreated = exifDate;
				}
				else {
					// HEIC/HEIF carry the date in the container instead; read it via FFprobe.
					string ext = Path.GetExtension(imageFile.Path);
					if (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
						ext.Equals(".heif", StringComparison.OrdinalIgnoreCase)) {
						var creationTime = FFProbeEngine.GetCreationTime(imageFile.Path);
						if (creationTime.HasValue)
							imageFile.DateCreated = creationTime.Value;
					}
				}
			}

			if (!GrayBytesUtils.VerifyGrayScaleValues(grayBytes)) {
				imageFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Info($"ERROR: Graybytes too dark of: {imageFile.Path}");
				return false;
			}

			imageFile.grayBytes.TryAdd(0, grayBytes);
			return true;
		}
		catch (Exception ex) {
			Logger.Instance.Info(
				$"Exception, file: {imageFile.Path}, reason: {ex.Message}, stacktrace {ex.StackTrace}");
			imageFile.Flags.Set(EntryFlags.ThumbnailError);
			return false;
		}
	}
}
