# 遥测 IF/AF 接收与离线回放（v1.82）

**语言：** [English](../../fork-guide/telemetry-if-af.md) | 简体中文

SkyRoof Plus v1.82 为原有 [Telemetry 遥测面板](../../users_guide/telemetry_panel.md)增加 IC-9700 **12 kHz 实数 IF 接收**，并修复已完成 FM 鉴频的 AFSK 音频输入问题。保留原来的 SDR 复数 IQ 解码路径。**遥测 IF 接收不再要求先启动 IF→AF 虚拟声卡输出**，与为 WSJT-X 提供音频的 [IF→AF 接收链路](if-af-test.md)相互独立。

## 1. 接收源及信号域

在 **Tools → Settings → Telemetry → Receive input** 中选择：

| Telemetry RX Input | 实际信号 | 用途与依赖 |
|---|---|---|
| `SdrIq`（默认） | SDR Slicer 的 48 kHz 复数基带 | 保持原有 SDR 遥测接收 |
| `Shared12kIf` | 48 kHz PCM 中的实数 12 kHz IF | IF→AF 已运行时复用原始 IF；否则按 **IF→AF 输入来源设置**独立使用 USB IF 或认证 Direct LAN IF；**不依赖 VAC 播放端** |
| `Capture12kIf` | Windows 录音端点的实数 IF | 独立打开 USB IF 录音设备，使用设定的左/右声道 |
| `CaptureAf` | Windows AF 录音端点（如 VB-CABLE 的 **CABLE Output**） | 已由电台/软件完成解调的音频 |
| `LoopbackAf` | RS-BA1 等软件使用的 Windows 播放端点 | WASAPI Loopback 捕获该端点的**全部混音** |
| `IfAfDemodulatedAf` | SkyRoof IF→AF 已解调的内部 AF PCM | 无需再经 VAC/Loopback，但 **IF→AF DSP 必须正在运行** |

**原始 IF** 经 Hilbert FIR 形成解析复数信号，再由连续 NCO 将 **12,000 Hz** 混频至零频。**已 FM 鉴频的 AFSK 音频** 不适合再被当成 RF FM 信号直接鉴频；v1.82 提供 FM-IQ 兼容桥接，以匹配现有 SkyTlm AFSK 解调器的输入域。正确的 AFSK 音调中心、频偏、波特率、调制和帧格式仍由 **Telemetry → Signal Details** 中的发射机参数决定；普通 AF 解析变换使用另行设置的 **AF tone center (Hz)**。

特别注意：**9600 Bd FSK/GMSK 的 FM 鉴频后基带并不自动等于 Bell 202 AFSK**。此类信号建议使用真实 IF 或 SDR IQ，不能靠把 AF 中心频率设为 1700 Hz 就保证解码。

## 2. IC-9700 USB IF 接收步骤

1. 在电台上将 **USB AF/IF Output** 设置为 **IF**（不是 AF）。Windows 使用 **USB Audio CODEC** 的**录音端点**，设为 **48 kHz**；尽量关闭麦克风增强/自动增益。确认左、右哪个声道确实包含 12 kHz IF，不要直接混合可能反相的两个声道。
2. 在 SkyRoof **Tools → Settings → Telemetry** 中选择 `Capture12kIf` 独立采样；也可以选择 `Shared12kIf` 共享正在运行的 IF→AF 输入。若 IF→AF 没有启动、配置的来源是 USB，`Shared12kIf` 可以自行打开 USB IF 录音端点。
3. 在 Telemetry 选择正确的卫星和**数字遥测发射机**，检查调制类型、Baud、Framing 与频偏参数。**切换输入不会自动识别业务协议**。
4. 鼠标悬停在 **Telemetry 状态栏**：查看输入样本数、PCM 丢块、断流次数、RMS（dBFS）、解帧数、CRC、最近 CFO/SNR。

该链路仅作接收，不会将原始 IF 音频写入电台的发射播放设备，也不会代替 CAT/PTT 控制。

## 3. IC-9700 认证 Direct LAN IF 接收

