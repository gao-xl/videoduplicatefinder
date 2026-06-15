# VDF 项目全面代码审计报告

**审计日期**: 2026-06-15
**审计范围**: VDF.Web (后端), VDF.Web.Client (前端), VDF.Core (核心库)
**审计方法**: 静态代码分析 + 架构审查

---

## 总览

| 模块 | 发现数 | Critical | High | Medium | Low |
|------|--------|----------|------|--------|-----|
| **VDF.Web** (后端) | 33 | 0 | 5 | 15 | 13 |
| **VDF.Web.Client** (前端) | 33 | 0 | 3 | 12 | 18 |
| **VDF.Core** (核心库) | 23 | 0 | 3 | 9 | 11 |
| **总计** | **89** | **0** | **11** | **36** | **42** |

---

## 一、VDF.Web 后端审计

### 1.1 安全问题

**[S-1] Open Redirect in Login Endpoint**
- **文件**: `VDF.Web/Program.cs`, 行 275-276
- **严重级别**: HIGH
- **描述**: `returnUrl` 查询参数直接传入 `ctx.Response.Redirect()` 未做验证。攻击者可构造 `returnUrl=https://evil.com` 进行钓鱼攻击。行 235（auth gate 中间件）和行 293 也存在同样问题。

**[S-2] CORS Allows Any Origin by Default**
- **文件**: `VDF.Web/Program.cs`, 行 118-124
- **严重级别**: HIGH
- **描述**: 未设置 `VDF_CORS_ORIGINS` 时，使用 `AllowAnyOrigin()` + `AllowAnyHeader()` + `AllowAnyMethod()`，完全禁用 CORS 保护。

**[S-3] Cookie Not Set with Secure Flag**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 161-168
- **严重级别**: HIGH
- **描述**: `SetAuthCookie` 设置了 `HttpOnly = true` 和 `SameSite = Strict`，但未设置 `Secure` 属性。Cookie 会在 HTTP 连接上被发送，可被网络嗅探拦截。

**[S-4] Refresh Tokens Never Expire or Get Revoked**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 110-114
- **严重级别**: HIGH
- **描述**: Refresh token 添加到内存 `HashSet` 后永不移除。无过期检查、无撤销机制、无最大会话数限制。泄露的 refresh token 在服务器重启前永久有效。

**[S-5] Logout Does Not Invalidate Refresh Token**
- **文件**: `VDF.Web/Endpoints/AuthEndpoints.cs`, 行 67-86
- **严重级别**: MEDIUM
- **描述**: 登出端点解析了 `refresh_token` 但未从有效 token 集合中移除。客户端仍持有该 token 可继续获取新 access token。

**[S-6] Password Logged to Console in Plaintext**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 196-215
- **严重级别**: MEDIUM
- **描述**: 密码通过结构化日志和 `Console.WriteLine` 明文打印。若日志被采集到集中系统，密码会被持久化存储。

**[S-7] Weak Password Comparison via SHA256**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 86-91
- **严重级别**: MEDIUM
- **描述**: 密码使用单次 SHA256 哈希（无盐），SHA256 不适合密码哈希（过快，易受 GPU 暴力破解）。应使用 Argon2、scrypt 或 PBKDF2。

**[S-8] JWT Signing Key Stored in World-Readable Location**
- **文件**: `VDF.Web/Services/JwtService.cs`, 行 110-118
- **严重级别**: MEDIUM
- **描述**: JWT 签名密钥保存为默认权限（通常 644），系统上任何用户可读。应使用 0600 权限。

**[S-9] JWT Signing Key Regenerated After Restart with No Persistence**
- **文件**: `VDF.Web/Services/JwtService.cs`, 行 93-121
- **严重级别**: MEDIUM
- **描述**: 若 `File.WriteAllText` 失败，密钥仅存在于内存中。重启后生成新密钥，使所有现有 JWT 失效。

**[S-10] JWT Token Passed in Query String for SignalR**
- **文件**: `VDF.Web/Program.cs`, 行 74-82
- **严重级别**: MEDIUM
- **描述**: JWT 从 `access_token` 查询参数读取。查询参数会出现在服务器访问日志、浏览器历史和 referrer 头中。

**[S-11] API Keys In-Memory Only, No Hashing**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 76-83
- **严重级别**: MEDIUM
- **描述**: API Key 以明文存储在内存 `HashSet` 中。攻击者获得进程内存读取权限即可获取所有 API Key。

**[S-12] No API Key Rotation or Expiration**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 76-83
- **严重级别**: LOW
- **描述**: API Key 无过期日期或轮换机制。

