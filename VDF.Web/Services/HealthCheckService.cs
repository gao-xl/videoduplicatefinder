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
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */

using System.Diagnostics;
using System.Reflection;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.Web.Services;

public sealed class HealthReport {
	public string Status { get; init; } = "Unhealthy";
	public bool Ffmpeg { get; init; }
	public bool Database { get; init; }
	public string Timestamp { get; init; } = DateTime.UtcNow.ToString("o");
	public string Version { get; init; } = string.Empty;
	public string RuntimeVersion { get; init; } = string.Empty;
	public long MemoryUsedMb { get; init; }
	public int ThreadCount { get; init; }
	public string FfmpegVersion { get; init; } = string.Empty;
	public int DatabaseEntries { get; init; }
}

public sealed class HealthCheckService {
	public Task<HealthReport> CheckHealthAsync() {
		bool ffmpegOk = ScanEngine.FFmpegExists && ScanEngine.FFprobeExists;
		bool databaseOk = CheckDatabaseAccessible();

		string status = (ffmpegOk, databaseOk) switch {
			(true, true) => "Healthy",
			(_, false) => "Unhealthy",
			_ => "Degraded"
		};

		// Get version info
		string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
		string runtimeVersion = Environment.Version.ToString();

		// Get memory usage
		long memoryUsedMb = GC.GetTotalMemory(false) / (1024 * 1024);

		// Get thread count
		int threadCount = Process.GetCurrentProcess().Threads.Count;

		// Get FFmpeg version
		string ffmpegVersion = GetFfmpegVersion();

		// Get database entry count
		int databaseEntries = DatabaseUtils.Database.Count;

		return Task.FromResult(new HealthReport {
			Status = status,
			Ffmpeg = ffmpegOk,
			Database = databaseOk,
			Version = version,
			RuntimeVersion = runtimeVersion,
			MemoryUsedMb = memoryUsedMb,
			ThreadCount = threadCount,
			FfmpegVersion = ffmpegVersion,
			DatabaseEntries = databaseEntries,
		});
	}

	static string GetFfmpegVersion() {
		try {
			if (!ScanEngine.FFmpegExists) return "not found";
			var psi = new ProcessStartInfo {
				FileName = VDF.Core.FFTools.FfmpegEngine.FFmpegPath,
				Arguments = "-version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var process = Process.Start(psi);
			if (process == null) return "unknown";
			process.WaitForExit(2000);
			string output = process.StandardOutput.ReadToEnd();
			// Extract version from first line: "ffmpeg version X.Y.Z ..."
			var match = System.Text.RegularExpressions.Regex.Match(output, @"ffmpeg version (\S+)");
			return match.Success ? match.Groups[1].Value : "unknown";
		}
		catch {
			return "error";
		}
	}

	static bool CheckDatabaseAccessible() {
		try {
			string dbFolder = CoreUtils.ResolveDatabaseFolder(null);
			if (!Directory.Exists(dbFolder)) {
				// Try to create the directory
				Directory.CreateDirectory(dbFolder);
			}
			// Verify the directory exists and is accessible
			// This avoids creating temp files on every health check
			return Directory.Exists(dbFolder);
		}
		catch {
			return false;
		}
	}
}
