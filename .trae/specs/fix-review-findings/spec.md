# 修复代码审查遗留问题 Spec

## Why
本次代码审查发现 14 个未处理的问题（3 Critical, 6 Warning, 5 Info），涵盖：并发竞态条件边界情况、FFmpeg 资源释放链路、代码可读性改进、安全配置完善等。

## What Changes
- **Critical**: 修复 ScanEngine 并发竞态条件边界情况（锁粒度问题）
- **Critical**: 确保 VideoStreamDecoder 正确释放 FFmpeg 原生资源
- **Critical**: 统一 ScanEngine async void 异常传播路径
- **Warning**: 添加端点级别细粒度速率限制
- **Warning**: 重构 AuthEndpoints 登录请求解析逻辑
- **Warning**: JwtService.ValidateToken 异常记录改进
- **Warning**: ScanEngine 语言服务 key 改为强类型常量
- **Warning**: 移除未使用的 Antiforgery 注册
- **Warning**: ScanEngine 过大问题评估（Info 类，待评估）
- **Info**: 确认密码验证实现正确（已有，无需修改）
- **Info**: 确认 Cookie 安全配置良好（已有，无需修改）
- **Info**: 确认生产环境异常处理（日志级别待验证）
- **Info**: 简化 ScanEngine 并发比较逻辑
- **Info**: 确认数据库索引（SQLite 索引覆盖待验证）

## Impact
- Affected specs: fix-audit-findings（重叠：并发安全、异常处理）
- Affected code:
  - `VDF.Core/ScanEngine.cs` — 并发锁粒度、async void、语言服务 key
  - `VDF.Core/FFTools/FFmpegNative/VideoStreamDecoder.cs` — 资源释放
  - `VDF.Web/Endpoints/AuthEndpoints.cs` — 请求解析
  - `VDF.Web/Services/JwtService.cs` — 异常日志
  - `VDF.Web/Program.cs` — Antiforgery、限流

## ADDED Requirements

### Requirement: ScanEngine 并发锁粒度
系统 SHALL 确保在比较阶段，锁的粒度覆盖整个 "检查+获取+修改" 流程，避免竞态条件。

#### Scenario: 线程 A 和线程 B 并发合并同一 group
- **WHEN** 线程 A 持有 baseMembers 锁，线程 B 同时尝试合并
- **THEN** 锁正确保护 groupMembers 字典操作，无 KeyNotFoundException

### Requirement: VideoStreamDecoder 资源释放
系统 SHALL 确保所有 VideoStreamDecoder 实例在使用完毕后正确释放 FFmpeg 原生资源。

#### Scenario: 采样完成后资源释放
- **WHEN** ScanEngine 完成视频帧采样
- **THEN** VideoStreamDecoder.Dispose() 被调用，原生指针全部置 null

### Requirement: AuthEndpoints 请求解析重构
系统 SHALL 统一登录端点的请求解析逻辑，消除重复的 JSON/form 解析代码。

#### Scenario: JSON 请求
- **WHEN** Content-Type 为 application/json
- **THEN** 请求体被正确解析为 LoginRequest

#### Scenario: Form 请求
- **WHEN** Content-Type 为 application/x-www-form-urlencoded
- **THEN** 请求体被正确解析

### Requirement: JwtService 异常日志
系统 SHALL 在 Token 验证失败时记录足够信息用于排查，但不泄露敏感内容。

#### Scenario: 密钥不匹配
- **WHEN** ValidateToken 因密钥不匹配失败
- **THEN** 日志记录 "Token validation failed: key mismatch"，不记录 token 内容

### Requirement: 端点级别速率限制
系统 SHALL 对重操作端点（如 `/scan/start`）实施更严格的速率限制。

#### Scenario: 扫描端点限流
- **WHEN** 同一 IP 在 1 分钟内第 3 次调用 POST `/scan/start`
- **THEN** 返回 HTTP 429

## MODIFIED Requirements

### Requirement: Antiforgery 处理
`AddAntiforgery()` 已注册但未使用。系统 SHALL 移除未使用的服务注册，或实现 CSRF 防护。

#### Scenario: 移除未使用服务
- **WHEN** 代码审查发现 Antiforgery 未实际使用
- **THEN** 移除 Program.cs 中的 AddAntiforgery() 调用

## REMOVED Requirements

（无移除的需求）
