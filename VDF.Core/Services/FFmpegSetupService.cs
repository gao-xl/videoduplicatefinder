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

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FFmpeg.AutoGen;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;

namespace VDF.Core.Services {

	/// <summary>
	/// Archive format of a <see cref="FfmpegDownloadPlan"/>. Drives the extraction dispatch
	/// in <see cref="FFmpegSetupService.ExtractArchive"/>.
	/// </summary>
	public enum ArchiveType {
		Zip,
		TarXz,
		TarGz
	}

	/// <summary>
	/// Coarse-grained phase reported through <see cref="FFmpegSetupProgress"/>. Callers map
	/// this to their own UI (GUI localized strings, Web progress-bar percent bands).
	/// </summary>
	public enum FFmpegSetupStage {
		Preparing,
		Downloading,
		Verifying,
		Extracting,
		Installing,
		Completed,
		Failed
	}

	/// <summary>Why a <see cref="DownloadAndInstallAsync"/> run did not succeed.</summary>
	public enum FFmpegSetupFailureReason {
		None,
		NoPlansAvailable,
		DownloadFailed
	}

	/// <summary>
	/// A single candidate FFmpeg archive to download. Mirrors the record previously
	/// duplicated in GUI <c>MainWindowVM_FfmpegDownloader</c> and Web <c>FFmpegSetupService</c>.
	/// </summary>
	public sealed record FfmpegDownloadPlan(Uri DownloadUrl, string ArchiveFileName, ArchiveType ArchiveKind, string DisplayName);

	/// <summary>
	/// Progress payload reported via <see cref="IProgress{T}"/>. Carries the raw data both
	/// frontends need (GUI formats a localized overlay string, Web maps to a 0–100 progress bar).
	/// <see cref="DownloadPercent"/> is the 0–100 fraction of the current archive download and is
	/// only meaningful during the <see cref="FFmpegSetupStage.Downloading"/> stage.
	/// </summary>
	public sealed record FFmpegSetupProgress {
		public FFmpegSetupStage Stage { get; init; }
		/// <summary>Default English status message. GUI ignores this in favour of localized strings.</summary>
		public string StatusMessage { get; init; } = string.Empty;
		public string? DisplayName { get; init; }
		public long? BytesDownloaded { get; init; }
		public long? TotalBytes { get; init; }
		public double DownloadPercent { get; init; }
	}

	/// <summary>
	/// Outcome of <see cref="DownloadAndInstallAsync"/>. <see cref="Exception"/> is set only when
	/// a non-HTTP exception aborted the run (callers type-check it to pick the right message);
	/// per-plan <see cref="HttpRequestException"/>s are absorbed and surfaced via
	/// <see cref="ErrorMessage"/> with <see cref="Exception"/> left null.
	/// </summary>
	public sealed record FFmpegSetupResult(
		bool Success,
		FFmpegSetupFailureReason FailureReason,
		string? ErrorMessage,
		string? TargetFolder,
		string? ExtractedFolder,
		Exception? Exception);

	/// <summary>
	/// Shared FFmpeg download/verify/extract/install pipeline. Replaces the ~900 lines of
	/// duplicated logic between <c>VDF.GUI/ViewModels/MainWindowVM_FfmpegDownloader.cs</c> and
	/// <c>VDF.Web/Services/FFmpegSetupService.cs</c>. Pure BCL + Core deps — no Avalonia
	/// dispatcher, no SignalR. Progress is reported through <see cref="IProgress{T}"/> so each
	/// frontend can forward it to its own UI thread.
	/// </summary>
	public sealed class FFmpegSetupService {

		/// <summary>500 MB safety cap — matches both prior implementations.</summary>
		public const long MaxDownloadBytes = 500 * 1024 * 1024;

