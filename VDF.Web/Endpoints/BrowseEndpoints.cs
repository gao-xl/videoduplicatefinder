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

using System.Security.Cryptography;

namespace VDF.Web.Endpoints;

static class BrowseEndpoints {
	public static WebApplication MapBrowseApi(this WebApplication app) {
		app.MapPost("/api/browse", async (HttpRequest request) => {
			try {
				using var reader = new StreamReader(request.Body);
				var body = await reader.ReadToEndAsync();
				var json = System.Text.Json.JsonDocument.Parse(body);
				var path = json.RootElement.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : "/";

				if (string.IsNullOrEmpty(path))
					path = "/";

				path = Path.GetFullPath(path);

				if (!Directory.Exists(path))
					return Results.NotFound(new { error = "directory_not_found" });

				var entries = new List<object>();

				try {
					foreach (var dir in Directory.EnumerateDirectories(path)) {
						var name = Path.GetFileName(dir);
						if (name != null) {
							entries.Add(new {
								name,
								path = dir,
								isDirectory = true,
							});
						}
					}
				}
				catch { }

				try {
					foreach (var file in Directory.EnumerateFiles(path)) {
						var name = Path.GetFileName(file);
						if (name != null) {
							entries.Add(new {
								name,
								path = file,
								isDirectory = false,
							});
						}
					}
				}
				catch { }

				return Results.Ok(entries);
			}
			catch (Exception ex) {
				return Results.Json(new { error = ex.Message }, statusCode: 500);
			}
		}).RequireAuthorization();

		return app;
	}
}
