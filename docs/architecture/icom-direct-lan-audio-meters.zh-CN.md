# Icom Direct LAN 双向音频与 CI-V 仪表

## 入口与功能
在 `Tools → Settings → Icom Direct LAN` 展开 **Receive audio / Transmit audio / Meter readback**。
电台 IP、用户名、密码及 Direct LAN Scope Source 仍在 `Icom LAN Spectrum` 配置，二者共用一次认证会话，并不另建 RS-BA1 登录。

- **AF Transmission (RX)**：允许电台向软件送出接收音频。选定 Windows `Speaker` 可本地播放；留空时使用系统默认扬声器。若不希望本地播放，可关闭该输出或将音频交给独立 IF→AF 链路处理。
- **MOD Transmission (TX)**：在电台支持的采样率内协商 TX 通道；选择 Windows `Microphone` 进行采集，留空时使用系统默认录音设备。麦克风可指向虚拟声卡录音端，适合 WSJT-X 等软件音频。音频数据本身不会自动打开 PTT，只有 SkyRoof 当前 CAT 引擎实际读回 PTT ON 时才发送。
- RX/TX 采样率可选 8–48 kHz 多档，编码可选单声道 LPCM16 / PCM8 / u-law；默认双方 48 kHz、LPCM16，接收预缓冲 120 ms，发射预缓冲 150 ms。
- **Retransmission Audio Data**：在网络设备按序号请求音频上行数据重发时，从本会话的历史缓冲重发；不会改变 PTT。
- **Meter readback**：可分别打开 S、PWR、ALC、SWR，调整轮询间隔，优先在底部状态栏显示。显示基于 IC-9700 的 CI-V `15 02/11/13/12` 数值，不是额外的 RF 传感器，也不代表经校准的瓦特数。

## 电台操作说明
- IC-9700 必须设置 Network Control 为 ON，使用电台实际配置的网络账户及密码。
- 使用 LAN TX 音频时，电台实际的 MOD Input Select 应切到 LAN；如果电台选为 MIC 或 ACC，即使网络上传 PCM 正常也不会有预期的调制。
- 当前的 Direct LAN 连接只建立音频和 CI-V 媒体流，不提供 Windows 虚拟 COM 驱动。仍可使用 SkyCAT/现有 CAT 后端的 PTT、频率和多普勒功能，注意 CAT 连接实际所有权。
- 本地扬声器链输出网络 RX AF。如果电台设置为网络 IF 且启用了本地扬声器，将听到 IF 而不是已解调的语音；应使用 IF→AF 设置或选择电台 AF 输出。
- 只有开启 `Icom LAN Spectrum → Source = DirectLan` 并手动 START 后，才有本功能所需的 Direct LAN 会话。修改设置并 Apply/OK 会刷新该会话。

## 2026-11-11 回归问题与诊断

本次 IC-9700 用户现场日志证明：正常认证后的音频 UDP 链路可以收到 1,522 个数据包，但旧 RX 解码器成功计数为 0。旧代码依赖有限的报文 ID 白名单，并要求内层 dataLength 与 UDP 负载长度完全一致；实际 Icom RX 报文格式可能随固件和编码变化。修复后仍严格校验外围 UDP 长度、认证会话 ID 和 PCM 样本对齐，并优先根据 UDP 实际负载边界解码。 接收缓冲的重排序改为使用 Icom 音频 UDP **外层传输序号**（而非可能保持不变的内层音频序号），避免收到有效 PCM 后仍被重排序器当作重复包丢弃。

关闭 Direct LAN 时，先取消采集并给协议层发送关闭/退出请求的机会；只有正常退出失败才强制关闭 UDP 套接字。握手异常现在区分 **control greeting、CI-V greeting、audio greeting**，不是一律报告笼统的 timeout。

底栏四表位于 **Rotator 状态之后、CPU Load 之前**，右侧优先保留可见空间；本地没有新的有效读回时显示 `--`，不应将其理解成仪表真正读到了 0。

### 新日志关键字段

- `Direct LAN media: RX UDP=...`：收到网络音频包，不等于解码成功。
- `decoded=...` 与 `rejected=...`：RX 成功解码与帧校验失败数。首次解码还记录采样数和 RMS，以判断是否全静音。
- `rejected RX UDP audio framing`：记录报文长度、类型、标识、声明长度、会话 ID 是否匹配，不记录凭据和 PCM 内容。
- `meter requests=...` 与 `readbacks=...`：区分已发送 CI-V 请求、成功关联回传。
- `TX UDP=...` 与 `TX rejected/no CAT PTT=...`：区分已上行数据与 CAT 未报告 PTT ON。

