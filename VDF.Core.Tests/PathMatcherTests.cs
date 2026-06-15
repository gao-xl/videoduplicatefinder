using VDF.Core;
using Xunit;

namespace VDF.Core.Tests;

public class PathMatcherTests {
	[Fact]
	public void IsIncluded_ExactMatch_ReturnsTrue() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.True(matcher.IsIncluded(@"C:\Videos"));
	}

	[Fact]
	public void IsIncluded_SubDirectory_ReturnsTrue() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.True(matcher.IsIncluded(@"C:\Videos\Movies"));
	}

	[Fact]
	public void IsIncluded_DeepSubDirectory_ReturnsTrue() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.True(matcher.IsIncluded(@"C:\Videos\Movies\Action"));
	}

	[Fact]
	public void IsIncluded_UnrelatedPath_ReturnsFalse() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.False(matcher.IsIncluded(@"C:\Music"));
	}

	[Fact]
	public void IsIncluded_PartialPrefix_ReturnsFalse() {
		// "C:\Vid" should NOT match "C:\Videos" - it's a different directory
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.False(matcher.IsIncluded(@"C:\Vid"));
	}

	[Fact]
	public void IsIncluded_MultipleRoots_ReturnsTrueForAny() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos", @"D:\Archive" });
		Assert.True(matcher.IsIncluded(@"D:\Archive\Old"));
	}

	[Fact]
	public void IsIncluded_EmptyRoots_ReturnsFalse() {
		var matcher = new ScanEngine.PathMatcher(new HashSet<string>());
		Assert.False(matcher.IsIncluded(@"C:\Videos"));
	}

	[Fact]
	public void IsIncluded_HiddenDirectory_ReturnsFalse() {
		// Directories starting with "." should not match (like the original logic)
		var matcher = new ScanEngine.PathMatcher(new HashSet<string> { @"C:\Videos" });
		Assert.False(matcher.IsIncluded(@"C:\Videos\.git"));
	}
}
