# 现代化技术栈重构 Spec

## Why
当前项目使用 Blazor Server 作为 Web UI，依赖 SignalR WebSocket 连接，在 NAS 等弱网/远程环境下体验差；桌面 GUI 基于 Avalonia 但 UI 风格陈旧；认证机制简单（内存 token + cookie），安全性不足；FFmpeg 操作仍大量依赖 CLI 进程调用而非原生绑定。需要通过技术栈升级来提升跨平台兼容性、远程访问体验、安全性和可靠性。

## What Changes
- **BREAKING**: 将 Web UI 从 Blazor Server 替换为 React + Vite 现代化 SPA 前端
- **BREAKING**: 将后端 Web 层重构为 ASP.NET Core Minimal API + SignalR（实时进度推送）
- 增强认证系统：JWT 替代内存 token，支持 API Key、HTTPS、Rate Limiting、CORS
- 扩展 FFmpeg 原生绑定覆盖范围，减少 CLI 进程调用
- 优化 NAS/远程访问：支持反向代理子路径部署、网络路径（SMB/NFS）健壮性、SSE 降级
- 现代化桌面 GUI 主题和交互设计
- 数据库层升级：引入 SQLite（通过 Microsoft.Data.Sqlite）替代自定义 JSON 存储
- Docker 镜像优化：多阶段构建、非 root 用户、健康检查

## Impact
- Affected specs: Web UI、认证系统、FFmpeg 交互层、数据库层、Docker 部署
- Affected code:
  - `VDF.Web/` — 完全重写
  - `VDF.Core/FFTools/` — 扩展原生绑定
  - `VDF.Core/Utils/DatabaseUtils.cs` — 迁移到 SQLite
  - `VDF.Core/Settings.cs` — 新增安全/网络配置
  - `VDF.GUI/` — 主题现代化
  - `VDF.Web/Dockerfile` — 优化
  - 新增 `VDF.Web.Client/` — React SPA 前端项目

---

## ADDED Requirements

### Requirement: React SPA 前端
系统 SHALL 提供基于 React + Vite + TypeScript 的现代化 SPA 前端，替代现有 Blazor Server UI。

#### Scenario: 用户通过浏览器访问 Web UI
- **WHEN** 用户在浏览器中访问 VDF Web 服务
- **THEN** 系统返回 React SPA 应用，无需 WebSocket 依赖即可渲染基础页面
- **AND** 扫描进度通过 SignalR WebSocket 或 SSE 降级实时推送

#### Scenario: 弱网环境下的远程访问
- **WHEN** 用户通过 NAS 或远程网络访问 Web UI，网络延迟高或不稳定
- **THEN** 前端仍可正常加载和操作，不因 WebSocket 断开而白屏
- **AND** 实时进度通过 SSE 降级或轮询获取

### Requirement: REST API 层
系统 SHALL 提供完整的 RESTful API，作为前后端通信的唯一接口。

#### Scenario: 前端发起扫描请求
- **WHEN** 前端发送 POST /api/scan/start
- **THEN** 系统启动扫描任务并返回 202 Accepted + 任务 ID
- **AND** 扫描进度通过 GET /api/scan/progress 或 SignalR 推送

#### Scenario: 获取扫描结果
- **WHEN** 前端发送 GET /api/results
- **THEN** 系统返回分页的重复文件组列表，包含缩略图 URL

### Requirement: JWT 认证与安全增强
系统 SHALL 使用 JWT 替代内存 token 进行认证，并支持多种安全特性。

#### Scenario: 用户登录
- **WHEN** 用户提交密码登录
- **THEN** 系统验证密码后签发 JWT access token（短期）和 refresh token（长期）
- **AND** JWT 包含用户角色声明，支持未来多用户扩展

#### Scenario: API Key 认证
- **WHEN** 外部脚本或自动化工具携带 API Key 请求 API
- **THEN** 系统验证 API Key 后允许访问，无需 JWT 登录流程

#### Scenario: HTTPS 支持
- **WHEN** 管理员配置了 TLS 证书路径
- **THEN** 系统自动启用 HTTPS，拒绝非加密连接

#### Scenario: Rate Limiting
- **WHEN** 同一 IP 在短时间内发送大量请求
- **THEN** 系统对登录接口实施速率限制（如 5 次/分钟），防止暴力破解

### Requirement: FFmpeg 原生绑定扩展
系统 SHALL 扩展 FFmpeg.AutoGen 原生绑定覆盖范围，减少 CLI 进程调用。

