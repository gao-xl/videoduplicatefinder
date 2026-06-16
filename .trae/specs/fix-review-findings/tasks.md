# Tasks

- [ ] Task 1: 修复 ScanEngine 并发锁粒度问题 (Critical)
  - [ ] 1.1: 分析 L1187-1198 处的锁逻辑，确认 baseMembers 锁是否覆盖完整操作
  - [ ] 1.2: 如需要，扩大锁范围或使用更细粒度的锁策略
  - [ ] 1.3: 验证修复后并发测试不抛出 KeyNotFoundException

- [ ] Task 2: 确保 VideoStreamDecoder 资源释放 (Critical)
  - [ ] 2.1: 检查 VideoStreamDecoder.Dispose() 实现，确认所有原生指针正确释放
  - [ ] 2.2: 确认 ScanEngine 调用方正确使用 using 或手动 Dispose
  - [ ] 2.3: 运行长时间扫描后检查句柄泄漏

- [ ] Task 3: ScanEngine async void 异常传播 (Critical)
  - [ ] 3.1: 检查 ScanEngine 中是否还存在 async void 方法
  - [ ] 3.2: 确认 TaskScheduler.UnobservedTaskException 处理链路稳定

- [ ] Task 4: 添加端点级别速率限制 (Warning)
  - [ ] 4.1: 在 Program.cs 添加 `/scan/start` 专用限流策略（每 IP 每分钟 3 次）
  - [ ] 4.2: 验证限流生效

- [ ] Task 5: 重构 AuthEndpoints 登录请求解析 (Warning)
  - [ ] 5.1: 分析 L23-36 处重复的 JSON/form 解析逻辑
  - [ ] 5.2: 提取为辅助方法或使用 ASP.NET Core 内置内容协商
  - [ ] 5.3: 验证 JSON 和 form 请求都能正确处理

- [ ] Task 6: JwtService.ValidateToken 异常日志改进 (Warning)
  - [ ] 6.1: 在 catch 块中添加区分异常类型的日志记录
  - [ ] 6.2: 确保不泄露 token 内容

- [ ] Task 7: ScanEngine 语言服务 key 改为强类型常量 (Warning)
  - [ ] 7.1: 搜索 ScanEngine 中 LanguageService.Instance.Get 调用
  - [ ] 7.2: 创建强类型常量或 enum 替代字符串字面量

- [ ] Task 8: 移除未使用的 Antiforgery 注册 (Warning)
  - [ ] 8.1: 确认 AddAntiforgery() 未被 UseAntiforgery() 调用
  - [ ] 8.2: 移除 Program.cs 中的 AddAntiforgery() 调用

- [ ] Task 9: ScanEngine 并发比较逻辑简化评估 (Info)
  - [ ] 9.1: 评估 L1187-1241 处的三层分支逻辑是否可提取为独立方法
  - [ ] 9.2: 如评估认为值得重构，则实施；否则标记为 wontfix

- [ ] Task 10: 验证 SQLite 数据库索引覆盖 (Info)
  - [ ] 10.1: 检查 SqliteDatabase.cs 中 FileEntry.Path 等字段是否有索引
  - [ ] 10.2: 如缺少索引，添加；否则确认已有索引

# Task Dependencies
- [Task 1], [Task 2], [Task 3] 可并行（不同代码区域）
- [Task 4], [Task 5], [Task 6], [Task 7], [Task 8] 可并行（不同文件）
- [Task 9] 依赖 [Task 1] 的分析结果
- [Task 10] 独立
