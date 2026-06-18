using VDF.Core.Services;

namespace VDF.Core.Tests.Services;

public class ThumbnailServiceTests {
	[Fact]
	public void IsPathAllowed_RejectsPathsOutsideRoots() {
		var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		using var svc = new ThumbnailService(null, new ThumbnailServiceOptions {
			AllowedRoots = [root],
		});

		string inside = Path.Combine(root, "allowed.mp4");
		Assert.True(svc.IsPathAllowed(inside));
		Assert.False(svc.IsPathAllowed(@"C:\definitely\not\allowed.mp4"));
	}

	[Fact]
	public void MakeKey_IsDeterministic() {
		var k1 = ThumbnailService.MakeKey(@"C:\a.mp4", TimeSpan.FromSeconds(1.5), 320, 80);
		var k2 = ThumbnailService.MakeKey(@"C:\a.mp4", TimeSpan.FromSeconds(1.5), 320, 80);
		Assert.Equal(k1, k2);
		Assert.NotEqual(k1, ThumbnailService.MakeKey(@"C:\a.mp4", TimeSpan.FromSeconds(1.5), 160, 80));
	}

	[Fact]
	public void PackFolder_PersistsEntries() {
		var folder = Path.Combine(Path.GetTempPath(), "vdf-thumb-" + Guid.NewGuid().ToString("N"));
		try {
			using (var svc = new ThumbnailService(null, new ThumbnailServiceOptions { PackFolder = folder })) {
				var key = "test-key";
				var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
				svc.AppendIfMissing(key, s => s.Write(jpeg));
				svc.FlushIndex();
			}

			using var svc2 = new ThumbnailService(null, new ThumbnailServiceOptions { PackFolder = folder });
			Assert.True(svc2.TryGetPackEntry("test-key", out _, out var len));
			Assert.Equal(4, len);
		}
		finally {
			try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
		}
	}
}