**现场复测顺序**：启动 Direct LAN，确认 `control/CI-V/audio` 均连接；观察 RX decoded 是否增加；切换一颗卫星，确认会话和四表不中断；然后在电台 MOD 选择 LAN 的前提下，通过已生效 CAT PTT 短时验证 TX UDP 计数。当前没有独立 IC-9700 录包，因此完整的网络音频互操作性仍需实机验收。

## 2026-11-11 第二次实机日志复测：仪表与上行音频可观测性

现场日志 `SkyRoof_20261111_072.txt` 显示：

- RX 音频累计 9,564 包且全部解码成功，说明 RX 网络音频不再被解码器拒绝。
- TX 音频累计 1,784 个 UDP PCM 包，证明操作系统音频已经进入 SkyRoof 发送路径；**不是 RF 输出确认**。
- CI-V 四表累计请求 116 次，但成功匹配回传为 0；原 Direct LAN 轮询仍保留作备用，优先通过 SkyCAT 已连接的 CI-V owner 读取 `U METER_READ_FIELD S|PWR|ALC|SWR`，不要让另一个网络认证会话争用仪表应答。
- 若 SkyCAT 仪表读取连续失败，CAT 工作线程会短暂退出该轮询并在 15 秒后恢复尝试；切回 Direct LAN 原轮询，优先保证频率跟踪、PTT 的响应速度。
- 底部状态栏显示 **LAN TX n pkts · MIC x dBFS**：实时展示已提交到 UDP 的音频包数量和最近成功发送 PCM 的 RMS 电平。显示 `PTT ON · no PCM` 表示当前 CAT 已按下 PTT，但软件端没有新的有效音频包；`LAN TX offline` 表示音频连接未建立。
- 该本地 TX 电平不是 IC-9700 内部 ALC 值，真正的射频功率、驻波和 ALC 仍必须以电台 CI-V 仪表读回或独立测量为依据。未接入真正的 LAN MOD 输出回读，不能据此宣称 RF 调制成功。
- 控制端最初的 `control greeting` 超时在用户日志中持续一段时间后自行恢复。由于没有同步网络抓包，这可能与会话释放/其他远控客户端竞争有关，当前无法将其定性为电台固件故障。

## 2026-11-11 第三次现场截图：SSB/FM 电平与多普勒

- IC-9700 的 CI-V `15 02/11/12/13` 分别读取 S/PO/SWR/ALC；原始读数是 BCD 0–255 相对刻度，不是经过功率计校准的瓦数。ALC 高并不能当作 RF 功率高。
- **收信状态**：优先只读主接收机的 S 表；软件不因 S0 自动补造 SNR 或信号强度。若在已知强信号（例如接收机前面板 S9）时仍返回 `raw=000`，需结合 SkyCAT 日志的 `15 02` 实测读数和 MAIN/SUB 工作焦点复查。SkyCAT 的 Duplex CAT 配置已经在读 TX 频率结束后将频段焦点恢复至主接收机。
- **发信状态**：只采样 PO/ALC/SWR，且采样预算优先 PO 与 ALC；这些读数来自不同时间点，不应理解为同时瞬时测量。短时 SSB 语音功率可能在低优先级轮询的采样间隔中消失。界面 `PWR↑` 只表示 **1.5 秒内电台曾返回过更高 PO 样本**，不是用 ALC、MIC dBFS 推算出来的 RF 功率。频率调度中 PTT/多普勒优先于仪表。
- SkyCAT 的低优先级 `15 xx` 读操作最长占用从 **650 ms 降至 250 ms**。周期性输出 `IC-9700 meter CI-V 15 XX: raw=..., latency=... ms`，便于下次查明 S0 是真实 `0000` 还是响应超时。连续有待写频率/模式/PTT 时优先完成关键 CAT 操作，仪表每约 1.2 秒保留一次刷新机会。
- SkyRoof 的 SkyCAT 四表轮询受 500 ms UI 计时器驱动，实际最小间隔是 **500 ms**，即使在设置中输入更小值也不会更频繁地触发 SkyCAT 读操作。Direct LAN 原生备用读回仍使用设置中的原生轮询间隔。
- **本次没有电台前面板或外接功率计同时刻记录**，故不能确认截图中 SSB `PWR 0%` 是否确实没有 RF，也不能证明 `S0` 是接收机选择错误。必须在有可复现强信号的情况下对照前面板和 `raw` 日志验证。

