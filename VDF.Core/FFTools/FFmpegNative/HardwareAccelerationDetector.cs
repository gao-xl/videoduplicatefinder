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

using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using VDF.Core.Utils;

namespace VDF.Core.FFTools.FFmpegNative {

	/// <summary>
	/// Detects available hardware acceleration methods at runtime by attempting
	/// to create each HW device type. Results are cached after the first call.
	/// </summary>
	static unsafe class HardwareAccelerationDetector {

		static Lazy<AVHWDeviceType[]> _cachedDevices = CreateLazy();

		static Lazy<AVHWDeviceType[]> CreateLazy() =>
			new Lazy<AVHWDeviceType[]>(DetectAvailableDevicesCore, LazyThreadSafetyMode.ExecutionAndPublication);

		/// <summary>
		/// Detects which hardware acceleration device types are available on this system.
		/// Results are cached after the first successful call.
		/// </summary>
		public static AVHWDeviceType[] DetectAvailableDevices() => _cachedDevices.Value;

		static AVHWDeviceType[] DetectAvailableDevicesCore() {
			if (!FFmpegHelper.CanLoadNativeLibraries)
				return Array.Empty<AVHWDeviceType>();

			var available = new System.Collections.Generic.List<AVHWDeviceType>();

			// Probe each HW device type by trying to create a device context.
			// Order matters: prefer types that typically give better performance.
			var candidates = new[] {
				AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
				AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
				AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
				AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
				AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
			};

			foreach (var deviceType in candidates) {
				if (TryCreateDevice(deviceType)) {
					available.Add(deviceType);
				}
			}

			return available.ToArray();
		}

		/// <summary>
		/// Returns the best available hardware device type based on the current
		/// platform and detected devices, or <see cref="AVHWDeviceType.AV_HWDEVICE_TYPE_NONE"/>
		/// when no suitable device is found.
		/// </summary>
		public static AVHWDeviceType GetBestAvailableDevice() {
			var devices = DetectAvailableDevices();
			if (devices.Length == 0)
				return AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

			// On Windows prefer D3D11VA (widest codec support), then QSV, then CUDA
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_QSV) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_QSV;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2;
			}
			// On Linux prefer VAAPI, then QSV, then CUDA
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_QSV) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_QSV;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA;
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU;
			}
			// On macOS prefer VideoToolbox
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				if (Array.IndexOf(devices, AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX) >= 0)
					return AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
			}

			// Fallback: return the first available
			return devices[0];
		}

		/// <summary>
		/// Resets the cached detection results so the next call to
		/// <see cref="DetectAvailableDevices"/> will re-probe.
		/// </summary>
		public static void InvalidateCache() {
			_cachedDevices = null;
		}

		static bool TryCreateDevice(AVHWDeviceType deviceType) {
			try {
				AVBufferRef* deviceCtx = null;
				int ret = ffmpeg.av_hwdevice_ctx_create(&deviceCtx, deviceType, null, null, 0);
				if (ret >= 0 && deviceCtx != null) {
					ffmpeg.av_buffer_unref(&deviceCtx);
					return true;
				}
				// Clean up on failure (deviceCtx may still have been allocated in some error paths)
				if (deviceCtx != null)
					ffmpeg.av_buffer_unref(&deviceCtx);
			}
			catch {
				// av_hwdevice_ctx_create can throw via FFmpeg.AutoGen if the function
				// symbol is not resolved (older FFmpeg builds without the device type).
			}
			return false;
		}
	}
}
