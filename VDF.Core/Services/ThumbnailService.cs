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

using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using VDF.Core.Utils;

namespace VDF.Core.Services {

	/// <summary>
	/// Configuration for <see cref="ThumbnailService"/>.
	/// </summary>
	public sealed class ThumbnailServiceOptions {
		/// <summary>Maximum number of entries in the in-memory LRU cache. Default 2048.</summary>
		public int LruCapacity { get; set; } = 2048;
		/// <summary>
		/// Folder where <c>thumbs.pack</c> + <c>thumbs.idx</c> are stored. Null = no persistence
		/// (memory-only). When set, the pack is opened/created on construction.
		/// </summary>
		public string? PackFolder { get; set; }
		/// <summary>
		/// Optional list of allowed root directories for path validation. When non-empty,
		/// <see cref="ThumbnailService.GetThumbnailBytes"/> rejects paths outside these roots.
		/// Null/empty = no restriction (suitable for local GUI use).
		/// </summary>
		public IReadOnlyList<string>? AllowedRoots { get; set; }
	}

	/// <summary>A single thumbnail extraction request for batch pre-extraction.</summary>
	public sealed record ThumbnailRequest(string Path, TimeSpan Position, int MaxWidth, int JpegQuality);

	/// <summary>
	/// Two-level thumbnail cache: in-memory LRU (byte[] JPEG) + persistent pack file
	/// (<c>thumbs.pack</c> + <c>thumbs.idx</c>). Replaces GUI's <c>ThumbPack</c> +
	/// <c>LRUBitmapCache</c> and Web's <c>ThumbnailLruCache</c> with a single Core service.
	///
	/// The service does NOT depend on Avalonia or ASP.NET Core. It returns byte[] JPEGs;
	/// GUI converts to Avalonia Bitmap in its wrapper, Web writes bytes directly to HTTP response.
	///
	/// Pack file format is identical to the previous GUI <c>ThumbPack</c> format, so existing
	/// user packs load without migration.
	/// </summary>
	public sealed class ThumbnailService : IDisposable {

		readonly ScanEngine? _engine;
		readonly LruCache _lru;
		ThumbPack? _pack;
		string[]? _normalizedRoots;
		bool _disposed;

		/// <summary>
		/// Construct with a <see cref="ScanEngine"/> (for <see cref="ScanEngine.ExtractThumbnailJpeg"/>)
		/// and options (LRU size, pack folder, allowed roots). The engine may be null when only pack
		/// operations are used (e.g. GUI's batch-write flow); <see cref="GetThumbnailBytes"/> requires
		/// a non-null engine to extract on miss.
		/// </summary>
		public ThumbnailService(ScanEngine? engine, ThumbnailServiceOptions options) {
			if (options == null) throw new ArgumentNullException(nameof(options));
			_engine = engine;
			_lru = new LruCache(Math.Max(1, options.LruCapacity));
			if (!string.IsNullOrEmpty(options.PackFolder))
				_pack = ThumbPack.Open(options.PackFolder!);
			_normalizedRoots = options.AllowedRoots != null && options.AllowedRoots.Count > 0
				? options.AllowedRoots
					.Where(r => !string.IsNullOrEmpty(r))
					.Select(r => Path.GetFullPath(r))
					.ToArray()
				: null;
		}

		/// <summary>True when a persistent pack file is configured.</summary>
		public bool HasPack => _pack != null;

		/// <summary>Folder containing the pack file, or null when persistence is disabled.</summary>
		public string? PackFolder => _pack?.Folder;

		/// <summary>Number of entries currently in the in-memory LRU.</summary>
		public int LruCount => _lru.Count;

		// ── On-demand retrieval (Web) ───────────────────────────────────────