1. 配置 Direct LAN 的**认证接收音频**，将电台 **LAN AF/IF Output = IF**。它与 **USB AF/IF Output** 是两个独立设置，不能混用。
2. 在 SkyRoof 的 **IF→AF 设置**中选 **Authenticated LAN IF** 输入，在 **Telemetry** 中选 `Shared12kIf`。即使没有启动 IF→AF/VAC 播放输出，遥测也可订阅认证 LAN 媒体会话。
3. LAN PCM 经 UDP 序号重排和重采样变为 **48 kHz**。丢包、流重启或采样队列丢块会标记为信号不连续：下游 Hilbert/NCO 复位，遥测突发检测、调制解码器与解帧器重建，防止把不相邻的信号拼接成一帧。
4. 如果状态栏提示断流/丢块持续增长，先排查 LAN 连接、采样率与系统负载；不要只凭零 CRC 成功帧就认定卫星协议错误。

**边界：** Direct LAN 必须实际传入 IF，而不是普通 AF。实时卫星遥测仍受所选卫星的 AOS/LOS 判断影响；离线回放不受实时地平线门控影响。

## 4. Recorder：录制 IF、IQ 与离线解码

详细按钮操作见 [Recorder 英文手册](../../users_guide/recorder_panel.md)。

- **录原始 IF：** **Recorder → Record ▼ → Audio Source → Raw 12-kHz IF**，点击 **Record Audio**，保存为 `*.if.wav`，即 **48 kHz、单声道、32-bit float**。文件内容是**实数 IF**，不是 AF 语音。
- **录转换后的 IQ：** **Recorder → Record ▼ → I/Q Source → 12-kHz IF → complex I/Q**，点击 **Record I/Q**，保存为 `*.iq.wav`，即 **48 kHz、双声道、32-bit float**，左 I、右 Q。其频谱是由 12 kHz IF 变频得到的基带，并非 SDR 原生宽带射频 IQ。

Recorder 的 IF 录制可共享已运行的 IF→AF 原始 IF；未运行时可使用设置中的 **Recorder 12-kHz IF Capture Device** 独立 USB 录音。**不要将 Telemetry 可以独立接入 Direct LAN 误认为 Recorder 在 IF→AF 停止时也一定能独立录制 LAN IF**。

**离线遥测解码操作：**

1. 打开 Telemetry 面板，选择录音对应的卫星、下行发射机及正确的调制/帧格式。
2. 在 Recorder **Load** 48 kHz 单声道的 `*.if.wav`：软件按后缀识别原始 IF，通过与实时接收相同的 **IF→IQ** 转换器读入；`*.iq.wav` 则直接载入复数 IQ。普通不带 `.if.wav` 后缀的单声道 `.wav` 按 AF 回放。
3. 点击 **Play Back**。即使 Telemetry 实时输入不是 `SdrIq`，离线 IQ 也能进入遥测解码器；在波形上点击跳转会重建解码状态。
4. **回放中不向 SatNOGS 上传帧**。可在本地检查解码树、KISS 输出和帧内容，停止回放后恢复实时接收。

## 5. 状态诊断、KISS 和 SatNOGS

Telemetry **状态栏悬停提示**现在显示输入样本数、PCM 丢块、断流复位、IF/AF RMS（dBFS）、解码帧数、CRC 成功与失败计数、内层业务包计数、最近帧 CFO 与 SNR。**CRC 有效**的帧才允许将当前过境标记为已收到有效遥测、确认人工调整的解调参数。上述数值是接收/解码诊断，不等于实测 BER/PER 或校准的射频灵敏度。

内置 **KISS-over-TCP**（默认 TCP **8100**）使用同一连接的 **KISS 虚拟端口**区分协议层次：

| 虚拟端口 | KISS 命令字节 | 输出内容 |
|---|---|---|
| 0 | `0x00` | 外层射频解码帧/CCSDS TM 帧（未显式 CRC 失败） |
| 1 | `0x10` | 已重组的内层业务包，例如 AO-123/ASRTU-1 的 TM/KISS Space Packet；**内部 KISS 没有独立 FCS** |
| 2 | `0x20` | **明确 CRC 失败**的外层帧 |

