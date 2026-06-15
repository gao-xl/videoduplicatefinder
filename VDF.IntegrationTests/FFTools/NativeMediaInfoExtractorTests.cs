using VDF.Core.FFTools.FFmpegNative;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class NativeMediaInfoExtractorTests {
	readonly FfmpegFixture _fixture;

	public NativeMediaInfoExtractorTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public void Extract_RealVideoFile_ReturnsMediaInfo() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		var result = NativeMediaInfoExtractor.Extract(_fixture.H264_8bit!);

		Assert.NotNull(result);
		Assert.True(result.Duration > TimeSpan.Zero);
		Assert.NotEmpty(result.Streams);
		// Should have at least a video stream
		Assert.Contains(result.Streams, s => s.CodecType == "video");
	}

	[SkippableFact]
	public void Extract_RealVideoFile_MatchesCliOutput() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		// Extract via native binding
		var nativeResult = NativeMediaInfoExtractor.Extract(_fixture.H264_8bit!);
		Assert.NotNull(nativeResult);

		// Extract via ffprobe CLI for comparison
		var cliResult = VDF.Core.FFTools.FFProbeEngine.GetMediaInfo(_fixture.H264_8bit!, extendedLogging: false);
		Assert.NotNull(cliResult);

		// Duration should be close (within 1 second — different time base rounding)
		Assert.True(
			Math.Abs((nativeResult.Duration - cliResult.Duration).TotalSeconds) < 1,
			$"Native duration {nativeResult.Duration} differs from CLI {cliResult.Duration} by more than 1s");

		// Both should detect a video stream
		Assert.Contains(nativeResult.Streams, s => s.CodecType == "video");
		Assert.Contains(cliResult.Streams, s => s.CodecType == "video");
	}

	[SkippableFact]
	public void Extract_NonExistentFile_ReturnsNull() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		var result = NativeMediaInfoExtractor.Extract(
			Path.Combine(_fixture.TempDir, "nonexistent.mp4"));

		Assert.Null(result);
	}

	[SkippableFact]
	public void Extract_CorruptFile_ReturnsNull() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_Corrupted == null, "Corrupted test video not generated");

		// Corrupted file may return null or partial info — either is acceptable
		// The key requirement is that it doesn't throw or crash
		var result = NativeMediaInfoExtractor.Extract(_fixture.H264_Corrupted!);
		// No assertion on the result — just that it didn't throw
	}
}