		/// <summary>
		/// On-demand thumbnail retrieval with two-level cache: LRU → pack → extract.
		/// Returns JPEG bytes, or null when extraction fails or the path is rejected by
		/// <see cref="ThumbnailServiceOptions.AllowedRoots"/>.
		/// </summary>
		public byte[]? GetThumbnailBytes(string path, TimeSpan position, int maxWidth, int jpegQuality, CancellationToken cancellationToken = default) {
			ThrowIfDisposed();
			if (string.IsNullOrEmpty(path)) return null;
			if (!IsPathAllowed(path)) return null;

			string key = MakeKey(path, position, maxWidth, jpegQuality);

			// Level 1: in-memory LRU
			if (_lru.TryGet(key, out var cached)) return cached;

			// Level 2: persistent pack
			if (_pack != null && _pack.TryGetEntry(key, out _, out var len) && len > 0) {
				using var stream = _pack.OpenKey(key);
				if (stream != null) {
					var bytes = new byte[len];
					int read = 0;
					while (read < len) {
						int n = stream.Read(bytes, read, len - read);
						if (n <= 0) break;
						read += n;
					}
					if (read == len) {
						_lru.Set(key, bytes);
						return bytes;
					}
				}
			}

			// Extract via ScanEngine
			if (_engine == null) return null;
			var jpeg = ScanEngine.ExtractThumbnailJpeg(path, position, maxWidth, jpegQuality);
			if (jpeg == null || jpeg.Length == 0) return null;

			// Store in both levels
			_lru.Set(key, jpeg);
			_pack?.AppendIfMissing(key, s => s.Write(jpeg, 0, jpeg.Length));

			return jpeg;
		}

		/// <summary>
		/// Batch pre-extract thumbnails for the given requests. Used by GUI's batch mode and
		/// any caller that wants to warm the cache. Reports the number of completed items.
		/// </summary>
		public async Task PreExtractThumbnailsAsync(
			IEnumerable<ThumbnailRequest> requests,
			IProgress<int>? progress,
			CancellationToken cancellationToken) {
			ThrowIfDisposed();
			var list = requests.ToList();
			int done = 0;
			await Parallel.ForEachAsync(list, new ParallelOptions {
				CancellationToken = cancellationToken,
				MaxDegreeOfParallelism = Environment.ProcessorCount,
			}, (req, ct) => {
				GetThumbnailBytes(req.Path, req.Position, req.MaxWidth, req.JpegQuality, ct);
				int current = Interlocked.Increment(ref done);
				progress?.Report(current);
				return ValueTask.CompletedTask;
			});
		}

		// ── Pack operations (GUI wrapper) ───────────────────────────────────

		/// <summary>
		/// Append a JPEG to the pack if the key is missing (or the existing entry is zero-length).
		/// Returns the (offset, length) of the entry. No-op when no pack is configured.
		/// </summary>
		public (long off, int len) AppendIfMissing(string key, Action<Stream> writeJpeg) {
			ThrowIfDisposed();
			if (_pack == null) return (0, 0);
			return _pack.AppendIfMissing(key, writeJpeg);
		}

		/// <summary>Open a stream over a pack entry, or null if the key is absent. Caller disposes.</summary>
		public Stream? OpenKey(string key) {
			ThrowIfDisposed();
			return _pack?.OpenKey(key);
		}

		/// <summary>Check whether the pack has an entry for the key, returning its (offset, length).</summary>
		public bool TryGetPackEntry(string key, out long off, out int len) {
			ThrowIfDisposed();
			if (_pack == null) { off = 0; len = 0; return false; }
			return _pack.TryGetEntry(key, out off, out len);
		}

		/// <summary>Flush the pack index to disk.</summary>
		public void FlushIndex() {
			ThrowIfDisposed();
			_pack?.FlushIndex();
		}

		/// <summary>Copy the raw pack file bytes to a destination stream (for backup/restore).</summary>
		public void CopyPackTo(Stream destination) {
			ThrowIfDisposed();
			_pack?.CopyTo(destination);
		}

		// ── Pack loading / switching ────────────────────────────────────────

		/// <summary>
		/// Open a pack from <paramref name="folder"/> (containing <c>thumbs.pack</c> + <c>thumbs.idx</c>),
		/// replacing any existing pack. The previous pack is disposed.
		/// </summary>
		public void OpenPack(string folder) {
			ThrowIfDisposed();
			try { _pack?.Dispose(); } catch { /* ignore */ }
			_pack = ThumbPack.Open(folder);
		}

		/// <summary>
		/// Load an existing pack from explicit file paths, replacing any existing pack.
		/// The previous pack is disposed.
		/// </summary>
		public void LoadFromPack(string packPath, string idxPath) {
			ThrowIfDisposed();
			try { _pack?.Dispose(); } catch { /* ignore */ }
			_pack = ThumbPack.OpenFiles(packPath, idxPath);
		}

		/// <summary>Flush all pending state: pack index to disk.</summary>
		public void Flush() {
			ThrowIfDisposed();
			_pack?.FlushIndex();
		}

		/// <summary>Clear the in-memory LRU cache. Pack entries are unaffected.</summary>
		public void ClearMemoryCache() {
			ThrowIfDisposed();
			_lru.Clear();
		}

		// ── Path safety ─────────────────────────────────────────────────────

