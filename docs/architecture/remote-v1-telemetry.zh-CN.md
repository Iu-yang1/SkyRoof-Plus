# Remote Telemetry PCM V1

独立 DSP 复用仓库 vendored VE3NEA.SkyTlm、VE3NEA.Dsp 与既有 TelemetryRealAudioIqConverter / TelemetryAfskFmIqConverter；不在 Web 浏览器执行 modem。当前输入是 station RX SignalBus 48 kHz PCM。选择 tone AF、FM discriminator 或真实 12 kHz IF，必须对应实际采集信号；此选项不切换电台硬件输出，不是复数 SDR IQ 输入。

- 手动可配 NORAD / transmitter / baud / modulation / framing / deviation / differential / Manchester。AO-73、AO-27、UNNE-1B 初始预设；AO-123 CCSDS short-KISS 对正确 NORAD 和频率的配置才启用，API 支持 CCSDS framing 参数。
- Worker 有界队列；配置和 PCM 同序，源 epoch 或采样间断销毁旧 modem，禁止跨间断拼帧。显式 STOP flush，旧配置的迟到块不进入新 modem。
- 保留 CRC/FEC true/false/null；AO-73、UNNE-1B 复用已有字段解释，ASRTU 内包标注原始外层 RS 证据，不能当作内包 CRC。Web 读取最近 64 记录和完整 HEX，暂不提供长期 archive、KISS TCP、SatNOGS 上传、SSTV/CODEC2 播放、自动 transmitter resolver、直接 SDR IQ。
- 认证 GET 和 Operator/Admin PUT /api/v1/dsp/telemetry。Dockview 工具栏 TELEMETRY 打开独立面板，不强制改写已有布局。
- 新 CI 固定真实 off-air 录音仓库 952ddfe53f62a150c53559249c83370630254cab，测试 AO-73 / AO-27 / AISTECHSAT-3 / Suomi 100 的 CRC/FEC，再将 AO-73 真实 PCM 送入发布版独立 DSP 共享内存，通过 gRPC 配置与 STOP，核对 256-byte 输出、HEX 和字段。新 CI 尚未完成，旧 WinForms 证据不等同本次迁移验收。

Q65 AP 增量：My Call、DX Call/Grid、AP off/CQ/full、QSO progress、Max Drift（bins）、Pileup RX、B90×Ts、Gaussian/Lorentzian 直接传递 mfsk C ABI v3。仍未实现自动 QSO 或月面跟踪。


## 2026-11-25 第二阶段

- `f854204` [真实录音 CI 45518011776](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45518011776) 实际通过：AO-73 1、AO-27 2、AISTECHSAT-3 5、Suomi 100 2 个 CRC/FEC 有效帧；AO-73 发布版独立 DSP SignalBus / gRPC / 256 B / 工程量字段均通过。
- `7e0935f` 复用既有 TelemetryFrameArchive 和 KISS TCP，显式 SAVE BIN+LOG / KISS PORT。默认 archive %LOCALAPPDATA%/SkyRoof/Remote/TelemetryDecodes/Binary，可用 SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY 改路径；默认 KISS 127.0.0.1，SKYROOF_TELEMETRY_KISS_BIND 可设站点地址。KISS channel 0 外层、1 内包、2 CRC fail，客户端命令忽略。STOP/切换 flush 旧帧和 archive 队列；KISS 监听可保留以发送末帧。KISS 端口设 0 关闭。
- `5cc9bad` 新增真实 AO-73 帧 BIN / LOG / KISS TCP payload 一致性与 HTTP Q65/Telemetry JSON result 测试，未取得最终结果。
- `9012304` / `e571417` 从现有 Station catalog 的 manual / gr_sats / SatNOGS 字段生成 decoder profile；相同发射机、频率与调制保留原桌面 SatYAML 元数据，manual 优先；不从未知 framing 猜测协议。UI LOAD TELEMETRY PROFILE，保留实际 AF/IF 选择与手动修改；支持 CCSDS frame size、RS、scrambler、convolutional、NRZI。UNNE 协议 ID 与实际 NORAD 分离。此阶段不等于独立在线完整 SatYAML/JE9PEL enrichment、自动调谐或业务校准。
- 当前遗留：独立 SDR IQ 数据路径、自动跟踪/调谐联动、SatNOGS 上传、SSDV/SSTV/语音、远程归档列表和下载；实机测试尚无。


## 2026-11-25 第三阶段：SatNOGS SiDS 和复数 IQ

旧段落关于不支持 KISS、上传与直接 IQ 的说法以本节增量为准。

DSP 中 `SKYROOF_SATNOGS_TOKEN`、`SKYROOF_STATION_CALLSIGN`、`SKYROOF_STATION_GRID` 必须有效，配置 UploadSatnogs 才会发送。默认 https://db.satnogs.org/api/telemetry/，`SKYROOF_SATNOGS_TELEMETRY_URL` 仅允许 HTTPS 或本机 HTTP fixture。独立 128 项队列，超容量有计数，失败不阻塞解码。CRC 明确失败和 ASRTU 派生内包不上传；未知 CRC 保留原协议语义。STOP 不取消已接收排队帧，进程退出可取消未发送项；不承诺持久化投递。API token 留在台站，不发给浏览器。CI 使用本地 HTTP 校验实际 AO-73 HEX、NORAD、呼号、网格坐标、UTC 与认证头，不访问公共上传服务。

本机受信任生产者可以连接 DSP 的 `SKYROOF_DSP_PIPE`（默认 skyroof-station-dsp），先通过 ConfigureTelemetry 选择 Input=iq-cf32，之后使用 DspWaveform.PublishTelemetryIq 客户端流式 RPC。TelemetryIqBlock 为 48 kHz 基带 CF32 小端交织 I/Q，1–4096 complex samples/block，FirstSampleIndex 单调连续（间隙将重建 demodulator）。单生产者拥有本次流；配置变更使旧流失效。正常 EOF 在所有样本处理后 flush modem/archive，并返回 epoch、blocks、samples。该输入不经过 AF converter；不是宽带 ADC IQ 自动调谐或 SDR 驱动。

固定公开真实样本 N6RFM/HADESx-Decoder 238386904c44a60657bac0039c2edb681446d2c0，经脚本核对 blob SHA1 与长度；回归将原始 50 kHz 捕获以 48 kHz 送入私有 IPC，保留 800 Hz residual CFO。断言 type 1/14 CRC 有效及 clock 192304/192124。打包 CI 固定 SkySignals-Plus 72137519f9f4d59ebbc3fc0a788405ce2e2e37f5，使用 native UNNE deframer；旧 vendor 编译兼容 fallback。新 [Remote Telemetry Recorded IQ](https://github.com/Iu-yang1/SkyRoof-Plus/actions/workflows/remote-telemetry-iq.yml) 尚未完成；不声称实机 SDR 或语音播放已通过。
