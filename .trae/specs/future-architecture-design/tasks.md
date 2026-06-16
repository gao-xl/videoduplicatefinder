# Tasks

## Phase 1: 基础设施

- [ ] Task 1: 提取接口定义
  - [ ] 1.1: 创建 `VDF.Core/Interfaces/` 目录
  - [ ] 1.2: 定义 `ISimilarityComparer` 接口（用于可插拔哈希算法）
  - [ ] 1.3: 定义 `IFileSystemProvider` 接口（用于云存储支持）
  - [ ] 1.4: 定义 `IMediaAnalyzer` 接口（帧采样和哈希）
  - [ ] 1.5: 定义 `IThumbnailGenerator` 接口

- [ ] Task 2: 重构 LanguageService 使用强类型 key
  - [ ] 2.1: 创建 `VDF.Core/Utils/LanguageKeys.cs` 包含所有语言 key 常量
  - [ ] 2.2: 修改 LanguageService.Get() 接受强类型参数
  - [ ] 2.3: 更新所有调用方使用强类型常量
  - [ ] 2.4: 添加编译器检查确保所有 key 存在

- [ ] Task 3: 配置集中管理
  - [ ] 3.1: 评估所有硬编码默认值
  - [ ] 3.2: 将默认值移至 Settings 类或配置文件
  - [ ] 3.3: 移除重复的配置读取逻辑

## Phase 2: 性能

- [ ] Task 4: 默认启用 Native FFmpeg
  - [ ] 4.1: 将 `UseNativeFfmpegBinding` 默认值改为 `true`
  - [ ] 4.2: 更新文档说明性能差异
  - [ ] 4.3: 添加 Process 模式作为后备的说明

- [ ] Task 5: 实现增量扫描
  - [ ] 5.1: 在数据库中记录文件修改时间
  - [ ] 5.2: 比较时跳过未变更的文件
  - [ ] 5.3: 添加 "force full scan" 选项

- [ ] Task 6: GPU 智能调度
  - [ ] 6.1: 扩展 HardwareAccelerationDetector 检测逻辑
  - [ ] 6.2: 根据 GPU 可用性自动选择最佳路径
  - [ ] 6.3: 添加日志说明选择的解码路径

## Phase 3: 扩展性

- [ ] Task 7: 插件系统
  - [ ] 7.1: 设计插件加载机制（MEF 或自定义）
  - [ ] 7.2: 定义插件接口（`ISimilarityPlugin`, `IStoragePlugin`）
  - [ ] 7.3: 实现插件注册和发现
  - [ ] 7.4: 添加示例插件

- [ ] Task 8: 云存储支持（可选）
  - [ ] 8.1: 实现 S3 文件系统 provider
  - [ ] 8.2: 实现 Azure Blob 文件系统 provider
  - [ ] 8.3: 测试远程文件访问性能

## Phase 4: 用户体验

- [ ] Task 9: 批量操作
  - [ ] 9.1: 添加批量删除 API
  - [ ] 9.2: 添加批量移动/归档 API
  - [ ] 9.3: 前端实现批量选择 UI
  - [ ] 9.4: 添加撤销支持

- [ ] Task 10: 结果预览
  - [ ] 10.1: 添加视频内联播放器组件
  - [ ] 10.2: 实现缩略图点击播放
  - [ ] 10.3: 添加全屏预览模式

- [ ] Task 11: PWA 支持
  - [ ] 11.1: 添加 Web App Manifest
  - [ ] 11.2: 实现 Service Worker
  - [ ] 11.3: 添加离线扫描结果查看

# Task Dependencies
- [Task 1] → [Task 2] (LanguageService 重构依赖接口定义)
- [Task 1] → [Task 7] (插件系统依赖接口定义)
- [Task 4] 可独立进行
- [Task 5] 依赖 Task 4
- [Task 6] 可独立进行
- [Task 8] 依赖 Task 1
- [Task 9] 依赖 Task 1
- [Task 10] 可独立进行
- [Task 11] 依赖 Task 9
