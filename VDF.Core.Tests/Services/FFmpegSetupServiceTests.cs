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

using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using VDF.Core.Services;

namespace VDF.Core.Tests.Services;

public class FFmpegSetupServiceTests {
	const string BtbnLatest = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
	const string YtDlpLatest = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/";

	// ── MapToFfmpegMajor ──

	[Theory]
	[InlineData(62, 61, 60, 8)]
	[InlineData(61, 60, 59, 7)]
	[InlineData(60, 59, 58, 6)]
	[InlineData(59, 58, 57, 5)]
	public void MapToFfmpegMajor_RecognisedMajors_PicksHighest(int avcodec, int avformat, int avutil, int expected) =>
		Assert.Equal(expected, FFmpegSetupService.MapToFfmpegMajor(avcodec, avformat, avutil));

	[Fact]
	public void MapToFfmpegMajor_MixedVersions_PicksHighestRecognised() {
		// avcodec 62 (FFmpeg 8) + avformat 60 (FFmpeg 6) → 8
		Assert.Equal(8, FFmpegSetupService.MapToFfmpegMajor(62, 60, 59));
	}

	[Fact]
	public void MapToFfmpegMajor_AllUnknown_ReturnsZero() =>
		Assert.Equal(0, FFmpegSetupService.MapToFfmpegMajor(1, 2, 3));

	[Fact]
	public void MapToFfmpegMajor_AllZero_ReturnsZero() =>
		Assert.Equal(0, FFmpegSetupService.MapToFfmpegMajor(0, 0, 0));

	// ── GetVersionTag ──

	[Theory]
	[InlineData(8, "8.1")]
	[InlineData(7, "7.1")]
	[InlineData(6, "6.1")]
	[InlineData(5, "5.1")]
	public void GetVersionTag_KnownMajors_ReturnsLatestMinor(int major, string tag) =>
		Assert.Equal(tag, FFmpegSetupService.GetVersionTag(major));

	[Theory]
	[InlineData(0)]
	[InlineData(4)]
	[InlineData(99)]
	public void GetVersionTag_UnknownMajor_DefaultsTo7_1(int major) =>
		Assert.Equal("7.1", FFmpegSetupService.GetVersionTag(major));

	// ── BuildDownloadPlans — URL construction & architecture branching ──

