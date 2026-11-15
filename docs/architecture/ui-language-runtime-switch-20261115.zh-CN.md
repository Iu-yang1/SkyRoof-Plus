# SkyRoof-Plus 双语言页面切换

工作分支：`feature/bilingual-ui-live-switch-p0-p4-20261115`。页面本地化只改变界面文字，不修改收发、DSP、轨道或日志协议。

## 使用

打开 `Tools → Language → 简体中文 / English` 即可切换并持久化到 `Settings.json` 的 `Ui.Language`。在 `Tools → Settings` 中也可预览语言；选择 Apply/OK 才提交，Cancel 恢复原语言。

## 实现

- `SkyRoof/Localization/UiLocalizationService.cs`：基于 .NET `ResourceManager` 的语义资源键、语言通知、WinForms 控件生命周期绑定及显式控件标题刷新。
- `UiStrings.resx` 是英文基础资源，`UiStrings.zh-CN.resx` 是中文卫星资源。缺失目标语言翻译时，回退英文。
- `SettingsLanguage.cs` 继续管理 PropertyGrid 的中英文名称、类别、帮助和枚举值；类型身份、JSON 属性名及数值转换不变。
- `MainForm.Localization.cs` 提供 `Tools → Language` 与 DockPanel 原位更新；不重建 Dock 布局、不触发 CAT/PTT、旋转器、频谱采集或解码器重新连接。
- Recorder 的录制/回放、Telemetry 解码状态、CW 接收与发射状态、FT4 奇偶时隙、Auto Selection 过境状态、Spectrum 工具提示、AFC、SSTV 四个工作区与常见状态均接入资源。卫星管理、解码参数、SDR 设备、AMSAT 报告等对话框使用同一接口。
- DownloadDialog 原来以 `Button.Text == "Close"` 决定能否关闭；现改为独立 `canClose` 和 `cancellationRequested` 状态，语言不会改变下载流程。

## 验收

1. 选择 English，重启检查保存；切回简体中文，确认主菜单、工作台与设置页同步。
2. 保持 CW/遥测/频谱/录音等运行状态切换语言；不允许切换操作触发频率、PTT 或云台指令。
3. 设置页语言预览再 Cancel，检查界面回滚；Apply 后检查重启持续有效。
4. SSTV 模板、录音机 I/Q 来源、FT4 模式、卫星分组和发射机标识、ADIF 字段及原始日志数据必须保持不变。
5. 100%、125%、150%、200% DPI 及亮/暗主题验证无新增文字遮挡；保持原有缩放策略。
6. 执行 Windows 构建、xUnit、资源键覆盖/占位符一致性测试、aislop 与安装器冒烟。实机联调与模拟验证分开报告。

保留 CAT/CI-V、AFC、WPM、PTT、REF、VBW、AVG、STOP、SETS 等技术缩写。第三方驱动返回的原始诊断字符串可保持英文，方便定位真实故障。少量尚未迁移的边缘文本仍使用旧字串，不应把自动化通过等同于逐页面视觉验收完成。