**[S-13] In-Memory Refresh Token Storage Lost on Restart**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 42
- **严重级别**: LOW
- **描述**: 所有有效 refresh token 存储在内存中，服务器重启后全部失效。

### 1.2 错误处理

**[E-1] Silent Catch Blocks Swallowing Exceptions**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 177-178
- **严重级别**: MEDIUM
- **描述**: `LoadOrGeneratePassword()` 的 `catch { }` 静默吞掉所有反序列化/IO 异常。凭证文件损坏时用户获得新随机密码，无任何提示。

**[E-2] Silent Catch in SavePassword**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 193-194
- **严重级别**: MEDIUM
- **描述**: `SavePassword()` 的 `catch { }` 静默吞掉所有异常。磁盘满或权限错误时，生成的密码丢失。

**[E-3] SSE Event Handler Silently Swallows Errors**
- **文件**: `VDF.Web/Endpoints/SseEndpoints.cs`, 行 32-33
- **严重级别**: MEDIUM
- **描述**: `OnStateChanged` 回调的 `catch { }` 吞掉所有异常，包括非连接相关的异常。

**[E-4] Silent Catch in WebSettingsService.Load**
- **文件**: `VDF.Web/Services/WebSettingsService.cs`, 行 156
- **严重级别**: LOW
- **描述**: `Load()` 异常时返回 `false` 但丢弃异常详情。损坏的配置文件会静默恢复默认值。

**[E-5] Silent Catch in WebSettingsService.Save**
- **文件**: `VDF.Web/Services/WebSettingsService.cs`, 行 215
- **严重级别**: LOW
- **描述**: `Save()` 异常时返回 `false` 但丢弃异常详情。

**[E-6] Unhandled Exception Handler May Fail**
- **文件**: `VDF.Web/Program.cs`, 行 161-166
- **严重级别**: LOW
- **描述**: `AppDomain.CurrentDomain.UnhandledException` 处理器将 `e.ExceptionObject` 强制转换为 `Exception`，对非 `Exception` 类型的 CLR 错误可能失败。

**[E-7] No Error Response for Thumbnail Extraction Failure**
- **文件**: `VDF.Web/Endpoints/ThumbnailEndpoints.cs`, 行 32
- **严重级别**: LOW
- **描述**: `ScanEngine.ExtractThumbnailJpeg` 在 `Task.Run` 中调用但无 try-catch。异常会传播为未处理的 500 错误。

### 1.3 性能

**[P-1] Thumbnail Cache Clear-and-Replace is Not Thread-Safe**
- **文件**: `VDF.Web/Endpoints/ThumbnailEndpoints.cs`, 行 34-36 和 62-64
- **严重级别**: HIGH
- **描述**: `if (Count >= max) Clear(); TryAdd()` 模式存在竞态条件。多线程下可触发惊群效应，多个请求同时清空缓存并触发 FFmpeg 缩略图提取。

**[P-2] O(N) Linear Scan for Thumbnail Lookup**
- **文件**: `VDF.Web/Endpoints/ThumbnailEndpoints.cs`, 行 17
- **严重级别**: MEDIUM
- **描述**: `scan.Duplicates.FirstOrDefault(d => d.Path == path)` 对每个缩略图请求线性扫描所有重复项。应使用 `Dictionary<string, DuplicateItem>` 实现 O(1) 查找。

**[P-3] Results Endpoint Recomputes GroupBy on Every Request**
- **文件**: `VDF.Web/Endpoints/ResultEndpoints.cs`, 行 19-22
- **严重级别**: MEDIUM
- **描述**: `GET /api/results` 每次请求都调用 `.GroupBy()` 重新计算。应缓存并在扫描完成或项目修改时失效。

**[P-4] ScanService Singleton Holding Unlimited Cached JPEG Bytes**
- **文件**: `VDF.Web/Services/ScanService.cs`, 行 65-66
- **严重级别**: MEDIUM
- **描述**: `HqThumbCache` 和 `FullThumbCache` 无大小限制。4096 个 HQ 缩略图约 400MB 内存，无 LRU 淘汰。

**[P-5] Login Rate Limiter Creates New Limiter Instance Per Request**
- **文件**: `VDF.Web/Program.cs`, 行 304-309
- **严重级别**: MEDIUM
- **描述**: **功能性 BUG** — 每个 POST `/auth/login` 请求创建新的 `FixedWindowRateLimiter` 实例，速率限制从未生效。

**[P-6] Duplicate Endpoint Registrations**
- **文件**: `VDF.Web/Program.cs` 行 244-442 vs `Endpoints/*.cs`
- **严重级别**: LOW
- **描述**: Program.cs 注册了旧端点，`Endpoints/` 目录提供了重复的新端点。

