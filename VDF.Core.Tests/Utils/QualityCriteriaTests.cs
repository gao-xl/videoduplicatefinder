using System.Text;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests.Utils;

public class QualityCriteriaTests {
	[Fact]
	public void PickKeeper_PrefersHigherResolution() {
		var a = new DuplicateItem { Path = "a", FrameSizeInt = 1000, Similarity = 100 };
		var b = new DuplicateItem { Path = "b", FrameSizeInt = 2000, Similarity = 100 };
		var keeper = QualityCriteria.PickKeeper([a, b]);
		Assert.Equal("b", keeper.Path);
	}

	[Fact]
	public void Resolve_IncludesHdrFormatRank_AsTiebreaker() {
		var names = QualityCriteria.Resolve(["Duration"]).Select(c => c.Name).ToList();
		Assert.Contains("HdrFormatRank", names);
	}
}

public class ResultsCsvExporterTests {
	[Fact]
	public void Export_IncludesCheckedColumn_WhenPathsProvided() {
		var item = new DuplicateItem {
			Path = @"C:\a.mp4",
			GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Similarity = 100,
		};
		var bytes = ResultsCsvExporter.ExportToUtf8Bom(
			[item],
			checkedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Path });
		var text = Encoding.UTF8.GetString(bytes);
		Assert.Contains("Checked", text);
		Assert.Contains(",True", text);
	}

	[Fact]
	public void Export_OmitsCheckedColumn_WhenDisabled() {
		var item = new DuplicateItem { Path = "a", GroupId = Guid.NewGuid() };
		var bytes = ResultsCsvExporter.ExportToUtf8Bom([item], includeCheckedColumn: false);
		var text = Encoding.UTF8.GetString(bytes);
		Assert.DoesNotContain("Checked", text);
	}
}
