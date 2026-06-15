using FFmpeg.AutoGen;
using VDF.Core.FFTools.FFmpegNative;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class HardwareAccelerationDetectorTests {
	readonly FfmpegFixture _fixture;

	public HardwareAccelerationDetectorTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public void DetectAvailableDevices_DoesNotThrow() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		var devices = HardwareAccelerationDetector.DetectAvailableDevices();
		Assert.NotNull(devices);
		// The result can be empty (no HW accel available) — that's fine
	}

	[SkippableFact]
	public void DetectAvailableDevices_ResultsAreCachedAfterFirstCall() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		// Invalidate cache first to ensure clean state
		HardwareAccelerationDetector.InvalidateCache();

		var first = HardwareAccelerationDetector.DetectAvailableDevices();
		var second = HardwareAccelerationDetector.DetectAvailableDevices();

		// Second call should return the exact same array instance (cached)
		Assert.Same(first, second);
	}
}
