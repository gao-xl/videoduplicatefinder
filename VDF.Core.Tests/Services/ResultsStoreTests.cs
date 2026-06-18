using System.Text;
using System.Text.Json;
using VDF.Core.Services;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests.Services;

public class ResultsStoreTests {
	[Fact]
	public async Task SaveJson_LoadJson_RoundTrips() {
		var store = new ResultsStore();
		var path = Path.Combine(Path.GetTempPath(), "vdf-test-" + Guid.NewGuid().ToString("N") + ".json");
		try {
			var items = new List<ScanResultEntry> {
				new() {
					Item = new DuplicateItem { Path = @"C:\videos\a.mp4", GroupId = Guid.NewGuid(), Similarity = 99.5f },
					Checked = true,
					ThumbnailKey = "abc123",
				},
			};

			await store.SaveJsonAsync(path, items);
			var loaded = await store.LoadJsonAsync(path);

			Assert.Single(loaded.Items);
			Assert.Equal(items[0].Item.Path, loaded.Items[0].Item.Path);
			Assert.True(loaded.Items[0].Checked);
			Assert.Equal("abc123", loaded.Items[0].ThumbnailKey);
		}
		finally {
			TryDelete(path);
		}
	}

	[Fact]
	public void ParseItems_AcceptsLegacyDuplicateItemVmShape() {
		const string json = """
			{
			  "version": 1,
			  "items": [
			    {
			      "itemInfo": { "Path": "C:\\legacy.mp4", "GroupId": "11111111-1111-1111-1111-111111111111", "Similarity": 100 },
			      "Checked": true,
			      "ThumbnailKey": "legacy-key"
			    }
			  ]
			}
			""";

		using var doc = JsonDocument.Parse(json);
		var items = ResultsStore.ParseItems(doc.RootElement);

		Assert.Single(items);
		Assert.Equal(@"C:\legacy.mp4", items[0].Item.Path);
		Assert.True(items[0].Checked);
	}

	[Fact]
	public void ParseItems_AcceptsRawLegacyArray() {
		const string json = """
			[
			  { "Path": "C:\\raw.mp4", "GroupId": "22222222-2222-2222-2222-222222222222", "Similarity": 95 }
			]
			""";

		using var doc = JsonDocument.Parse(json);
		var items = ResultsStore.ParseItems(doc.RootElement);
		Assert.Single(items);
		Assert.Equal(@"C:\raw.mp4", items[0].Item.Path);
	}

	static void TryDelete(string path) {
		try { if (File.Exists(path)) File.Delete(path); } catch { }
	}
}