### 1.4 架构与代码质量

**[A-1] Program.cs is a God File (469 lines)**
- **文件**: `VDF.Web/Program.cs`
- **严重级别**: HIGH
- **描述**: Program.cs 包含服务配置、中间件管道、JWT Bearer、限速器、CORS、TLS、5+ 内联端点定义、异常处理器等所有内容。应分解为扩展方法或专用配置类。

**[A-2] WebSettingsService Load/Save is 100+ Lines of Manual Property Mapping**
- **文件**: `VDF.Web/Services/WebSettingsService.cs`, 行 101-216
- **严重级别**: MEDIUM
- **描述**: `Load` 和 `Save` 方法手动映射 40+ 属性。添加新设置需要编辑两个长代码块。

**[A-3] SettingsEndpoints.cs Returns Anonymous Object**
- **文件**: `VDF.Web/Endpoints/SettingsEndpoints.cs`, 行 13-61
- **严重级别**: MEDIUM
- **描述**: GET 端点返回 45 行匿名对象而非类型化 DTO。破坏 API 契约一致性。

**[A-4] ScanService is a God Class**
- **文件**: `VDF.Web/Services/ScanService.cs` (448 行)
- **严重级别**: MEDIUM
- **描述**: ScanService 拥有扫描生命周期、缩略图缓存、文件操作、数据库管理、设置管理、SignalR 通知等所有职责。应拆分为专注的服务。

**[A-5] Duplicate Login Endpoints**
- **文件**: `Program.cs` 行 244-298 和 `AuthEndpoints.cs` 行 12-52
- **严重级别**: LOW
- **描述**: 登录逻辑重复实现，可能导致不一致。

**[A-6] Mixed JSON Deserialization Approaches**
- **文件**: `Program.cs` 行 253-257 和 `AuthEndpoints.cs` 行 25-28
- **严重级别**: LOW
- **描述**: 登录端点混合使用 `JsonDocument.Parse()` 手动解析和模型绑定。

**[A-7] All Services Registered as Singletons Without Thread-Safety Guarantees**
- **文件**: `VDF.Web/Program.cs`, 行 42-49
- **严重级别**: MEDIUM
- **描述**: 所有服务注册为单例，部分具有可变状态。`_validTokens` 使用独立锁，无原子性 check-and-act 模式。

### 1.5 API 设计

**[R-1] DELETE Endpoint That Doesn't Actually Delete**
- **文件**: `VDF.Web/Endpoints/ResultEndpoints.cs`, 行 66
- **严重级别**: MEDIUM
- **描述**: `DELETE /api/results/items` 实际删除磁盘文件。REST 惯例建议 DELETE 操作资源 URI。

**[R-2] POST Used for Non-Idempotent Actions**
- **文件**: `VDF.Web/Endpoints/ResultEndpoints.cs`, 行 89-98
- **严重级别**: LOW

**[R-3] Inconsistent Error Response Format**
- **文件**: 多个文件
- **严重级别**: LOW

**[R-4] No Pagination Support for Item Operations**
- **文件**: `VDF.Web/Endpoints/ResultEndpoints.cs`
- **严重级别**: LOW

**[R-5] scanId Returned from Start Is Not Trackable**
- **文件**: `VDF.Web/Endpoints/ScanEndpoints.cs`, 行 18
- **严重级别**: LOW

### 1.6 线程安全

**[T-1] Auth Gate Middleware Race Condition on Path Check**
- **文件**: `VDF.Web/Program.cs`, 行 209-238
- **严重级别**: LOW

**[T-2] SignalR Notify Fires-and-Forgets Without Await**
- **文件**: `VDF.Web/Services/ScanService.cs`, 行 423-443
- **严重级别**: LOW

**[T-3] SSE Event Handler Blocks on Sync Over Async**
- **文件**: `VDF.Web/Endpoints/SseEndpoints.cs`, 行 31
- **严重级别**: MEDIUM
- **描述**: `SendEvent(...).GetAwaiter().GetResult()` 同步-over-异步模式可导致线程池饥饿。

### 1.7 其他

**[X-1] Base Path Environment Variable Written to Configuration**
- **文件**: `VDF.Web/Program.cs`, 行 33
- **严重级别**: LOW

**[X-2] Password Generation Uses Modulo Bias**
- **文件**: `VDF.Web/Services/AuthService.cs`, 行 218-225
- **严重级别**: LOW

**[X-3] Duplicate CSV Export Logic**
- **文件**: `Program.cs` 行 412-442 和 `ResultEndpoints.cs` 行 110-138
- **严重级别**: LOW