		/// <summary>
		/// Maps FFmpeg library version majors to the BtbN/yt-dlp "major" tag used in download URLs.
		/// Returns the highest recognised major across avcodec/avformat/avutil, or 0 if none match.
		/// </summary>
		public static int MapToFfmpegMajor(int avcodecMajor, int avformatMajor, int avutilMajor) {
			int[] majors = { avcodecMajor, avformatMajor, avutilMajor };
			int want = 0;
			foreach (var m in majors) {
				int v = m switch {
					62 => 8,
					61 => 7,
					60 => 6,
					59 => 5,
					_ => 0
				};
				if (v > want) want = v;
			}
			return want;
		}

		/// <summary>
		/// Maps an FFmpeg major (from <see cref="MapToFfmpegMajor"/>) to the version tag embedded
		/// in BtbN/yt-dlp release URLs. BtbN publishes only the latest minor per major; the n8.0
		/// tag was retired when 8.1 landed, so a hardcoded "8.0" 404s on every fresh install.
		/// </summary>
		public static string GetVersionTag(int ffMajor) => ffMajor switch {
			8 => "8.1",
			7 => "7.1",
			6 => "6.1",
			5 => "5.1",
			_ => "7.1"
		};

		/// <summary>
		/// Builds the download plan list for the given platform/architecture. Pure function so
		/// tests can exercise URL construction and architecture branching without touching
		/// <see cref="RuntimeInformation"/>.
		/// </summary>
		public static IReadOnlyList<FfmpegDownloadPlan> BuildDownloadPlans(int ffMajor, bool isWindows, bool isLinux, bool isMacOS, Architecture arch) {
			var plans = new List<FfmpegDownloadPlan>();
			string versionTag = GetVersionTag(ffMajor);

			if (isWindows) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-win64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-win64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows x64 ({versionTag})"));
					break;
				case Architecture.X86:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-win32-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-win32-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows x86 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-winarm64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-winarm64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows ARM64 ({versionTag})"));
					break;
				}
			}
			else if (isLinux) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linux64-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linux64-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux x64 ({versionTag})"));
					break;
				case Architecture.X86:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linux32-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linux32-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux x86 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linuxarm64-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linuxarm64-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux ARM64 ({versionTag})"));
					break;
				case Architecture.Arm:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linuxarmhf-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linuxarmhf-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux ARMHF ({versionTag})"));
					break;
				}
			}
			else if (isMacOS) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-macos64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-macos64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"macOS x64 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-macosarm64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-macosarm64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"macOS ARM64 ({versionTag})"));
					break;
				}
			}

			return plans;
		}

		/// <summary>
		/// Download plans for the current runtime (OS + process architecture). Reads the FFmpeg
		/// AutoGen version constants to pick the matching BtbN/yt-dlp release.
		/// </summary>
		public IReadOnlyList<FfmpegDownloadPlan> GetSharedFfmpegDownloadPlans() {
			int ffMajor = MapToFfmpegMajor(ffmpeg.LIBAVCODEC_VERSION_MAJOR, ffmpeg.LIBAVFORMAT_VERSION_MAJOR, ffmpeg.LIBAVUTIL_VERSION_MAJOR);
			return BuildDownloadPlans(
				ffMajor,
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
				RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
				RuntimeInformation.ProcessArchitecture);
		}

		/// <summary>
		/// Parses a GNU sha256sum <c>checksums.sha256</c> blob and returns the hash for
		/// <paramref name="archiveFileName"/>, or null if no entry matches. Filename matching is
		/// case-insensitive (BtbN/yt-dlp archives are lowercase but callers may pass any casing).
		/// </summary>
		public static string? FindExpectedChecksum(string checksumText, string archiveFileName) {
			foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				// Format: "hash  filename" (GNU sha256sum — two spaces)
				var parts = line.Split("  ", 2, StringSplitOptions.None);
				if (parts.Length == 2 && parts[1].Trim().Equals(archiveFileName, StringComparison.OrdinalIgnoreCase)) {
					return parts[0].Trim().ToLowerInvariant();
				}
			}
			return null;
		}

		/// <summary>
		/// Human-readable byte size (B/KB/MB/GB). Shared by both frontends' progress formatting.
		/// </summary>
		public static string FormatBytes(long? bytes) {
			if (bytes == null) return "?";
			double size = bytes.Value;
			string[] units = { "B", "KB", "MB", "GB" };
			int unit = 0;
			while (size >= 1024 && unit < units.Length - 1) {
				size /= 1024;
				unit++;
			}
			return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unit]);
		}

		/// <summary>
		/// Downloads, verifies, extracts and installs FFmpeg for the current platform. Iterates
		/// <see cref="GetSharedFfmpegDownloadPlans"/>; a per-plan <see cref="HttpRequestException"/>
		/// advances to the next plan, while any other exception aborts and is returned in
		/// <see cref="FFmpegSetupResult.Exception"/>. Progress is reported through
		/// <paramref name="progress"/> if non-null.
		/// </summary>
		public async Task<FFmpegSetupResult> DownloadAndInstallAsync(IProgress<FFmpegSetupProgress>? progress = null, CancellationToken cancellationToken = default) {
			string? extractedFolder = null;
			string? targetFolder = null;
			try {
				progress?.Report(new FFmpegSetupProgress {
					Stage = FFmpegSetupStage.Preparing,
					StatusMessage = "Preparing FFmpeg download..."
				});

				var plans = GetSharedFfmpegDownloadPlans();
				if (plans.Count == 0) {
					progress?.Report(new FFmpegSetupProgress {
						Stage = FFmpegSetupStage.Failed,
						StatusMessage = "No FFmpeg download available for this platform/architecture."
					});
					return new FFmpegSetupResult(
						false,
						FFmpegSetupFailureReason.NoPlansAvailable,
						"No FFmpeg download available for this platform/architecture.",
						null,
						extractedFolder,
						null);
				}

				string? lastHttpError = null;
				foreach (var plan in plans) {
					cancellationToken.ThrowIfCancellationRequested();
					string tempRoot = Path.Combine(Path.GetTempPath(), "VDF.FFmpegDownload");
					string downloadPath = Path.Combine(tempRoot, plan.ArchiveFileName);
					extractedFolder = Path.Combine(tempRoot, "extracted");

					Directory.CreateDirectory(tempRoot);
					if (Directory.Exists(extractedFolder))
						Directory.Delete(extractedFolder, true);
					Directory.CreateDirectory(extractedFolder);

					try {
						await DownloadFileAsync(plan.DownloadUrl, downloadPath, plan, progress, cancellationToken);

						progress?.Report(new FFmpegSetupProgress {
							Stage = FFmpegSetupStage.Verifying,
							StatusMessage = "Verifying checksum...",
							DisplayName = plan.DisplayName
						});
						await VerifyChecksumAsync(plan.DownloadUrl, downloadPath, plan.ArchiveFileName);

						progress?.Report(new FFmpegSetupProgress {
							Stage = FFmpegSetupStage.Extracting,
							StatusMessage = "Extracting FFmpeg...",
							DisplayName = plan.DisplayName
						});
						ExtractArchive(downloadPath, extractedFolder, plan.ArchiveKind);

						progress?.Report(new FFmpegSetupProgress {
							Stage = FFmpegSetupStage.Installing,
							StatusMessage = "Installing FFmpeg...",
							DisplayName = plan.DisplayName
						});
						targetFolder = Path.Combine(CoreUtils.CurrentFolder, "bin");
						Directory.CreateDirectory(targetFolder);
						var targetLibFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
							? Path.Combine(CoreUtils.CurrentFolder, "lib")
							: targetFolder;
						Directory.CreateDirectory(targetLibFolder);
						CopyFfmpegFiles(extractedFolder, targetFolder, targetLibFolder);

						progress?.Report(new FFmpegSetupProgress {
							Stage = FFmpegSetupStage.Completed,
							StatusMessage = "FFmpeg downloaded and installed successfully.",
							DisplayName = plan.DisplayName,
							DownloadPercent = 100
						});
						return new FFmpegSetupResult(true, FFmpegSetupFailureReason.None, null, targetFolder, extractedFolder, null);
					}
					catch (OperationCanceledException) {
						throw;
					}
					catch (HttpRequestException ex) {
						lastHttpError = ex.Message;
					}
				}

				progress?.Report(new FFmpegSetupProgress {
					Stage = FFmpegSetupStage.Failed,
					StatusMessage = lastHttpError ?? "FFmpeg download failed."
				});
				return new FFmpegSetupResult(
					false,
					FFmpegSetupFailureReason.DownloadFailed,
					lastHttpError ?? "FFmpeg download failed.",
					targetFolder,
					extractedFolder,
					null);
			}
			catch (OperationCanceledException) {
				throw;
			}
			catch (Exception ex) {
				progress?.Report(new FFmpegSetupProgress {
					Stage = FFmpegSetupStage.Failed,
					StatusMessage = ex.Message
				});
				return new FFmpegSetupResult(false, FFmpegSetupFailureReason.DownloadFailed, ex.Message, targetFolder, extractedFolder, ex);
			}
		}

		async Task DownloadFileAsync(Uri downloadUrl, string destinationPath, FfmpegDownloadPlan plan, IProgress<FFmpegSetupProgress>? progress, CancellationToken cancellationToken) {
			using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
			using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");

			var totalBytes = response.Content.Headers.ContentLength;
			if (totalBytes > MaxDownloadBytes)
				throw new HttpRequestException($"Download too large ({totalBytes} bytes, max {MaxDownloadBytes})");

			await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
			await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

			var buffer = new byte[81920];
			long totalRead = 0;
			int read;
			while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0) {
				await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				totalRead += read;
				if (totalRead > MaxDownloadBytes)
					throw new HttpRequestException($"Download exceeded size limit ({MaxDownloadBytes} bytes)");

				double percent = totalBytes.HasValue && totalBytes.Value > 0
					? totalRead / (double)totalBytes.Value * 100
					: 0;
				progress?.Report(new FFmpegSetupProgress {
					Stage = FFmpegSetupStage.Downloading,
					StatusMessage = string.Format(CultureInfo.InvariantCulture,
						"Downloading FFmpeg ({0})... {1} / {2}",
						plan.DisplayName, FormatBytes(totalRead), FormatBytes(totalBytes)),
					DisplayName = plan.DisplayName,
					BytesDownloaded = totalRead,
					TotalBytes = totalBytes,
					DownloadPercent = percent
				});
			}
		}

		static async Task VerifyChecksumAsync(Uri downloadUrl, string filePath, string archiveFileName) {
			// Derive checksums URL from download URL (same directory, different file)
			var checksumUrl = new Uri(downloadUrl, "checksums.sha256");
			try {
				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
				var checksumText = await client.GetStringAsync(checksumUrl);

				string? expectedHash = FindExpectedChecksum(checksumText, archiveFileName);
				if (expectedHash == null) {
					// Archive not listed in checksums file — warn but continue
					Logger.Instance.Info($"FFmpeg download: no checksum entry found for '{archiveFileName}', skipping verification");
					return;
				}

				await using var fs = File.OpenRead(filePath);
				var hashBytes = await SHA256.HashDataAsync(fs);
				var actualHash = Convert.ToHexStringLower(hashBytes);

				if (actualHash != expectedHash)
					throw new InvalidOperationException(
						$"Checksum mismatch for '{archiveFileName}': expected {expectedHash}, got {actualHash}. The download may be corrupted or tampered with.");
			}
			catch (HttpRequestException) {
				// Checksums file not available — warn but continue (don't block install)
				Logger.Instance.Info("FFmpeg download: could not fetch checksums.sha256, skipping verification");
			}
		}

		/// <summary>
		/// Extracts <paramref name="archivePath"/> into <paramref name="targetFolder"/>. Zip
		/// archives go through <see cref="SafeExtractZip"/> (path-traversal guarded); tar.xz/tar.gz
		/// are delegated to the system <c>tar</c> tool. NOTE: do NOT pass
		/// <c>--no-absolute-filenames</c> — it is not a valid GNU tar option (rejected even by GNU
		/// tar 1.35) and is absent from BSD/busybox tar, so it aborted extraction on Linux/macOS
		/// with "tar: unrecognized option" (issue #788). tar already strips leading '/'s by
		/// default, and the archive is checksum-verified.
		/// </summary>
		internal static void ExtractArchive(string archivePath, string targetFolder, ArchiveType type) {
			if (type == ArchiveType.Zip) {
				SafeExtractZip(archivePath, targetFolder);
				return;
			}

			var psi = new ProcessStartInfo {
				FileName = "tar",
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			if (type == ArchiveType.TarXz) {
				psi.ArgumentList.Add("-xJf");
			}
			else if (type == ArchiveType.TarGz) {
				psi.ArgumentList.Add("-xzf");
			}
			else {
				throw new InvalidOperationException("Unsupported archive type");
			}
			psi.ArgumentList.Add(archivePath);
			psi.ArgumentList.Add("-C");
			psi.ArgumentList.Add(targetFolder);

			using var process = new Process { StartInfo = psi };
			process.Start();
			process.WaitForExit();
			if (process.ExitCode != 0) {
				string error = process.StandardError.ReadToEnd();
				throw new IOException(string.IsNullOrWhiteSpace(error) ? "Failed to extract archive." : error);
			}
		}

		static void CopyFfmpegFiles(string sourceRoot, string targetFolder, string targetLibFolder) {
			string ffmpegName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
			string ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

			string? ffmpegPath = Directory.EnumerateFiles(sourceRoot, ffmpegName, SearchOption.AllDirectories).FirstOrDefault();
			string? ffprobePath = Directory.EnumerateFiles(sourceRoot, ffprobeName, SearchOption.AllDirectories).FirstOrDefault();

			if (ffmpegPath == null || ffprobePath == null)
				throw new FileNotFoundException("ffmpeg/ffprobe not found in the extracted archive.");

			string? binFolder = Path.GetDirectoryName(ffmpegPath);
			if (string.IsNullOrEmpty(binFolder))
				throw new DirectoryNotFoundException("Failed to locate ffmpeg folder in the archive.");

			foreach (var file in Directory.EnumerateFiles(binFolder)) {
				string fileName = Path.GetFileName(file);
				CopyFile(file, Path.Combine(targetFolder, fileName));
			}

			var libraryFiles = FFmpegHelper.GenerateLibraryFileNames();
			foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
				var fileName = Path.GetFileName(file);
				if (libraryFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase)) {
					CopyFile(file, Path.Combine(targetLibFolder, fileName));
				}
			}
		}

		static void CopyFile(string sourcePath, string destinationPath) {
			string targetPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? FFToolsUtils.LongPathFix(destinationPath)
				: destinationPath;
			File.Copy(sourcePath, targetPath, true);
		}

		/// <summary>
		/// Extracts a zip archive rejecting entries that would escape <paramref name="targetFolder"/>
		/// (zip-slip protection). Mirrors the implementation previously duplicated in both frontends.
		/// </summary>
		internal static void SafeExtractZip(string archivePath, string targetFolder) {
			string fullTarget = Path.GetFullPath(targetFolder);
			using var zip = ZipFile.OpenRead(archivePath);
			foreach (var entry in zip.Entries) {
				string dest = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
				if (!dest.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal)
					&& dest != fullTarget)
					throw new InvalidOperationException($"ZIP entry '{entry.FullName}' would extract outside target directory");
				if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
					Directory.CreateDirectory(dest);
				}
				else {
					Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
					entry.ExtractToFile(dest, true);
				}
			}
		}
	}
}
