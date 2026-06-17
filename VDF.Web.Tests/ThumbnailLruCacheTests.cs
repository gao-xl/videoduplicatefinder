using VDF.Web.Endpoints;

namespace VDF.Web.Tests;

public class ThumbnailLruCacheTests {
	[Fact]
	public void GetOrAdd_NewKey_AddsEntry() {
		// Arrange
		var cache = new ThumbnailLruCache(10);
		var factoryCalled = false;

		// Act
		var result = cache.GetOrAdd("key1", () => {
			factoryCalled = true;
			return new byte[] { 1, 2, 3 };
		});

		// Assert
		Assert.True(factoryCalled);
		Assert.Equal(3, result.Length);
		Assert.Equal(1, cache.Count);
	}

	[Fact]
	public void GetOrAdd_ExistingKey_ReturnsCachedValue() {
		// Arrange
		var cache = new ThumbnailLruCache(10);
		var callCount = 0;

		// Act
		var result1 = cache.GetOrAdd("key1", () => {
			callCount++;
			return new byte[] { 1, 2, 3 };
		});
		var result2 = cache.GetOrAdd("key1", () => {
			callCount++;
			return new byte[] { 4, 5, 6 };
		});

		// Assert
		Assert.Equal(1, callCount);
		Assert.Equal(result1, result2);
	}

	[Fact]
	public void GetOrAdd_ExceedsMaxSize_EvictsOldest() {
		// Arrange
		var cache = new ThumbnailLruCache(3);

		// Act - Add 4 entries (exceeds max of 3)
		cache.GetOrAdd("key1", () => new byte[] { 1 });
		cache.GetOrAdd("key2", () => new byte[] { 2 });
		cache.GetOrAdd("key3", () => new byte[] { 3 });
		cache.GetOrAdd("key4", () => new byte[] { 4 });

		// Assert - Should have evicted some entries
		Assert.True(cache.Count <= 3);
	}

	[Fact]
	public void Clear_RemovesAllEntries() {
		// Arrange
		var cache = new ThumbnailLruCache(10);
		cache.GetOrAdd("key1", () => new byte[] { 1 });
		cache.GetOrAdd("key2", () => new byte[] { 2 });

		// Act
		cache.Clear();

		// Assert
		Assert.Equal(0, cache.Count);
	}

	[Fact]
	public void GetOrAdd_ConcurrentAccess_ThreadSafe() {
		// Arrange
		var cache = new ThumbnailLruCache(100);
		var tasks = new List<Task>();
		var exceptions = new List<Exception>();

		// Act - Add entries concurrently
		for (int i = 0; i < 50; i++) {
			int index = i;
			tasks.Add(Task.Run(() => {
				try {
					cache.GetOrAdd($"key{index}", () => new byte[] { (byte)index });
				}
				catch (Exception ex) {
					lock (exceptions) {
						exceptions.Add(ex);
					}
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		// Assert
		Assert.Empty(exceptions);
		Assert.True(cache.Count > 0);
	}

	[Fact]
	public void GetOrAdd_LargePayload_HandledCorrectly() {
		// Arrange
		var cache = new ThumbnailLruCache(5);
		var largeData = new byte[1024 * 1024]; // 1MB

		// Act
		var result = cache.GetOrAdd("large", () => largeData);

		// Assert
		Assert.Equal(1024 * 1024, result.Length);
	}

	[Fact]
	public void GetOrAdd_ReturnsNull_WhenFactoryReturnsNull() {
		// Arrange
		var cache = new ThumbnailLruCache(10);

		// Act
		var result = cache.GetOrAdd("key1", () => null!);

		// Assert
		Assert.Null(result);
	}
}
