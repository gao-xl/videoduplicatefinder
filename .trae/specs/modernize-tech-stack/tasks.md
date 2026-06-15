# Tasks

- [x] Task 1: 搭建 React SPA 前端项目骨架
  - [x] SubTask 1.1: 在 VDF.Web.Client/ 目录初始化 React + Vite + TypeScript 项目
  - [x] SubTask 1.2: 配置 Vite 代理（开发时代理到 ASP.NET Core 后端）
  - [x] SubTask 1.3: 安装核心依赖：React Router、TanStack Query、Tailwind CSS
  - [x] SubTask 1.4: 创建基础布局组件（导航栏、侧边栏、内容区）
  - [x] SubTask 1.5: 实现明暗主题切换（CSS 变量 + context provider）

- [x] Task 2: 实现 ASP.NET Core Minimal API 层
  - [x] SubTask 2.1: 定义 API 路由结构（/api/scan/*, /api/results/*, /api/settings/*, /api/auth/*）
  - [x] SubTask 2.2: 实现扫描相关 API（start, stop, pause, resume, progress）
  - [x] SubTask 2.3: 实现结果相关 API（list, delete, move, link, export）
  - [x] SubTask 2.4: 实现设置相关 API（get, update, database maintenance）
  - [x] SubTask 2.5: 实现缩略图 API（/api/thumbnail/hq, /api/thumbnail/full）
  - [x] SubTask 2.6: 配置 SignalR Hub 用于实时进度推送
  - [x] SubTask 2.7: 配置 SSE 降级端点（/api/scan/events）用于不支持 WebSocket 的环境

- [x] Task 3: 实现 JWT 认证与安全增强
  - [x] SubTask 3.1: 实现 JWT 签发和验证逻辑（access token + refresh token）
  - [x] SubTask 3.2: 实现 API Key 认证中间件
  - [x] SubTask 3.3: 添加 ASP.NET Core Rate Limiting 中间件（登录接口限制）
  - [x] SubTask 3.4: 配置 CORS 策略
  - [x] SubTask 3.5: 支持 HTTPS 配置（VDF_TLS_CERT, VDF_TLS_KEY 环境变量）
  - [x] SubTask 3.6: 更新 AuthService 向后兼容 VDF_WEB_PASSWORD 和 VDF_WEB_AUTH

- [x] Task 4: 扩展 FFmpeg 原生绑定
  - [x] SubTask 4.1: 实现原生 MediaInfo 提取（替代 ffprobe CLI 调用）
  - [x] SubTask 4.2: 增强 AudioStreamDecoder 用于 Chromaprint 音频提取
  - [x] SubTask 4.3: 为原生绑定失败场景添加 CLI 回退逻辑
  - [x] SubTask 4.4: 添加硬件加速自动检测（自动选择可用的 HW 加速模式）

- [x] Task 5: 迁移数据库到 SQLite
  - [x] SubTask 5.1: 定义 SQLite 数据库 schema（FileEntry, DuplicateGroup, Settings 等表）
  - [x] SubTask 5.2: 实现 Microsoft.Data.Sqlite 数据访问层
  - [x] SubTask 5.3: 启用 WAL 模式和事务支持
  - [x] SubTask 5.4: 实现旧版 JSON 数据库自动迁移
  - [x] SubTask 5.5: 更新 DatabaseUtils 所有方法使用 SQLite

- [x] Task 6: NAS/远程访问优化
  - [x] SubTask 6.1: 实现 VDF_BASE_PATH 环境变量支持（子路径部署）
  - [x] SubTask 6.2: 添加 /health 健康检查端点
  - [x] SubTask 6.3: 优化网络路径扫描的错误处理和超时
  - [x] SubTask 6.4: 配置反向代理友好头（X-Forwarded-For, X-Forwarded-Proto）

- [x] Task 7: 实现前端核心页面
  - [x] SubTask 7.1: 实现登录页面（JWT 认证流程）
  - [x] SubTask 7.2: 实现扫描配置页面（路径管理、文件夹浏览器、启动扫描）
  - [x] SubTask 7.3: 实现扫描进度页面（SignalR/SSE 实时更新）
  - [x] SubTask 7.4: 实现结果页面（卡片布局、缩略图懒加载、元数据对比、选择操作）
  - [x] SubTask 7.5: 实现设置页面（所有设置项分组展示）
  - [x] SubTask 7.6: 实现对比模态框（并排/滑动对比模式）
  - [x] SubTask 7.7: 实现键盘导航（j/k/x 快捷键）
  - [x] SubTask 7.8: 实现文件操作面板（删除确认、移动、创建链接）

- [x] Task 8: 现代化桌面 GUI 主题
  - [x] SubTask 8.1: 更新 Avalonia 主题样式（圆角、阴影、动画过渡）
  - [x] SubTask 8.2: 实现桌面 GUI 明暗主题切换
  - [x] SubTask 8.3: 优化卡片布局和缩略图展示

- [x] Task 9: Docker 镜像优化
  - [x] SubTask 9.1: 更新 Dockerfile — 多阶段构建包含 React SPA 编译
  - [x] SubTask 9.2: 配置非 root 用户运行
  - [x] SubTask 9.3: 添加 HEALTHCHECK 指令
  - [x] SubTask 9.4: 配置 ASP.NET Core 提供静态文件服务（React SPA 构建产物）

- [x] Task 10: 集成测试与文档更新
  - [x] SubTask 10.1: 为 API 层编写集成测试
  - [x] SubTask 10.2: 为 SQLite 数据库迁移编写测试
  - [x] SubTask 10.3: 为 FFmpeg 原生绑定扩展编写测试
  - [x] SubTask 10.4: 更新 README 文档反映新架构和使用方式

# Task Dependencies
- Task 2 depends on Task 3 (API 需要认证中间件) — DONE
- Task 7 depends on Task 1, Task 2, Task 3 (前端依赖 API 和认证) — DONE
- Task 9 depends on Task 1, Task 2, Task 7 (Docker 构建需要前端编译产物) — DONE
- Task 4, Task 5, Task 6, Task 8 可并行执行（独立模块） — DONE
- Task 10 depends on all other tasks — DONE
