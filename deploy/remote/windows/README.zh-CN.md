# SkyRoof Remote V1 Windows x64 多进程开发测试包

此包包含 Device、Station、Media、Dsp、Host、Supervisor 六个 .NET 10 进程，以及 React/Dockview 网页，不是完整 SkyRoof Remote V1 正式版本。

- Device：独立控制 SkyCAT/rigctld 兼容 CAT TCP；处理上下行频率、模式、PTT、设备读回、崩溃事务恢复与超时释放
- Station：独立 gRPC 命名管道控制入口
- Media：独立 gRPC Windows Named Pipe RX PCM16 入口，将真实来源提供的 PCM 帧写入 SignalBus，并与 Host 的认证 WSS 音频端点联动
- Dsp：从 Media 共享内存 PCM16 产生真实 AF Hann-FFT 音频频谱，发布 475-bin AF Spectrum SignalBus
- Host：Identity/SQLite、MessagePack SignalR、身份与权限、网页
- Supervisor：普通后台运行并兼容 Windows Service 托管，可监管上述进程

Windows x64 需 .NET 10 Runtime 和可访问的本机 SkyCAT/rigctld 服务。在 supervisor.json.example 基础上创建 supervisor.json，配置安装目录、台站设备连接、CAT 端口、服务环境变量和本机管理员初始化凭据 SKYROOF_BOOTSTRAP_ADMIN_USER / SKYROOF_BOOTSTRAP_ADMIN_PASSWORD。启动后管理员账号密码以 Identity 哈希形式写入 SQLite；不要将明文凭据提交到仓库。

在 PowerShell 设置 SKYROOF_SUPERVISOR_CONFIG 环境变量指向 supervisor.json，再执行 dotnet 路径/到/Supervisor/SkyRoof.Remote.Supervisor.dll。开发模式 Host 默认 http://127.0.0.1:8076；外网使用必须由 HTTPS/WSS 代理保护。FRP TCP/QUIC 与 UDP 的单独部署文档见 deploy/remote/README.zh-CN.md。

**验收方式：** GitHub Actions 从发布目录实际启动 Device、Station 和 Host，连接模拟 rigctld TCP 端点，验证多用户 PTT、角色/Token、断开释放等。不能将此结果视为 IC-9700 Direct LAN、音频/WebRTC Audio Track、DSP、卫星跟踪或 Windows Service Session 0 的完整验收。

**回滚：** 停止独立 Supervisor 子进程，不覆盖原 SkyRoof。确保发射已释放后可重新运行原版 SkyRoof；保留 SQLite 用户数据和本机事务日志以便问题追溯。

## Media Service RX PCM 接入（开发阶段）

从 Windows 本机私有管道 SKYROOF_MEDIA_PIPE（示例：skyroof-station-media-primary）调用 MediaIngress.PublishRx 客户端流式 RPC。传入 PCM16 小端、48 kHz、单/双声道，每包 1–960 sample/channel，附单调递增 FirstSampleIndex。gRPC 的首次 PublishRx 建立新的音源代际，后接入的音源接管共享流，旧音源不再写入。时间戳与序号由服务端 SignalBus 记录；传输期间的样本缺口可在 RxStreamReceipt.SamplesSkipped 诊断，数据不会被误重放。Host /ws/audio 使用相同 SKYROOF_RX_AUDIO_BUS 和匹配的 SKYROOF_RX_AUDIO_CHANNELS（1 或 2）供合法已登录用户消费。

当前不包含真实 IC-9700 Direct LAN 捕获、RX PCM 自动重采样、TX PCM 或数字模式定时发射，也不应把此入口作为公网服务暴露。

## AF Spectrum（音频 FFT）

在独立 Dsp 进程的 SKYROOF_RX_AUDIO_BUS 指定与 Media 相同的 RX PCM 总线，在 SKYROOF_AF_SPECTRUM_BUS 指定输出总线，并将相同输出配置给 Host。客户端登录后通过 /ws/af-spectrum 获取实际 48 kHz PCM 音频 FFT：4096 点 Hann 窗、1600 点步进、理论约 30 帧/秒，0–24 kHz 共 475 个功率电平（dBFS），每帧拥有 Epoch 与序号。浏览器的 AF Spectrum 独立窗口渲染这些实测数据。不能据此推断电台 RF/IF 频谱；该功能不提供任何 RF 发射或旋转控制。

## Windows Service 双模式宿主

管理员 PowerShell：先用 dotnet publish -r win-x64 得到 Supervisor exe，再执行 Install-RemoteService.ps1，传入 -Executable、-Configuration 及可选 -ServiceName、-Startup auto。安装完成后执行 Start-Service SkyRoofRemote。普通后台运行仍使用 SKYROOF_SUPERVISOR_CONFIG；SCM 启动通过 --config 指向同一份 JSON。卸载执行 Remove-RemoteService.ps1，保留 SQLite 及设备持久化数据。Windows Service 的 Session 0 与交互式 WinForms 不同，实机时需核对服务账户对串口、CAT 服务、命名管道和用户数据目录的访问权限。独立 CI 使用真实 SCM 注册/启动/停止和受管子进程退出检查；该模拟不代表已完成真实设备的 Windows Service 访问测试。
