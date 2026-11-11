# Icom Direct LAN 双向音频与 CI-V 仪表

## 入口与功能
在 `Tools → Settings → Icom Direct LAN` 展开 **Receive audio / Transmit audio / Meter readback**。
电台 IP、用户名、密码及 Direct LAN Scope Source 仍在 `Icom LAN Spectrum` 配置，二者共用一次认证会话，并不另建 RS-BA1 登录。

- **AF Transmission (RX)**：允许电台向软件送出接收音频。选定 Windows `Speaker` 可本地播放；留空则不启用本地扬声器，已有 IF→AF 接收链仍可独立消费原始接收数据。
- **MOD Transmission (TX)**：在电台支持的采样率内协商 TX 通道；选择 Windows `Microphone` 才进行采集。麦克风可指向虚拟声卡录音端，适合 WSJT-X 等软件音频。音频数据本身不会自动打开 PTT，只有 SkyRoof 当前 CAT 引擎实际读回 PTT ON 时才发送。
- RX/TX 采样率可选 8–48 kHz 多档，编码可选单声道 LPCM16 / PCM8 / u-law；默认双方 48 kHz、LPCM16，接收预缓冲 120 ms，发射预缓冲 150 ms。
- **Retransmission Audio Data**：在网络设备按序号请求音频上行数据重发时，从本会话的历史缓冲重发；不会改变 PTT。
- **Meter readback**：可分别打开 S、PWR、ALC、SWR，调整轮询间隔，优先在底部状态栏显示。显示基于 IC-9700 的 CI-V `15 02/11/13/12` 数值，不是额外的 RF 传感器，也不代表经校准的瓦特数。

## 电台操作说明
- IC-9700 必须设置 Network Control 为 ON，使用电台实际配置的网络账户及密码。
- 使用 LAN TX 音频时，电台实际的 MOD Input Select 应切到 LAN；如果电台选为 MIC 或 ACC，即使网络上传 PCM 正常也不会有预期的调制。
- 当前的 Direct LAN 连接只建立音频和 CI-V 媒体流，不提供 Windows 虚拟 COM 驱动。仍可使用 SkyCAT/现有 CAT 后端的 PTT、频率和多普勒功能，注意 CAT 连接实际所有权。
- 本地扬声器链输出网络 RX AF。如果电台设置为网络 IF 且启用了本地扬声器，将听到 IF 而不是已解调的语音；应使用 IF→AF 设置或选择电台 AF 输出。
- 只有开启 `Icom LAN Spectrum → Source = DirectLan` 并手动 START 后，才有本功能所需的 Direct LAN 会话。修改设置并 Apply/OK 会刷新该会话。

## 验收边界
代码层提供 CI-V 相关响应校验、设备能力采样率协商、LPCM/PCM/u-law 封包与解码测试、状态栏数据显示与 stale 处理。Windows x64 CI 自动构建及单元测试不是实际 IC-9700 网络音频发射验收。需用真实电台检验 RF PTT 与 LAN MOD 模式、分组乱序/丢包及音频延迟，保留原有 RS-BA1 方案作为对照。

参考：Icom IC-9700 CI-V Reference Guide；wfview [Icom UDP 协议结构](https://github.com/wf-group/wfview/blob/master/include/packettypes.h)。