		/// <summary>
		/// Returns true when <paramref name="path"/> is allowed by
		/// <see cref="ThumbnailServiceOptions.AllowedRoots"/>. When no roots are configured,
		/// all paths are allowed (local GUI mode).
		/// </summary>
		public bool IsPathAllowed(string path) {
			if (_normalizedRoots == null || _normalizedRoots.Length == 0) return true;
			try {
				string full = Path.GetFullPath(path);
				foreach (var root in _normalizedRoots) {
					if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
						return true;
					if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
						full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
						return true;
				}
				return false;
			}
			catch {
				return false;
			}
		}

		/// <summary>
		/// Update the allowed-roots list at runtime (e.g. when the Web's IncludeList changes
		/// after a settings PUT). Pass null to disable path validation.
		/// </summary>
		public void SetAllowedRoots(IReadOnlyList<string>? roots) {
			_normalizedRoots = roots != null && roots.Count > 0
				? roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => Path.GetFullPath(r)).ToArray()
				: null;
		}

		// ── Key generation ──────────────────────────────────────────────────

		/// <summary>Build a cache key from path + extraction parameters (XxHash64 hex).</summary>
		public static string MakeKey(string path, TimeSpan position, int maxWidth, int jpegQuality) {
			return XxHash64Hex($"{path}|{position.TotalSeconds:F2}|{maxWidth}|{jpegQuality}");
		}

		/// <summary>XxHash64 of a string, returned as lowercase hex. Same scheme as the previous GUI helper.</summary>
		public static string XxHash64Hex(string s) {
			ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(s.AsSpan());
			byte[] hash = XxHash64.Hash(bytes);
			return Convert.ToHexStringLower(hash);
		}

