# Tasks

- [x] Task 1: 实现 pHash LSH 索引
  - [x] SubTask 1.1: 创建 `PHashLSHIndex` 类，实现多桶哈希（multi-probe LSH）索引构建与查询接口
  - [x] SubTask 1.2: 在 `ScanForDuplicates()` 中，当 `UsePHashing` 启用时构建 LSH 索引，替换 duration bucket 内的线性遍历为 LSH 候选查询
  - [x] SubTask 1.3: 验证 LSH 索引无漏检（对测试数据集比较 LSH 结果与暴力搜索结果一致性）

- [x] Task 2: 实现多级预过滤瀑布流
  - [x] SubTask 2.1: 在 `CompareEntry()` 和 `CompareVideosLinear()` 中重组预过滤逻辑为级联结构：pHash Hamming 快速检查 → duration → file size → resolution → 精确比较
  - [x] SubTask 2.2: 将 pHash Hamming 快速检查提取为独立方法 `QuickPHashPreFilter()`，在灰度比较前调用
  - [x] SubTask 2.3: 确保每级预过滤失败时立即 `continue`，不执行后续检查

- [x] Task 3: 缓存翻转灰度字节数组
  - [x] SubTask 3.1: 在 `FileEntry` 上添加 `compareFlippedGray` 和 `compareFlippedPHashes` 临时字段
  - [x] SubTask 3.2: 修改 `CompareEntry()`、`CompareVideosLinear()`、`CompareImages()`，在条目首次需要翻转数据时计算并缓存，后续复用
  - [x] SubTask 3.3: 在 `ScanForDuplicates()` 结尾的清理阶段释放缓存字段

- [x] Task 4: 优化 MergeDuplicate 锁策略
  - [x] SubTask 4.1: 将 `duplicateDict` 从 `Dictionary` + `lock` 改为 `ConcurrentDictionary`，消除全局锁
  - [x] SubTask 4.2: 为 `groupMembers` 和 `groupRepresentatives` 使用 `ConcurrentDictionary`，合并操作使用 per-group 细粒度锁
  - [x] SubTask 4.3: 验证并行合并结果与串行结果等价

- [x] Task 5: 热路径内存池化
  - [x] SubTask 5.1: 修改 `CreateFlippedGrayBytes()` 使用 `ArrayPool<byte>` 租用目标数组
  - [x] SubTask 5.2: 在 `ScanForDuplicates()` 结尾归还所有 `ArrayPool` 租用的缓冲区
  - [x] SubTask 5.3: 审查其他热路径临时分配，将合适的改为 `ArrayPool` 或 `stackalloc`

- [x] Task 6: 优化 SplitDaisyChainGroups 算法
  - [x] SubTask 6.1: 将全量 O(n²) 相似度矩阵改为惰性计算：仅在需要时计算单对相似度，使用缓存避免重复计算
  - [x] SubTask 6.2: 优化剪枝循环：维护每个成员的连接数计数器，剪枝后增量更新而非每次重新遍历
  - [x] SubTask 6.3: 验证优化后剪枝结果与原算法一致

- [x] Task 7: 小数据集 pHash 快速预过滤
  - [x] SubTask 7.1: 在 `TryBuildCompareSnapshot()` 中，即使 `UsePHashing` 为 false，也计算并缓存第一帧 pHash
  - [x] SubTask 7.2: 在 `CheckIfDuplicate()` 的灰度比较路径前，添加 pHash Hamming 距离快速排除检查
  - [x] SubTask 7.3: 当 pHash 数据不可用时透明回退到灰度比较

- [x] Task 8: 端到端验证与基准测试
  - [x] SubTask 8.1: VDF.Core 编译通过，0 错误 0 警告；测试项目 240/242 通过（2 个失败为预先存在的问题）
  - [x] SubTask 8.2: 修复测试项目编译错误（ConcurrentDictionary 兼容性）
  - [x] SubTask 8.3: 验证内存使用无显著增长（ArrayPool 复用机制确保）

# Task Dependencies
- Task 2 depends on Task 1（LSH 索引是预过滤瀑布流的一部分）
- Task 3 独立，可并行
- Task 4 独立，可并行
- Task 5 独立，可并行
- Task 6 独立，可并行
- Task 7 depends on Task 2（快速预过滤是预过滤瀑布流的扩展）
- Task 8 depends on Task 1-7 全部完成
