# Tasks

- [x] Task 1: 后端推送当前缩略图路径
  - [x] 1.1: 修改 `ScanProgressResponse` 添加 `CurrentThumbnailPath` 字段
  - [x] 1.2: 在 `ScanService` 进度更新时，设置 `CurrentThumbnailPath`
  - [x] 1.3: 验证 SignalR/SSE 推送包含缩略图路径

- [x] Task 2: 创建 LivePreviewPanel 组件
  - [x] 2.1: 创建 `VDF.Web.Client/src/components/LivePreviewPanel.tsx`
  - [x] 2.2: 实现缩略图加载状态显示
  - [x] 2.3: 实现错误状态和空状态显示

- [x] Task 3: 集成到 ScanPage
  - [x] 3.1: 在 ScanPage 添加侧边栏容器
  - [x] 3.2: 集成 LivePreviewPanel 组件
  - [x] 3.3: 订阅实时进度更新

- [x] Task 4: 样式调整
  - [x] 4.1: 设计侧边栏布局（宽度、位置）
  - [x] 4.2: 缩略图显示样式（适应容器、占位符）
  - [x] 4.3: 响应式设计

# Task Dependencies
- [Task 2] 依赖 [Task 1]（需要后端推送缩略图路径）
- [Task 3] 依赖 [Task 2]
- [Task 4] 可与 Task 3 并行
