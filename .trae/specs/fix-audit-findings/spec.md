# 代码审计问题修复 Spec

## Why
全面代码审计发现 89 个问题（0 Critical, 11 High, 36 Medium, 42 Low），涵盖安全漏洞、线程安全、错误处理、架构和可访问性。其中 5 个问题需立即修复（登录限速失效、Cookie 无 Secure 标志、Open Redirect、Refresh Token 永不过期、GC 可回收 FFmpeg 回调），另有 7 个高优先级问题。本 spec 聚焦于 HIGH 及关键路径问题的修复，以及部分影响较大的 MEDIUM 问题。

## What Changes
- 修复登录限速器失效 BUG：改为 per-IP 固定分区限速器
- 修复 Cookie 安全标志：设置 `Secure = true` + HTTPS 环境检测
- 修复 Open Redirect：验证 `returnUrl` 必须以 `/` 开头且不含 `://`
- 修复 Refresh Token 永不过期：添加时间戳 + TTL + 撤销方法
- 修复 GC 可回收 FFmpeg 回调：将委托提升为类字段
- 修复 SQLite 并发安全：所有数据库操作加锁
- 修复 async void：改为 `async Task` + 调用方 await
- 修复缩略图缓存竞态：使用 `ConcurrentDictionary` + `Lazy<byte[]>`
- 修复 CORS 默认全开：默认拒绝，明确配置允许来源
- 修复前端 Token 过期问题：`accessTokenFactory` 改为每次读取最新值
- 修复 FileEntry 字典并发访问：使用 `ConcurrentDictionary`
- 修复所有静默 catch 块：添加日志记录
- 修复密码明文日志：移除密码明文输出
- 修复 Logout 不撤销 Refresh Token
- 修复非原子递增：使用 `Interlocked`
- 修复前端 SSE/SignalR Token 过期问题

## Impact
- Affected specs: modernize-tech-stack（认证系统重叠）, optimize-scan-algorithm（线程安全重叠）
- Affected code:
  - `VDF.Web/Program.cs` — 限速器、CORS、中间件
  - `VDF.Web/Services/AuthService.cs` — Token 管理、密码处理
  - `VDF.Web/Services/JwtService.cs` — JWT 密钥管理
  - `VDF.Web/Endpoints/AuthEndpoints.cs` — 登出端点
  - `VDF.Web/Endpoints/ThumbnailEndpoints.cs` — 缩略图缓存
  - `VDF.Web/Endpoints/SseEndpoints.cs` — SSE 错误处理
  - `VDF.Web/Services/WebSettingsService.cs` — 错误处理
  - `VDF.Web/Services/ScanService.cs` — 缩略图缓存
  - `VDF.Core/FFTools/FFmpegNative/NativeMediaInfoExtractor.cs` — GC 回调
  - `VDF.Core/Data/SqliteDatabase.cs` — 线程安全
  - `VDF.Core/FileEntry.cs` — 并发字典
  - `VDF.Core/ScanEngine.cs` — async void、原子递增
  - `VDF.Core/FFTools/FfmpegEngine.cs` — 静态可变状态
  - `VDF.Core/FFTools/FFmpegNative/HardwareAccelerationDetector.cs` — 竞态条件
  - `VDF.Web.Client/src/hooks/useSignalR.ts` — Token 刷新
  - `VDF.Web.Client/src/hooks/useSSE.ts` — Token 刷新
  - `VDF.Web.Client/src/api/client.ts` — Token 存储

## ADDED Requirements

### Requirement: 登录限速器
系统 SHALL 对 POST `/auth/login` 端点实施 per-IP 固定窗口限速，每个 IP 地址每分钟最多 5 次登录尝试。

#### Scenario: 限速生效
- **WHEN** 同一 IP 在 1 分钟内第 6 次调用 POST `/auth/login`
- **THEN** 返回 HTTP 429 Too Many Requests

#### Scenario: 限速器共享
- **WHEN** 多个并发登录请求到达
- **THEN** 所有请求共享同一限速器实例

### Requirement: Cookie 安全标志
系统 SHALL 在设置认证 Cookie 时包含 `Secure` 标志（当运行在 HTTPS 环境时）。

#### Scenario: HTTPS 环境下 Cookie 安全
- **WHEN** 应用运行在 HTTPS 端口
- **THEN** 认证 Cookie 设置 `Secure = true`

#### Scenario: HTTP 环境下兼容
- **WHEN** 应用运行在 HTTP 端口（开发环境）
- **THEN** 认证 Cookie 不设置 `Secure` 标志，但记录警告日志