	[Fact]
	public void BuildDownloadPlans_WindowsX64_BtbnZipUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: true, isLinux: false, isMacOS: false, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.Zip, plan.ArchiveKind);
		Assert.StartsWith(BtbnLatest, plan.DownloadUrl.ToString());
		Assert.Contains("win64-gpl-shared-8.1", plan.DownloadUrl.ToString());
		Assert.EndsWith(".zip", plan.DownloadUrl.ToString());
		Assert.Equal(plan.DownloadUrl.Segments[^1], plan.ArchiveFileName);
		Assert.Contains("Windows x64", plan.DisplayName);
	}

	[Fact]
	public void BuildDownloadPlans_WindowsX86_BtbnZipUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(7, isWindows: true, isLinux: false, isMacOS: false, Architecture.X86);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.Zip, plan.ArchiveKind);
		Assert.Contains("win32-gpl-shared-7.1", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_WindowsArm64_BtbnZipUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: true, isLinux: false, isMacOS: false, Architecture.Arm64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.Zip, plan.ArchiveKind);
		Assert.Contains("winarm64-gpl-shared-8.1", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_LinuxX64_BtbnTarXzUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: false, isLinux: true, isMacOS: false, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.TarXz, plan.ArchiveKind);
		Assert.StartsWith(BtbnLatest, plan.DownloadUrl.ToString());
		Assert.Contains("linux64-gpl-shared-8.1", plan.DownloadUrl.ToString());
		Assert.EndsWith(".tar.xz", plan.DownloadUrl.ToString());
		Assert.Contains("Linux x64", plan.DisplayName);
	}

	[Fact]
	public void BuildDownloadPlans_LinuxArm_BtbnTarXzUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(7, isWindows: false, isLinux: true, isMacOS: false, Architecture.Arm);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.TarXz, plan.ArchiveKind);
		Assert.Contains("linuxarmhf-gpl-shared-7.1", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_LinuxArm64_BtbnTarXzUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(7, isWindows: false, isLinux: true, isMacOS: false, Architecture.Arm64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.TarXz, plan.ArchiveKind);
		Assert.Contains("linuxarm64-gpl-shared-7.1", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_MacOSX64_YtDlpZipUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: false, isLinux: false, isMacOS: true, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.Zip, plan.ArchiveKind);
		// macOS builds come from yt-dlp, not BtbN
		Assert.StartsWith(YtDlpLatest, plan.DownloadUrl.ToString());
		Assert.Contains("macos64-gpl-shared-8.1", plan.DownloadUrl.ToString());
		Assert.EndsWith(".zip", plan.DownloadUrl.ToString());
		Assert.Contains("macOS x64", plan.DisplayName);
	}

	[Fact]
	public void BuildDownloadPlans_MacOSArm64_YtDlpZipUrl() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: false, isLinux: false, isMacOS: true, Architecture.Arm64);
		var plan = Assert.Single(plans);
		Assert.Equal(ArchiveType.Zip, plan.ArchiveKind);
		Assert.StartsWith(YtDlpLatest, plan.DownloadUrl.ToString());
		Assert.Contains("macosarm64-gpl-shared-8.1", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_UnsupportedOS_ReturnsEmpty() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: false, isLinux: false, isMacOS: false, Architecture.X64);
		Assert.Empty(plans);
	}

	[Fact]
	public void BuildDownloadPlans_UnsupportedArch_ReturnsEmpty() {
		// WebAssembly is not a supported FFmpeg target architecture
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: true, isLinux: false, isMacOS: false, Architecture.Wasm);
		Assert.Empty(plans);
	}

	[Fact]
	public void BuildDownloadPlans_ArchiveFileNameMatchesUrlLastSegment() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: true, isLinux: false, isMacOS: false, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Equal(plan.DownloadUrl.Segments[^1], plan.ArchiveFileName);
	}

	[Fact]
	public void BuildDownloadPlans_VersionTag8_Uses8_1() {
		var plans = FFmpegSetupService.BuildDownloadPlans(8, isWindows: true, isLinux: false, isMacOS: false, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Contains("n8.1", plan.DownloadUrl.ToString());
		Assert.Contains("-8.1.zip", plan.DownloadUrl.ToString());
	}

	[Fact]
	public void BuildDownloadPlans_VersionTag7_Uses7_1() {
		var plans = FFmpegSetupService.BuildDownloadPlans(7, isWindows: false, isLinux: true, isMacOS: false, Architecture.X64);
		var plan = Assert.Single(plans);
		Assert.Contains("n7.1", plan.DownloadUrl.ToString());
		Assert.Contains("-7.1.tar.xz", plan.DownloadUrl.ToString());
	}

	// ── FindExpectedChecksum — checksum comparison logic ──

	[Fact]
	public void FindExpectedChecksum_MatchingEntry_ReturnsLowercasedHash() {
		string checksums = "abc123def456  ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip");
		Assert.Equal("abc123def456", hash);
	}

	[Fact]
	public void FindExpectedChecksum_NoMatchingEntry_ReturnsNull() {
		string checksums = "abc123  other.zip\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg.zip");
		Assert.Null(hash);
	}

	[Fact]
	public void FindExpectedChecksum_FilenameCaseInsensitive() {
		string checksums = "deadbeef  FFMPEG.ZIP\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg.zip");
		Assert.Equal("deadbeef", hash);
	}

	[Fact]
	public void FindExpectedChecksum_HashLowercased() {
		string checksums = "ABCDEF0123456789  ffmpeg.zip\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg.zip");
		Assert.Equal("abcdef0123456789", hash);
	}

	[Fact]
	public void FindExpectedChecksum_MultipleLines_PicksCorrectEntry() {
		string checksums =
			"111  ffmpeg-win64.zip\n" +
			"222  ffmpeg-win32.zip\n" +
			"333  ffmpeg-linux64.tar.xz\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg-linux64.tar.xz");
		Assert.Equal("333", hash);
	}

	[Fact]
	public void FindExpectedChecksum_EmptyText_ReturnsNull() =>
		Assert.Null(FFmpegSetupService.FindExpectedChecksum("", "ffmpeg.zip"));

	[Fact]
	public void FindExpectedChecksum_EmptyLinesSkipped() {
		// GNU sha256sum format: "hash  filename" with no leading spaces on content lines
		string checksums = "\n\nabc  ffmpeg.zip\n\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg.zip");
		Assert.Equal("abc", hash);
	}

	[Fact]
	public void FindExpectedChecksum_TwoSpaceSeparatorRequired() {
		// GNU sha256sum uses exactly two spaces. A single space should not match.
		string checksums = "abc ffmpeg.zip\n";
		string? hash = FFmpegSetupService.FindExpectedChecksum(checksums, "ffmpeg.zip");
		Assert.Null(hash);
	}

	// ── FormatBytes ──

	[Theory]
	[InlineData(null, "?")]
	[InlineData(0L, "0 B")]
	[InlineData(512L, "512 B")]
	[InlineData(1024L, "1 KB")]
	[InlineData(1536L, "1.5 KB")]
	[InlineData(1048576L, "1 MB")]
	[InlineData(1073741824L, "1 GB")]
	public void FormatBytes_VariousSizes(long? bytes, string expected) =>
		Assert.Equal(expected, FFmpegSetupService.FormatBytes(bytes));

	// ── SafeExtractZip / ExtractArchive — tar/zip extraction dispatch ──

	[Fact]
	public void SafeExtractZip_NormalZip_ExtractsAllFiles() {
		using var temp = new TempDir();
		string sourceDir = Path.Combine(temp.Path, "source");
		Directory.CreateDirectory(sourceDir);
		File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "hello");
		Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
		File.WriteAllText(Path.Combine(sourceDir, "sub", "b.txt"), "world");

		string zipPath = Path.Combine(temp.Path, "archive.zip");
		ZipFile.CreateFromDirectory(sourceDir, zipPath);

		string extractDir = Path.Combine(temp.Path, "extracted");
		Directory.CreateDirectory(extractDir);

		FFmpegSetupService.SafeExtractZip(zipPath, extractDir);

		Assert.True(File.Exists(Path.Combine(extractDir, "a.txt")));
		Assert.True(File.Exists(Path.Combine(extractDir, "sub", "b.txt")));
		Assert.Equal("hello", File.ReadAllText(Path.Combine(extractDir, "a.txt")));
	}

	[Fact]
	public void SafeExtractZip_PathTraversalEntry_Throws() {
		using var temp = new TempDir();
		string zipPath = Path.Combine(temp.Path, "evil.zip");
		using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
			var entry = zip.CreateEntry("../escape.txt");
			using var writer = new StreamWriter(entry.Open());
			writer.Write("malicious");
		}

		string extractDir = Path.Combine(temp.Path, "extracted");
		Directory.CreateDirectory(extractDir);

		Assert.Throws<InvalidOperationException>(() => FFmpegSetupService.SafeExtractZip(zipPath, extractDir));
		// Ensure the escape file was NOT created
		string escapePath = Path.Combine(temp.Path, "escape.txt");
		Assert.False(File.Exists(escapePath));
	}

	[Fact]
	public void ExtractArchive_Zip_DispatchesToSafeExtractZip() {
		using var temp = new TempDir();
		string sourceDir = Path.Combine(temp.Path, "source");
		Directory.CreateDirectory(sourceDir);
		File.WriteAllText(Path.Combine(sourceDir, "nested.txt"), "data");

		string zipPath = Path.Combine(temp.Path, "archive.zip");
		ZipFile.CreateFromDirectory(sourceDir, zipPath);

		string extractDir = Path.Combine(temp.Path, "extracted");
		Directory.CreateDirectory(extractDir);

		FFmpegSetupService.ExtractArchive(zipPath, extractDir, ArchiveType.Zip);

		Assert.True(File.Exists(Path.Combine(extractDir, "nested.txt")));
	}

	[Fact]
	public void ExtractArchive_InvalidType_ThrowsInvalidOperationException() {
		using var temp = new TempDir();
		string extractDir = Path.Combine(temp.Path, "extracted");
		Directory.CreateDirectory(extractDir);

		Assert.Throws<InvalidOperationException>(() =>
			FFmpegSetupService.ExtractArchive("nonexistent", extractDir, (ArchiveType)999));
	}

	[Fact]
	public void ExtractArchive_TarGz_ExtractsViaTar() {
		if (!IsTarAvailable())
			return; // tar not on PATH — skip on exotic CI runners

		using var temp = new TempDir();
		string sourceDir = Path.Combine(temp.Path, "source");
		Directory.CreateDirectory(sourceDir);
		File.WriteAllText(Path.Combine(sourceDir, "inside.txt"), "tar-content");
		Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
		File.WriteAllText(Path.Combine(sourceDir, "sub", "deep.txt"), "deep");

		// Build a .tar then gzip it → .tar.gz
		string tarPath = Path.Combine(temp.Path, "archive.tar");
		TarFile.CreateFromDirectory(sourceDir, tarPath, includeBaseDirectory: false);
		string tgzPath = Path.Combine(temp.Path, "archive.tar.gz");
		using (var tgzStream = File.Create(tgzPath))
		using (var gzip = new GZipStream(tgzStream, CompressionMode.Compress))
		using (var tarStream = File.OpenRead(tarPath)) {
			tarStream.CopyTo(gzip);
		}

		string extractDir = Path.Combine(temp.Path, "extracted");
		Directory.CreateDirectory(extractDir);

		FFmpegSetupService.ExtractArchive(tgzPath, extractDir, ArchiveType.TarGz);

		Assert.True(File.Exists(Path.Combine(extractDir, "inside.txt")));
		Assert.True(File.Exists(Path.Combine(extractDir, "sub", "deep.txt")));
		Assert.Equal("tar-content", File.ReadAllText(Path.Combine(extractDir, "inside.txt")));
	}

	static bool IsTarAvailable() {
		try {
			var psi = new System.Diagnostics.ProcessStartInfo {
				FileName = "tar",
				Arguments = "--version",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using var p = System.Diagnostics.Process.Start(psi);
			if (p == null) return false;
			p.WaitForExit(5000);
			return p.ExitCode == 0;
		}
		catch {
			return false;
		}
	}

	sealed class TempDir : IDisposable {
		public string Path { get; }
		public TempDir() {
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VDF.Tests." + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}
		public void Dispose() {
			try { Directory.Delete(Path, true); } catch { }
		}
	}
}