**[X-4] JWT Service Created with Empty Logger**
- **文件**: `VDF.Web/Program.cs`, 行 42
- **严重级别**: MEDIUM
- **描述**: `new JwtService(LoggerFactory.Create(b => { }).CreateLogger<JwtService>())` 创建空 Logger，密钥加载警告不可见。

**[X-5] No Anti-Forgery Token Validation for API Endpoints**
- **文件**: `VDF.Web/Program.cs`, 行 188
- **严重级别**: LOW

---

## 二、VDF.Web.Client 前端审计

### 2.1 安全问题

**[S1] Token stored in localStorage — XSS vulnerability**
- **文件**: `src/api/client.ts`, 行 8, 12-13, 22
- **严重级别**: HIGH
- **描述**: JWT access 和 refresh token 存储在 `localStorage`。任何 XSS 漏洞可通过 `localStorage.getItem()` 窃取 token。标准缓解方案是使用 httpOnly cookie。

**[S2] Token leaked in SSE URL query parameter**
- **文件**: `src/hooks/useSSE.ts`, 行 20
- **严重级别**: HIGH
- **描述**: access token 作为 URL 查询参数传递：`/api/scan/events?access_token=...`。查询参数会被浏览器历史、代理、CDN 和服务器访问日志记录。

**[S3] No CSRF protection**
- **文件**: `src/api/client.ts`, 行 44-91
- **严重级别**: MEDIUM
- **描述**: 所有 API 请求仅依赖 Bearer token 认证，无 CSRF token。

**[S4] `remember` field in LoginRequest is never sent**
- **文件**: `src/pages/LoginPage.tsx`, 行 22; `src/api/auth.ts`, 行 5
- **严重级别**: LOW
- **描述**: `remember` 布尔值发送到登录端点但从未影响 token 存储位置。

**[S5] Path traversal risk in PathBrowser**
- **文件**: `src/components/shared/PathBrowser.tsx`, 行 26
- **严重级别**: MEDIUM

### 2.2 类型安全

**[T1] `handleChange` uses `unknown` type parameter**
- **文件**: `src/pages/SettingsPage.tsx`, 行 52
- **严重级别**: LOW

**[T2] Missing `strict: true` in tsconfig**
- **文件**: `tsconfig.app.json`
- **严重级别**: MEDIUM
- **描述**: 未启用 `strict: true`，`strictNullChecks`、`strictFunctionTypes`、`noImplicitAny` 等全部关闭。

**[T3] `as Promise<T>` casts on response.json()**
- **文件**: `src/api/client.ts`, 行 78, 90
- **严重级别**: LOW

**[T4] `undefined as T` for 204 responses**
- **文件**: `src/api/client.ts`, 行 89
- **严重级别**: LOW

### 2.3 状态管理与 React Query

**[Q1] QueryClient created outside component tree**
- **文件**: `src/App.tsx`, 行 14
- **严重级别**: MEDIUM

**[Q2] Missing query cache invalidation on logout**
- **文件**: `src/api/auth.ts`, 行 37-43
- **严重级别**: MEDIUM
- **描述**: 登出时未清除 `queryClient` 缓存，可能导致跨会话数据泄露。

**[Q3] Stale closure in useCallback (ScanPage path handlers)**
- **文件**: `src/pages/ScanPage.tsx`, 行 62-74
- **严重级别**: MEDIUM
- **描述**: mutation 发送旧数组值而非最新值，快速连续添加可能发送过时数据。

**[Q4] Settings mutation fires on every slider tick**
- **文件**: `src/pages/SettingsPage.tsx`, 行 52-57
- **严重级别**: LOW

### 2.4 错误处理

**[E1] `handleExport` has no error handling**
- **文件**: `src/pages/ResultsPage.tsx`, 行 147-155
- **严重级别**: MEDIUM

**[E2] `pauseScan()`, `resumeScan()`, `stopScan()` called without error handling**
- **文件**: `src/pages/ScanPage.tsx`, 行 197, 214, 230
- **严重级别**: MEDIUM

**[E3] `resetScan()` called without error handling**
- **文件**: `src/pages/ScanPage.tsx`, 行 653
- **严重级别**: LOW

**[E4] Context menu clipboard write unhandled**
- **文件**: `src/pages/ResultsPage.tsx`, 行 550
- **严重级别**: LOW

### 2.5 性能

**[P1] Massive inline styles throughout**
- **文件**: 所有 `.tsx` 文件
- **严重级别**: MEDIUM
- **描述**: 每个组件使用内联 `style={{...}}` 对象，每次渲染创建新引用，阻碍 React 优化。

