# SkyRoof / SkyTlm 常见业余卫星遥测链路审计与实录矩阵（2026-11-17）

## 目标和证据等级

从天线/电台或 SDR 输入，到 IQ、调制解调、FEC、成帧、卫星协议解释、KISS/上传/UI，逐层追踪真实解码结果。把“能识别调制”与“完整 CRC/RS 验证且输出正确卫星帧”严格区分。

证据等级：
- **A：实录端到端**：公开录音经 SkyRoof 输入适配、SkyTlm 解调和实际 FEC/CRC 验证，能匹配独立参考。
- **B：SkyTlm 原始空口录音**：经原生流式解码器和协议 FEC/CRC，未覆盖 SkyRoof 输入/输出接口。
- **C：协议或合成信号**：单元测试、已知软比特/原始帧；不能代替空口实录。
- **D：仅理论支持/待测**：卫星参数来自 gr-satellites/SatYAML，但尚无该路径的实际回归结果。

## 卫星与协议矩阵

| 卫星或代表族 | NORAD | 调制 / 速率 | 帧/FEC | 实录候选 | 初始证据 |
|---|---:|---|---|---|---|
| AO-73 / FUNcube-1 | 39444 | DBPSK 1200 Bd | AO-40 FEC；256 B；Viterbi+RS | satellite-recordings/ao73.wav | A：实录有效 RS 帧；IF 变频仿真 |
| JO-97 / JY1Sat、EO-88 | 按 SatYAML | DBPSK 1200 Bd | AO-40 FEC | 尚需按卫星独立验证 | C |
| AO-27 | 22825 | AFSK 1200 Bd | AX.25（Bell-202） | satellite-recordings/ao27.wav | A：实录 2 帧 |
| AISTECHSAT-3 | 44103 | FSK 4800/9600 Bd | AX100 ASM+Golay | satellite-recordings/aistechsat3.wav | A：4800 Bd FM 鉴频 AF 重建 5 帧 |
| Suomi 100 | 按 SatYAML | GMSK（现场速率取 SatYAML） | AX100 | satellite-recordings/suomi_100.wav；SkyTlm off-air corpus | A：实录 2 帧；独立 SkyTlm corpus B |
| GOMX-3 | 40949 | FSK 19200 Bd | AX100 Reed Solomon | satellite-recordings/gomx_3.wav | D |
| CAS-4A、XW-2A | 42761、40903 | FSK 4800 / 9600 Bd | AX.25 G3RUH | 公开录音需对应链路 | C |
| MONITOR-1 / RS32S | 53374 | FSK 1200–9600 Bd | USP | SkyTlm USP 参考帧/录音 | B/C |
| ASRTU-1 / AO-123 | 61781 | BPSK 9600 Bd | CCSDS Concatenated + short TM/KISS | 已有 AO-123 接收记录与工具回归 | C；用户报告 IF 链路正常 |
| UNNE-1B | 101101 | FSK 200 Bd | GENESIS 型帧与 CRC/VOICE | N6RFM/HADESx 真实 IQ 三份 | A |
| HADES-SA | 按 SatYAML | FSK 800 Bd | HADES | SkyTlm fsk_hades_HADES-SA.wav | B |
| Geoscan / RS 系列 | 按 SatYAML | 依型号 FSK | GEOSCAN / AX.25 | SkyTlm 帧及协议 corpus | B/C |
| QO-100 AO-40 uncoded beacon | 26609 家族 | DBPSK Manchester（按具体信标） | AO-40 uncoded（不同于 FEC） | satellite-recordings/qo100.wav | **未实现该专用帧型**，不得错误归入 AO40FEC |
| SMOG-P / SMOG-1 | 按 SatYAML | 多种 FSK | AO-40 FEC short / CRC-16-ARC、RA | satellite-recordings/smog_p.wav | **不等于已支持的标准长 AO40FEC** |

以上频率、帧型与速率由 gr-satellites 官方 Supported Satellites / SatYAML 定义为准；真实录音来自 daniestevez/satellite-recordings（48 kHz mono），不得以文件名推断其当前调制模式。

## 已发现的跨卫星故障机制

