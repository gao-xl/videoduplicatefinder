# Tasks

- [x] Task 1: 修复登录限速器失效 (P-5)
  - [x] 1.1: 将 `Program.cs` 中 POST `/auth/login` 的 `FixedWindowRateLimiter` 从局部变量改为共享的 per-IP 固定分区限速器
  - [x] 1.2: 验证限速器在并发请求下正确共享实例

- [x] Task 2: 修复 Cookie Secure 标志 (S-3)
  - [x] 2.1: 在 `AuthService.SetAuthCookie` 中检测是否运行在 HTTPS 环境
  - [x] 2.2: HTTPS 环境下设置 `Secure = true`，HTTP 环境下记录警告日志

- [x] Task 3: 修复 Open Redirect (S-1)
  - [x] 3.1: 创建 `IsValidReturnUrl` 辅助方法，验证 URL 以 `/` 开头且不含 `://`
  - [x] 3.2: 在 `Program.cs` 行 275-276、235、293 三处 `Redirect` 调用前应用验证
  - [x] 3.3: 在 `AuthEndpoints.cs` 登录端点中应用同样的验证

- [x] Task 4: 修复 Refresh Token 永不过期 (S-4) + 登出不撤销 (S-5)
  - [x] 4.1: 将 `HashSet<string>` 替换为 `ConcurrentDictionary<string, RefreshTokenEntry>`，包含创建时间和最后使用时间
  - [x] 4.2: 在 `RefreshTokenAsync` 中检查 Token 是否过期（7 天 TTL）并更新最后使用时间
  - [x] 4.3: 实现最大会话数限制（5 个），超出时撤销最早的 Token
  - [x] 4.4: 在 `AuthEndpoints.cs` 登出端点中从集合移除 Refresh Token
  - [x] 4.5: 添加后台清理过期 Token 的逻辑

- [x] Task 5: 修复 GC 可回收 FFmpeg 回调 (Issue 1.1)
  - [x] 5.1: 将 `NativeMediaInfoExtractor` 中的 `AVIOInterruptCB_callback` 委托从局部变量提升为类字段

- [x] Task 6: 修复 SQLite 并发安全 (Issue 2.1)
  - [x] 6.1: 在 `SqliteDatabase` 中添加 `lock` 对象保护所有公共方法
  - [x] 6.2: 确保 `SaveDatabaseSqlite()` 和 `SaveFileEntries()` 在锁内执行

- [x] Task 7: 修复 async void (Issue 5.1)
  - [x] 7.1: 将 `ScanEngine.StartSearch()` 从 `async void` 改为 `async Task`
  - [x] 7.2: 将 `ScanEngine.StartCompare()` 从 `async void` 改为 `async Task`
  - [x] 7.3: 更新所有调用方以 await 返回的 Task

- [x] Task 8: 修复缩略图缓存竞态 (P-1)
  - [x] 8.1: 将 `ThumbnailEndpoints` 和 `ScanService` 中的缩略图缓存改为 `ConcurrentDictionary` + `Lazy<byte[]>` 模式
  - [x] 8.2: 移除手动的 `if (Count >= max) Clear(); TryAdd()` 模式，改用原子操作

- [x] Task 9: 修复 CORS 默认全开 (S-2)
  - [x] 9.1: 未配置 `VDF_CORS_ORIGINS` 时，仅允许同源请求并记录警告
  - [x] 9.2: 配置了 `VDF_CORS_ORIGINS` 时，使用 `WithOrigins()` 明确指定允许的来源

- [x] Task 10: 修复 FileEntry 字典并发访问 (Issue 3.1)
  - [x] 10.1: 将 `FileEntry.grayBytes` 和 `PHashes` 从 `Dictionary<double, byte[]?>` 改为 `ConcurrentDictionary<double, byte[]?>`

- [x] Task 11: 修复前端 Token 过期问题 (X1/X2)
  - [x] 11.1: 修改 `useSignalR.ts` 中 `accessTokenFactory` 为每次从 localStorage 读取最新值
  - [x] 11.2: 修改 `useSSE.ts` 中 Token 获取方式为每次读取最新值

- [x] Task 12: 修复静默 catch 块 (E-1 ~ E-5)
  - [x] 12.1: `AuthService.LoadOrGeneratePassword()` — 添加日志记录凭证文件损坏
  - [x] 12.2: `AuthService.SavePassword()` — 添加日志记录保存失败
  - [x] 12.3: `SseEndpoints.OnStateChanged` — 添加日志记录异常
  - [x] 12.4: `WebSettingsService.Load()` — 添加日志记录异常详情
  - [x] 12.5: `WebSettingsService.Save()` — 添加日志记录异常详情

- [x] Task 13: 修复密码明文日志 (S-6)
  - [x] 13.1: 移除 `AuthService` 中密码明文的 `Console.WriteLine` 和结构化日志输出

- [x] Task 14: 修复非原子递增 (Issue 3.2, 3.4, 3.5, 5.5)
  - [x] 14.1: `ScanEngine.processedFiles` — 改用 `Interlocked.Increment`
  - [x] 14.2: `FfmpegEngine._nativeConsecutiveFailures` — 改用 `Interlocked.Increment`/`Decrement`
  - [x] 14.3: `ScanEngine.IncrementProgress` 中的计数器 — 改用 `Interlocked`

- [x] Task 15: 修复 HardwareAccelerationDetector 竞态 (Issue 1.6)
  - [x] 15.1: 为 `_cachedDevices` 添加 `lock` 或使用 `Lazy<T>` 确保线程安全

# Task Dependencies
- [Task 4] 依赖 [Task 2]（Cookie 安全标志和 Token 管理相关）
- [Task 8] 与 [Task 6] 可并行（不同模块）
- [Task 10] 与 [Task 6] 可并行（不同文件）
- [Task 7] 与 [Task 14] 建议顺序执行（都涉及 ScanEngine.cs）
- [Task 12] 与 [Task 13] 建议顺序执行（都涉及 AuthService.cs）
- [Task 11] 独立于后端任务，可并行
