# Tasks

- [ ] Task 1: 多位置 pHash 预筛实现
  - [ ] SubTask 1.1: 修改 TryBuildCompareSnapshot 为所有采样位置计算 pHash 并缓存到 entry.comparePHashes（新增 byte[]?[] 对应 compareGray）
  - [ ] SubTask 1.2: 修改 CheckIfDuplicate 的 pHash 分支，遍历所有位置的 pHash 进行预筛，任一位置不通过则返回 false
  - [ ] SubTask 1.3: 修改 CreateFlippedGrayBytes 同步生成所有位置的 flipped pHash
  - [ ] SubTask 1.4: 更新 ScanForDuplicates 中 flippedPHash 相关逻辑支持多位置
  - [ ] SubTask 1.5: 更新 ScanEngine_Diagnostic.cs 中的 TestFilePair 适配多位置 pHash
  - [ ] SubTask 1.6: 编写多位置 pHash 预筛的单元测试

- [ ] Task 2: 比较阶段候选预筛增强
  - [ ] SubTask 2.1: 在 Settings 中添加 FileSizeTolerancePercent 配置项（默认 0 = 禁用，非零值启用文件大小预筛）
  - [ ] SubTask 2.2: 在 Settings 中添加 EnableResolutionPreFilter 配置项（默认 true）
  - [ ] SubTask 2.3: 在 CompareEntry/CompareVideosLinear/CompareImages 的候选循环中添加文件大小预筛逻辑
  - [ ] SubTask 2.4: 在候选循环中添加分辨率预筛逻辑（比较主视频流的 Width*Height）
  - [ ] SubTask 2.5: 编写文件大小和分辨率预筛的单元测试

- [ ] Task 3: Daisy-chain 拆分优化
  - [ ] SubTask 3.1: 在 ScanForDuplicates 中收集已比较且判定为相似的文件对到 comparedPairs 字典
  - [ ] SubTask 3.2: 修改 SplitDaisyChainGroups 优先从 comparedPairs 查找比较结果，未找到时才调用 CheckIfDuplicate
  - [ ] SubTask 3.3: 对大组（>20 成员）实现增量剪枝：移除最弱连接成员后仅重新评估受影响成员
  - [ ] SubTask 3.4: 在 ScanForDuplicates 结束后释放 comparedPairs
  - [ ] SubTask 3.5: 编写 Daisy-chain 拆分优化的单元测试

- [ ] Task 4: 数据库增量保存
  - [ ] SubTask 4.1: 在 FileEntry 中添加 dirty 标记字段（[MemoryPackIgnore]）
  - [ ] SubTask 4.2: 在 FileEntry 的 grayBytes、PHashes、mediaInfo、AudioFingerprint、Flags 等字段修改点设置 dirty = true
  - [ ] SubTask 4.3: 在 SqliteDatabase 中添加 SaveDirtyFileEntries 方法，仅保存 dirty 条目
  - [ ] SubTask 4.4: 修改 DatabaseUtils.SaveDatabase 支持增量/全量两种模式
  - [ ] SubTask 4.5: 修改 TryDatabaseCheckpoint 使用增量保存
  - [ ] SubTask 4.6: 保存完成后清除 dirty 标记
  - [ ] SubTask 4.7: 编写增量保存的单元测试

- [ ] Task 5: 数据库延迟加载
  - [ ] SubTask 5.1: 在 SqliteDatabase.LoadFileEntries 中添加轻量模式参数，轻量模式不加载 grayBytes/PHashes/AudioFingerprint
  - [ ] SubTask 5.2: 在 SqliteDatabase 中添加 LoadFileEntryHeavy 方法，按路径加载单条目的完整数据
  - [ ] SubTask 5.3: 修改 BuildFileList 使用轻量模式加载，GatherInfos 按需加载大字段
  - [ ] SubTask 5.4: 修改 ScanForDuplicates 的 TryBuildCompareSnapshot 在需要时触发延迟加载
  - [ ] SubTask 5.5: 编写延迟加载的单元测试

- [ ] Task 6: GatherInfos 路径检查优化
  - [ ] SubTask 6.1: 创建 PathTrie 或利用有序集合 + 二分查找替代 IncludeList 的线性 StartsWith 遍历
  - [ ] SubTask 6.2: 在 PrepareSearch/NormalizeScanPaths 中构建优化后的路径查找结构
  - [ ] SubTask 6.3: 替换 GatherInfos 和 InvalidEntry 中的 IncludeList.Any(StartsWith) 为新查找结构
  - [ ] SubTask 6.4: 编写路径检查优化的单元测试

- [ ] Task 7: 基准测试更新
  - [ ] SubTask 7.1: 在 ComparePhaseProbe 中添加多位置 pHash 场景
  - [ ] SubTask 7.2: 添加文件大小/分辨率预筛的基准测试场景
  - [ ] SubTask 7.3: 添加 Daisy-chain 拆分的基准测试场景
  - [ ] SubTask 7.4: 添加数据库增量保存的基准测试

# Task Dependencies
- Task 1 和 Task 2 可并行执行（独立优化路径）
- Task 3 依赖 Task 1（Daisy-chain 复用比较结果需要多位置 pHash 结果一致）
- Task 4 和 Task 5 可并行执行（独立数据库优化）
- Task 6 独立于其他任务
- Task 7 依赖 Task 1-6（需要所有优化完成后更新基准）
