# 重复识别算法优化与性能优化 Spec

## Why
当前 `ScanForDuplicates()` 使用 O(n²) 暴力配对比较，即使有 duration bucket 预筛选，在大规模数据集（10万+文件）下比较阶段耗时极长。pHash 仅用于单对比较而未建立索引结构，无法快速剪枝。热路径中存在重复计算（翻转灰度）、锁争用（`MergeDuplicate` 单锁）和不必要的内存分配，进一步拖慢扫描速度。

## What Changes
- 引入 pHash 局部敏感哈希（LSH）索引，将 pHash 候选查找从 O(n) 降至 O(1)~O(log n)
- 实现多级预过滤瀑布流：pHash LSH → duration bucket → file size → resolution → 灰度/pHash 精确比较
- 缓存翻转灰度字节数组，避免每个比较伙伴重复计算
- 优化 `MergeDuplicate` 锁策略，减少并行路径下的锁争用
- 使用 `ArrayPool` 复用热路径临时缓冲区
- 优化 `SplitDaisyChainGroups` 算法复杂度
- 为小数据集路径添加 pHash 快速预过滤

## Impact
- Affected specs: 核心扫描引擎重复检测能力
- Affected code:
  - `VDF.Core/ScanEngine.cs` — `ScanForDuplicates()`, `MergeDuplicate()`, `SplitDaisyChainGroups()`, `CompareEntry()`, `CompareVideosLinear()`, `CompareImages()`
  - `VDF.Core/pHash/PerceptualHash.cs` — 可能需要批量计算接口
  - `VDF.Core/pHash/PHashCompare.cs` — 可能需要批量 Hamming 距离计算
  - `VDF.Core/Utils/GrayBytesUtils.cs` — 翻转灰度缓存相关
  - `VDF.Core/Settings.cs` — 新增 LSH 相关配置项

## ADDED Requirements

### Requirement: pHash LSH 索引加速候选查找
系统 SHALL 在 `ScanForDuplicates()` 开始前，基于所有有效条目的 pHash 值构建 LSH（局部敏感哈希）索引结构。当 `UsePHashing` 启用时，每个条目仅与 LSH 索引返回的候选集进行比较，而非与所有 duration bucket 内的条目逐一比较。

#### Scenario: LSH 索引构建与查询
- **WHEN** 扫描引擎准备比较阶段且 `UsePHashing` 为 true
- **THEN** 系统构建 LSH 索引（基于 pHash 的多桶哈希），并在比较时仅查询索引返回的候选条目
- **AND** 候选集 SHALL 包含所有 Hamming 距离在阈值内的条目（无漏检）

#### Scenario: LSH 索引禁用回退
- **WHEN** `UsePHashing` 为 false
- **THEN** 系统回退到现有的 duration bucket 线性扫描路径，行为不变

### Requirement: 多级预过滤瀑布流
系统 SHALL 在 `CheckIfDuplicate` 之前实施多级预过滤：pHash Hamming 快速筛选 → duration 容差 → file size 容差 → resolution 容差 → 精确灰度/pHash 比较。每级预过滤 SHALL 尽早跳过不可能匹配的条目对。

#### Scenario: 预过滤级联跳过
- **WHEN** 两个条目进入比较流程
- **THEN** 系统依次执行 pHash Hamming 快速检查（若启用）、duration 检查、file size 检查、resolution 检查
- **AND** 任一级失败 SHALL 立即跳过该对，不执行后续更昂贵的比较

### Requirement: 翻转灰度字节缓存
系统 SHALL 在比较阶段为每个条目一次性计算翻转灰度字节数组（当 `CompareHorizontallyFlipped` 启用时），并在该条目的所有比较中复用，而非每次比较重新计算。

#### Scenario: 翻转灰度缓存命中
- **WHEN** `CompareHorizontallyFlipped` 为 true 且条目进入比较循环
- **THEN** 翻转灰度字节数组仅计算一次并缓存于条目上
- **AND** 后续所有与该条目的比较复用缓存值

### Requirement: MergeDuplicate 锁优化
系统 SHALL 减少 `MergeDuplicate` 中的锁争用。使用 `ConcurrentDictionary` 替代 `lock(duplicateDict)` 保护的主字典，或使用细粒度 per-group 锁，使并行比较线程不因合并操作互相阻塞。

#### Scenario: 并行合并无全局锁争用
- **WHEN** 多个并行线程同时发现不同组的重复对
- **THEN** 各线程 SHALL 能独立合并而不争用同一把锁
- **AND** 合并结果 SHALL 与串行执行等价

### Requirement: 热路径内存池化
系统 SHALL 使用 `ArrayPool<byte>` 复用翻转灰度计算和其他热路径中的临时 byte[] 缓冲区，减少 GC 压力。

#### Scenario: 临时数组复用
- **WHEN** 比较阶段需要分配临时 byte 数组（如翻转灰度）
- **THEN** 系统 SHALL 从 `ArrayPool` 租用而非 `new byte[]`，使用后归还
- **AND** 在扫描结束时 SHALL 归还所有租用的缓冲区

### Requirement: SplitDaisyChainGroups 复杂度优化
系统 SHALL 优化 `SplitDaisyChainGroups` 的算法复杂度。当前对每个 3+ 成员组构建 O(n²) 相似度矩阵并迭代剪枝，对于大组（10+ 成员）开销显著。优化后 SHALL 使用更高效的连通分量检测和剪枝策略。

#### Scenario: 大组剪枝性能
- **WHEN** 一个重复组包含 10+ 成员
- **THEN** 剪枝算法 SHALL 在 O(n × average_connections) 时间内完成，而非 O(n²)
- **AND** 剪枝结果 SHALL 与原算法语义等价

### Requirement: 小数据集 pHash 快速预过滤
系统 SHALL 在 `UsePHashing` 禁用时，仍为视频比较提供基于第一帧 pHash 的可选快速预过滤。当两个视频的 pHash Hamming 距离超过阈值时直接跳过，避免昂贵的灰度逐像素比较。

#### Scenario: 非 pHash 模式下的 Hamming 预过滤
- **WHEN** `UsePHashing` 为 false 但条目已有 pHash 数据
- **THEN** 系统 SHALL 先计算 Hamming 距离，若远超阈值则跳过灰度比较
- **AND** 若 pHash 数据不可用，SHALL 回退到灰度比较（行为不变）

## MODIFIED Requirements

### Requirement: ScanForDuplicates 比较流程
原流程：duration bucket 分组 → 线性遍历候选 → 灰度/pHash 精确比较。
修改后流程：
1. 若 `UsePHashing` 启用：构建 LSH 索引 → LSH 候选查询 → 多级预过滤 → 精确比较
2. 若 `UsePHashing` 禁用：duration bucket 分组 → pHash Hamming 快速预过滤（可选） → 多级预过滤 → 灰度精确比较

比较结果 SHALL 与原算法完全一致（无漏检、无误检），仅改变候选筛选策略以减少无效比较次数。
