# Remote Telemetry PCM V1

独立 DSP 复用仓库 vendored VE3NEA.SkyTlm、VE3NEA.Dsp 与既有 TelemetryRealAudioIqConverter / TelemetryAfskFmIqConverter；不在 Web 浏览器执行 modem。当前输入是 station RX SignalBus 48 kHz PCM。选择 tone AF、FM discriminator 或真实 12 kHz IF，必须对应实际采集信号；此选项不切换电台硬件输出，不是复数 SDR IQ 输入。

- 手动可配 NORAD / transmitter / baud / modulation / framing / deviation / differential / Manchester。AO-73、AO-27、UNNE-1B 初始预设；AO-123 CCSDS short-KISS 对正确 NORAD 和频率的配置才启用，API 支持 CCSDS framing 参数。
- Worker 有界队列；配置和 PCM 同序，源 epoch 或采样间断销毁旧 modem，禁止跨间断拼帧。显式 STOP flush，旧配置的迟到块不进入新 modem。
- 保留 CRC/FEC true/false/null；AO-73、UNNE-1B 复用已有字段解释，ASRTU 内包标注原始外层 RS 证据，不能当作内包 CRC。Web 读取最近 64 记录和完整 HEX，暂不提供长期 archive、KISS TCP、SatNOGS 上传、SSTV/CODEC2 播放、自动 transmitter resolver、直接 SDR IQ。
- 认证 GET 和 Operator/Admin PUT /api/v1/dsp/telemetry。Dockview 工具栏 TELEMETRY 打开独立面板，不强制改写已有布局。
- 新 CI 固定真实 off-air 录音仓库 952ddfe53f62a150c53559249c83370630254cab，测试 AO-73 / AO-27 / AISTECHSAT-3 / Suomi 100 的 CRC/FEC，再将 AO-73 真实 PCM 送入发布版独立 DSP 共享内存，通过 gRPC 配置与 STOP，核对 256-byte 输出、HEX 和字段。新 CI 尚未完成，旧 WinForms 证据不等同本次迁移验收。

Q65 AP 增量：My Call、DX Call/Grid、AP off/CQ/full、QSO progress、Max Drift（bins）、Pileup RX、B90×Ts、Gaussian/Lorentzian 直接传递 mfsk C ABI v3。仍未实现自动 QSO 或月面跟踪。
