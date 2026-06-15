# 算法与性能进一步优化 — 任务清单

所有任务按依赖关系排序：独立的、风险小的任务优先，便于增量回归。

## [x] Task 0: 现有代码审计与瓶颈定位（Prerequisite，已完成）
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 通读 `ScanEngine.cs` 中的 `ScanForDuplicates`、`CompareImages`、`CompareVideosLinear`、`CompareVideosLSH`、`CompareEntry`、`MergeDuplicate`、`SplitDaisyChainGroups`、`QuickPHashPreFilterMulti`、`CheckIfDuplicate`；
  - 通读 `PHashLSHIndex.cs` 的位位置选择与查询逻辑；
  - 通读 `GatherInfos` 中 IncludeList 路径检查；
  - 记录其中的重复代码与潜在低效之处。
- **Acceptance Criteria Addressed**: 为所有 FR 提供输入上下文。
- **Test Requirements**:
  - `programmatic` TR-0.1: `dotnet test VDF.Core.Tests` 全部通过（基线）。
  - `programmatic` TR-0.2: `PHashLSHIndex.SelfTest` 运行成功并输出基准统计。
- **Notes**: 仅作为后续任务的输入；不产生代码变更。

## [ ] Task 1: 抽取共享比较流水线 `TryComparePair`
- **Priority**: P0
- **Depends On**: Task 0
- **Description**:
  - 在 `ScanForDuplicates` 内部新增一个本地函数（或在 `ScanEngine` 中添加静态辅助方法）`TryComparePair(entry, compItem, entryFlippedGray, entryFlippedPHashes, out difference, out flags, prefilterOnly)`，完整封装：
    1. QuickPHashPreFilterMulti；
    2. 时长、文件大小、分辨率、folder 预过滤；
    3. CheckIfDuplicate（必要时使用 flipped 版本）；
    4. 硬链接排除。
  - `CompareImages` / `CompareVideosLinear` / `CompareVideosLSH` / `CompareEntry` 改为调用此共享方法，并删除各自的内联预过滤代码。
- **Acceptance Criteria Addressed**: FR-1, AC-1。
- **Test Requirements**:
  - `programmatic` TR-1.1: `dotnet build -c Release` 成功。
  - `programmatic` TR-1.2: `dotnet test VDF.Core.Tests` 全部通过。
  - `programmatic` TR-1.3: `grep -n 'QuickPHashPreFilterMulti\|fileSize\|resolution' CompareImages/CompareVideos*` 不应在这些函数体内出现（仅在 `TryComparePair` 中出现一次）。
  - `human-judgment` TR-1.4: Code Review 确认比较逻辑与原实现等价。
- **Notes**: `entryFlippedGray` / `entryFlippedPHashes` 可能为 null（图片路径无 flipped pHash），需与原实现一致地处理。

## [ ] Task 2: PHashLSHIndex — 确定性位位置选择
- **Priority**: P1
- **Depends On**: Task 0
- **Description**:
  - 修改 `PHashLSHIndex`：
    - 构造函数中不再使用 `Random` 随机挑选位位置；
    - 替代方案：将 64 位按 `table * keyLength` 分成多段，使用确定性策略（如轮转 + 交错）选择位位置；
    - 为多表使用不相交的位位置集合，降低信息冗余；
    - 保留构造函数签名 `(int numTables, int keyLength, int hammingThreshold)` 不变；
    - 更新 `SelfTest` 以报告新索引的 recall 与候选集大小，并验证两次运行结果一致。
- **Acceptance Criteria Addressed**: FR-2, AC-2。
- **Test Requirements**:
  - `programmatic` TR-2.1: `PHashLSHIndex.SelfTest` 两次运行输出相同数值（确定性）。
  - `programmatic` TR-2.2: 10 万随机 hash 的 self-test，recall ≥ 99% 且候选集大小 ≤ 原实现的 80%。
  - `programmatic` TR-2.3: `dotnet test VDF.Core.Tests` 全部通过。
- **Notes**: 若构建时检测到数据集很小（如 < 500 条），可回退到线性扫描，避免索引构建开销。

