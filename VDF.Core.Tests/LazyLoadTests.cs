using VDF.Core;
using VDF.Core.Data;
using Xunit;

namespace VDF.Core.Tests;

public class LazyLoadTests {
	[Fact]
	public void FileEntry_HeavyFieldsLoaded_DefaultTrue() {
		var entry = new FileEntry();
		Assert.True(entry._heavyFieldsLoaded);
	}

	[Fact]
	public void FileEntry_HeavyFieldsLoaded_SetFalse() {
		var entry = new FileEntry();
		entry._heavyFieldsLoaded = false;
		Assert.False(entry._heavyFieldsLoaded);
	}

	[Fact]
	public void FileEntry_HeavyFieldsLoaded_NotSerialized() {
		var entry = new FileEntry { _Path = "/test.mp4", _heavyFieldsLoaded = false };
		var bytes = MemoryPack.MemoryPackSerializer.Serialize(entry);
		var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<FileEntry>(bytes)!;
		Assert.True(deserialized._heavyFieldsLoaded); // default true after deserialization
	}
}
