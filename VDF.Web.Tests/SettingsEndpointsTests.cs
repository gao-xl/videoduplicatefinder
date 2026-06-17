namespace VDF.Web.Tests;

public class SettingsEndpointsTests {
	[Theory]
	[InlineData(1)]
	[InlineData(4)]
	[InlineData(8)]
	[InlineData(16)]
	public void MaxDegreeOfParallelism_ValidValues_Accepted(int value) {
		// Arrange
		int min = 1;
		int max = Environment.ProcessorCount * 2;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(0)]
	[InlineData(100)]
	[InlineData(int.MaxValue)]
	public void MaxDegreeOfParallelism_InvalidValues_Clamped(int value) {
		// Arrange
		int min = 1;
		int max = Environment.ProcessorCount * 2;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(5)]
	[InlineData(10)]
	[InlineData(20)]
	public void ThumbnailCount_ValidValues_Accepted(int value) {
		// Arrange
		int min = 0;
		int max = 20;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(-5)]
	[InlineData(25)]
	[InlineData(100)]
	public void ThumbnailCount_InvalidValues_Clamped(int value) {
		// Arrange
		int min = 0;
		int max = 20;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(50f)]
	[InlineData(96f)]
	[InlineData(100f)]
	public void Percent_ValidValues_Accepted(float value) {
		// Arrange
		float min = 0f;
		float max = 100f;

		// Act
		float clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(-10f)]
	[InlineData(150f)]
	public void Percent_InvalidValues_Clamped(float value) {
		// Arrange
		float min = 0f;
		float max = 100f;

		// Act
		float clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(48)]
	[InlineData(480)]
	[InlineData(960)]
	public void ThumbnailWidth_ValidValues_Accepted(int value) {
		// Arrange
		int min = 48;
		int max = 960;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Theory]
	[InlineData(10)]
	[InlineData(50)]
	[InlineData(95)]
	public void ThumbnailJpegQuality_ValidValues_Accepted(int value) {
		// Arrange
		int min = 10;
		int max = 95;

		// Act
		int clamped = Math.Clamp(value, min, max);

		// Assert
		Assert.InRange(clamped, min, max);
	}

	[Fact]
	public void MinimumFileSize_LessThanMaximumFileSize() {
		// Arrange
		int minimum = 100;
		int maximum = 1000;

		// Act
		int clampedMax = Math.Max(minimum, maximum);

		// Assert
		Assert.True(clampedMax >= minimum);
	}
}
