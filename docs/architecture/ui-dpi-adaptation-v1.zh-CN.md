# SkyRoof-Plus UI / DPI 适配路线（v1）

## 边界与设计目标

**目标：窗体、字体、控件一起按比例缩放。** 保持原有停靠区域、频谱、CW Skimmer、频率条和旋转器操作方式，不把窗口改造成按宽度自动重排的响应式页面，也不通过新增滚动条掩盖控件溢出。

当前主要事实（基于 `master`）：

1. `Program.cs` 使用 `HighDpiMode.SystemAware`，主要为 Wine 兼容；`MainForm` 及大量 Designer 控件使用 `AutoScaleMode.Font`。
2. `UiSettings.StoreWindowPosition` 原来只保存主窗体的位置和物理像素宽高。DPI 改变后直接回填这些数值会造成窗口与字体比例不一致。
3. 停靠布局由 `DockHost.SaveAsXml` 保存，几何数据与窗体尺寸分别持久化；未确认跨 DPI 同步变换前，不应随意对 XML 坐标重复乘倍数。
4. 顶部工具栏包含固定宽度的 `SatelliteSelecionWidget`、`FrequencyWidget`、`GainWidget`、`RotatorWidget`；当前 `UpdateSatellitePhotoVisibility` 只隐藏卫星照片，不对剩余控件统一缩放。
5. 频谱页已有按 DPI 计算标签宽度的逻辑和 100/125/150/200% 测试基线；CW Console 等页面仍有显式最小尺寸，需要逐窗检查裁切。

## 分期实施

### P0 — 主窗体尺寸的 DPI 一致性（本次）

- 存储 `MainWindowDpi`，恢复时按 `当前 DPI / 保存 DPI` 缩放完整窗口尺寸。
- **不缩放跨屏虚拟桌面坐标**，窗口不在任何屏幕可视区域时继续使用原有的 `EnsureFormVisible`。
- 旧版 Settings.json 不含 DPI 时不猜测原始 DPI，保持原有恢复行为；第一次保存后记录实际 DPI。
- 验证 100↔150%、200→100%、125→150%、旧配置和负坐标多屏布局。

### P1 — 顶部工具栏同比例缩放

- 在选定的全局 UI 缩放比下，对工具栏整体容器、每个控件位置和字体应用**同一个几何倍率**；只针对一次完整布局基线转换，避免累积缩放产生漂移。
- 量化主窗体在 100/125/150/200% 和常见显示宽度下的工具栏宽度预算及文本截断。
- 不对频率条/按钮进行按宽度重排；必要时允许用户选择较低的整体缩放倍率，优先保持控制器可见、可操作。

#### P1 已实现（本次）

- `UniformControlScale` 启动后只捕获一次 WinForms 已完成系统 DPI 缩放的控件树基线；每次改变比例都从基线计算尺寸、坐标、Padding、Margin 和 Font，避免累积舍入与多次放大。
- 对已人工缩放的顶部 UserControl 关闭内部重复 `AutoScaleMode.Font`，防止双倍缩放。所有顶部控件仍保持原来的左右 Dock 与顺序。
- `Tools → UI Scale` 提供 75/85/100/110/125% 手动偏好，受可用宽度统一缩小；卫星照片仍是唯一可选隐藏项。默认 100%。
- Windows CI 增加 1280/1920 宽度与 96/120/144/192 DPI 的模拟截图矩阵，尺寸/字体重复缩放一致性单元测试。模拟截图非完整 SkyRoof 主界面，也不代表硬件联调。
- 若目标窗口过窄，允许整体缩小到 25%，但不单独压缩单个按钮，不修改布局结构，不增添滚动条。推荐在可读性要求较高的环境使用足够窗口宽度。

### P2 — 停靠窗口和专属弹出面板

- 审查 Sky View、Satellite Passes、Icom LAN Spectrum、CW Console、FT4、Rotator（含 G-5500）面板的固定字体、最小尺寸和对齐。
- 为停靠面板和弹出窗定义共享缩放基准，所有绝对尺寸及文本同步调整；保持数据区的实时绘制缓存，避免在每帧重建控件。
- 对缩放后内容几何做自动化裁切检测，而不是用 `AutoScroll = true` 或 `WrapContents = true` 代替缩放。