### Requirement: Open Redirect 防护
系统 SHALL 验证所有 `returnUrl` 参数，仅允许相对路径重定向。

#### Scenario: 恶意重定向被阻止
- **WHEN** `returnUrl` 包含 `://` 或以 `//` 开头
- **THEN** 重定向到默认页面 `/`

#### Scenario: 合法相对路径放行
- **WHEN** `returnUrl` 以 `/` 开头且不含 `://`
- **THEN** 正常重定向到 `returnUrl`

### Requirement: Refresh Token 生命周期管理
系统 SHALL 为 Refresh Token 实施过期和撤销机制。

#### Scenario: Token 过期
- **WHEN** Refresh Token 超过 7 天未使用
- **THEN** 拒绝该 Token，要求重新登录

#### Scenario: 登出撤销
- **WHEN** 用户调用登出端点
- **THEN** 从有效 Token 集合中移除该 Refresh Token

#### Scenario: 最大会话数
- **WHEN** 用户已有 5 个有效 Refresh Token
- **THEN** 撤销最早的 Token 后再签发新 Token

### Requirement: FFmpeg 回调委托安全
系统 SHALL 确保 FFmpeg 中断回调委托不被 GC 回收。

#### Scenario: 长时间 FFmpeg 操作
- **WHEN** `avformat_open_input` 执行时间超过 GC 周期
- **THEN** 回调委托仍然有效，不会导致 segfault

### Requirement: SQLite 线程安全
系统 SHALL 确保所有 SQLite 数据库操作线程安全。

#### Scenario: 并发数据库写入
- **WHEN** 多个工作线程同时调用 `SaveDatabaseSqlite()`
- **THEN** 操作被正确序列化，不会导致 SQLITE_MISUSE 或数据损坏

### Requirement: ScanEngine 异步安全
系统 SHALL 将 `StartSearch()` 和 `StartCompare()` 从 `async void` 改为 `async Task`。

#### Scenario: 扫描期间异常
- **WHEN** 扫描过程中抛出异常
- **THEN** 异常被调用方捕获并记录，不会终止进程

### Requirement: 缩略图缓存线程安全
系统 SHALL 使用线程安全的数据结构实现缩略图缓存。

#### Scenario: 并发缩略图请求
- **WHEN** 多个请求同时访问缩略图缓存
- **THEN** 不会出现竞态条件导致的缓存清空或重复 FFmpeg 调用

### Requirement: CORS 安全默认
系统 SHALL 默认拒绝跨域请求，仅允许明确配置的来源。

#### Scenario: 未配置 CORS
- **WHEN** 未设置 `VDF_CORS_ORIGINS` 环境变量
- **THEN** 仅允许同源请求，记录警告日志

#### Scenario: 配置了 CORS
- **WHEN** 设置了 `VDF_CORS_ORIGINS=https://example.com`
- **THEN** 仅允许来自 `https://example.com` 的跨域请求

### Requirement: FileEntry 字典线程安全
系统 SHALL 使用 `ConcurrentDictionary` 替代 `Dictionary` 存储 `grayBytes` 和 `PHashes`。

#### Scenario: 并发写入
- **WHEN** 多个工作线程同时对同一 FileEntry 写入 grayBytes
- **THEN** 不会抛出异常或导致数据损坏

### Requirement: 前端 Token 动态读取
系统 SHALL 在每次 SSE/SignalR 连接时读取最新的 access token。

#### Scenario: Token 刷新后重连
- **WHEN** access token 被刷新
- **THEN** SSE 和 SignalR 的后续连接使用新 Token

### Requirement: 错误处理日志记录
系统 SHALL 在所有 catch 块中记录异常信息，不得静默吞掉异常。

#### Scenario: 凭证文件损坏
- **WHEN** `LoadOrGeneratePassword()` 反序列化失败
- **THEN** 记录警告日志，说明凭证文件损坏并生成新密码

### Requirement: 密码安全
系统 SHALL 确保密码不被明文记录到日志中。

#### Scenario: 密码变更
- **WHEN** 系统生成或更新密码
- **THEN** 日志中仅记录"密码已更新"，不包含密码明文

### Requirement: 原子递增
系统 SHALL 使用 `Interlocked.Increment` 替代非原子的 `++` 操作用于共享计数器。

#### Scenario: 并行工作线程计数
- **WHEN** 多个工作线程同时递增 `processedFiles`
- **THEN** 计数值准确，无丢失更新

## MODIFIED Requirements

### Requirement: 登出端点
登出端点 SHALL 从有效 Refresh Token 集合中移除请求中的 Refresh Token，并清除认证 Cookie。

## REMOVED Requirements

（无移除的需求）
