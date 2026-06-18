# 统一后端改造 Spec

## Why
当前 GUI / CLI / Web 三前端各自实现批量文件操作、FFmpeg 下载、Settings 同步、扫描编排、缩略图缓存等业务逻辑，导致：
- FFmpeg 下载代码在 GUI 与 Web 重复 ~900 行（URL/版本/校验逻辑需双改）；
- Settings 在 4-5 处手工字段拷贝（已有字段遗漏 bug 注释为证）；
- 批量删除/移动/链接逻辑三处分歧（回收站回退、硬链接创建流程不一致）；
- Web 缺少扫描结果与缩略图持久化，重启即丢，而 GUI 已有 `backup.scanresults`。

维护成本高、行为不一致、功能缺口明显。

## What Changes
- 新增 `VDF.Core/Services/` 服务层，承载所有共享业务逻辑，三前端统一消费。
- 提取 `FFmpegSetupService` 到 Core，GUI/Web 共用，删除两份重复实现。
- 提取 `FileOperationsService`（删除/移动/硬链接/符号链接/回收站/`DropSingletonGroups`）到 Core。
- 提取 `ScanOrchestrator` 统一扫描状态机（取消/暂停/进度节流），替代三套各自实现。
- 提取 `ThumbnailService` 统一缩略图缓存与持久化策略，Web 也能跨会话保留。
- 提取 `ResultsStore` 统一扫描结果持久化，Web 重启可恢复。
- 统一 `Settings` 单一来源：GUI/Web 仅保留 UI 专属字段，Core `Settings` 通过统一序列化消除手工拷贝。
- 统一 `QualityRanker` 准则与 CSV 导出字段（含 `Checked` 列）。
- **BREAKING**：删除 `VDF.GUI/ViewModels/MainWindowVM_FfmpegDownloader.cs` 与 `VDF.Web/Services/FFmpegSetupService.cs` 的独立实现，改为调用 Core。

## Impact
- Affected specs: `future-architecture-design`（部分重叠，本 spec 聚焦"统一后端"而非"插件化/云存储"，两者互补）
- Affected code:
  - 新增 `VDF.Core/Services/`：`FFmpegSetupService`、`FileOperationsService`、`ScanOrchestrator`、`ThumbnailService`、`ResultsStore`
  - `VDF.GUI/ViewModels/MainWindowVM.cs` — 删除 `SyncCoreSettings`、`DeleteInternal`、`DropSingletonGroups` 中的重复逻辑
  - `VDF.GUI/ViewModels/MainWindowVM_FfmpegDownloader.cs` — 删除重复实现，改为薄包装
  - `VDF.GUI/Utils/ThumbnailStore.cs` — 改为调用 Core `ThumbnailService`
  - `VDF.GUI/Data/SettingsFile.cs` — 仅保留 GUI 专属字段
  - `VDF.GUI/Data/ScanResultsEnvelope.cs` — 改为调用 Core `ResultsStore`
  - `VDF.Web/Services/ScanService.cs` — 删除 `DeleteItemsAsync`/`MoveItemsAsync`/`CreateLinksAsync`/`DropSingletonGroups` 重复实现
  - `VDF.Web/Services/FFmpegSetupService.cs` — 删除重复实现，改为薄包装
  - `VDF.Web/Services/WebSettingsService.cs` — 仅保留 Web 专属字段
  - `VDF.Web/Endpoints/ThumbnailEndpoints.cs` — 改为调用 Core `ThumbnailService`
  - `VDF.Web/Endpoints/ResultEndpoints.cs` — 改为调用 Core `QualityRanker` 与 CSV 导出
  - `VDF.CLI/Commands/ScanRunner.cs` — 改为调用 `ScanOrchestrator`
  - `VDF.CLI/Commands/MarkCommand.cs` — 改为调用 `FileOperationsService`

## ADDED Requirements

### Requirement: 共享 FFmpeg 安装服务
系统 SHALL 在 `VDF.Core/Services/` 提供唯一的 FFmpeg 下载/校验/解压服务，GUI 与 Web 共用同一实现。

#### Scenario: GUI 触发 FFmpeg 下载
- **WHEN** 用户在 GUI 点击"下载 FFmpeg"
- **THEN** 调用 Core `FFmpegSetupService`，进度通过 `IProgress<FFmpegSetupProgress>` 上报 GUI

#### Scenario: Web 触发 FFmpeg 下载
- **WHEN** Web 启动检测到 FFmpeg 缺失
- **THEN** 调用同一 Core `FFmpegSetupService`，进度通过 SignalR 广播

#### Scenario: URL 或版本映射修复
- **WHEN** 维护者更新 BtbN 下载源或版本映射
- **THEN** 仅修改 Core 一处，GUI 与 Web 同时生效

### Requirement: 统一文件操作服务
系统 SHALL 在 `VDF.Core/Services/` 提供 `FileOperationsService`，统一删除/移动/硬链接/符号链接/回收站/`DropSingletonGroups` 实现，GUI/CLI/Web 共用。

