# Tasks

## Phase 1: 提取共享服务到 Core

- [x] Task 1: 提取 FFmpegSetupService 到 Core
  - [x] 1.1: 在 `VDF.Core/Services/` 创建 `FFmpegSetupService`，合并 GUI `MainWindowVM_FfmpegDownloader.cs` 与 Web `FFmpegSetupService.cs` 的下载计划/校验/解压逻辑
  - [x] 1.2: 定义进度上报抽象 `IProgress<FFmpegSetupProgress>`，兼容 GUI `Dispatcher.UIThread.Post` 与 Web `Notify()`+SignalR
  - [x] 1.3: GUI `MainWindowVM_FfmpegDownloader.cs` 改为薄包装，删除重复的 `FfmpegDownloadPlan`/`DownloadFileAsync`/`VerifyChecksumAsync`/`ExtractArchive` 等
  - [x] 1.4: Web `FFmpegSetupService.cs` 改为薄包装，删除重复实现，保留 Docker 检测等 Web 专属逻辑
  - [x] 1.5: 单元测试覆盖下载计划生成、校验和比对、tar/zip 解压

- [x] Task 2: 提取 FileOperationsService 到 Core
  - [x] 2.1: 在 `VDF.Core/Services/` 创建 `FileOperationsService`，统一删除/移动/硬链接/符号链接/回收站
  - [x] 2.2: 统一 Windows `SHFileOperation` 批量回收逻辑与永久删除回退
  - [x] 2.3: 统一硬链接/符号链接创建（采用 Web 的临时文件+重命名安全流程）
  - [x] 2.4: 统一 `DropSingletonGroups` 与数据库同步逻辑
  - [x] 2.5: GUI `MainWindowVM.DeleteInternal` 改为调用 Core 服务
  - [x] 2.6: Web `ScanService.DeleteItemsAsync`/`MoveItemsAsync`/`CreateLinksAsync` 改为调用 Core 服务
  - [x] 2.7: CLI `MarkCommand.ExecuteDeletion` 改为调用 Core 服务
  - [x] 2.8: 集成测试覆盖回收站回退、硬链接创建、符号链接创建、组清理

- [x] Task 3: 提取 ScanOrchestrator 到 Core
  - [x] 3.1: 在 `VDF.Core/Services/` 创建 `ScanOrchestrator`，封装 `ScanState` 枚举 + 取消 + 暂停 + 进度节流
  - [x] 3.2: 定义统一进度事件 `ScanProgressArgs`（替代 GUI/Web 两套 DTO 映射）
  - [x] 3.3: GUI `Scanner` 相关逻辑改为调用 `ScanOrchestrator`，`IsScanning`/`IsBusy` 等 ReactiveUI 属性仅绑定 orchestrator 事件
  - [x] 3.4: CLI `ScanRunner` 改为调用 `ScanOrchestrator`，保留 `Console.Error` 进度输出
  - [x] 3.5: Web `ScanService` 改为调用 `ScanOrchestrator`，SignalR 广播 orchestrator 事件
  - [x] 3.6: 测试覆盖取消、暂停、进度节流、错误状态转换

## Phase 2: 统一数据与缓存

- [ ] Task 4: 提取 ThumbnailService 到 Core
  - [ ] 4.1: 在 `VDF.Core/Services/` 创建 `ThumbnailService`，合并 GUI `ThumbnailStore` 的 pack 持久化与 Web 的内存 LRU
  - [ ] 4.2: 统一持久化策略（pack 文件 + 内存 LRU 两级），Web 启用持久化
  - [ ] 4.3: GUI `ThumbnailStore` 改为薄包装
  - [ ] 4.4: Web `ThumbnailEndpoints` 改为调用 Core 服务，删除 `ScanService.HqThumbCache`/`FullThumbCache`
  - [ ] 4.5: 测试覆盖持久化加载、LRU 淘汰、按需提取、路径安全校验

- [ ] Task 5: 提取 ResultsStore 到 Core
  - [ ] 5.1: 在 `VDF.Core/Services/` 创建 `ResultsStore`，统一持久化格式（基于 GUI `ScanResultsEnvelope` 的版本化 JSON）
  - [ ] 5.2: GUI `ScanResultsEnvelope` 改为调用 Core 服务
  - [ ] 5.3: Web `ScanService` 增加结果持久化与重启恢复（消除 Web 功能缺口）
  - [ ] 5.4: 测试覆盖保存/加载/版本迁移/备份导入

## Phase 3: 统一配置与导出

- [x] Task 6: Settings 单一来源
  - [x] 6.1: 评估 Core `Settings` 序列化方式（反射或源生成器），确保新增字段自动序列化
  - [x] 6.2: GUI `SettingsFile` 仅保留 UI 专属字段（窗口位置/主题/快捷键/自定义命令），Core 字段透传
  - [x] 6.3: Web `WebSettingsService` 仅保留 Web 专属字段（`AutoLoadThumbnails`/`ThumbnailWidth`/`ThumbnailJpegQuality`）
  - [x] 6.4: 删除 `MainWindowVM.SyncCoreSettings` 与 `WebSettingsService.Load/Save` 中的手工字段拷贝
  - [x] 6.5: 统一 `SettingsEndpoints` PUT 校验与 `WebSettingsService.Load` 校验
  - [x] 6.6: 测试覆盖新增字段自动序列化、单位转换集中在绑定层

- [ ] Task 7: 统一 QualityRanker 与 CSV 导出
  - [ ] 7.1: 合并 GUI `QualityCriteriaMap`（6 项）与 Web `keepbest` 硬编码准则（8 项，含 `HdrFormatRank`）到 Core
  - [ ] 7.2: 统一 CSV 导出字段（含 `Checked` 列），提取到 Core
  - [ ] 7.3: GUI `MainWindowVM_Utils.cs` 与 Web `ResultEndpoints` 改为调用 Core 实现
  - [ ] 7.4: 测试覆盖准则排序、CSV 字段一致性、`autoselect` 模式行为

# Task Dependencies
- [Task 2] 依赖 [Task 1]（共享进度抽象与 Core 服务层基础设施）
- [Task 3] 可与 [Task 1][Task 2] 并行
- [Task 4] 依赖 [Task 3]（ThumbnailService 需要扫描状态协调）
- [Task 5] 依赖 [Task 3]
- [Task 6] 可与 Phase 1/2 并行
- [Task 7] 依赖 [Task 2]（CSV 导出涉及文件操作后的状态）
