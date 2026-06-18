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

using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ReactiveUI;
using VDF.Core.FFTools;
using VDF.Core.Services;
using VDF.Core.Utils;
using System.Reactive;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
		bool _isFfmpegDownloadInProgress;
		public bool IsFfmpegDownloadInProgress {
			get => _isFfmpegDownloadInProgress;
			set => this.RaiseAndSetIfChanged(ref _isFfmpegDownloadInProgress, value);
		}

		public ReactiveCommand<Unit, Unit> DownloadSharedFfmpegCommand => ReactiveCommand.CreateFromTask(async () => {
			await DownloadSharedFfmpegAsync();
		});

		async Task DownloadSharedFfmpegAsync() {
			if (IsFfmpegDownloadInProgress) return;
			IsFfmpegDownloadInProgress = true;
			IsBusy = true;
			IsBusyOverlayText = App.Lang["Message.FfmpegDownloadPreparing"];
			string? errorMessage = null;
			string? extractedFolder = null;
			string? targetFolder = null;

			var coreService = new FFmpegSetupService();
			var progress = new UIThreadProgress<FFmpegSetupProgress>(UpdateDownloadProgress);

			try {
				var result = await coreService.DownloadAndInstallAsync(progress);
				extractedFolder = result.ExtractedFolder;
				targetFolder = result.TargetFolder;

				if (!result.Success) {
					errorMessage = FormatErrorMessage(result);
				}

				bool ffmpegFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) != null;
				bool ffprobeFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) != null;
				// When a non-HTTP exception aborted the run, the original code surfaced the temp
				// extraction folder in the instructions; otherwise the install target (null until
				// files are copied). Preserve that distinction.
				string? folderForInstructions = result.Exception != null ? extractedFolder : targetFolder;
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(ffmpegFound, ffprobeFound, folderForInstructions, errorMessage));
			}
			catch (HttpRequestException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (IOException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadIoFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (UnauthorizedAccessException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadAccessFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (Exception ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			finally {
				IsBusy = false;
				IsBusyOverlayText = string.Empty;
				IsFfmpegDownloadInProgress = false;
			}
		}

		static string? FormatErrorMessage(FFmpegSetupResult result) {
			if (result.FailureReason == FFmpegSetupFailureReason.NoPlansAvailable)
				return App.Lang["Message.FfmpegDownloadUnsupported"];
			if (result.Exception is UnauthorizedAccessException uaEx)
				return string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadAccessFailed"], uaEx.Message);
			if (result.Exception is IOException ioEx)
				return string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadIoFailed"], ioEx.Message);
			if (result.Exception is HttpRequestException httpEx)
				return string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], httpEx.Message);
			if (result.Exception != null)
				return string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], result.Exception.Message);
			// Per-plan HTTP failures (all plans exhausted) — ErrorMessage holds the last ex.Message.
			return string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], result.ErrorMessage ?? string.Empty);
		}

		void UpdateDownloadProgress(FFmpegSetupProgress p) {
			switch (p.Stage) {
			case FFmpegSetupStage.Preparing:
				IsBusyOverlayText = App.Lang["Message.FfmpegDownloadPreparing"];
				break;
			case FFmpegSetupStage.Downloading:
				IsBusyOverlayText = string.Format(
					CultureInfo.InvariantCulture,
					App.Lang["Message.FfmpegDownloadProgress"],
					p.DisplayName,
					Math.Round(p.DownloadPercent, 1),
					FFmpegSetupService.FormatBytes(p.BytesDownloaded),
					FFmpegSetupService.FormatBytes(p.TotalBytes));
				break;
			case FFmpegSetupStage.Verifying:
				IsBusyOverlayText = App.Lang["Message.FfmpegDownloadVerifying"];
				break;
			case FFmpegSetupStage.Extracting:
			case FFmpegSetupStage.Installing:
				IsBusyOverlayText = App.Lang["Message.FfmpegDownloadExtracting"];
				break;
			}
		}

		static string BuildFfmpegInstallInstructions(bool ffmpegFound, bool ffprobeFound, string? targetFolder, string? errorMessage) {
			var sb = new System.Text.StringBuilder();
			if (!string.IsNullOrWhiteSpace(errorMessage)) {
				sb.AppendLine(errorMessage);
				sb.AppendLine();
			}

			if (ffmpegFound && ffprobeFound) {
				// Everything is in place — the manual install/restart instructions below
				// would only contradict the success (the scan continues right away).
				sb.AppendLine(App.Lang["Message.FfmpegDownloadVerified"]);
				if (!string.IsNullOrWhiteSpace(targetFolder)) {
					sb.AppendLine();
					sb.AppendLine(string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadTargetFolder"], targetFolder));
				}
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadMissing"]);

			if (!string.IsNullOrWhiteSpace(targetFolder)) {
				sb.AppendLine();
				sb.AppendLine(string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadTargetFolder"], targetFolder));
			}

			sb.AppendLine();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadWindowsInfo"]);
				sb.AppendLine();
				sb.AppendLine(App.Lang["Message.FfmpegDownloadWindowsRestart"]);
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadStopApp"]);
			sb.AppendLine();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacHeader"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacBrew"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacPorts"]);
				return sb.ToString();
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxHeader"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxDeb"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxFedora"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxArch"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxSuse"]);
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadUnsupported"]);
			return sb.ToString();
		}
	}

	/// <summary>
	/// Forwards <see cref="IProgress{T}"/> reports to the Avalonia UI thread via
	/// <see cref="Dispatcher.UIThread.Post"/>, matching the original FfmpegDownloader behaviour.
	/// </summary>
	sealed class UIThreadProgress<T> : IProgress<T> {
		readonly Action<T> _callback;
		public UIThreadProgress(Action<T> callback) => _callback = callback;
		public void Report(T value) => Dispatcher.UIThread.Post(() => _callback(value));
	}
}
