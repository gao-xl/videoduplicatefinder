# 后端核心扫描与重复识别算法优化 Spec

## Why
当前重复识别引擎在大规模视频库（10万+文件）下扫描和比较阶段耗时过长：比较阶段为 O(n²) 复杂度，pHash 仅在第一帧位置计算且仅用于单对过滤，灰度比较路径缺少多位置 pHash 快速预筛，Daisy-chain 拆分需要对大组做 O(n²) 重新比较，数据库加载/保存为全量序列化。需要通过算法改进和性能优化显著缩短大规模扫描的端到端时间。

## What Changes
- 优化 pHash 比较路径：支持多位置 pHash 预筛，当任一位置的 pHash 差异超过阈值时直接跳过灰度逐像素比较
- 优化比较阶段的候选过滤：增加文件大小预筛和分辨率预筛，在进入像素级比较前快速排除不可能重复的候选对
- 优化 Daisy-chain 拆分：缓存比较结果避免重复计算，对大组采用增量式剪枝
- 优化数据库 I/O：增量保存（仅写入变更条目），延迟加载（按需反序列化 grayBytes/PHashes）
- 优化 GatherInfos 阶段的重复路径检查：将 IncludeList 的 StartsWith 检查替换为有序集合的区间查找
- 优化 SplitDaisyChainGroups 中的 pairwise 比较：复用 ScanForDuplicates 已产生的比较结果

## Impact
- Affected specs: 扫描引擎核心比较逻辑、pHash 模块、数据库层
- Affected code:
  - `VDF.Core/ScanEngine.cs` — 比较路径、候选过滤、Daisy-chain 拆分
  - `VDF.Core/pHash/PerceptualHash.cs` — 多位置 pHash 计算
  - `VDF.Core/pHash/PHashCompare.cs` — 多位置 pHash 比较
  - `VDF.Core/Utils/GrayBytesUtils.cs` — 可能的 SIMD 优化
  - `VDF.Core/Data/SqliteDatabase.cs` — 增量保存、延迟加载
  - `VDF.Core/FileEntry.cs` — 新增 transient 比较缓存字段
  - `VDF.Core/Settings.cs` — 新增配置项
  - `VDF.Core.Tests/` — 新增/更新测试
  - `VDF.Benchmarks/` — 新增/更新基准测试

---

## ADDED Requirements

### Requirement: 多位置 pHash 预筛
系统 SHALL 在比较阶段为每个采样位置计算 pHash，并使用多位置 pHash 作为快速预筛：当任一位置的 pHash Hamming 距离超过阈值时，直接跳过该候选对的灰度逐像素比较。

#### Scenario: 多位置 pHash 预筛生效
- **WHEN** 系统在比较阶段处理两个视频文件
- **AND** UsePHashing 为 true 且 ThumbnailCount > 1
- **THEN** 系统为每个采样位置计算 pHash 并逐一比较
- **AND** 当所有位置的 pHash 相似度均满足阈值时，才进入灰度逐像素比较
- **AND** 当任一位置的 pHash 相似度低于阈值时，直接判定为非重复，跳过灰度比较

#### Scenario: 单位置 pHash 向后兼容
- **WHEN** 系统在比较阶段处理两个视频文件
- **AND** UsePHashing 为 true 且 ThumbnailCount == 1
- **THEN** 行为与当前实现一致，仅使用第一帧的 pHash 进行过滤

### Requirement: 比较阶段候选预筛增强
系统 SHALL 在进入像素级比较前增加文件大小和分辨率预筛，快速排除不可能重复的候选对。

#### Scenario: 文件大小预筛
- **WHEN** 系统比较两个视频文件
- **AND** 两个文件的大小差异超过配置的阈值
- **THEN** 系统直接跳过该候选对的像素级比较

#### Scenario: 分辨率预筛
- **WHEN** 系统比较两个视频文件
- **AND** 两个文件的分辨率差异显著（如一个为 4K 另一个为 480p）
- **THEN** 系统直接跳过该候选对的像素级比较
- **AND** 此预筛可通过配置开关禁用（默认启用）

