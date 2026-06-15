# 算法与性能进一步优化 Spec

## Overview
- **Summary**: 在现有 LSH + 多级预过滤 + 并行比较的基础上，进一步优化 ScanEngine、pHash、GrayBytes 和数据库层的算法和数据结构，以降低大规模数据集（10万+ 文件）下的端到端扫描时间，并提高重复对召回率。
- **Purpose**: 识别并解决当前实现中仍存在的显著瓶颈：(a) CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 四套几乎相同的预过滤逻辑重复实现与维护成本，(b) pHash LSH 索引使用随机 bit-position 缺乏确定性与可复用性，(c) SplitDaisyChainGroups 组内比较仍可能触发昂贵的灰字节两两比较，(d) MergeDuplicate 在组合并时可能调用代表成员 CheckIfDuplicate 产生重复计算，(e) QuickPHashPreFilter 对多位置 pHash 未做更激进的早退出，(f) GatherInfos 中 IncludeList 线性扫描仍在大数据下存在 O(n*m) 成本。
- **Target Users**: 需要扫描大型媒体库（10万+ 视频/图片）、需要在更短时间内获得更高召回率的高级用户。

## Goals
1. **减少代码重复**: 将 CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 的预过滤与比较流水线合并为一个共享方法，降低维护成本与出错概率。
2. **提升候选生成质量**: 改进 pHash LSH 索引，使用确定性位位置选择 + 多阈值探针策略，提升低相似度阈值下的召回率并降低候选集大小。
3. **加速 Daisy-chain 拆分**: 对 SplitDaisyChainGroups 重用 ScanForDuplicates 阶段产生的比较结果缓存，并将组内相似度比较优先使用 pHash 路径，避免无意义的灰字节重新比较。
4. **降低锁竞争与合并成本**: 优化 MergeDuplicate，减少代表成员（groupRepresentatives）重复比较次数；在无代表冲突时跳过昂贵的代表验证 CheckIfDuplicate。
5. **更快的 pHash 预过滤**: 在 QuickPHashPreFilterMulti 中使用"任一位置早退出"策略，并缓存每个条目的 pHash 最大值/最小值以更快速排除不匹配对。
6. **IncludeList 路径检查加速**: 对大型 IncludeList 建立前缀树或排序数组+二分，将 O(n*m) 降到 O(n*log m)。
7. **保持语义等价**: 所有优化不应改变重复对的判定结果。新增/修改的代码必须通过现有单元测试和集成测试。

## Non-Goals (Out of Scope)
- 不引入新的深度学习哈希算法（如 CNN-based perceptual hash）；当前 DCT-pHash 作为主比较路径保持不变。
- 不更改 ThumbnailCount / Percent / 等用户可配置项的默认值或语义。
- 不重写或替换 Ffmpeg / FFprobe 解码路径。
- 不重构 GUI/Web 的 UI 组件；仅优化引擎层。
- 不改变数据库 Schema；优化对现有数据文件向后兼容。

## Background & Context
当前 ScanEngine 已具备 LSH 索引、多级预过滤、ArrayPool 翻转灰度缓存、细粒度锁、惰性 SplitDaisyChainGroups 等优化。但在大规模场景下仍存在以下可改进之处：
- **LSH 索引的位位置选择**（`PHashLSHIndex.cs`）使用 `Random` 随机选择 `keyLength` 个位位置，每次启动索引不同，使得：(a) 难以做基准测试对比，(b) 未考虑 pHash 的位重要性分布（DCT 低频位更有信息量），(c) 跨多表的位位置可能重叠导致信息冗余。
- **重复的预过滤代码**（`ScanEngine.cs` CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 四处包含几乎相同的 duration / fileSize / resolution / folder / CheckIfDuplicate 流水线）：任何一处改动需要在四处同步，容易漏改并引入不一致行为。
- **MergeDuplicate 代表验证**: 组合并时每次都对 groupRepresentatives 调用 CheckIfDuplicate，对已比较过的代表对没有缓存结果。在大型库中有数百个组的情况下，代表验证成为一个不可忽略的成本。
- **SplitDaisyChainGroups**: 虽然使用了 simCache，但对 pHash 模式的条目仍走完整的 CheckIfDuplicate 灰字节路径；可以优先使用 pHash 路径提前返回。
- **IncludeList 路径检查**: 在 `GatherInfos` 中仍使用 `IncludeList.Any(f => entry.Folder.StartsWith(f) ...)` 的 O(m) 线性扫描，当 IncludeList 很大（>100 个目录）时成本显著。
- **GetEntryPixelCount / prefilter 临时计算**: 每次比较都对两个条目重新计算 `Width*Height`，可在构建 ScanList 时一次性缓存。

