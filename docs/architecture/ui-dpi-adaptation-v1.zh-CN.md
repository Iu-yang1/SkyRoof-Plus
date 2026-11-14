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

### P2 — 停靠窗口和专属弹出面板

- 审查 Sky View、Satellite Passes、Icom LAN Spectrum、CW Console、FT4、Rotator（含 G-5500）面板的固定字体、最小尺寸和对齐。
- 为停靠面板和弹出窗定义共享缩放基准，所有绝对尺寸及文本同步调整；保持数据区的实时绘制缓存，避免在每帧重建控件。
- 对缩放后内容几何做自动化裁切检测，而不是用 `AutoScroll = true` 或 `WrapContents = true` 代替缩放。

### P3 — 多显示器跨 DPI 与长期 UI 验收

- 在 Windows 10/11 对 96/120/144/192 DPI 与跨屏移动进行真实 `Form`、`DockPanel` 视觉与键鼠操作测试；同时评估 Wine 的 `SystemAware` 行为，再决定能否启用 `PerMonitorV2`。
- 检查显示器热插拔、睡眠后恢复、改变主显示器、最大化后恢复、布局 XML 反序列化、主题切换和浮动窗口。
- 核对 CAT/PTT、旋转器按住/松手、Track、CW 发送以及频谱正常更新，不让 UI 缩放改变控制状态机。

## 本次验收边界

P0 只修复主窗口恢复尺寸与屏幕 DPI 的历史不一致。它不是所有停靠窗口的整体缩放实现，也不能证明跨显示器 `PerMonitorV2` 已经可用。当前没有真实 Windows 多 DPI 截图和硬件控制验证；xUnit/CI 与模拟 UI 证明的是代码正确性及其覆盖范围，不替代实际多屏验收。

部署：通过合并至 `master` 交付源代码。**本次不改动已发布的 v1.73 Release 安装器**，等 P1–P3 的 UI 缩放设计和测试达到发布标准后统一迭代版本。