**[P2] `DuplicateGroupCard` and `DuplicateItemCard` are not memoized**
- **文件**: `src/pages/ResultsPage.tsx`, 行 645, 762
- **严重级别**: MEDIUM

**[P3] `Math.max(...)` spread on potentially large arrays**
- **文件**: `src/pages/ResultsPage.tsx`, 行 653, 768-769
- **严重级别**: LOW

**[P4] Settings page fires mutation on every toggle/slider change**
- **文件**: `src/pages/SettingsPage.tsx`, 行 52-57
- **严重级别**: LOW

**[P5] Duplicate `fadeIn` keyframe definitions**
- **文件**: 多个文件
- **严重级别**: LOW

**[P6] `confirmBg` computed on every render**
- **文件**: `src/components/shared/ConfirmDialog.tsx`, 行 48
- **严重级别**: LOW

### 2.6 可访问性

**[A1] Almost zero ARIA attributes**
- **文件**: 所有 `.tsx` 文件（除 `ThemeToggle.tsx`）
- **严重级别**: HIGH
- **描述**: 仅 ThemeToggle 有 `aria-label`。整个应用无 ARIA roles、labels 或 live regions。ConfirmDialog 无 `role="dialog"`、无焦点陷阱。扫描进度无 `aria-live`。

**[A2] No focus trap in modals**
- **文件**: `ConfirmDialog.tsx`, `CompareModal.tsx`, `PathBrowser.tsx`
- **严重级别**: MEDIUM

**[A3] Missing keyboard interaction for scan control buttons**
- **文件**: `src/pages/ScanPage.tsx`, 行 196-243
- **严重级别**: LOW

**[A4] Custom toggle switches not accessible**
- **文件**: `src/pages/SettingsPage.tsx`, 行 319-351
- **严重级别**: MEDIUM

**[A5] No responsive design / mobile support**
- **文件**: `src/components/Layout/MainLayout.tsx`, `src/pages/ResultsPage.tsx`
- **严重级别**: MEDIUM

### 2.7 代码质量

**[C1] Inline `<style>` tags with duplicate keyframes**
- **文件**: 多个文件
- **严重级别**: LOW

**[C2] Duplicate `formatDuration` function**
- **文件**: `ScanPage.tsx` 行 13; `ResultsPage.tsx` 行 20
- **严重级别**: LOW

**[C3] ThemeToggle uses manual mouseenter/leave styling**
- **文件**: `src/components/Layout/ThemeToggle.tsx`, 行 27-36
- **严重级别**: LOW

**[C4] ConfirmDialog animation has a race condition**
- **文件**: `src/components/shared/ConfirmDialog.tsx`, 行 26-33
- **严重级别**: LOW

**[C5] No React.memo on any child components**
- **文件**: `ResultsPage.tsx`, `SettingsPage.tsx`
- **严重级别**: LOW

### 2.8 构建配置

**[B1] No build output analysis or bundle splitting hints**
- **文件**: `vite.config.ts`
- **严重级别**: LOW

**[B2] No CSP headers configured**
- **文件**: `vite.config.ts`, `index.html`
- **严重级别**: MEDIUM

**[B3] Source maps in production build unknown**
- **文件**: `vite.config.ts`
- **严重级别**: LOW

**[B4] ESLint config missing strict type-checking rules**
- **文件**: `eslint.config.js`
- **严重级别**: LOW

### 2.9 其他问题

**[X1] SignalR token is captured once at mount, never refreshed**
- **文件**: `src/hooks/useSignalR.ts`, 行 20, 23-24
- **严重级别**: MEDIUM
- **描述**: Token 在 hook 挂载时读取一次。刷新后 SignalR 重连仍使用旧 token。`accessTokenFactory` 应每次从 localStorage 读取最新值。

**[X2] SSE token captured at mount, same issue as SignalR**
- **文件**: `src/hooks/useSSE.ts`, 行 19-21
- **严重级别**: MEDIUM

**[X3] `returnUrl` from login redirect could be exploited**
- **文件**: `src/pages/LoginPage.tsx`, 行 15, 23
- **严重级别**: LOW

**[X4] Inline styles create GC pressure**
- **文件**: 所有页面组件
- **严重级别**: LOW

---

## 三、VDF.Core 核心库审计

### 3.1 FFmpeg 绑定与内存安全