## Functional Requirements
### FR-1: 统一比较流水线
系统 SHALL 提供一个共享的 `bool TryComparePair(entry, compItem, entryFlippedGray, entryFlippedPHashes, out difference, out flags)` 方法，封装预过滤 + 比较 + 硬链接检查的完整逻辑。CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 四套逻辑 SHALL 调用此共享方法，不再重复实现预过滤步骤。

### FR-2: 确定性 LSH 索引
`PHashLSHIndex` SHALL 改为使用确定性位位置选择策略：
1. 位位置 SHALL 基于 pHash 的位重要性（DCT 低频位更重要）而非随机选择；
2. 多表 SHALL 使用不相交或最小重叠的位位置集合；
3. 提供与现有随机索引向后兼容的查询接口（`Query(hash, excludeIndex)` 签名不变）；
4. SelfTest SHALL 继续通过，并验证召回率不低于原实现。

### FR-3: 多位置 pHash 预过滤加速
`QuickPHashPreFilterMulti` SHALL:
1. 对第一个位置先检查 Hamming 距离，若超过阈值立即返回 false（早退出）；
2. 若所有位置均可用，优先按位置顺序逐个检查；任一失败即早退出；
3. 不改变行为语义（仍然返回 true/false，不影响比较结果）。

### FR-4: MergeDuplicate 代表验证优化
`MergeDuplicate` SHALL:
1. 维护一个 `(groupIdA, groupIdA) → bool` 的代表比较结果缓存；
2. 相同 groupId 对重复调用时命中缓存，避免重复对代表条目执行 CheckIfDuplicate；
3. 代表条目相同时（例如两个组共享同一代表）直接返回 true。

### FR-5: SplitDaisyChainGroups 比较结果复用
`SplitDaisyChainGroups` SHALL:
1. 在 ScanForDuplicates 结束后获得一份"已比较过的 (path1,path2) → 是否重复"缓存作为输入；
2. 在组内两两比较时优先查询缓存，缓存缺失时才执行 CheckIfDuplicate；
3. 对 pHash 模式的条目在 CheckIfDuplicate 内部优先走 pHash 路径而非灰字节路径（已在 CheckIfDuplicate 中实现，仅需确认无回归）。

### FR-6: IncludeList 路径检查优化
系统 SHALL:
1. 为 `Settings.IncludeList` 和比较阶段的 `SameFolderAtDepth` 构建有序前缀数组；
2. 路径检查使用二分或前缀树（trie）查找，而非现有线性扫描；
3. 对短列表（≤32 条）保持现有线性逻辑作为 fast-path，避免小数据下的构建开销。

### FR-7: 预计算的条目属性缓存
在 `ScanForDuplicates` 开始前的列表构建阶段，系统 SHALL 为每个 ScanList 条目预计算：
1. `pixelCount`（宽×高，用于分辨率预过滤）；
2. `durationSeconds`（缓存 Duration.TotalSeconds，避免每次重新访问 TimeSpan）；
并在预过滤阶段直接使用这些预计算值。

## Non-Functional Requirements
### NFR-1: 性能
- 对 10 万条视频、ThumbnailCount=3 的库，比较阶段总耗时 SHALL 相比优化前降低 ≥15%（在同一台开发机上，使用 BenchmarkDotNet 的端到端探针）；
- LSH Query 候选集平均大小 SHALL 相比原实现降低 ≥20%，同时在 Hamming ≤ 12 的匹配对中召回率 ≥ 99%（由 SelfTest 验证）。

### NFR-2: 代码可维护性
- `ScanForDuplicates` 方法总代码行数 SHALL ≤ 当前值的 70%（通过抽取共享方法实现）；
- 预过滤逻辑只有一份实现，CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 不再各自独立实现。

### NFR-3: 语义一致性
- 对任意固定输入库（相同文件、相同设置），优化前后产生的 Duplicates 集合 SHALL 集合相等（忽略 GroupId 的 Guid 差异，但组内成员 SHALL 一致）。

### NFR-4: 向后兼容
- 数据库格式（SQLite schema、MemoryPack 序列化格式）保持不变；
- 设置项（Settings）保持不变（无需迁移 JSON 配置）；
- CLI / GUI / Web 入口行为无变化。