#### P2 已实现（本次）

- `Tools → Dock & Popup Scale` 分别控制 Dock 与旋转器右键卡片的 75/85/100/110/125% 缩放。默认 100%，并兼容原有 Settings.json。
- 通过 `UniformControlScale(panel, scaleRootBounds: false)` 只缩放 Dock 内容的子控件及字体，不改 DockPanelSuite 负责的窗体坐标和外框尺寸，也不改停靠布局 XML。
- 为 Sky View 更新专用文本字体；为 Satellite Passes 同步自绘行高、字体和轨迹线宽；Rotator RC-3040S 的扩展面板高度也遵循当前比例。
- 所有动态创建及 XML 恢复的 DockContent 都注册到统一缩放管理；在 100% 默认值时避免无意义的二次布局。G-5500 专属右键面板采用相同固定基线缩放，STOP、Track、Park 和连续旋转回调不变。
- CI 使用 WinForms 模拟控件绘制和尺寸断言验证 Dock 自身尺寸未被缩放、G-5500 在 75/100/125% 下的控件没有越界；截图归档供检查。

### P3 — 多显示器跨 DPI 与长期 UI 验收

- 在 Windows 10/11 对 96/120/144/192 DPI 与跨屏移动进行真实 `Form`、`DockPanel` 视觉与键鼠操作测试；同时评估 Wine 的 `SystemAware` 行为，再决定能否启用 `PerMonitorV2`。
- 检查显示器热插拔、睡眠后恢复、改变主显示器、最大化后恢复、布局 XML 反序列化、主题切换和浮动窗口。
- 核对 CAT/PTT、旋转器按住/松手、Track、CW 发送以及频谱正常更新，不让 UI 缩放改变控制状态机。

#### P3 已实现（本次）

- `UiMonitorPlacement.KeepWindowVisible`：窗体至少一半仍处在可见工作区时，保留跨屏大小和负坐标；原显示器移除或大部分窗口不可见时恢复到最近的可用工作区。
- 兼容默认 DPI 模式仍为 `SystemAware`，可使用 `--per-monitor-dpi` 或环境变量 `SKYROOF_DPI_MODE=PerMonitorV2` 显式尝试动态高 DPI；`--system-dpi` 或 `SKYROOF_DPI_MODE=SystemAware` 恢复默认。
- `MainForm.OnDpiChanged` 在 .NET 原生 DPI 处理前恢复已有缩放基线，变化后重建 Toolbar、Dock、旋转器弹出面板的缩放缓存。若旋转器方向轮处于按住状态，关闭弹窗仍走原有 STOP 回调。
- 模拟测试包括窗口跨屏、负坐标、拔屏、工作区缩小、96/120/144/192 DPI 和模式选择。
- **未完成的实机验证边界：** 还没有真实 Windows 多显示器和 Wine 配套测试，因此不默认启用 PerMonitorV2，也不把纯几何测试等同于硬件/系统级多屏截图验收。

### P4 — UI 回归验收（下一阶段）

- 整合统一比例缩放、屏幕 DPI 映射、Dock 与弹出卡片的 WinForms 模拟渲染截图矩阵。
- 检查不重排、不新建滚动条、按钮文字未裁切、轨迹图像区域连续可用、操作命令与缩放无耦合。
- 在独立 Windows CI 上执行完整 xUnit、aislop、文档校验与 Orekit/IKVM 运行时；保存截图及报告，明确无法替代实机多屏测试的边界。

## 本次验收边界

P0 只修复主窗口恢复尺寸与屏幕 DPI 的历史不一致。它不是所有停靠窗口的整体缩放实现，也不能证明跨显示器 `PerMonitorV2` 已经可用。当前没有真实 Windows 多 DPI 截图和硬件控制验证；xUnit/CI 与模拟 UI 证明的是代码正确性及其覆盖范围，不替代实际多屏验收。

部署：通过合并至 `master` 交付源代码。**本次不改动已发布的 v1.73 Release 安装器**，等 P1–P3 的 UI 缩放设计和测试达到发布标准后统一迭代版本。
