// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Linq;
using ReactiveUI;
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		static QualityRanker.Criterion<DuplicateItemVM> ToVmCriterion(QualityRanker.Criterion<VDF.Core.ViewModels.DuplicateItem> c) =>
			new(c.Name, vm => c.Accessor(vm.ItemInfo), c.VideoOnly, c.Ascending);

		static IEnumerable<QualityRanker.Criterion<DuplicateItemVM>> ResolveCriteria(IEnumerable<string> names) =>
			QualityCriteria.Resolve(names).Select(ToVmCriterion);
	}

	sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
		public static readonly ReferenceEqualityComparer<T> Instance = new();
		public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
		public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
	}
}