#### Scenario: 获取媒体信息
- **WHEN** 系统需要获取视频文件的编码、分辨率、码率等信息
- **THEN** 优先使用 FFmpeg 原生绑定（avformat）读取，而非启动 ffprobe CLI 进程
- **AND** 仅在原生绑定失败时回退到 CLI 调用

#### Scenario: 音频流解码
- **WHEN** 系统需要提取音频用于 Chromaprint 指纹计算
- **THEN** 使用原生 AudioStreamDecoder 解码，而非通过 ffmpeg CLI 管道

### Requirement: NAS/远程访问优化
系统 SHALL 针对 NAS 和远程访问场景进行优化。

#### Scenario: 反向代理子路径部署
- **WHEN** 管理员配置 VDF_BASE_PATH=/vdf 环境变量
- **THEN** 所有 API 路由和前端资源路径均以 /vdf 为前缀
- **AND** 前端路由正确处理 base path

#### Scenario: 网络路径（SMB/NFS）扫描
- **WHEN** 用户添加 SMB 或 NFS 挂载路径作为扫描目录
- **THEN** 系统正确处理网络路径的权限错误和超时
- **AND** 扫描过程中网络断开时优雅降级而非崩溃

#### Scenario: 健康检查端点
- **WHEN** 外部监控或负载均衡器请求 GET /health
- **THEN** 系统返回服务状态（FFmpeg 可用性、数据库连接等）

### Requirement: SQLite 数据库
系统 SHALL 使用 SQLite 替代自定义 JSON 文件存储扫描数据库。

#### Scenario: 数据库迁移
- **WHEN** 系统首次启动且检测到旧版 JSON 数据库文件
- **THEN** 自动将数据迁移到 SQLite，迁移完成后保留原文件备份

#### Scenario: 并发写入安全
- **WHEN** 多个扫描任务或 API 请求同时写入数据库
- **THEN** 使用 WAL 模式和事务保证数据一致性

### Requirement: 现代化桌面 GUI
系统 SHALL 更新 Avalonia 桌面 GUI 的视觉设计，使其更现代。

#### Scenario: 主题更新
- **WHEN** 用户启动桌面 GUI
- **THEN** 界面采用更新的 Fluent 主题设计，包含圆角、阴影、动画过渡
- **AND** 支持明暗主题切换

### Requirement: Docker 镜像优化
系统 SHALL 优化 Docker 镜像构建和运行配置。

#### Scenario: 非 root 运行
- **WHEN** Docker 容器启动
- **THEN** 应用以非 root 用户运行，提高安全性

#### Scenario: 健康检查
- **WHEN** Docker 容器运行中
- **THEN** 内置 HEALTHCHECK 指令定期检查服务可用性

#### Scenario: 前端独立构建
- **WHEN** Docker 构建过程
- **THEN** React SPA 在构建阶段编译为静态文件，运行时由 ASP.NET Core 提供服务

---

## MODIFIED Requirements

### Requirement: 认证系统
原系统使用内存 token + cookie 认证。现修改为：
- JWT access token（15 分钟有效期）+ refresh token（30 天有效期）
- 支持 API Key 认证（通过 X-API-Key 请求头）
- 支持 VDF_WEB_PASSWORD 环境变量（向后兼容）
- 支持 VDF_WEB_AUTH=false 禁用认证（向后兼容）
- 新增 VDF_API_KEYS 环境变量配置 API Key 列表
- 新增 VDF_TLS_CERT 和 VDF_TLS_KEY 环境变量配置 HTTPS

### Requirement: Web UI 功能
原 Blazor Server UI 的所有功能 SHALL 在 React SPA 中完整保留：
- 扫描配置（包含/排除路径、文件夹浏览器）
- 实时扫描进度显示
- 重复文件组展示（卡片布局、缩略图、元数据对比）
- 文件操作（删除、移动、创建链接）
- 设置页面（所有现有设置项）
- CSV 导出
- 对比模态框（并排/滑动对比）
- 键盘导航
- 明暗主题切换

---

## REMOVED Requirements

### Requirement: Blazor Server 渲染模式
**Reason**: 替换为 React SPA，消除 WebSocket 强依赖，改善远程访问体验
**Migration**: 所有 Blazor 组件逻辑迁移到 React 组件 + REST API 调用

### Requirement: 内存 token 认证
**Reason**: 替换为 JWT，支持分布式部署和 API Key 认证
**Migration**: 现有 cookie 认证自动迁移到 JWT，VDF_WEB_PASSWORD 环境变量保持兼容
