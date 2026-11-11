# IC-705 / IC-905 P2 — 12 kHz IF 与 RS-BA1 LPCM 网络音频接入

## 已完成的协议层代码

- `SkyRoof/IcomLan/IcomLanLpcmAudioCodec.cs` 对 UDP 音频帧执行长度、包类型、音频标识、源/目的会话 SID 和 PCM 数据长度检查。
- 根据 RS-BA1 24 字节音频报头解析 `0x80 0x00` 标识、16-bit 大端音频序号和**小端有符号 PCM16 单声道**，归一化为 `float`。解码器并不从波形猜测 AF 或 IF。
- `IcomLanDirectSession` 在协商独立的音频 UDP 端口后尝试独立握手，确认该流特有的 SID；接收时维护本流 ping/echo，成功解码后发出 `AudioFrameReceived(sampleRate, frame)`。
- `IcomLanSpectrumCapture` 透传这个事件，作为后续 AF/IF DSP 接入点。旧 SkyCAT / 被动 RS-BA1 模式及 IC-9700 的 CI-V / CAT / PTT 状态机不变。
- 原来的 USB 48 kHz → IF/AF DSP 路径没有重构：IC-705、IC-905 使用相同的 Windows 48 kHz USB PCM 录音设备入口。设置页加入机型菜单提示。

## 电台侧配置

| 机型 | 12 kHz USB IF 输出 | 网络 IF 输出 |
| --- | --- | --- |
| IC-9700 | `Connectors → USB AF/IF Output → IF` | `LAN AF/IF Output → IF` |
| IC-705 | `SET → Connectors → USB AF/IF Output → Output Select = IF` | `SET → Connectors → WLAN AF/IF Output → Output Select = IF` |
| IC-905 | `SET → Connectors → USB/AV-OUT AF/IF Output → Output Select = IF` | `SET → Connectors → LAN AF/IF Output → Output Select = IF` |

`AF` 模式输出解调后的音频，**不是 12 kHz IF**；两者不能仅通过重命名设备互换。USB 路径应在 Windows 录音设备中选择实际电台的 USB Audio CODEC，输出到独立的虚拟声卡播放端，不能反馈到电台 TX 输入。

## 完整网络 PCM → IF-AF 接入（本迭代）

- **输入选择**：`Settings → IF-AF → IF input source` 可选 `UsbAudio`（默认，保持原有 USB 采集）或 `AuthenticatedLanIf`。后者从 **Icom Spectrum 面板选择 Direct LAN 源且已启动的认证会话**获取 RX LPCM，复用该连接，不另开可能冲突的 CI-V/Scope 会话。
- **电台输出**：IC-705 在 WLAN AF/IF Output Select、IC-905 在 LAN AF/IF Output Select 中选择 **IF**，不是 AF。48-kHz 网络 PCM 若只是 AF，无法在软件侧恢复 12-kHz IF。
- **序列和重排**：按照音频帧独立的 16 位 `AudioSequence`（不是传输外序号）重排。最大 8 帧/100 ms 缓冲，重复/晚到帧丢弃；超时后跳过缺口，将下一块标注为 `Discontinuity`，使 IF 解调和 AGC 复位。25 ms 定时刷新可处理最后一个待补包即使暂时没有新帧。
- **重采样**：48000 samples/s 原样旁路；其他 8–192 kHz 协商采样率使用带低通窗函数的分段连续 sinc 滤波，输出 48000 samples/s，并保持包间相位。收到断流、采样率改变或序号跳跃时清空滤波器历史。
- **DSP**：`IfAfService` 使用原有单所有者的有界 PCM 队列、`IfAfDemodulator`、AGC/滤波器、WASAPI 虚拟音频播放以及 `RawIfAvailable` 的 AFC/SSTV 分发通道。网络样本不会打开 USB 声卡；原有 USB 设置/路径保持兼容。
- **窗口生命周期**：当前共享 Direct LAN Scope 面板持有的网络会话。停止或关闭面板后网络输入会断开，IF-AF 状态转为等待重连；重新开启面板继续接收。原始 USB 模式无此依赖。
- **设备实际操作**：先配置 Spectrum = Direct LAN（设备 IP、用户名/密码和正确的 IC-705/IC-905 radio model），启动 Scope 面板，再启用 IF-AF 与 `AuthenticatedLanIf`，把 VAC 播放端作为 WSJT-X 的录音输入。切换 IF-AF 模式（USB/LSB/CW/FM）仍随无线电设置。

## 自动验收证据

新增协议仿真测试：16-bit 音频序列回绕、重复与乱序、100 ms 超时、缺包复位、500 ms 静默断流、采样率变化、44.1 kHz 1-kHz 频率保持、96→48 kHz 混叠抑制，以及网络 13-kHz IF 经过现有 `IfAfDemodulator` 恢复为 1-kHz AF。**尚未采集 IC-705 WLAN 或 IC-905 LAN 实机数据**；网络延迟/电台 AF/IF 菜单设置及设备音频输出仍需独立实机验收。

## 证据级别与剩余工作

**协议单测**：构造样本验证有符号 16-bit 到浮点转换（含负满幅/零/正满幅）、报文长度、16-bit 序号、SID 错误关联及不完整音频载荷。

**当前限制**：网络 PCM 现已接入 IF→AF DSP、做有限重排和重采样，但没有实现 RS-BA1 协议层向电台发送“重传丢失音频包”的请求（逾期选择跳过并复位）；网络音频必须来自 Spectrum 面板所持有的 Direct LAN 会话；尚无真实 IC-705/905 的 AF/IF 切换、端到端时延或设备故障恢复实测数据。不能以模拟帧验证替代实机验收。

**实机验证**：IC-705 WLAN / IC-905 LAN 音频协议握手、PCM 序号与报文长度，以及 USB IF 输出，尚没有相应实机抓包或录音文件，本 PR 不宣称实机通过。此功能目前不涉及音频发射链路。

## 协议资料

- Rafe 项目对 Icom RS-BA1 UDP 的开源重构说明（Audio 16+8 字节报头、48 kHz mono LPCM、内外序号、丢包恢复）：https://patrick.lidstone.net/wiki/rsba1-protocol/
- IC-705 Basic Manual：https://www.manualslib.com/manual/1875929/Icom-Ic-705.html?page=66
- IC-905 Basic Manual：https://www.manualslib.com/manual/3030522/Icom-Ic-905.html?page=68

注意：RS-BA1 UDP 帧格式是第三方逆向资料，不是 Icom 官方发布的网络协议标准。只有 AF/IF 菜单与 12 kHz IF 输出能力由 Icom 手册直接确认。
