# 扫描进度实时预览 Spec

## Why
用户反馈扫描 2147 个文件需要较长时间，但没有直观的视觉反馈。在侧边栏实时显示当前处理文件的缩略图，可以让用户直观了解扫描进度和当前正在分析的文件。

## What Changes
- **新增侧边栏组件** — `LivePreviewPanel`，显示当前扫描文件的缩略图和基本信息
- **后端推送当前文件** — 通过 SignalR/SSE 实时推送 `currentFile` 路径
- **前端订阅更新** — 侧边栏组件订阅进度更新，动态获取缩略图

## Impact
- Affected specs: 无
- Affected code:
  - `VDF.Web.Client/src/components/` — 新增 `LivePreviewPanel.tsx`
  - `VDF.Web.Client/src/pages/ScanPage.tsx` — 集成侧边栏
  - `VDF.Web/Hubs/ScanHub.cs` — 添加当前文件推送
  - `VDF.Web/Services/ScanService.cs` — 订阅进度变化

## ADDED Requirements

### Requirement: 侧边栏实时预览面板
系统 SHALL 在扫描页面侧边栏显示当前正在处理文件的缩略图预览。

#### Scenario: 扫描进行中
- **WHEN** 扫描处于 Running 状态
- **THEN** 侧边栏显示当前文件的缩略图、文件名、处理进度

#### Scenario: 缩略图加载中
- **WHEN** 当前文件的缩略图正在加载
- **THEN** 显示加载动画占位符

#### Scenario: 缩略图不可用
- **WHEN** 当前文件无法生成缩略图（如权限问题）
- **THEN** 显示占位符图像 + 错误提示

#### Scenario: 扫描空闲/完成
- **WHEN** 扫描处于 Idle/Done/Error 状态
- **THEN** 侧边栏显示上次扫描的最后一个文件或空状态

## 接口设计

### 后端推送（SignalR/SSE）
```csharp
// 进度更新消息添加 currentThumbnailPath 字段
public class ScanProgressResponse {
    public string CurrentFile { get; set; }
    public string? CurrentThumbnailPath { get; set; }  // 新增
}
```

### 前端组件 API
```typescript
interface LivePreviewPanelProps {
  currentFile: string | null
  thumbnailUrl: string | null
  isLoading: boolean
  error: string | null
}
```

## 缩略图获取
- 前端通过 `/api/thumbnail/{filePath}` 获取指定文件的缩略图
- 如果缩略图已缓存，直接返回
- 如果未生成，使用 `ThumbnailCount=1` 快速生成一张

## 布局
```
+------------------+------------------------+
|   ScanPage       |                        |
|                  |                        |
|  +-----------+   |   +---------------+   |
|  | 扫描配置   |   |   |               |   |
|  +-----------+   |   |   LivePreview  |   |
|                  |   |     Panel      |   |
|  +-----------+   |   |               |   |
|  | 扫描日志   |   |   |  [缩略图]     |   |
|  +-----------+   |   |  文件名        |   |
|                  |   |  处理进度       |   |
|                  |   +---------------+   |
+------------------+------------------------+
```