## [ ] Task 3: QuickPHashPreFilterMulti — 多位置早退出
- **Priority**: P1
- **Depends On**: Task 0
- **Description**:
  - 检查 `QuickPHashPreFilterMulti` 现状：若多位置可用但位置 0 的 Hamming 已远超阈值，直接返回 false；
  - 对多位置条目，按位置顺序逐个检查；任一位置 Hamming > `floor((1 - Percent) * 64) + margin`，立即返回 false；
  - 阈值由 `Settings.Percent` 派生；为避免与 CheckIfDuplicate 的严格校验冲突，margin 默认 0。
- **Acceptance Criteria Addressed**: FR-3, AC-3。
- **Test Requirements**:
  - `programmatic` TR-3.1: 新增单元测试：构造两对条目，一对第一个位置 Hamming 大，另一个位置小；验证 QuickPHashPreFilterMulti 对前者直接返回 false，未访问后续位置；
  - `programmatic` TR-3.2: 对相同对在优化前后跑 CheckIfDuplicate，结果一致；
  - `programmatic` TR-3.3: `dotnet test VDF.Core.Tests` 全部通过。
- **Notes**: 该预过滤器仅作"快速排除"，允许 false-positive（即返回 true 但实际不重复）；false-negative（返回 false 但实际重复）会降低召回率，**必须避免**。

## [ ] Task 4: MergeDuplicate 代表验证缓存
- **Priority**: P1
- **Depends On**: Task 0
- **Description**:
  - 在 `ScanForDuplicates` 本地作用域中新增 `ConcurrentDictionary<(Guid, Guid), bool> repCache`；
  - 修改 `MergeDuplicate`：在调用代表验证 `CheckIfDuplicate(repBase, null, null, repComp, out _)` 前，先查询缓存；key 为 `(smallerGuid, largerGuid)`；
  - 对组合并后的组（absorbed group）删除其在 repCache 中的条目（避免 stale data）。
- **Acceptance Criteria Addressed**: FR-4, AC-4。
- **Test Requirements**:
  - `programmatic` TR-4.1: 基准中大型库下，repCache 命中率 > 30%；
  - `programmatic` TR-4.2: 两次对同一对组调用 MergeDuplicate，第二次不调用 CheckIfDuplicate；
  - `programmatic` TR-4.3: `dotnet test VDF.Core.Tests` 全部通过。

## [ ] Task 5: SplitDaisyChainGroups 比较结果复用与 pHash 优先
- **Priority**: P1
- **Depends On**: Task 1, Task 4
- **Description**:
  - 让 `ScanForDuplicates` 收集一份"已比较过的 (path1,path2) → bool"缓存：在 MergeDuplicate 成功写入时，同时将该比较结果写入 `ConcurrentDictionary<(string,string), bool> pairComparisonCache`（key 为按 Ordinal 排序的两个 path）；
  - 让 `SplitDaisyChainGroups` 的 `AreSimilar` 在 Cache miss 时才调用 `CheckIfDuplicate`；
  - 在 `CheckIfDuplicate` 中确认已有的多位置 pHash 分支会在启用 pHash 时优先被使用。
- **Acceptance Criteria Addressed**: FR-5, AC-5。
- **Test Requirements**:
  - `programmatic` TR-5.1: 人工构造 5 条目的组，其中 4 对已由 ScanForDuplicates 缓存，验证 SplitDaisyChainGroups 只执行 1 次 CheckIfDuplicate；
  - `programmatic` TR-5.2: `dotnet test VDF.Core.Tests` 全部通过；
  - `programmatic` TR-5.3: 端到端扫描的重复集合与优化前一致（忽略 GroupId）。

## [ ] Task 6: IncludeList 与路径检查优化
- **Priority**: P2
- **Depends On**: Task 0
- **Description**:
  - 新增静态辅助类 `PathPrefixMatcher`：构造时接受 `IReadOnlyList<string>`，若数量 ≤ 32 则直接保存为列表（线性扫描），否则构建一个按 path 排序的数组 + 提供 `BinarySearch`/前缀匹配；
  - 替换 `GatherInfos` 中的 `IncludeList.Any(f => entry.Folder.StartsWith(f) ...)` 与 `_includeMatcher.IsIncluded(entry.Folder)`；
  - 在比较阶段的 `SameFolderAtDepth` 中同样复用该辅助类（如需）。
