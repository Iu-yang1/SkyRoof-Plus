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