1. **缓冲区有效长度错误**：`TelemetryDecocder.Process(DataEventArgs<Complex32>)` 原先调用 `Pipeline.Push(args.Data)` 与 `Detector.Push(args.Data)`，忽略 `args.Count`。池化数组超过有效长度时会把旧数据写进连续 IQ 时间线。已修改为统一使用 `AsSpan(0,args.Count)` 并增加重复复用缓冲测试。
2. **差分 BPSK 被错误解释成相干 BPSK**：`SignalParamsResolver.Resolve` 在 SatYAML 没有明确 `precoding` 字段时默认 `Differential=false`，会使 AO-40 FEC 族不能正确进行 DBPSK 解调。已对标准 AO-40 FEC 设置协议级默认差分值，同时保留显式预编码覆盖；非 AO-40 的 BPSK 行为保持原状。
3. **协议名称与支持级别混淆**：不能把 AO-40 FEC、AO-40 uncoded、AO-40 FEC short、AO-40 FEC CRC-16-ARC 当成同一链路。现有 `ExtractFraming` 排除了不支持的变体，这个原则应保持。
4. **数据源不等价**：SDR 原始 IQ、12 kHz IF、已经鉴频的 AFSK AF、BPSK USB AF 需要不同的转换。12 kHz IF 不应再次 FM 鉴频，AFSK AF 不能直接按 RF-FM 的原始 IQ 输入处理。
5. **输入动态变更/接收器生命周期**：设备切换、丢块、LO/Doppler 重调后，前端 Hilbert/NCO 与 SkyTlm 内部解码状态应在同一世代重新初始化；检查连续相位、队列堆积和旧帧错误关联。
6. **出帧不等于有效业务包**：端到端验收还需要验证 FEC/CRC、长度、卫星归属、用户界面帧计数和 KISS/归档，AO-73 期望 AO40FEC/RS 256 字节。

## 验收基线

- SkyTlm 常用 FSK/GFSK/GMSK/BPSK/AFSK/AX25/AX100/CCSDS/AO40FEC/USP/GEOSCAN 协议测试保持成功。
- SkyRoof 现有 1653 项左右 xUnit 基线保持（以本分支 CI 实际计数为准）。
- UNNE-1B 原始真实 IQ 仍需 CRC 正确 Type1、Type14 与连续六个 CODEC2 帧。
- AO-73 优先使用 `ao73.wav` 测试 48kHz 实录 → Hilbert IQ → SkyTlm BPSK → AO-40 FEC/RS 完整 256 B 帧；记录数字中心频率的调谐假设，不能把人工候选搜索称为自动 AFC。
- 安装包中的 SkyTlm DLL 与经过同一测试的 pinned fork 构建保持 SHA256 一致。
- 尚无用户 IC-9700 实机测试时如实说明，不能因此取消已通过模拟验证的能力。

## 来源
- https://www.amsat.org/two-way-satellites/ao-73-funcube-1/
- https://gr-satellites.readthedocs.io/en/latest/supported_satellites.html
- https://github.com/daniestevez/satellite-recordings
- https://thedocumentation.org/gr-satellites/quick_start/
- https://github.com/Iu-yang1/VE3NEA.SkySignals-Plus

## 真实音频回归结果（源录音固定提交 952ddfe）