### Requirement: Daisy-chain 拆分优化
系统 SHALL 优化 SplitDaisyChainGroups 的实现，避免对已比较过的文件对重复执行 CheckIfDuplicate。

#### Scenario: 复用比较结果
- **WHEN** SplitDaisyChainGroups 需要构建 pairwise 相似矩阵
- **THEN** 系统复用 ScanForDuplicates 阶段已产生的比较结果
- **AND** 仅对未在扫描阶段比较过的文件对执行新的 CheckIfDuplicate 调用

#### Scenario: 大组增量剪枝
- **WHEN** 一个重复组包含超过 20 个成员
- **THEN** 系统采用增量式剪枝策略，优先移除连接度最低的成员
- **AND** 每次移除后仅重新评估受影响成员的连接度，而非重建整个矩阵

### Requirement: 数据库增量保存
系统 SHALL 支持增量保存，仅写入自上次保存以来变更的条目，而非全量序列化。

#### Scenario: 增量保存变更条目
- **WHEN** 扫描过程中触发数据库保存（检查点或阶段完成）
- **THEN** 系统仅保存 dirty 标记的 FileEntry 条目
- **AND** 保存完成后清除 dirty 标记

#### Scenario: 全量保存回退
- **WHEN** 数据库 schema 发生变更或首次保存
- **THEN** 系统执行全量保存以确保一致性

### Requirement: 数据库延迟加载
系统 SHALL 支持延迟加载大字段（grayBytes、PHashes、AudioFingerprint），仅在需要时从数据库反序列化。

#### Scenario: 比较阶段按需加载
- **WHEN** 比较阶段需要某条目的 grayBytes 或 PHashes
- **THEN** 系统从数据库按需加载该条目的大字段
- **AND** 已加载的字段缓存在内存中避免重复读取

#### Scenario: 列表展示不加载大字段
- **WHEN** 用户在 GUI 或 Web 中浏览数据库条目列表
- **THEN** 系统不加载 grayBytes、PHashes、AudioFingerprint 等大字段
- **AND** 仅加载路径、大小、时长等元数据字段

### Requirement: GatherInfos 路径检查优化
系统 SHALL 优化 GatherInfos 阶段中 IncludeList 的路径包含检查，将 O(n) 的 StartsWith 遍历替换为更高效的查找。

#### Scenario: 大型 IncludeList 下的路径检查
- **WHEN** IncludeList 包含大量路径（>50 个）
- **THEN** 系统使用有序集合或前缀树进行路径包含检查
- **AND** 路径检查的时间复杂度从 O(n*len) 降低到 O(log(n)*len)

---

## MODIFIED Requirements

### Requirement: pHash 比较模式
原系统仅在第一帧位置计算和使用 pHash。现修改为：
- 在 TryBuildCompareSnapshot 中为所有采样位置计算 pHash 并缓存
- 在 CheckIfDuplicate 中使用所有位置的 pHash 进行预筛
- 当 UsePHashing 为 true 且 ThumbnailCount > 1 时，所有位置的 pHash 必须通过阈值才进入灰度比较
- 向后兼容：ThumbnailCount == 1 时行为不变

### Requirement: ScanForDuplicates 比较结果缓存
原系统在 ScanForDuplicates 完成后丢弃所有比较结果。现修改为：
- 保留比较阶段产生的"已比较且相似"的文件对集合
- SplitDaisyChainGroups 可复用此集合避免重复比较
- 比较结果在 SplitDaisyChainGroups 完成后释放

### Requirement: 数据库保存策略
原系统在每次保存时全量序列化所有条目。现修改为：
- FileEntry 新增 dirty 标记，任何字段变更时设置
- 保存时仅写入 dirty 条目
- 检查点保存使用增量模式
- 阶段完成保存仍可使用全量模式确保一致性

---

## REMOVED Requirements

（无移除的需求）