虚拟端口号**不是独立 TCP 监听端口号**。只处理 KISS 虚拟端口 0 的客户端不会看到端口 1、2 的包。SatNOGS 仅接收符合现有上传条件的**外层**射频帧；内层包、离线回放与未经保存确认的人工参数覆盖均不会提交。AO-123 的 CCSDS TM 帧校验失败时会清除部分拼装的 KISS 包，防止不同业务包跨丢失帧错误拼接；UNNE-1B 的 Type-C CRC/解扰使用专用实现。

### UNNE-1B 官方 SoundModem 与解码 DLL 对照

AMSAT-EA 的 [UNNE-1B 官方传输协议](https://www.amsat-ea.org/app/download/14192756/AMSAT%2BEA%2B-%2BTransmissions%2Boverview%2Bfor%2BUNNE-1B.pdf)规定：初始遥测为 **200 bit/s 二进制 FSK**，双音间隔 **1125 Hz**（名义半频偏 **562.5 Hz**）；**低音 mark 代表 1**，同步字 `BF35`，下行 **436.888 MHz**。卫星可经地面指令更改传输速率，但不能因为 SoundModem 有 800 Bd 菜单就默认当前使用 800 Bd。普通初始遥测应选择 **FSK / 200 Bd / HADES/GENESIS**；频偏以实际信号测量为准。

2026 年 10 月的官方 UZ7HO 界面示例中，**A/B 通道选中了 `FSK UNNE-1B 800bd`**，下拉菜单另有 **`FSK UNNE-1B 200bd`**。图中的 RX BPF **2500 Hz**、TX BPF **2700 Hz**、判决后 LPF **600 Hz**、FIR taps **256/128**、额外 RX **2 对**（每对 **100 Hz**）和 Bits Recovery **NONE**，属于 **SoundModem 音频 FSK 调制解调器**的设置，**不是** SkyRoof 原始 IF→IQ 转换器参数。2 对附加接收器表示中心及 **±100、±200 Hz 共 5 个接收假设**。TXDelay/TXTail 属于发射控制，与纯接收遥测无关。

用户提供的 `unne1b.dll` 实测为 **32 位原生 PE DLL**，内含 **AMSAT-EA v1.14 (Bytes)** 标识。导出 `telemetry_packet_size`、`crc16`、`SelfSyncDeScrambler`、`procesar` 和 `visualiza_*` 等**解帧、校验、解扰和业务解析**函数；并非 SoundModem 的音频 FSK 解调器。**64 位 SkyRoof 不能直接载入该 32 位 DLL**。当前 C# 独立实现保留协议长度、加扰态 CRC-CCITT-FALSE 和自同步解扰；在 v1.82 之后的源码修订中，还纠正了原业务显示中的**卫星时钟/计数器小端读取**、**面板功率 2 mW/LSB**、12 位电压/电流打包字段。该修正解决的是“CRC 有效但显示数值不对”，**不等于已完成真实 200 Bd FSK 位同步验证**。

源代码对照：[AMSAT-EA 原版 C 解码器](https://github.com/AMSAT-EA/HADES-R/tree/main/byte_version) · [UZ7HO SoundModem](https://uz7.ho.ua/packetradio.htm)。

## 6. 验证结果与排障

v1.82 正式构建完成 Windows 编译、安装包生成、**1,632 项 xUnit 测试全部通过**与固定版本 aislop 审查。自动化覆盖 IF→IQ 连续性、AFSK FM-IQ 恢复、KISS 端口封装等。**没有在本次发布中进行 IC-9700 实机 USB/LAN 接收，亦未取得 UNNE-1B 200 Bd FSK 或 AO-123 9600 Bd BPSK 的真实 BER/PER 结论**。

无法解码时，按顺序检查：电台 USB/LAN **IF 与 AF 模式**是否选对 → 48 kHz 与左/右声道 → 状态栏 RMS 和断流计数 → 卫星/发射机/调制/波特率/帧格式 → CRC 结果。音频接收状态为绿色不能证明业务帧已经正确解码。

延伸阅读：[Telemetry 接收设置（英文）](../../users_guide/setting_up_telemetry_decoding.md) · [Telemetry 面板](../../users_guide/telemetry_panel.md) · [Recorder](../../users_guide/recorder_panel.md) · [IF→AF / WSJT-X](if-af-test.md) · [v1.82 正式 Release](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.82)。