## 2026-11-12 TX 语音断续修复与验证

实机监听报告 LAN TX 语音偶尔断续。旧发送链使用 WASAPI 采集回调作为 UDP 发送节奏，采集回调可能突发，且没有回调时 UDP 发送立即暂停。此现象并不直接证明网络丢包或 PTT 失效。

新发送链以**每 20 ms 一帧 PCM**定时输出，按采样率精确计算每帧样本数（包括 11.025/22.05 kHz 等不能整除 50 的采样率），单 UDP 音频负载上限保留为 1364 字节。额外使用约 60 ms 本地启动缓冲吸收 Windows 回调抖动，最多保留 250 ms 未发送语音；临时采集欠载时补静音，不再让发往电台的 UDP 音频时钟停止。PTT 关闭时清除旧语音，不会替用户触发 PTT。

每 5 秒记录的 `Direct LAN TX pacing` 诊断字段：

- `frames`：已执行的 20 ms 音频时钟帧；`TX UDP` 是实际音频 UDP 包数，两者不同，因为 LPCM16 48 kHz 的一帧需拆成两包。
- `lateTicks`：20 ms 定时器两次唤醒相隔超过 30 ms 的次数；持续增加意味着本机调度或发送过程出现长暂停。
- `captureQueueOverflows`：WASAPI 回调源缓冲接近上限、可能舍弃旧音频的次数。
- `PCM overflowSamples`：250 ms 本地抖动缓冲实际舍弃的旧采样数；`underrunSilenceSamples`：启动完成后因为缺少采集数据而补的静音样本数。
- `PTT interruptions`：TX 语音 worker 曾经识别到 PTT ON 后又识别为 OFF 的次数。正常结束通联也会增加一次；**如果按住 PTT 持续讲话期间增加，需要排查 CAT 状态和 PTT 回读抖动**。
- `bufferedSamples`：当前待发送的本地 PCM 样本数（48 kHz 时 960 样本为 20 ms）。

**实机复测**：建议与对方连续测试 30–60 秒 SSB 语音，或使用独立接收机录制；同时观察本机 TX 发射是否始终保持、`lateTicks`、`underrunSilenceSamples` 和 `captureQueueOverflows` 是否增加。若统计稳定但对方仍然听到间歇，应继续核对电台 LAN MOD 输入、网络丢包、外部监听录音，以及端到端实际 PTT 状态。官方 RS-BA1 / wfview 的经验同样表明 TX 预缓冲过低、网络负载过高和高采样率可能导致断续；这是后续排查方向，不是自动化测试已经证明的故障根因。

## 2026-11-12 TX/RX 预缓冲重新核对与 TX 语音 AGC

本轮拆分了原来共用 `Transmit.PrebufferMs` 的两套功能。原有 JSON 字段 `PrebufferMs` 保持原名和默认 150 ms，**仅用于发给 IC-9700 的 RS-BA1 TX network buffer 协商字段**；新字段 `CaptureBufferMs`（默认 150 ms）单独用于 Windows WASAPI `InputSoundcard` 录音缓冲；`JitterPrefillMs`（默认 60 ms）与 `JitterMaximumMs`（默认 250 ms）分别设定 SkyRoof 20 ms 音频定时发送器的启动预填充量和最大积压量。

RX 的 `Receive.PrebufferMs` 默认 120 ms，只传给 48 kHz `OutputSoundcard` 的 `latencyMilliseconds` 参数；它不更改电台 UDP 接收参数。RX 网络包还受 `IcomNetworkIfPcmPipeline` 最大 8 包／100 ms 的乱序等待窗口影响，这是不同层级的缓存。RX 扬声器音量 `SpeakerVolume`（0–100%）设置本地播放器音量，不修改无线电的 AF 增益、RF 增益或 LAN 原始音频。

TX 固定 `MicrophoneGain` 是 0–200% 的软件 PCM 倍率（100% = 0 dB、200% ≈ +6.02 dB），在音频编包前应用。新增 `Transmit → Voice AGC`：默认**关闭**，供 SSB/FM 语音手动开启；不要对 FT4/FT8/Q65 或经过校准的数字调制信号开启。语音 AGC 为每个 20 ms PCM 帧维护连续增益状态，按帧 RMS 和目标电平调整，静音／低于噪声门限时不提升到语音目标，并使用峰值上限避免数字采样削波。停止 PTT 时重置 AGC 增益状态，不进行独立的 CAT/PTT 操作。