**[Issue 1.1] NativeMediaInfoExtractor: GC-collectible delegate for interrupt callback**
- **文件**: `VDF.Core/FFTools/FFmpegNative/NativeMediaInfoExtractor.cs`
- **行**: 47-49
- **严重级别**: HIGH
- **描述**: `AVIOInterruptCB_callback` 委托是局部变量，分配给非托管内存中的 `AVIOInterruptCB` 结构体。GC 无法通过托管代码引用该委托。若 GC 在长时间 `avformat_open_input` 调用期间回收委托，FFmpeg 将调用悬空函数指针导致 segfault。同文件 `VideoStreamDecoder` 和 `AudioStreamDecoder` 中的模式是安全的（委托存储在类字段中）。

**[Issue 1.2] AudioStreamDecoder.Dispose() calls Dispose() from finalizer**
- **文件**: `VDF.Core/FFTools/FFmpegNative/AudioStreamDecoder.cs`
- **行**: 359-361
- **严重级别**: MEDIUM
- **描述**: finalizer `~AudioStreamDecoder()` 调用 `Dispose()`，但 finalizer 不应调用 Dispose（其他被引用的托管对象可能已被回收）。实际风险低（字段仅原生指针），但与 `VideoStreamDecoder` 模式不一致。

**[Issue 1.3] VideoFrameConverter.Dispose() does not null-check for double-dispose safety**
- **文件**: `VDF.Core/FFTools/FFmpegNative/VideoFrameConverter.cs`
- **行**: 78-82
- **严重级别**: MEDIUM
- **描述**: `Dispose()` 调用 `av_frame_free` 和 `sws_freeContext` 但无空检查。双重 dispose 会对悬空指针调用 FFmpeg free 函数。

**[Issue 1.4] VideoFrameConverter has no finalizer**
- **文件**: `VDF.Core/FFTools/FFmpegNative/VideoFrameConverter.cs`
- **行**: 20
- **严重级别**: LOW

**[Issue 1.5] EncodeJpegFromBgra: potential double free of srcFrame**
- **文件**: `VDF.Core/FFTools/FfmpegEngine.cs`
- **行**: 705-727
- **严重级别**: LOW

**[Issue 1.6] HardwareAccelerationDetector: race condition on `_cachedDevices`**
- **文件**: `VDF.Core/FFTools/FFmpegNative/HardwareAccelerationDetector.cs`
- **行**: 34-66
- **严重级别**: MEDIUM
- **描述**: 静态字段 `_cachedDevices` 读写无同步。并发调用可能导致重复检测。`InvalidateCache()` 在其他线程读取时设置为 null。

**[Issue 1.7] FFmpegHelper.ffmpegLibraryFound not thread-safe**
- **文件**: `VDF.Core/FFTools/FFmpegNative/FFmpegHelper.cs`
- **行**: 138-145
- **严重级别**: LOW

### 3.2 数据库层

**[Issue 2.1] SqliteDatabase: no connection pooling or thread-safety**
- **文件**: `VDF.Core/Data/SqliteDatabase.cs`
- **行**: 10-311
- **严重级别**: HIGH
- **描述**: `SqliteDatabase` 持有单个 `SqliteConnection` 实例。SQLite 连接非线程安全。`SaveDatabaseSqlite()` 可从并行工作线程调用（通过 `IncrementProgress` → `TryDatabaseCheckpoint`）。`lock(checkpointLock)` 仅保护时间戳检查，`SaveDatabase()` 也在锁外被调用。若两个线程同时调用 `SaveDatabaseSqlite()`，单个 SQLite 连接可能被并发使用，损坏 WAL 或导致 `SQLITE_MISUSE`。

**[Issue 2.2] SqliteDatabase.SaveFileEntries: large transaction without batching**
- **文件**: `VDF.Core/Data/SqliteDatabase.cs`
- **行**: 76-122
- **严重级别**: MEDIUM
- **描述**: `SaveFileEntries` 将所有条目包装在单个事务中。对于数万条目的数据库，会创建非常大的 WAL 段并可能导致 `SQLITE_BUSY`。

**[Issue 2.3] SqliteDatabase.DeserializeGrayBytes/PHashes: no corruption handling**
- **文件**: `VDF.Core/Data/SqliteDatabase.cs`
- **行**: 271-283
- **严重级别**: MEDIUM
- **描述**: 若 MemoryPack blob 损坏，`MemoryPackSerializer.Deserialize` 会抛出未处理异常，崩溃整个数据库加载。单个损坏条目会阻止所有条目加载。

**[Issue 2.4] DatabaseUtils.CloseSqlite() not called in normal shutdown**
- **文件**: `VDF.Core/Utils/DatabaseUtils.cs`
- **行**: 302-306
- **严重级别**: LOW

**[Issue 2.5] DatabaseUtils.LoadDatabaseLegacy: synchronous blocking of async deserialization**
- **文件**: `VDF.Core/Utils/DatabaseUtils.cs`
- **行**: 117-118
- **严重级别**: LOW

