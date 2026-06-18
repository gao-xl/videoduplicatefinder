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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */

using System;
using System.IO;
using VDF.Core.Services;

namespace VDF.GUI.Utils {
	/// <summary>
	/// Thin GUI wrapper around <see cref="ThumbnailService"/>. Provides:
	///  - Static <see cref="Provider"/> access for VMs (backward compat with the previous ThumbPack-based API)
	///  - <see cref="LRUBitmapCache"/>: Avalonia Bitmap conversion layer on top of Core's byte[] LRU
	///  - Pack-folder management helpers (<see cref="InvalidateIfWidthChanged"/>, <see cref="DeletePackFolder"/>)
	///  - Backup/restore integration (the Core pack file is packed/unpacked by GUI's zip code)
	///
	/// The pack persistence, byte[] LRU, and on-demand extraction logic live in
	/// <see cref="ThumbnailService"/> (VDF.Core). This wrapper only adds GUI-specific
	/// concerns: Bitmap conversion and temp-folder lifecycle.
	/// </summary>
	internal static class ThumbCacheHelpers {
		/// <summary>
		/// The active <see cref="ThumbnailService"/>, or null when no pack is open.
		/// GUI VMs access this to call <see cref="ThumbnailService.AppendIfMissing"/>,
		/// <see cref="ThumbnailService.OpenKey"/>, <see cref="ThumbnailService.FlushIndex"/>, etc.
		/// </summary>
		public static ThumbnailService? Provider { get; set; }

		/// <summary>XxHash64 of a string, returned as lowercase hex. Delegates to Core.</summary>
		public static string XxHash64Hex(string s) => ThumbnailService.XxHash64Hex(s);

		public static void DeletePackFolder(string? folder) {
			try {
				if (Directory.Exists(folder))
					Directory.Delete(folder, recursive: true);
			}
			catch { /* ignore */ }
		}

		public static string EnsureFolder(string baseFolder, string name) {
			var f = Path.Combine(baseFolder, name);
			Directory.CreateDirectory(f);
			return f;
		}

		/// <summary>
		/// Deletes the pack if the thumbnail width setting changed since the cache was created.
		/// Stores the current width in a marker file alongside the pack.
		/// </summary>
		public static void InvalidateIfWidthChanged(string packFolder, int currentWidth) {
			try {
				var markerPath = Path.Combine(packFolder, "thumbwidth.txt");
				if (File.Exists(markerPath)) {
					var stored = File.ReadAllText(markerPath).Trim();
					if (int.TryParse(stored, out var oldWidth) && oldWidth == currentWidth)
						return; // width unchanged, cache is valid
				}
				// Width changed or marker doesn't exist — delete the pack so it regenerates
				DeletePackFolder(packFolder);
				Directory.CreateDirectory(packFolder);
				File.WriteAllText(Path.Combine(packFolder, "thumbwidth.txt"), currentWidth.ToString());
			}
			catch { /* ignore */ }
		}

		/// <summary>
		/// Disposes the current provider, deletes its pack folder, and opens a new
		/// <see cref="ThumbnailService"/> for <paramref name="packFolder"/> using the
		/// given <paramref name="engine"/> (for on-demand extraction).
		/// </summary>
		public static void SetActiveProvider(VDF.Core.ScanEngine engine, string packFolder) {
			var oldFolder = Provider?.PackFolder;
			try { Provider?.Dispose(); } catch { }
			DeletePackFolder(oldFolder);

			Provider = new ThumbnailService(engine, new ThumbnailServiceOptions { PackFolder = packFolder });
		}
	}

	/// <summary>
	/// Small, size-limited LRU cache for UI bitmaps (RAM capped). Sits on top of Core's
	/// byte[] LRU as the Bitmap conversion layer: the Core service returns byte[] JPEGs,
	/// and this cache avoids re-decoding the same JPEG to an Avalonia Bitmap on every access.
	/// </summary>
	internal static class LRUBitmapCache {
		static readonly object gate = new();
		static readonly LinkedList<string> lru = new();
		static readonly Dictionary<string, (Avalonia.Media.Imaging.Bitmap bmp, LinkedListNode<string> node, long size)> map = new();
		static long currentBytes;
		public static long MaxBytes { get; set; } = 128L * 1024 * 1024; // 128 MB

		static long ApproxSize(Avalonia.Media.Imaging.Bitmap bmp)
			=> (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;

		public static Avalonia.Media.Imaging.Bitmap? GetOrCreate(string key, Func<Avalonia.Media.Imaging.Bitmap?> loader) {
			lock (gate) {
				if (map.TryGetValue(key, out var e)) {
					lru.Remove(e.node); lru.AddFirst(e.node); return e.bmp;
				}
			}
			var bmp = loader();
			if (bmp == null) return null;
			var size = ApproxSize(bmp);
			lock (gate) {
				var node = new LinkedListNode<string>(key);
				lru.AddFirst(node);
				map[key] = (bmp, node, size);
				currentBytes += size;
				EvictIfNeeded();
			}
			return bmp;
		}

		static void EvictIfNeeded() {
			while (currentBytes > MaxBytes && lru.Last != null) {
				var key = lru.Last.Value;
				lru.RemoveLast();
				if (map.Remove(key, out var e)) {
					currentBytes -= e.size;
					// Important: do not dispose – UI may still display the bitmap.
					//try { e.bmp.Dispose(); } catch { }
				}
			}
		}
	}

}
