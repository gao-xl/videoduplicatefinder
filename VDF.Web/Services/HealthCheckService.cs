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

using VDF.Core;
using VDF.Core.Utils;

namespace VDF.Web.Services;

public sealed class HealthReport {
	public string Status { get; init; } = "Unhealthy";
	public bool Ffmpeg { get; init; }
	public bool Database { get; init; }
	public string Timestamp { get; init; } = DateTime.UtcNow.ToString("o");
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

		return Task.FromResult(new HealthReport {
			Status = status,
			Ffmpeg = ffmpegOk,
			Database = databaseOk,
		});
	}

	static bool CheckDatabaseAccessible() {
		try {
			string dbFolder = CoreUtils.ResolveDatabaseFolder(null);
			if (!Directory.Exists(dbFolder))
				Directory.CreateDirectory(dbFolder);
			// Verify write access by testing a temp file in the database directory
			string testFile = Path.Combine(dbFolder, $".healthcheck-{Guid.NewGuid():N}");
			File.WriteAllText(testFile, "ok");
			File.Delete(testFile);
			return true;
		}
		catch {
			return false;
		}
	}
}
