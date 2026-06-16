## Phase 1: 基础设施

- [ ] 接口定义完整，包含 ISimilarityComparer, IFileSystemProvider, IMediaAnalyzer, IThumbnailGenerator
- [ ] LanguageService 使用强类型 key，编译器检查生效
- [ ] 所有硬编码默认值移至配置，无重复读取逻辑

## Phase 2: 性能

- [ ] UseNativeFfmpegBinding 默认为 true
- [ ] 增量扫描跳过未变更文件，第二次扫描时间显著减少
- [ ] GPU 智能调度正确检测并选择最佳解码路径

## Phase 3: 扩展性

- [ ] 插件系统可加载和卸载插件
- [ ] S3/Azure Blob 文件系统 provider 可正常工作
- [ ] 新哈希算法可通过插件添加

## Phase 4: 用户体验

- [ ] 批量删除/移动可在一次操作中处理多个文件
- [ ] 结果页面可内联预览视频
- [ ] PWA 可离线访问扫描结果
