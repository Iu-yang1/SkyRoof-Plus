# SkyRoof 设置页双语完整性检查与 IF→AF、遥测配置说明

> 适用于 SkyRoof-Plus PR #220。此文记录设置面板的翻译边界及设备配置方法；不改变 CAT、PTT、声卡采样或遥测解码器的实际功能。

## 设置页双语显示策略

- 通过现有 **Settings → Language** 切换 **简体中文 / English**，支持切换后立即刷新设置页属性列表。
- 主设置组及其可展开子项：Icom LAN、Icom 频谱、CAT、旋转器、轨道/星历、SDR、变频器、FT4、CW、IF→AF、遥测、录音、语音播报、ONVIF 等，均使用符合工程含义的中文字段与帮助说明。
- 配置项布尔显示为 **是/否**（English 为 True/False）；包含专门翻译表的枚举下拉选项，如遥测音源、声道、IF→AF 解调/音频路由、AFC、CW 接收、旋转器模式与瀑布图质量，显示中文，同时允许正常选择和回读。
- 项目内所有原有 **JSON 属性键、枚举数值、设备 ID、端口号、频率、波特率** 均保持不变；中文是 PropertyGrid 表示层，不改设备状态机、控制协议或数据序列化。
- **SNR、CAT、CI-V、IF、AF、AFC、FFT、Q65、SSB、CW、MHz、Hz、dBFS** 等技术缩写保留，避免用含混的中文替代。
- 频段、带宽及频率的数值仍采用标准单位（Hz/kHz），例如 CW 滤波带宽 500 Hz、FM 12.5 kHz。

## 遥测设置（Telemetry）

| 中文名称 | 原英文名称 | 用法 |
| --- | --- | --- |
| 遥测接收输入 | Telemetry RX Input | 选择以下之一：SDR IQ、共享 IF、独立 IF 录音、AF 录音、AF 播放回环、内部 IF→AF 解调 AF |
| 12 kHz IF 录音设备 | 12 kHz IF capture device | 仅 **Capture12kIf** 使用；例如 IC-9700 USB Audio CODEC 的 Windows 录音端，前提是电台输出 IF、48 kHz |
| AF 音频录音设备 | AF capture device | 仅 **CaptureAf** 使用；例如 VB-CABLE 的 **CABLE Output**（Windows 录音端） |
| AF 播放回环设备 | AF playback loopback device | 仅 **LoopbackAf** 使用；选择 RS-BA1 实际播放到的 Windows 扬声器端点 |
| AF 载波中心频率 (Hz) | AF tone center (Hz) | AFSK 的载波中心频率；Bell 202 1200/2200 Hz 双音取 1700 Hz。真实 12 kHz IF 输入不使用它 |
| 保存到文件 | Save to File | 保存解码完成的遥测帧，不会改变 PCM 或解码算法 |
| SSTV 自动保存滤波器 | SSTV Auto-Save Filter | 仅控制 SSTV 自动保存的图像后处理，不干扰遥测 |
| KISS 服务器 | KISS Server | 向其他应用通过 TCP 分发已解码的遥测帧 |
| SatNOGS 上传 | SatNOGS Upload | 上传遥测至 SatNOGS DB，须填写有效 API Key |

**对截图的建议：** 如果 IF→AF 已占用 `麦克风 (USB Audio CODEC)`，优先选 **共享 IF→AF 原始 12 kHz IF**。若只有遥测需要独立打开真实 IF 设备，则改用 `Capture12kIf`，设备选相同的 USB Audio CODEC；否则两条采集可能冲突。

## IF→AF 解调设置（12 kHz IF → AF / WSJT-X）

| 中文分组 | 主要项目 | 说明 |
| --- | --- | --- |
| 常规设置 | 启用、原始 IF 输入来源、USB 12 kHz IF 设备、IF 采集声道 | 选择实际带有 12 kHz IF 的声卡左声道或右声道。**不要将反相左右声道随意混合** |
| 常规设置 | 解调模式、AF 输出路由、输出延迟预设 | 可跟随电台 USB/LSB/CW/FM；自动匹配 VB-CABLE 或手动指定播放端点 |
| 常规设置 | 虚拟声卡配对状态、WSJT-X 虚拟录音输入 | 只读诊断已中文化，便于识别设备未安装/未配对 |
| 接收滤波器 | SSB、CW、FM 带宽 | 保留各自预设与频率单位，并说明适用模式 |
| 接收滤波器 | 只读的 SSB/CW/FM 实际截止频率、中心频率和偏移 | 区别实际有效值与可编辑的滤波器预设 |
| 音频增强 | FM 语音处理、静噪、SSB 陷波与降噪 | **FM 去加重用于模拟语音**；数据遥测一般选择关闭，以避免改变判频音频 |
| 常规设置 | SSB/CW 慢速 AGC、目标 RMS、最大增益、独立输出增益 | 保留对数字模式的影响说明；不会更改原始 12 kHz IF |

当 IF→AF 使用 `AutoSignedCable` 时，CABLE Input 是 Windows **播放端**，CABLE Output 是 Windows **录音端**。这是虚拟声卡设备方向，不是“把 IF 输入接反”。

## 语音播报

- **语音播报声卡**：输出 AOS/卫星方位角语音的 Windows 播放设备；
- **语音引擎**：可用的 Windows TTS 声音；
- **音量**：0 为静音；
- **AOS 升起播报 #1/#2**：启用、提前分钟数、消息模板；
- **卫星位置播报**：启用、最小角度变化、消息模板。文本占位符（如 `{name}`、`{minutes}`、`{azimuth}`、`{elevation}`）不翻译，以保证运行时替换正确。

## 自动化验收边界

`SettingsChineseSupplementTests` 验证音源、滤波、声道和布尔下拉、中文/英文切换、IF→AF 只读状态与 JSON 不变；`SettingsChineseCoverageTests` 检查设置页主要分组和子项的中文标签、类别与帮助描述。Windows CI 的结果请以 [PR #220](https://github.com/Iu-yang1/SkyRoof-Plus/pull/220) 的 Checks 为准。

未连接实际 IC-9700 和系统声卡的自动测试，不能替代音频录制、遥测误码率或实机 CAT/PTT 验证。本次是设置页语言补全，不发布新 Release。
