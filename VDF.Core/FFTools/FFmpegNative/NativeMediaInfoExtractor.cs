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
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using VDF.Core.Utils;

namespace VDF.Core.FFTools.FFmpegNative {

	/// <summary>
	/// Extracts <see cref="MediaInfo"/> from a file using FFmpeg.AutoGen (avformat)
	/// without launching an ffprobe CLI process. Returns null on failure so the caller
	/// can fall back to the CLI path.
	/// </summary>
	static unsafe class NativeMediaInfoExtractor {

		/// <summary>
		/// Stored as a class field to prevent the GC from collecting the delegate
		/// while FFmpeg holds a native function pointer to it during blocking I/O.
		/// </summary>
		static AVIOInterruptCB_callback? _interruptCbDelegate;

		/// <summary>
		/// Extracts media information from <paramref name="filePath"/> using native
		/// FFmpeg bindings. Returns null when extraction fails (corrupt file, missing
		/// libraries, etc.) so the caller can fall back to the ffprobe CLI.
		/// </summary>
		public static MediaInfo? Extract(string filePath) {
			if (!FFmpegHelper.CanLoadNativeLibraries)
				return null;

			AVFormatContext* pFormatContext = null;
			try {
				pFormatContext = ffmpeg.avformat_alloc_context();
				if (pFormatContext == null)
					return null;

				// Interrupt callback: abort blocking I/O after 15 seconds.
				long deadlineTicks = Stopwatch.GetTimestamp() + (long)(15.0 / 1000.0 * Stopwatch.Frequency);
				_interruptCbDelegate = new AVIOInterruptCB_callback(_ =>
					Stopwatch.GetTimestamp() > deadlineTicks ? 1 : 0);
				pFormatContext->interrupt_callback = new AVIOInterruptCB { callback = _interruptCbDelegate };

				var pCtx = pFormatContext;
				int openRet = ffmpeg.avformat_open_input(&pCtx, filePath, null, null);
				pFormatContext = pCtx;
				if (openRet < 0)
					return null;

				int infoRet = ffmpeg.avformat_find_stream_info(pFormatContext, null);
				if (infoRet < 0)
					return null;

				var streams = new System.Collections.Generic.List<MediaInfo.StreamInfo>();

				for (int i = 0; i < (int)pFormatContext->nb_streams; i++) {
					var avStream = pFormatContext->streams[i];
					var codecPar = avStream->codecpar;
					var si = new MediaInfo.StreamInfo {
						Index = i.ToString()
					};

					// Codec name — avcodec_get_name returns a managed string in FFmpeg.AutoGen
					si.CodecName = ffmpeg.avcodec_get_name(codecPar->codec_id) ?? string.Empty;

					// Codec long name from descriptor
					AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get(codecPar->codec_id);
					if (desc != null && desc->long_name != null)
						si.CodecLongName = Marshal.PtrToStringAnsi((IntPtr)desc->long_name) ?? string.Empty;
					else
						si.CodecLongName = string.Empty;

					// Codec type
					si.CodecType = codecPar->codec_type switch {
						AVMediaType.AVMEDIA_TYPE_VIDEO => "video",
						AVMediaType.AVMEDIA_TYPE_AUDIO => "audio",
						AVMediaType.AVMEDIA_TYPE_SUBTITLE => "subtitle",
						AVMediaType.AVMEDIA_TYPE_DATA => "data",
						AVMediaType.AVMEDIA_TYPE_ATTACHMENT => "attachment",
						_ => codecPar->codec_type.ToString()
					};

					// Video-specific
					if (codecPar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) {
						si.Width = codecPar->width;
						si.Height = codecPar->height;
						si.PixelFormat = GetPixelFormatName(codecPar->format);
						si.BitRate = codecPar->bit_rate;

						// Frame rate from stream
						AVRational rFrameRate = avStream->r_frame_rate;
						if (rFrameRate.num > 0 && rFrameRate.den > 0)
							si.FrameRate = rFrameRate.num / (float)rFrameRate.den;

						// HDR detection from codec parameters
						si.HdrFormat = DetectHdrFormat(codecPar);
					}

					// Audio-specific
					if (codecPar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) {
						si.SampleRate = codecPar->sample_rate;
						si.Channels = codecPar->ch_layout.nb_channels;
						si.ChannelLayout = GetChannelLayoutName(ref codecPar->ch_layout);
						si.BitRate = codecPar->bit_rate;
					}

					streams.Add(si);
				}

				// Duration — prefer container duration (AV_TIME_BASE = microseconds)
				TimeSpan duration = TimeSpan.Zero;
				if (pFormatContext->duration > 0) {
					duration = TimeSpan.FromSeconds(pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE);
				}
				else {
					// Fallback: check individual stream durations
					for (int i = 0; i < (int)pFormatContext->nb_streams; i++) {
						var avStream = pFormatContext->streams[i];
						if (avStream->duration > 0) {
							var tb = avStream->time_base;
							double streamDuration = avStream->duration * (double)tb.num / tb.den;
							if (streamDuration > duration.TotalSeconds)
								duration = TimeSpan.FromSeconds(streamDuration);
						}
					}
				}
				duration = duration.TrimMiliseconds();

				// Bitrate fallback: if no stream had a bitrate, use the container bitrate
				bool foundBitRate = false;
				foreach (var s in streams) {
					if (s.BitRate > 0) { foundBitRate = true; break; }
				}
				if (!foundBitRate && pFormatContext->bit_rate > 0 && streams.Count > 0) {
					streams[0].BitRate = pFormatContext->bit_rate;
				}

				return new MediaInfo {
					Streams = streams.ToArray(),
					Duration = duration
				};
			}
			catch {
				return null;
			}
			finally {
				if (pFormatContext != null) {
					AVFormatContext* pCtx = pFormatContext;
					ffmpeg.avformat_close_input(&pCtx);
				}
			}
		}

		static string GetPixelFormatName(int pixelFormat) {
			if (pixelFormat < 0)
				return string.Empty;
			// av_get_pix_fmt_name returns a managed string in FFmpeg.AutoGen
			string? name = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)pixelFormat);
			return name ?? string.Empty;
		}

		static string GetChannelLayoutName(ref AVChannelLayout layout) {
			// Get a human-readable channel layout name
			byte* buf = stackalloc byte[256];
			fixed (AVChannelLayout* pLayout = &layout) {
				ffmpeg.av_channel_layout_describe(pLayout, buf, 256);
			}
			return Marshal.PtrToStringAnsi((IntPtr)buf) ?? string.Empty;
		}

		/// <summary>
		/// Detects HDR format from codec parameters' color transfer characteristic.
		/// Since AVStream side_data is not exposed in this version of FFmpeg.AutoGen,
		/// we can only detect HLG and HDR10 from color_trc; Dolby Vision and HDR10+
		/// detection is deferred to the ffprobe CLI fallback.
		/// </summary>
		static string DetectHdrFormat(AVCodecParameters* codecPar) {
			string colorTransfer = GetColorTransferName(codecPar->color_trc);

			if (string.IsNullOrEmpty(colorTransfer))
				return string.Empty;

			if (colorTransfer.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase))
				return "HLG";
			if (colorTransfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase))
				return "HDR10";

			return string.Empty;
		}

		static string GetColorTransferName(AVColorTransferCharacteristic trc) {
			return trc switch {
				AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 => "smpte2084",
				AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67 => "arib-std-b67",
				_ => string.Empty
			};
		}
	}
}
