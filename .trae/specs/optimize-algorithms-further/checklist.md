# 算法与性能进一步优化 — 验证清单

## 代码构建
- [ ] `dotnet build -c Release` 在 VDF.sln 下成功，零新增 warning。
- [ ] `dotnet build -c Debug` 同样成功。

## 单元测试
- [ ] `dotnet test VDF.Core.Tests --no-build -c Release` 全部通过。
- [ ] `dotnet test VDF.CLI.Tests --no-build -c Release` 全部通过（若有相关集成）。
- [ ] 新增的 `TryComparePair` 调用路径在测试中通过至少一个扫描用例。
- [ ] 新增 `QuickPHashPreFilterMulti` 早退出行为的单元测试通过：第一个位置 Hamming 高时立即返回 false。
- [ ] 新增 `PHashLSHIndex` 确定性行为的单元测试通过：两次构造相同输入返回相同候选。
- [ ] 新增 `PathPrefixMatcher` 的单元测试通过：短列表与线性扫描结果一致，长列表性能 ≥ 2x。
- [ ] 新增代表验证缓存（`repCache`）的单元测试通过：第二次对同一对组调用 MergeDuplicate 时不再调用 CheckIfDuplicate。
- [ ] 新增 `SplitDaisyChainGroups` 复用缓存的单元测试通过：仅未缓存对调用 CheckIfDuplicate。

## 集成 / 端到端测试
- [ ] 使用 `FakeDatabaseGenerator` 生成一个固定大小的假库（如 1000 条目），在"优化前 commit"与"HEAD"分别跑完整扫描与比较。
- [ ] 两次运行的重复集合（按 path 去重 + GroupId 忽略）完全一致，即 `HashSet<string>` 的 `SetEquals` 为 true。
- [ ] 若机器可访问实际媒体文件，跑一次 `VDF.Benchmarks` 中的 benchmark，耗时与优化前的 ratio 可测量。

## 性能验收
- [ ] `PHashLSHIndex.SelfTest`：recall ≥ 99%，候选集平均大小 ≤ 优化前的 80%（在相同输入下报告数值）。
- [ ] 比较阶段总耗时：在 10 万条目库下，总耗时 ≤ 优化前 85%。
- [ ] 非 pHash 模式下（灰字节路径）：比较结果与优化前完全一致（位-位相等的 duplicate 集合）。

## 代码可维护性
- [ ] `ScanForDuplicates` 方法内 LOC ≤ 优化前的 70%（可通过 `git diff --stat` 或脚本测量）。
- [ ] 预过滤逻辑（duration/fileSize/resolution/folder）仅在 `TryComparePair`（或共享方法）中出现一次。
- [ ] CompareImages / CompareVideosLinear / CompareVideosLSH / CompareEntry 方法体均为薄包装，调用共享方法。

## 确定性与可复现性
- [ ] `PHashLSHIndex` 两次构造对同一输入返回相同结果（无 `Random` 残余）。
- [ ] 基准测试在同一台机器的相同条件下可重复（±5% 范围内）。

## 向后兼容
- [ ] SQLite schema 未变更；对既有数据库运行扫描无异常。
- [ ] `Settings` JSON 格式无变化；旧配置文件直接可加载。
- [ ] CLI 入口（`VideoDuplicateFinder.CLI`）与 GUI 行为无感知变化。

## 代码风格
- [ ] 新增代码遵循项目已有 C# 风格（naming、缩进、access modifier）。
- [ ] 新方法有 XML-doc 或简要内联注释说明用途与关键分支。
- [ ] 无明显死代码、无明显调试 `Console.WriteLine` 残留。