固定公开录音仓库提交 `952ddfe53f62a150c53559249c83370630254cab`，对应
[SkyRoof Action #44045074478](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44045074478)
的 Windows 实际接收链测试结果如下：

| 实录样本 | 输入表示 | 数字中心/重构频偏 | 校验正确帧 | 结果 |
|---|---|---|---:|---|
| `ao73.wav` | SSB/USB 真实 AF → Hilbert IQ | 1700 Hz | 1 个 AO40FEC、256 B、RS 有效 | 通过 |
| `ao27.wav` | FM 鉴频后 AF → 重构 FM IQ | 1800 Hz | 2 个 AX.25 校验正确帧 | 通过 |
| `aistechsat3.wav` | FM 鉴频后 AF → 重构 FM IQ | 1200 Hz（4800 Bd / 4） | 5 个 AX100 ASM/FEC 有效帧 | 通过 |
| `suomi_100.wav` | FM 鉴频后 AF → 重构 FM IQ | 2400 Hz（9600 Bd / 4） | 2 个 AX100 ASM/FEC 有效帧 | 通过 |

测试均为原始真实 WAV；没有合成传输帧、软比特或伪造 CRC/RS。公开录音中的
AISTECHSAT-3/Suomi 100 在发送段外存在强噪声和明显满幅削顶（全文件接近满幅
采样分别约 34.6% / 39.4%），因此**不能**把“单纯的 Hilbert 解析音频 IQ 转换”
当作它们的 RF IQ 接收方式。该失败样本经 FM 鉴频音频转换修正后已在同一真实
数据上恢复出 CRC/RS 有效帧。

### 操作员如何选择输入

在 SkyRoof 的 **Settings → Telemetry** 中：

- **SDR IQ**：使用原始复数 IQ；不进行 AF 音频桥接。
- **12-kHz IF**：使用原始 12 kHz IF → 解析 IQ；不进行二次 FM 鉴频。
- **AF capture / loopback → ToneAudio**：适合 USB/SSB 音调，例如 AO-73
  的 1200 Bd DBPSK 遥测（中心可用 1700 Hz）；保留既有默认行为。
- **AF capture / loopback → FmDiscriminator**：适合 FM 接收机已鉴频的
  FSK、GFSK、GMSK 基带；将声音重新转换为复数 FM IQ 后给 SkyTlm 处理。
  `FskAfDeviationHz = 0` 时，按发射机已知 deviation 或 `baud/4`
  估算；若有实测偏差，可手动填 Hz。AFSK Bell-202 保留原有 1800 Hz
  FM 音频桥接行为。

这些选项明确区分输入波形，而**不是**限制用户操作。CRC/RS 才是对真实解码
结果的最终验证。固定中心或偏差参数的实录回归不是完整过境自动 AFC 测试。

## 遗留能力边界

AO-73/FUNcube 的 256 字节 AO-40 FEC 帧解码已经有真实录音证据；
但 SkyTlm 的 Telemetry/Definitions 当前尚无 FUNcube-1 专用的工程量
定义，因而 SkyRoof 即使成功出帧，也可能只显示 RAW HEX/ASCII 与
CRC/RS 元信息，而不显示温度、电压和姿态等字段。必须在取得经过验证的
FUNcube 专用字段映射后再新增工程量解析，不能凭猜测解释位域。


## AO-73 解帧后的业务显示

新增的 `SkyRoof/DSP/Funcube1Telemetry.cs` 为 RS 完整校验的 **256 字节**
AO-73/FUNcube-1 帧提供最小的确定性遥测解释，并接入
`TelemetryPanel.BuildFrameText`。已定义：

- Header：最高 2 bit 为卫星/平台类型，低 6 bit 为帧类型；标准 FC1
  的 WO1–WO12、HR、FM 采用公开类型表；Nayif 等 extended 类型不套用 FC1 表。
- FC1 飞行型号/工程型号：实时 EPS 的光伏板原始 ADC、光电流、电池电压、
  系统电流、重启/软件异常计数、温度传感器原始值、复位原因、MPPT 原始值。
- 固定 SW 位域：24 bit 序列号以及食/安全模式标志。其偏移严格来自
  FC1 实时记录的 EPS 192 bit + BOB 100 bit + RF 48 bit + PA 32 bit +
  Ants 20 bit，SW 从物理帧 byte 50 开始。

这些字段仅在结构正确的 FC1 帧上解释；对 FC2/extended 保留通用头部，
避免把其他卫星的字节误读为 FC1 的电源电压。原始 ADC 值不擅自换算为
电压/电流或摄氏度，官方完整校准和 Whole Orbit 多包重组仍属于后续能力。

独立结构参考：`daniestevez/gr-satellites/python/telemetry/funcube.py`
（GPL-3.0-or-later；本仓库仅实现公开的字节/位域功能事实，不移植其程序代码）。

## 流式输入块边界验收

新增 `SatelliteRecordedAudioRegressionTests.RecordedAudio_BlockBoundariesPreserveIqPhaseAndVerifiedFrames`：
对 AO-73 的 Hilbert/NCO 和另外三个样本的 FM-IQ 桥接分别采用混合大小块
（1、17、97、231、1600、4093、4096、4800 采样）逐块输入，
与同一整段 PCM 的单次转换逐样本比较 IQ，并再次要求实际
CRC/RS 有效帧。这直接检验 Windows/WASAPI 真实采集块长度不规则时，
Hilbert 滤波器、NCO 与 FM 相位累积是否连续；它不代替硬件接收验证。
