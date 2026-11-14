# SkyRoof：IF→AF 录音与遥测 IF/AF 输入

2026-11-14 · 针对 SkyRoof-Plus `feature/ifaf-recorder-telemetry-inputs`。

## 1. 截图中的设备与 Windows 音频方向

- **Recorder Audio Source = RsBa1Loopback**：从 Recorder RS-BA1 Playback Device 的 Windows **播放端点**采集 *整个* WASAPI Loopback 混音；与 USB AF Capture Device 的值无关。
- **Recorder USB AF Capture Device = CABLE Output (VB-Audio Virtual Cable)**：这是虚拟声卡的 Windows **录音端点**；只有选择 Recorder Source = WasapiCapture 才会使用它，通常对应虚拟声卡播放端 **CABLE Input**。
- **Recorder RS-BA1 Playback Device = 扬声器（UU远程虚拟音频设备）**：RsBa1Loopback 实际录制的播放端。如果 RS-BA1 不是播放到该端点，录音不会取得 RS-BA1 的声音。
- **Speaker Audio Device = <please select>**：SkyRoof SDR 监听输出未选择，不等于 IF→AF 的虚拟声卡输出设置。若只给外部软件输出 AF，未必需要 SkyRoof 同时播放到扬声器。
- **FM Squelch = False**：对弱信号数字遥测通常推荐保持关闭，避免包首或弱信号部分被静噪消除。

## 2. 推荐的直接 IF→AF 录音

`12-kHz real IF @ 48 kHz` → `IfAfService` → `IfAfDemodulator` → `IfAfAudioLeveler` → **`DemodulatedAfAvailable`** → `RecordingManager` → `Save .wav`。

在 **Tools / Settings > Audio** 中选择 `Recorder Audio Source = IfAfDemodulated`，或在 **Recorder > Record ▼ > Audio Source** 选择 `IF -> AF decoded PCM (direct, 48 kHz)`。确保 IF→AF 正在运行，再点击 Record Audio；结束后选 Save .wav。录到的是 **48 kHz、单声道、解调及音频电平处理之后的 PCM**，不是 12 kHz IF、VAC/RS-BA1 设备混音或硬件 TX 音频。录音容量继续沿用现有 30 分钟限制，未修改导出、播放或录音格式。

`Auto` 仍优先使用已启用的 SDR；SDR 未运行但 IF→AF 正在运行则自动录制直接 AF；都不运行才回退 RS-BA1 loopback。可以显式选择来源，避免 Auto 因运行状态不同而改变。

## 3. 遥测信号源选择

**Tools / Settings > Telemetry > Receive input**：

| Telemetry RX Input | 48 kHz 实际输入 | 预处理 | 用途 |
| --- | --- | --- | --- |
| `SdrIq`（默认） | 现有 Slicer 复数基带 | 无 | 原 SDR 解码流程，保持兼容 |
| `Shared12kIf` | IF→AF 服务的原始 USB 或已认证 LAN IF | Hilbert + 12 kHz NCO 下变频 | 与 IF→AF 复用同一采样源 |
| `Capture12kIf` | `12 kHz IF capture device` 的 Windows 录音端 | 同上 | 独立 USB IF 声卡接收 |
| `CaptureAf` | `AF capture device` 的 Windows 录音端 | Hilbert + 可配置 AF 中心频率下变频 | AFSK 音频（例如 VB-CABLE 的 CABLE Output） |
| `LoopbackAf` | `AF playback loopback device` 的 Windows 播放端混音 | 同上 | RS-BA1 等软件扬声器输出 |
| `IfAfDemodulatedAf` | IF→AF 服务的解调后 AF PCM | 同上 | 直接复用 SkyRoof 内部 AF，不需虚拟声卡 |

**AF tone center (Hz)** 默认为 1700 Hz，对应常见 1200/2200 Hz AFSK 的中点。其他频移键控音调必须按实际音调设置中心频率。实际的调制、baud、framing 与检测仍在 Telemetry 面板的 Signal Parameters 中设置：**更换输入不会自动推断卫星协议**。

**12 kHz IF 的前置条件**是声卡以 48 kHz 读取真实 12 kHz IF。如果电台的 USB AF/IF Output 设成 AF，则该 PCM 并不含 12 kHz IF；应选择 AF 输入或把电台的输出改成 IF。

**AF 不等于通用复数 IQ**：这次 AF 分支针对存在明确音频载波中心的 AFSK/FSK 类型，利用解析信号得到复数基带后继续交给原始 IQ 遥测解码器。未经验证的 FM 判决后 9600 bit/s 基带/GMSK 不能仅靠设置 1700 Hz 中心就保证正确，应优先使用真实 12 kHz IF 或 SDR IQ；FM 音频转数据符号的专用分支不在本次改动范围。

共享输入不会再次打开 IF 采集设备。独立 IF/AF 输入由遥测会话打开与关闭。接收回调只复制 PCM 到有界队列，后台执行 Hilbert 及 IQ 转换；音源切换时重建会话，不干扰已有 CAT/PTT、KISS 和 SatNOGS 逻辑。SdrIq 模式与音频模式互斥，不允许同一数据帧从两路重复进入解码器。

## 4. 验证范围

新增 `TelemetryAudioInputTests` 覆盖 12000 Hz IF、1700 Hz AF、2200 Hz AFSK、13500 Hz IF 偏移、非整数周期分块持续解调、复位与原 SDR 默认配置。此为 **合成信号 → IQ** 函数单测，不等于真实遥测 BER/PER 测试。交付前以 GitHub PR 的 Windows Compile Check、aislop、Docs Validation 实际结果为准；未进行实机声卡与 IC-9700 测试，也不创建 Release。