| 参数 | 默认值 | 作用 |
|---|---:|---|
| TX network prebuffer | 150 ms | 电台上行音频缓冲协商 |
| TX microphone capture buffer | 150 ms | Windows 录音缓冲 |
| TX software jitter prefill | 60 ms | 软件启动预填充 |
| TX software jitter maximum | 250 ms | 软件缓冲上限，超出丢弃最旧 PCM |
| RX speaker playback buffer | 120 ms | Windows 播放端延迟 |
| Microphone gain | 100% | AGC 前固定软件增益 |
| Voice AGC enabled | false | 语音电平自动控制 |
| Voice AGC target RMS | -18 dBFS | 语音目标平均电平 |
| Voice AGC maximum gain | +12 dB | 微弱语音最大增益 |
| Voice AGC noise floor | -48 dBFS | 不把背景噪声抬到语音目标 |
| Voice AGC attack / release | 40 / 450 ms | 增益下降与恢复的时间常数 |
| Voice AGC peak ceiling | -3 dBFS | 软件 PCM 峰值上限 |

每 5 秒的 `Direct LAN TX levels` 日志新增实际配置值、`voiceAgc`、`agcGain`、`agcFrames`，与 `TX pacing` 的欠载／丢包日志配合核对。注意 `agcGain`、`MIC dBFS` 仍只是本机 PCM 数据，不代表 IC-9700 的 ALC 或 RF 输出功率。SSB 模式最终应以电台 ALC 刻度和另一个台站的接收录音判断过载与失真；Icom 手册建议正常讲话 ALC 峰值大致落在 30–50% 刻度范围。

## 2026-11-12 MIC 有读数但 IC-9700 完全没有调制

用户实机确认：SkyRoof 状态栏显示 `MIC -17 dBFS`，发射时电台没有实际调制，PO 为零，ALC 未有有效读数。**PCM 包已经由本机送到 UDP 端口，并不证明 IC-9700 选择了 LAN 作为调制源。** 新增加电台端寄存器确认：

- IC-9700 `MENU → SET → Connectors → MOD Input`：`DATA OFF MOD`（非 DATA 的 SSB/FM/AM）和 `DATA MOD`（USB-D/FM-D 等数据模式）均支持 `LAN`；出厂默认分别为 `MIC,ACC` 和 `ACC`。
- CI-V 官方寄存器 `1A 05 0115` 为 DATA OFF MOD、`1A 05 0116` 为 DATA MOD；`05` 代表 LAN。`1A 05 0114` 为 LAN MOD Level，返回 0000–0255 的相对 BCD 刻度。
- Native Direct LAN TX 开始后按低优先级轮询三个寄存器并显示真实回读状态；选中 `Transmit → Auto select LAN MOD input`（默认 ON）时，只有已确认电台当前选择非 LAN，才向对应寄存器发送 `05` 写入，并通过新的电台回读核验；如果电台持续报告非 LAN，最多重试三次，不假设发出命令就是已生效。
- 用户可以关闭自动选择，以保留电台的手动输入源。**自动选择是电台级持久设置，并不保证断开 SkyRoof 时自动恢复 MIC；使用手持麦克风或本机讲话前，请按需将该设置改回 MIC/MIC,ACC。**
- 自动选择不发送 PTT、频率、模式或 LAN MOD Level 增益写入；LAN MOD Level 若为 0%，会显示警告但不会擅自放大电台的输入电平。TX AGC 与这个寄存器设置互相独立。

**确认方法**：在电台前面板选择 SSB，确认 `DATA OFF MOD=LAN`、`LAN MOD Level` 非零，按住已核验的 PTT 对麦克风讲话，观察电台自身 ALC 与 PO，同时使用独立接收机监听。若仍无调制，收集 `Direct LAN TX modulation source`、`Direct LAN media`、`Direct LAN TX pacing` 和 SkyCAT PTT 回读日志，进一步核对发送的音频报文及实际收发状态。

## 验收边界
代码层提供 CI-V 相关响应校验、设备能力采样率协商、LPCM/PCM/u-law 封包与解码测试、状态栏数据显示与 stale 处理。Windows x64 CI 自动构建及单元测试不是实际 IC-9700 网络音频发射验收。需用真实电台检验 RF PTT 与 LAN MOD 模式、分组乱序/丢包及音频延迟，保留原有 RS-BA1 方案作为对照。

参考：Icom IC-9700 CI-V Reference Guide；wfview [Icom UDP 协议结构](https://github.com/wf-group/wfview/blob/master/include/packettypes.h)。
