## Phase 1: 提取共享服务到 Core

- [x] `VDF.Core/Services/FFmpegSetupService` 存在，GUI 与 Web 共用同一实现
- [x] GUI `MainWindowVM_FfmpegDownloader.cs` 不再包含下载计划/校验/解压逻辑，仅做进度 UI 绑定
- [x] Web `FFmpegSetupService.cs` 不再包含下载计划/校验/解压逻辑，仅保留 Web 专属检测
- [x] FFmpeg 下载计划生成、校验和比对、tar/zip 解压有单元测试覆盖
- [x] `VDF.Core/Services/FileOperationsService` 存在，三前端共用
- [x] GUI `MainWindowVM.DeleteInternal` 改为调用 Core 服务
- [x] Web `ScanService.DeleteItemsAsync`/`MoveItemsAsync`/`CreateLinksAsync` 改为调用 Core 服务
- [x] CLI `MarkCommand.ExecuteDeletion` 改为调用 Core 服务
- [x] 回收站回退、硬链接创建、符号链接创建、`DropSingletonGroups` 有集成测试
- [x] `VDF.Core/Services/ScanOrchestrator` 存在，封装状态机/取消/暂停/进度节流
- [x] GUI/CLI/Web 均通过 `ScanOrchestrator` 驱动扫描，不再各自维护状态机
- [x] 取消、暂停、进度节流、错误状态转换有测试覆盖

## Phase 2: 统一数据与缓存

- [ ] `VDF.Core/Services/ThumbnailService` 存在，统一持久化 pack 与内存 LRU
- [ ] GUI `ThumbnailStore` 改为薄包装
- [ ] Web `ThumbnailEndpoints` 改为调用 Core 服务，删除 `ScanService.HqThumbCache`/`FullThumbCache`
- [ ] Web 重启后缩略图可从持久化 pack 加载，无需重新解码 FFmpeg
- [ ] 持久化加载、LRU 淘汰、按需提取、路径安全校验有测试覆盖
- [ ] `VDF.Core/Services/ResultsStore` 存在，统一结果持久化格式
- [ ] GUI `ScanResultsEnvelope` 改为调用 Core 服务
- [ ] Web 重启后可恢复上次扫描结果（功能缺口已消除）
- [ ] 保存/加载/版本迁移/备份导入有测试覆盖

## Phase 3: 统一配置与导出

- [x] Core `Settings` 为唯一规范，新增字段无需手工同步即可在 GUI/Web 序列化
- [x] GUI `SettingsFile` 仅保留 UI 专属字段（窗口位置/主题/快捷键/自定义命令）
- [x] Web `WebSettingsService` 仅保留 Web 专属字段
- [x] `MainWindowVM.SyncCoreSettings` 与 `WebSettingsService.Load/Save` 中的手工字段拷贝已删除
- [x] `SettingsEndpoints` PUT 校验与 `WebSettingsService.Load` 校验一致
- [x] 新增字段自动序列化、单位转换集中在绑定层有测试覆盖
- [ ] `QualityRanker` 准则在 Core 统一（含 `HdrFormatRank`），GUI/Web 共用
- [ ] CSV 导出字段一致（含 `Checked` 列），GUI/Web 共用 Core 实现
- [ ] 准则排序、CSV 字段一致性、`autoselect` 模式行为有测试覆盖
