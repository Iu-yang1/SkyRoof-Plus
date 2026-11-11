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

## 证据级别与剩余工作

**协议单测**：构造样本验证有符号 16-bit 到浮点转换（含负满幅/零/正满幅）、报文长度、16-bit 序号、SID 错误关联及不完整音频载荷。

**尚未完成**：这是网络 PCM 的*接收帧解码与事件发布*，还没有把事件接到现有 `IfAfService` 的有界缓存、断包重同步和 IF→AF 解调器；未实现音频 UDP 丢包后的完整重排序/重传请求；网络音频采样率可能经设备能力协商为非 48 kHz，尚不能直接送入固定 48 kHz DSP。不得将此 PR 称为可用的端到端网络 IF 解调。需补齐有序 PCM、协商采样率转换、设备输出类型明确选择与端到端延迟验证。

**实机验证**：IC-705 WLAN / IC-905 LAN 音频协议握手、PCM 序号与报文长度，以及 USB IF 输出，尚没有相应实机抓包或录音文件，本 PR 不宣称实机通过。此功能目前不涉及音频发射链路。

## 协议资料

- Rafe 项目对 Icom RS-BA1 UDP 的开源重构说明（Audio 16+8 字节报头、48 kHz mono LPCM、内外序号、丢包恢复）：https://patrick.lidstone.net/wiki/rsba1-protocol/
- IC-705 Basic Manual：https://www.manualslib.com/manual/1875929/Icom-Ic-705.html?page=66
- IC-905 Basic Manual：https://www.manualslib.com/manual/3030522/Icom-Ic-905.html?page=68

注意：RS-BA1 UDP 帧格式是第三方逆向资料，不是 Icom 官方发布的网络协议标准。只有 AF/IF 菜单与 12 kHz IF 输出能力由 Icom 手册直接确认。