- **Acceptance Criteria Addressed**: FR-6, AC-6。
- **Test Requirements**:
  - `programmatic` TR-6.1: 新增单元测试：构造 500 条目录 + 10 万条目，验证 PathPrefixMatcher 比线性扫描快 ≥ 2x；
  - `programmatic` TR-6.2: 短列表场景下，PathPrefixMatcher 与原逻辑结果一致；
  - `programmatic` TR-6.3: `dotnet test VDF.Core.Tests` 全部通过。
- **Notes**: 路径分隔符在 Windows/Linux 不同，需保留现有的 `OrdinalIgnoreCase`/平台处理逻辑。

## [ ] Task 7: ScanList 预计算条目属性缓存
- **Priority**: P2
- **Depends On**: Task 1
- **Description**:
  - 在构建 ScanList/Validate 阶段（`TryBuildCompareSnapshot` 之后），为每个条目计算并缓存：
    - `pixelCount`（`entry.mediaInfo.Width * entry.mediaInfo.Height`，对图片条目从图片 metadata 获取；不可用时为 -1）；
    - `durationSeconds`（`entry.mediaInfo.Duration.TotalSeconds`，仅视频）；
  - 在预过滤阶段直接读取这些值，避免重复的属性解引用与乘积计算。
  - 若 `FileEntry` 已有对应字段则直接赋值；否则使用一个并行数组或结构体。
- **Acceptance Criteria Addressed**: FR-7。
- **Test Requirements**:
  - `programmatic` TR-7.1: 构造一个含多种分辨率的假数据库，验证预过滤的排除结果与优化前一致；
  - `programmatic` TR-7.2: `dotnet test VDF.Core.Tests` 全部通过。

## [ ] Task 8: 基准测试与性能验证
- **Priority**: P0
- **Depends On**: Task 1–7
- **Description**:
  - 扩展 `VDF.Benchmarks` 中的 `ComparePhaseProbe`，在 10 万条假数据库上报告：
    - 比较阶段总耗时；
    - LSH 候选集大小分布；
    - 预过滤在每级的命中率（跳过对数）；
    - repCache 命中率；
    - pairComparisonCache 在 SplitDaisyChainGroups 中的命中率。
  - 在 Git 中建立一个 baseline commit（优化前），在 HEAD 运行 benchmark，报告对比比值。
- **Acceptance Criteria Addressed**: NFR-1, AC-7。
- **Test Requirements**:
  - `programmatic` TR-8.1: 10 万条目库，比较阶段总耗时 ≤ 优化前的 85%；
  - `programmatic` TR-8.2: `PHashLSHIndex.SelfTest` recall ≥ 99%，候选集平均大小 ≤ 原实现的 80%；
  - `programmatic` TR-8.3: 端到端（FakeDatabaseGenerator → Scan → Compare）重复集合与优化前相等（忽略 GroupId）。

## [ ] Task 9: 增量代码评审、修复潜在回归
- **Priority**: P1
- **Depends On**: Task 8
- **Description**:
  - 对所有改动执行 `dotnet format`（若配置了 code style）；
  - 检查是否有新的 warnings，如有则修复；
  - 检查所有新增注释/参数命名是否遵循现有代码库风格。
- **Acceptance Criteria Addressed**: NFR-2, NFR-4。
- **Test Requirements**:
  - `programmatic` TR-9.1: `dotnet build -c Release` 零新 warning；
  - `human-judgment` TR-9.2: Code Review 通过。

## 依赖关系图
```
Task0 ─┬─→ Task1 ──────────┬─→ Task5 ──→ Task8 ─→ Task9
       ├─→ Task2 ──────────┤
       ├─→ Task3 ──────────┤
       ├─→ Task4 ──────────┤
       ├─→ Task6 ──────────┤
       └─→ Task7 ──────────┘
```
（Task1-7 可独立开发；Task8 在 1-7 全完成后运行；Task9 收尾）