		void ThrowIfDisposed() {
			if (_disposed) throw new ObjectDisposedException(nameof(ThumbnailService));
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			try { _pack?.Dispose(); } catch { /* ignore */ }
		}
	}

	/// <summary>
	/// A large, append-only thumbnail cache:
	///  - thumbs.pack : Binary data (JPEGs in sequence)
	///  - thumbs.idx  : JSON { key -> (offset,length) }
	///
	/// Moved from VDF.GUI/Utils/ThumbnailStore.cs to VDF.Core so both GUI and Web share the
	/// same pack format. The JSON index uses <see cref="CoreJsonContext"/>'s ThumbPackIndex
	/// type info, which is wire-compatible with the previous GUI GuiJsonFieldsContext format.
	/// </summary>
	internal sealed class ThumbPack : IDisposable {
		readonly FileStream _fs;
		readonly string _packPath;
		readonly string _idxPath;
		Dictionary<string, (long off, int len)> _idx;
		readonly object _gate = new();
		public readonly string Folder;

		ThumbPack(FileStream fs, string idxPath, Dictionary<string, (long, int)> idx, string packPath, string folder) {
			_fs = fs; _idxPath = idxPath; _idx = idx; _packPath = packPath; Folder = folder;
		}

		public static ThumbPack Open(string folder) {
			Directory.CreateDirectory(folder);
			string packPath = Path.Combine(folder, "thumbs.pack");
			string idxPath = Path.Combine(folder, "thumbs.idx");
			return OpenFiles(packPath, idxPath);
		}

		public static ThumbPack OpenFiles(string packPath, string idxPath) {
			var fs = new FileStream(packPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			Dictionary<string, (long, int)> idx = File.Exists(idxPath)
				? JsonSerializer.Deserialize(
					File.ReadAllBytes(idxPath), CoreJsonContext.Default.ThumbPackIndex) ?? new()
				: new();
			string folder = Path.GetDirectoryName(packPath)!;
			return new ThumbPack(fs, idxPath, idx, packPath, folder);
		}

		public bool Contains(string key) {
			lock (_gate) return _idx.ContainsKey(key);
		}

		/// <summary>
		/// Inserts JPEG from src into the pack (if key does not exist OR existing entry
		/// is zero-length). Returns (offset, length).
		///
		/// Zero-length entries are treated as "missing" (issue #751): a 0-byte write means
		/// the producer failed, and recording a (off, 0) entry would permanently latch the key,
		/// so retries no-op forever. Leaving _idx untouched means the next attempt re-extracts.
		/// </summary>
		public (long off, int len) AppendIfMissing(string key, Action<Stream> writeJpeg) {
			lock (_gate) {
				if (_idx.TryGetValue(key, out var e) && e.Item2 > 0) return e;
				_fs.Seek(0, SeekOrigin.End);
				long off = _fs.Position;
				using var limiting = new LengthCountingStream(_fs);
				writeJpeg(limiting);
				limiting.Flush();
				int len = checked((int)limiting.BytesWritten);
				if (len == 0)
					return (off, 0);
				_idx[key] = (off, len);
				return (off, len);
			}
		}

		public bool TryGetEntry(string key, out long off, out int len) {
			lock (_gate) {
				if (_idx.TryGetValue(key, out var e)) { off = e.off; len = e.len; return true; }
				off = 0; len = 0; return false;
			}
		}

		public Stream? OpenKey(string key) {
			lock (_gate) {
				if (!_idx.TryGetValue(key, out var e)) return null;
				var rfs = new FileStream(_packPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, useAsync: false);
				return new StreamSlice(rfs, e.off, e.len, leaveOpen: true);
			}
		}

		public void FlushIndex() {
			lock (_gate) {
				var json = JsonSerializer.Serialize(_idx, CoreJsonContext.Default.ThumbPackIndex);
				File.WriteAllText(_idxPath, json);
			}
		}

		public void CopyTo(Stream destination) {
			lock (_gate) {
				_fs.Flush();
				_fs.Seek(0, SeekOrigin.Begin);
				_fs.CopyTo(destination);
				_fs.Seek(0, SeekOrigin.End);
			}
		}

		public void Dispose() { FlushIndex(); _fs.Dispose(); }

		public string? GetDirectory() => Folder;
	}

	/// <summary>Stream slice without copy (reads range [offset, offset+len) from shared FileStream).</summary>
	internal sealed class StreamSlice : Stream {
		readonly FileStream _fs;
		readonly long _start;
		readonly long _len;
		long _pos;
		readonly bool _leaveOpen;
		public StreamSlice(FileStream fs, long start, int len, bool leaveOpen) {
			_fs = fs; _start = start; _len = len; _pos = 0; _leaveOpen = leaveOpen;
		}
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => _len;
		public override long Position { get => _pos; set => Seek(value, SeekOrigin.Begin); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) {
			count = (int)Math.Min(count, _len - _pos);
			if (count <= 0) return 0;
			lock (_fs) {
				_fs.Seek(_start + _pos, SeekOrigin.Begin);
				int n = _fs.Read(buffer, offset, count);
				_pos += n; return n;
			}
		}
		public override long Seek(long offset, SeekOrigin origin) {
			long np = origin switch {
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _pos + offset,
				SeekOrigin.End => _len + offset,
				_ => _pos
			};
			_pos = Math.Max(0, Math.Min(_len, np));
			return _pos;
		}
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		protected override void Dispose(bool disposing) { if (!disposing || _leaveOpen) return; try { _fs.Dispose(); } catch { } }
	}

	internal sealed class LengthCountingStream : Stream {
		readonly Stream _inner;
		public long BytesWritten { get; private set; }
		public LengthCountingStream(Stream inner) => _inner = inner;
		public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() => _inner.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) {
			_inner.Write(buffer, offset, count);
			BytesWritten += count;
		}
		public override void Write(ReadOnlySpan<byte> buffer) {
			_inner.Write(buffer);
			BytesWritten += buffer.Length;
		}
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) {
			BytesWritten += buffer.Length;
			return _inner.WriteAsync(buffer, ct);
		}
	}

	/// <summary>
	/// Thread-safe LRU cache for byte[] JPEGs. Deterministic eviction: when capacity is exceeded,
	/// the least-recently-used entry is evicted. Used as Level 1 of the two-level cache.
	/// </summary>
	internal sealed class LruCache {
		readonly int _capacity;
		readonly object _gate = new();
		readonly LinkedList<string> _order = new();
		readonly Dictionary<string, byte[]> _map = new();

		public LruCache(int capacity) => _capacity = capacity;

		public bool TryGet(string key, out byte[]? value) {
			lock (_gate) {
				if (_map.TryGetValue(key, out value)) {
					_order.Remove(key);
					_order.AddFirst(key);
					return true;
				}
				value = null;
				return false;
			}
		}

		public void Set(string key, byte[] value) {
			lock (_gate) {
				if (_map.TryGetValue(key, out _))
					_order.Remove(key);
				_map[key] = value;
				_order.AddFirst(key);
				while (_map.Count > _capacity) {
					var last = _order.Last!;
					_order.RemoveLast();
					_map.Remove(last.Value);
				}
			}
		}

		public int Count { get { lock (_gate) return _map.Count; } }

		public void Clear() { lock (_gate) { _map.Clear(); _order.Clear(); } }
	}
}
