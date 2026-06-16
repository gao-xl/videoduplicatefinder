# VideoDuplicateFinder 未来架构设计方案

## Why
当前 VideoDuplicateFinder 已实现核心功能（视频扫描、相似度检测、缩略图生成），但存在以下问题限制其发展：
1. **架构扩展性不足** — ScanEngine 过于庞大（约 2500 行），难以添加新功能
2. **性能优化空间大** — GPU 利用率低，Native 模式未默认开启
3. **多平台支持不完善** — GUI 独占部分功能，Web/CLI 功能不完整
4. **可维护性问题** — 语言服务无编译时检查，配置分散

## What Changes

### 1. 模块化重构
- 将 ScanEngine 拆分为独立服务：
  - `FileScannerService` — 文件枚举和过滤
  - `MediaAnalyzerService` — 帧采样和哈希计算
  - `DuplicateDetectionService` — 相似度比较和分组
  - `ThumbnailService` — 缩略图生成和管理
- 提取公共接口 `IMediaAnalyzer`、`IDuplicateDetector` 等

### 2. 性能优化
- **默认启用 Native FFmpeg** — `UseNativeFfmpegBinding=true` 设为默认值
- **GPU 智能调度** — 根据硬件配置自动选择最佳解码路径
- **增量扫描** — 记录文件修改时间，只重新处理变更文件
- **并行对比算法** — 利用多核 CPU 并行化比较阶段

### 3. 扩展性增强
- **插件系统** — 支持自定义哈希算法（pHash、dHash、ahash）
- **云存储支持** — 抽象 `IFileSystemProvider`，支持 S3/Azure Blob
- **多语言架构** — 强类型语言 key（编译时检查）

### 4. 安全性强化
- **API 认证增强** — API Key 持久化和轮换
- **审计日志** — 记录所有删除/移动操作
- **HTTPS 强制** — 生产环境默认要求 HTTPS

### 5. 用户体验
- **批量操作** — 批量删除/移动/归档重复文件
- **结果预览** — 内联视频播放预览
- **进度预估** — 基于历史数据的智能时间预估
- **PWA 支持** — 离线访问、推送通知

## Impact
- Affected specs: 所有现有 spec
- Affected code:
  - `VDF.Core/ScanEngine.cs` → 拆分为多个服务
  - `VDF.Core/Utils/LanguageService.cs` → 强类型 key
  - `VDF.Web/Services/ScanService.cs` → 使用新服务
  - 新增 `VDF.Core/Interfaces/` — 抽象接口定义
  - 新增 `VDF.Core/Providers/` — 文件系统抽象

## ADDED Requirements

### Requirement: 模块化扫描引擎
系统 SHALL 将 ScanEngine 拆分为独立服务，通过接口通信。

#### Scenario: 添加新哈希算法
- **WHEN** 开发者需要添加新的相似度算法
- **THEN** 实现 `ISimilarityComparer` 接口并注册，无需修改核心逻辑

#### Scenario: 支持云存储
- **WHEN** 用户配置 S3 bucket 作为扫描源
- **THEN** 实现 `IFileSystemProvider` 接口，系统自动处理远程文件

### Requirement: 增量扫描
系统 SHALL 记录文件修改时间，只重新处理变更的文件。

#### Scenario: 第二次扫描
- **WHEN** 用户对同一文件夹进行第二次扫描
- **THEN** 跳过未变更的文件，复用缓存数据

### Requirement: 强类型语言 Key
系统 SHALL 使用强类型常量替代字符串字面量作为语言 key。

#### Scenario: 编译时检查
- **WHEN** 开发者拼写错误语言 key
- **THEN** 编译器报错，而非运行时才发现

### Requirement: GPU 智能调度
系统 SHALL 根据硬件能力自动选择最佳解码路径。

#### Scenario: NVIDIA GPU 可用
- **WHEN** 系统检测到 NVIDIA GPU 和 CUDA 支持
- **THEN** 自动启用 d3d11va + CUDA 加速路径

### Requirement: 批量操作
系统 SHALL 支持一次性对多个重复文件执行操作（删除/移动/归档）。

#### Scenario: 批量删除
- **WHEN** 用户选择删除 10 个重复文件
- **THEN** 系统一次性处理，逐一确认后执行

## MODIFIED Requirements

### Requirement: 默认配置
`UseNativeFfmpegBinding` SHALL 默认为 `true`。

#### Scenario: 新用户首次扫描
- **WHEN** 用户首次运行程序
- **THEN** 默认使用 Native FFmpeg 模式，享受最佳性能

## REMOVED Requirements

### Requirement: Process FFmpeg 模式
Process FFmpeg 模式作为后备选项保留，但不再推荐。

**Reason**: Native 模式已足够稳定，性能差异巨大（40-60x）

## 迁移计划

### Phase 1: 基础设施（1-2 个月）
1. 提取接口定义 (`ISimilarityComparer`, `IFileSystemProvider`)
2. 重构 LanguageService 使用强类型 key
3. 配置集中管理（移除硬编码默认值）

### Phase 2: 性能（1 个月）
1. 默认启用 Native FFmpeg
2. 实现增量扫描
3. GPU 智能调度

### Phase 3: 扩展性（1-2 个月）
1. 实现插件系统
2. 云存储支持（可选）
3. API 增强

### Phase 4: 用户体验（持续）
1. 批量操作
2. 结果预览
3. PWA 支持
