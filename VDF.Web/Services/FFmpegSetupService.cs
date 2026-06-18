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
using VDF.Core.Services;

namespace VDF.Web.Services;

public enum FFmpegSetupState {
	Idle,
	Checking,
	Ready,
	Downloading,
	Failed,
	DockerWarning
}

/// <summary>
/// Web-facing FFmpeg setup facade. Delegates the download/verify/extract/install pipeline to
/// <see cref="VDF.Core.Services.FFmpegSetupService"/> and keeps only Web-specific concerns:
/// Docker detection (<see cref="IsRunningInDocker"/>) and the <see cref="StateChanged"/>/
/// <see cref="StatusMessage"/>/<see cref="DownloadProgress"/> surface that Program.cs and the
/// React frontend observe.
/// </summary>
public sealed class FFmpegSetupService {
	readonly VDF.Core.Services.FFmpegSetupService _core = new();

	public FFmpegSetupState State { get; private set; }
	public string StatusMessage { get; private set; } = string.Empty;
	public double DownloadProgress { get; private set; }
	public bool IsReady => State == FFmpegSetupState.Ready;

	public event Action? StateChanged;

	void Notify() => StateChanged?.Invoke();

	public async Task CheckAndSetupAsync() {
		State = FFmpegSetupState.Checking;
		StatusMessage = "Checking FFmpeg availability...";
		Notify();

		await Task.Yield();

		if (ScanEngine.FFmpegExists && ScanEngine.FFprobeExists) {
			State = FFmpegSetupState.Ready;
			StatusMessage = "FFmpeg is available.";
			Notify();
			return;
		}

		if (IsRunningInDocker()) {
			State = FFmpegSetupState.DockerWarning;
			StatusMessage = "FFmpeg not found in Docker container. Add 'RUN apt-get update && apt-get install -y ffmpeg' to your Dockerfile, or mount FFmpeg binaries as a volume.";
			Notify();
			return;
		}

		await DownloadFfmpegAsync();
	}

	static bool IsRunningInDocker() {
		if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
			return true;
		try {
			if (File.Exists("/.dockerenv"))
				return true;
		}
		catch { }
		return false;
	}

	async Task DownloadFfmpegAsync() {
		State = FFmpegSetupState.Downloading;
		StatusMessage = "Preparing FFmpeg download...";
		DownloadProgress = 0;
		Notify();

		// Map Core progress to Web's 0–100 progress bar: download occupies 0–85, verify 87,
		// extract 90, install 95, done 100 — matching the prior Web-specific percent bands.
		var progress = new Progress<FFmpegSetupProgress>(p => {
			StatusMessage = p.StatusMessage;
			DownloadProgress = p.Stage switch {
				FFmpegSetupStage.Downloading => p.DownloadPercent * 0.85,
				FFmpegSetupStage.Verifying => 87,
				FFmpegSetupStage.Extracting => 90,
				FFmpegSetupStage.Installing => 95,
				FFmpegSetupStage.Completed => 100,
				_ => DownloadProgress
			};
			Notify();
		});

		var result = await _core.DownloadAndInstallAsync(progress);

		if (result.Success) {
			State = FFmpegSetupState.Ready;
			StatusMessage = "FFmpeg downloaded and installed successfully.";
			DownloadProgress = 100;
		}
		else if (result.FailureReason == FFmpegSetupFailureReason.NoPlansAvailable) {
			State = FFmpegSetupState.Failed;
			StatusMessage = result.ErrorMessage ?? "No FFmpeg download available for this platform/architecture.";
		}
		else if (result.Exception != null) {
			State = FFmpegSetupState.Failed;
			StatusMessage = $"FFmpeg setup failed: {result.Exception.Message}";
		}
		else {
			// All plans exhausted with HTTP errors — ErrorMessage holds the last ex.Message.
			State = FFmpegSetupState.Failed;
			StatusMessage = $"Download failed: {result.ErrorMessage}";
		}
		Notify();
	}
}