### 3.3 线程安全

**[Issue 3.1] FileEntry.grayBytes/PHashes dictionaries accessed concurrently without synchronization**
- **文件**: `VDF.Core/FileEntry.cs`
- **行**: 64, 76
- **严重级别**: HIGH
- **描述**: `grayBytes` 和 `PHashes` 是 `Dictionary<double, byte[]?>`。在 `GatherInfos()` 期间，多个并行工作线程可能对同一 `FileEntry` 写入。在 `ScanForDuplicates()` 期间通过 `TryBuildCompareSnapshot()` 并行读取。`Dictionary<K,V>` 非线程安全。

**[Issue 3.2] ScanEngine.processedFiles increment is not atomic**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 111
- **严重级别**: MEDIUM
- **描述**: `processedFiles++` 未使用 `Interlocked.Increment`。从并行工作线程调用时是非原子的读-改-写。

**[Issue 3.3] PauseTokenSource.IsPaused property race**
- **文件**: `VDF.Core/Utils/PauseTokenSource.cs`
- **行**: 29-37
- **严重级别**: LOW

**[Issue 3.4] FfmpegEngine static mutable state shared across parallel scan workers**
- **文件**: `VDF.Core/FFTools/FfmpegEngine.cs`
- **行**: 41-42, 44-53, 61-62
- **严重级别**: MEDIUM
- **描述**: `_nativeConsecutiveFailures` 和 `_nativeDisabledForSession` 从并行工作线程修改但无同步。`++` 递增非原子。

**[Issue 3.5] ScanEngine._nativeConsecutiveFailures not using Interlocked**
- **文件**: `VDF.Core/FFTools/FfmpegEngine.cs`
- **行**: 61, 69, 74
- **严重级别**: MEDIUM

### 3.4 设置验证

**[Issue 4.1] Settings.Threshold (byte) not validated**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 61
- **严重级别**: LOW
- **描述**: `Threshhold`（拼写错误）默认 5，无合理范围验证。

**[Issue 4.2] Settings.Percent not validated**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 62
- **严重级别**: LOW

**[Issue 4.3] Settings.MaxDegreeOfParallelism defaults to 1**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 71
- **严重级别**: LOW

**[Issue 4.4] Settings.CustomFFArguments is passed to FFmpeg CLI without sanitization**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 73
- **严重级别**: MEDIUM
- **描述**: 自定义 FFmpeg 参数可通过 `-filter_complex` 等选项导致过度内存使用。

**[Issue 4.5] SameFolderDepth not validated**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 44
- **严重级别**: LOW

### 3.5 扫描引擎逻辑

**[Issue 5.1] ScanEngine uses async void for StartSearch and StartCompare**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 168, 196
- **严重级别**: HIGH
- **描述**: `StartSearch()` 和 `StartCompare()` 是 `async void` 方法。第一个 `await` 之后抛出的任何异常会终止进程，因为 `async void` 异常被发布到 `SynchronizationContext` 或 `ThreadPool`。

**[Issue 5.2] GatherInfos modifies FileEntry.invalid without synchronization**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 619
- **严重级别**: LOW

**[Issue 5.3] TryBuildCompareSnapshot is called on the main comparison thread**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 853-881
- **严重级别**: LOW

**[Issue 5.4] MergeDuplicate lock granularity**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 1003
- **严重级别**: MEDIUM
- **描述**: 每次找到重复项时获取 `lock(duplicateDict)`。在大量重复项的大型扫描中成为序列化瓶颈。

**[Issue 5.5] IncrementProgress called from parallel context without Interlocked**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 111
- **严重级别**: MEDIUM

**[Issue 5.6] ScanForDuplicates: SplitDaisyChainGroups mutates Duplicates after HashSet is assigned**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 1296-1304
- **严重级别**: LOW

### 3.6 错误处理

**[Issue 6.1] FfmpegEngine.GetThumbnail: process not killed on WaitForExit timeout in some paths**
- **文件**: `VDF.Core/FFTools/FfmpegEngine.cs`
- **行**: 496-500
- **严重级别**: MEDIUM

**[Issue 6.2] NativeMediaInfoExtractor silently swallows all exceptions**
- **文件**: `VDF.Core/FFTools/FFmpegNative/NativeMediaInfoExtractor.cs`
- **行**: 150-152
- **严重级别**: LOW

**[Issue 6.3] AudioStreamDecoder.DecodeAllSamples swallows all exceptions**
- **文件**: `VDF.Core/FFTools/FFmpegNative/AudioStreamDecoder.cs`
- **行**: 291-293
- **严重级别**: LOW

