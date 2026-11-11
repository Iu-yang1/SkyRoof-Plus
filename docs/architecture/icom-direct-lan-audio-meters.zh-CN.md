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

## 验收边界
代码层提供 CI-V 相关响应校验、设备能力采样率协商、LPCM/PCM/u-law 封包与解码测试、状态栏数据显示与 stale 处理。Windows x64 CI 自动构建及单元测试不是实际 IC-9700 网络音频发射验收。需用真实电台检验 RF PTT 与 LAN MOD 模式、分组乱序/丢包及音频延迟，保留原有 RS-BA1 方案作为对照。

参考：Icom IC-9700 CI-V Reference Guide；wfview [Icom UDP 协议结构](https://github.com/wf-group/wfview/blob/master/include/packettypes.h)。
