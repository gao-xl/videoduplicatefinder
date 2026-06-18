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

using System.Linq;
using VDF.Core.ViewModels;

namespace VDF.Core.Utils {

	/// <summary>
	/// Canonical quality-ranking criteria shared by GUI, Web, and CLI.
	/// User ordering (from GUI settings) is resolved via <see cref="Resolve"/>.
	/// </summary>
	public static class QualityCriteria {

		public static readonly IReadOnlyDictionary<string, QualityRanker.Criterion<DuplicateItem>> CriteriaMap =
			new Dictionary<string, QualityRanker.Criterion<DuplicateItem>>(StringComparer.Ordinal) {
				["Duration"] = new("Duration", d => d.Duration, videoOnly: true),
				["Resolution"] = new("Resolution", d => d.FrameSizeInt, videoOnly: false),
				["FPS"] = new("FPS", d => d.Fps, videoOnly: true),
				["Bitrate"] = new("Bitrate", d => d.BitRateKbs, videoOnly: true),
				["Audio Bitrate"] = new("Audio Bitrate", d => d.AudioBitRateKbs, videoOnly: false),
				["Audio Sample Rate"] = new("Audio Sample Rate", d => d.AudioSampleRate, videoOnly: false),
				["HdrFormatRank"] = new("HdrFormatRank", d => d.HdrFormatRank, videoOnly: true),
				["Size"] = new("Size", d => d.SizeLong, videoOnly: false, ascending: true),
			};

		/// <summary>Default criterion order when the user has not configured a custom list.</summary>
		public static IEnumerable<string> DefaultOrder => [
			"Duration", "Resolution", "FPS", "Bitrate", "Audio Bitrate", "HdrFormatRank", "Size",
		];

		/// <summary>
		/// Yields criteria in the user's chosen order, then appends any map entries the
		/// user's saved list doesn't include (new criteria become tiebreakers).
		/// </summary>
		public static IEnumerable<QualityRanker.Criterion<DuplicateItem>> Resolve(IEnumerable<string>? names) {
			var order = names?.ToList() ?? DefaultOrder.ToList();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var name in order)
				if (CriteriaMap.TryGetValue(name, out var c) && seen.Add(name))
					yield return c;
			foreach (var kv in CriteriaMap)
				if (!seen.Contains(kv.Key))
					yield return kv.Value;
		}

		public static DuplicateItem PickKeeper(IList<DuplicateItem> items, IEnumerable<string>? userOrder = null) =>
			QualityRanker.PickKeeper(items, Resolve(userOrder), d => d.IsImage);

		/// <summary>Returns paths to select (all group members except the keeper).</summary>
		public static IReadOnlyList<string> SelectAllExceptKeeper(IList<DuplicateItem> groupItems, IEnumerable<string>? userOrder = null) {
			var keeper = PickKeeper(groupItems, userOrder);
			return groupItems.Where(d => !ReferenceEquals(d, keeper) && d.Path != keeper.Path)
				.Select(d => d.Path)
				.ToList();
		}
	}
}