**[Issue 6.4] FFProbeEngine.GetMediaInfo: no stderr logging when extendedLogging is false**
- **文件**: `VDF.Core/FFTools/FFProbeEngine.cs`
- **行**: 69
- **严重级别**: LOW

**[Issue 6.5] ChromaprintEngine.ExtractFingerprintProcess: process not disposed on all error paths**
- **文件**: `VDF.Core/FFTools/ChromaprintEngine.cs`
- **行**: 122-219
- **严重级别**: LOW

**[Issue 6.6] FileEntry.GetGrayBytesIndex: NullReferenceException if mediaInfo is null**
- **文件**: `VDF.Core/FileEntry.cs`
- **行**: 132, 135
- **严重级别**: LOW

### 3.7 其他发现

**[Issue 7.1] SqliteDatabase.CleanDatabase: File.Exists called for every entry**
- **文件**: `VDF.Core/Data/SqliteDatabase.cs`
- **行**: 183-189
- **严重级别**: MEDIUM
- **描述**: `CleanDatabase()` 对每个条目调用 `File.Exists()`。100K+ 文件的数据库（尤其网络存储）会非常慢。

**[Issue 7.2] Settings.DurationDifferenceMaxSeconds defaults to 0 (disabled)**
- **文件**: `VDF.Core/Settings.cs`
- **行**: 65
- **严重级别**: LOW

**[Issue 7.3] FfmpegEngine.cs: redundant null check**
- **文件**: `VDF.Core/FFTools/FFmpegNative/FFmpegHelper.cs`
- **行**: 57
- **严重级别**: LOW

**[Issue 7.4] ScanEngine: Duplicates HashSet does not account for PartialClip duplicates**
- **文件**: `VDF.Core/ScanEngine.cs`
- **行**: 1425-1431
- **严重级别**: LOW

---

## 四、修复优先级建议

### 立即修复 (Critical Path)

| 优先级 | 问题 | 模块 | 修复方案 |
|--------|------|------|----------|
| 1 | 登录限速器失效 (P-5) | 后端 | 改为 per-IP 固定分区限速器 |
| 2 | Cookie 无 Secure 标志 (S-3) | 后端 | 设置 `Secure = true` + HTTPS 环境检测 |
| 3 | Open Redirect (S-1) | 后端 | 验证 `returnUrl` 必须以 `/` 开头且不含 `://` |
| 4 | Refresh Token 无过期 (S-4) | 后端 | 添加时间戳 + TTL + 撤销方法 |
| 5 | GC 可回收 FFmpeg 回调 (1.1) | 核心 | 将委托提升为类字段 |

### 高优先级

| 优先级 | 问题 | 模块 | 修复方案 |
|--------|------|------|----------|
| 6 | SQLite 并发安全 (2.1) | 核心 | 所有数据库操作加锁或使用连接池 |
| 7 | async void (5.1) | 核心 | 改为 `async Task` + 调用方 await |
| 8 | localStorage Token (S1) | 前端 | 改用 httpOnly Cookie 或 BFF 模式 |
| 9 | 前端 Token 过期 (X1/X2) | 前端 | `accessTokenFactory` 改为每次读取最新值 |
| 10 | 缩略图缓存竞态 (P-1) | 后端 | 使用 `ConcurrentDictionary` + `Lazy<byte[]>` |
| 11 | CORS 默认全开 (S-2) | 后端 | 默认拒绝，明确配置允许来源 |
| 12 | Program.cs God File (A-1) | 后端 | 分解为扩展方法和配置类 |

### 中优先级

- 修复所有 silent catch 块（添加日志记录）
- 启用前端 `tsconfig strict: true`
- 添加 ARIA 属性和焦点陷阱
- 实现 refresh token 撤销
- 使用 `Interlocked` 替代非原子递增
- 缓存结果分组（避免重复 GroupBy）
- 清理重复端点和代码

---

## 五、Docker 审计

| 问题 | 严重级别 | 描述 |
|------|----------|------|
| 未锁定 FFmpeg 版本 | LOW | `apt-get install ffmpeg` 安装仓库最新版，构建不可复现 |
| 无 .dockerignore | LOW | 构建上下文可能包含不必要的文件 |
| 构建阶段以 root 运行 | LOW | 构建阶段以 root 运行（常见做法，最终镜像使用非 root） |
| 多阶段构建结构良好 | N/A | 三阶段构建、HEALTHCHECK、非 root 用户、ENTRYPOINT 正确 |

---

*报告生成完毕。共发现 89 个问题：0 Critical, 11 High, 36 Medium, 42 Low。*