## Constraints
- **语言**: C# 12 / .NET 8；
- **运行平台**: Windows (primary)、Linux、macOS；
- **SIMD 指令集**: 仅使用已存在的 `Avx2`/`Sse2`（在 GrayBytesUtils 中已有使用）；新增 SIMD 路径需有 scalar fallback；
- **依赖项**: 不引入新的 NuGet 包；仅使用现有依赖。
- **测试约束**: 所有现有单元测试 SHALL 继续通过；不得削弱测试覆盖率。

## Assumptions
- 用户的媒体库中 pHash 对 Hamming ≤ ~15 的匹配对已经覆盖绝大多数真实重复对；
- 大多数重复对在"第一个采样位置"就表现出相似的 pHash，因此早退出策略有效；
- 大多数用户的 IncludeList 较小（≤32 条），因此 fast-path 不受影响；大型库用户受益于 trie 优化。

## Acceptance Criteria
### AC-1: 共享比较流水线存在且被四处调用
- **Given** 一个干净的 build，
- **When** 检查 CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 方法体，
- **Then** 它们 SHALL 都调用共享的 `TryComparePair` 方法；预过滤逻辑仅出现在 `TryComparePair` 中。
- **Verification**: `programmatic`（通过 grep 或 Roslyn 分析器检查调用链）
- **Notes**: 比较入口函数的签名和返回值应与原实现兼容。

### AC-2: 确定性 LSH 索引
- **Given** 相同的输入哈希集合，
- **When** 在不同进程中构建 PHashLSHIndex 并执行 SelfTest，
- **Then** 两次运行 SHALL 产生相同的候选集和相同的 recall 统计；
- **And** recall SHALL ≥ 原随机实现，同时平均候选集大小 SHALL 更小（≤ 80%）。
- **Verification**: `programmatic`（SelfTest 输出 + 基准测试）

### AC-3: 多位置 pHash 预过滤早退出
- **Given** 一对视频条目，多位置 pHash，第一个位置 Hamming 距离远大于阈值，
- **When** QuickPHashPreFilterMulti 被调用，
- **Then** 它 SHALL 在检查第一个位置后立即返回 false（仅一次 Hamming 计算），不进入后续位置或灰字节比较。
- **Verification**: `programmatic`（单元测试：Mock/探测后续位置访问次数）

### AC-4: MergeDuplicate 代表验证不重复
- **Given** 两个组已合并过一次代表，后续再次尝试合并同一对组，
- **When** MergeDuplicate 内部调用代表验证，
- **Then** 第二次 SHALL 直接从缓存读取结果，不再调用 CheckIfDuplicate。
- **Verification**: `programmatic`（单元测试：通过一个探测计数器验证）

### AC-5: SplitDaisyChainGroups 使用缓存比较结果
- **Given** 一个包含 5 个成员的重复组，其中 4 对的比较结果已由 ScanForDuplicates 阶段缓存，
- **When** 进入 SplitDaisyChainGroups，
- **Then** 组内比较 SHALL 仅对未缓存的 1 对执行 CheckIfDuplicate，其余 4 对从缓存命中。
- **Verification**: `programmatic`（单元测试：缓存命中率断言）

### AC-6: IncludeList 查找性能
- **Given** IncludeList 包含 500 条目录，Database 包含 10 万条条目，
- **When** GatherInfos 执行路径检查，
- **Then** 总耗时 SHALL ≤ 原线性实现的 50%。
- **Verification**: `programmatic`（BenchmarkDotNet 基准测试）

### AC-7: 语义回归测试
- **Given** 一个固定的人工测试库（FakeDatabaseGenerator），
- **When** 运行完整扫描与比较，
- **Then** 优化前后的重复集合（忽略 GroupId） SHALL 完全相同。
- **Verification**: `programmatic`（集成测试：序列化 Duplicates 集合并 diff）

## Open Questions
- [ ] 对 bit-importance 的具体排序：是否需要在离线阶段（对一批真实 pHash）计算位信息量，还是可以直接按 DCT 频率顺序近似？
- [ ] LSH 超参数（numTables / keyLength / hammingThreshold）是否应暴露为 Settings，并根据数据集大小自动调整？当前硬编码 10/8/6 可能对超大规模库并非最优。
- [ ] 是否需要引入可选的 `groupComparisonCache` 上限与 LRU 策略，避免在极端大库中内存占用过高？