#### Scenario: 批量删除到回收站
- **WHEN** 任一前端请求批量删除文件到回收站
- **THEN** Core `FileOperationsService` 执行 Windows `SHFileOperation` 批量回收，失败回退永久删除，并同步数据库与 `DropSingletonGroups`

#### Scenario: 创建硬链接替代删除
- **WHEN** 任一前端请求以硬链接替代原文件
- **THEN** Core `FileOperationsService` 使用统一的临时文件+重命名安全流程创建硬链接

### Requirement: 统一扫描编排
系统 SHALL 在 `VDF.Core/Services/` 提供 `ScanOrchestrator`，封装扫描状态机、取消、暂停、进度节流，三前端共用。

#### Scenario: GUI 启动扫描
- **WHEN** GUI 调用 `ScanOrchestrator.StartAsync`
- **THEN** 通过事件/`IObservable` 上报进度，GUI 仅做 UI 绑定，不再自维护状态机

#### Scenario: Web 取消扫描
- **WHEN** Web 调用 `ScanOrchestrator.CancelAsync`
- **THEN** 同一 `CancellationToken` 传播到 `ScanEngine`，状态转为 `Aborted`

#### Scenario: 进度节流
- **WHEN** 扫描引擎高频上报进度
- **THEN** `ScanOrchestrator` 按统一节流策略（如每 100ms 或每 1% 进度）转发，避免 UI 线程过载

### Requirement: 统一缩略图服务
系统 SHALL 在 `VDF.Core/Services/` 提供 `ThumbnailService`，统一缓存与持久化策略，Web 也能跨会话保留缩略图。

#### Scenario: Web 重启后访问缩略图
- **WHEN** Web 进程重启后用户打开结果页
- **THEN** `ThumbnailService` 从持久化 pack 加载，无需重新解码 FFmpeg

#### Scenario: 内存 LRU 淘汰
- **WHEN** 内存缓存达到上限
- **THEN** 按统一 LRU 策略淘汰，持久化 pack 中的缩略图不受影响

### Requirement: 统一结果持久化
系统 SHALL 在 `VDF.Core/Services/` 提供 `ResultsStore`，GUI 与 Web 共用同一持久化格式，Web 重启可恢复。

#### Scenario: Web 重启后恢复结果
- **WHEN** Web 进程重启
- **THEN** `ResultsStore` 从磁盘加载上次扫描结果，前端可立即展示

#### Scenario: 结果格式版本迁移
- **WHEN** 持久化格式升级
- **THEN** `ResultsStore` 识别旧版本并迁移，旧备份仍可导入

### Requirement: Settings 单一来源
系统 SHALL 以 `VDF.Core/Settings` 为唯一规范，GUI/Web 仅附加 UI 专属字段，消除手工字段拷贝。

#### Scenario: 新增 Core Settings 字段
- **WHEN** 开发者在 Core `Settings` 添加新字段
- **THEN** GUI/Web 自动序列化该字段，无需在多处添加手工同步代码

#### Scenario: 单位转换集中化
- **WHEN** GUI 显示百分比而 Core 存储小数（如 `PartialClipMinRatioPercent / 100.0`）
- **THEN** 转换逻辑集中在绑定层，Core `Settings` 只存规范单位

## MODIFIED Requirements

### Requirement: 批量操作
系统 SHALL 通过 Core `FileOperationsService` 支持一次性对多个重复文件执行删除/移动/归档/链接，三前端行为一致，不再各自实现。

## REMOVED Requirements

### Requirement: GUI 独占的 FFmpeg 下载器
**Reason**: 与 Web 实现重复 ~900 行，URL/版本/校验修复需双改。
**Migration**: GUI 改为调用 Core `FFmpegSetupService`，删除 `MainWindowVM_FfmpegDownloader.cs` 中的下载/校验/解压实现，仅保留进度 UI 绑定。

### Requirement: Web 独占的批量文件操作
**Reason**: 与 GUI/CLI 重复实现，回收站回退与硬链接创建流程分歧。
**Migration**: Web `ScanService` 改为调用 Core `FileOperationsService`，删除 `DeleteItemsAsync`/`MoveItemsAsync`/`CreateLinksAsync`/`DropSingletonGroups` 重复实现。

### Requirement: 手工 Settings 字段同步
**Reason**: 4-5 处手工拷贝易遗漏（已有 `ThumbnailCount` 字段被遗漏的 bug 注释）。
**Migration**: GUI/Web `Settings` 类仅保留 UI 专属字段，Core `Settings` 通过统一序列化（反射或源生成器）自动处理共享字段，删除 `SyncCoreSettings` 与 `WebSettingsService.Load/Save` 中的手工拷贝。

## 范围说明
本 spec 聚焦"统一后端"——消除三前端业务逻辑重复、统一数据与缓存策略。`future-architecture-design` 中的插件化、云存储、强类型语言 key 等不在本 spec 范围内，两者互补可后续叠加。
