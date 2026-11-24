# SkyRoof Remote V1 — P0–P18 实施与验收状态

分支：feature/remote-v1-multiprocess-foundation；Draft PR #254。当前**不能**认为 P0–P18 已全部完成，也不能将开发测试包当成 Remote V1 正式 Release。

| 阶段 | 已提交、已纳入自动化验证的实现 | 仍缺少的发布条件 |
|---|---|---|
| P0 | 核对原 SkyRoof RadioLink、CAT/SkyCAT、Direct LAN、CI，建立独立测试基线 | 全部旧版性能回归基线 |
| P1 | Protobuf、gRPC Named Pipe、共享内存 SignalBus、流 Epoch/Sequence/SampleIndex | 生产 IPC ACL、协议升级兼容 |
| P2 | Supervisor 普通后台/Windows Service 双模式、SCM 实际注册启停 CI、崩溃恢复、Device PTT 和已配置旋转器 STOP 退出处理 | 实际硬件在 Session 0 的账户/ACL 和升级迁移验收 |
| P3 | 独立 Device：SkyCAT/rigctld CAT/PTT、读回和异常恢复；rotctld AZ/EL/STOP、机械限位、STOP 独立连接、多路径点 Park。新增独立 Icom LAN UDP 控制登录、认证 Token、授权流、保活和 CI-V/Audio 端口预绑定的可选接收 Worker | IC-9700 Direct LAN 实机登录/CI-V 完整 CAT、RF/IF 频谱、TX 音频、自动路径优化、RC-3040S 专用 Park 校验、SDR/ONVIF |
| P4 | Station gRPC + Orekit/SGP.NET 实轨道传播、EOP 缓存、连续跟踪与 Doppler、重启恢复 | 原 RadioLink 转发器数据库/映射、ISS OEM Hybrid、轨道源切换和完整排程迁移 |
| P5 | 独立 Dsp：SignalBus RX PCM16 + 4096-point Hann FFT 音频频谱、真实 1/2.5 kHz CI | FT4、CW、Q65、遥测等数字解码器迁移，当前仅音频 FFT |
| P6 | 独立 Media：PCM16 SignalBus、Opus 本机环回、认证 WSS/WebAudio、Icom UDP PCM16/PCM8/μ-law、8–192 kHz→48 kHz 有状态 ASRC、UDP 乱序/重复/缺包恢复及实时认证诊断；Device RX Worker 已连到 Media 私有 IPC | 当前新增整套真实 UDP 模拟器 E2E CI 待最终确认；IC-9700 实机登录/RX、外部网络压测、WebRTC Audio Track/RTC Data 动态切换、TX PCM 仍未完成 |
| P7 | 共用跨进程命令契约 | 原 WinForms 生命周期解耦及 IPC 功能整体替换 |
| P8 | Identity + EF SQLite、Cookie/Token/一次性码、站点管理员账号/角色管理与实时撤权测试 | 生产 EF Migrations、Windows 本机 SID/IPC 身份及多台站凭据管理 |
| P9 | SignalR MessagePack、CAT/旋转器实时控制和读回、认证 RX PCM/AF 频谱 WSS | 原电台频谱/状态的实际采集、Delta 与多客户端流控 |
| P10 | 固定 libdatachannel 的双 Peer ICE/SDP/DTLS/SCTP DataChannel 与 Opus RTP Audio Track 本机回环 | 与正式 Media、浏览器和 FRP UDP 的联合公网验证 |
| P11 | frps/frpc v0.72.0 TCP/QUIC 控制+UDP Socket 真实本地代理双路径测试，手工部署示例 | 公网/CGNAT 实测、FRP UDP+ICE 以及延迟/丢包验收 |
| P12 | 两个独立账户的后按优先 PTT、过时释放无效、数字暂停、崩溃/心跳回 RX、实时角色授权 | 真电台 PTT/音源切换、TX 缓冲、旋转器/摄像机仲裁 |
| P13 | WorkspaceRouter 处理台站切换下的原 PTT 事务绑定 | WinForms 完整多台站连接与凭据存储 |
| P14 | React Dockview：CAT、PTT、TLE/连续跟踪、旋转器及多路径点 Park、WebAudio、真实 AF FFT 及模拟 RF Scope | 原 SkyRoof 全部 Panel、多远端主机、浏览器操作验收 |
| P15 | UTC/单调时钟预约模型、SHA-256 PCM 封存和恢复 | 外部 WSJT-X CAT/PCM Bridge、真实 Timestamped RF TX 及标定 |
| P16 | 原生媒体、FRP、SignalBus、E2E 多进程、凭据、断线、崩溃等 CI | 大规模网络故障注入、24h 运行和无线电实机验收 |
| P17 | 六进程 Windows x64 开发包、从发布目录执行 IPC E2E、SCM 安装/卸载脚本及生命周期 CI | 正式安装器、服务账户真实设备访问、升级/回滚、签名与兼容迁移 |
| P18 | 独立 CI + 原项目 Compile Check、Docs 与 aislop Quality Gate | 所有阻断条件消除，许可证审查，正式 Release |

## 当前已闭合的模拟链路

认证用户（Administrator/Operator）→ SignalR/MessagePack → Host → Station Named Pipe/gRPC → Device → SkyCAT/rigctld TCP 模拟设备。覆盖 RX/TX 频率区分、PTT 抢占、用户撤权、断线释放、硬件确认、异常重启等。

另一条真实跨进程路径：Windows SignalBus PCM16 生产者 → Host 认证 WebSocket SAP/1 → 浏览器/独立客户端消费。Opus Audio Track 与 DataChannel 已在原生两 PeerConnection 上通过本地测试，但尚未连接真实电台。

## 禁止混同的结论

SkyCAT/rigctld 模拟测试不是 IC-9700 Direct LAN 全功能。Native Opus 与 WebRTC 本机环回不是公网 FRP UDP 浏览器实测。Web/Dockview 构建不是原 SkyRoof 全部停靠窗口的功能回归。开发 ZIP 不是正式 Release。

继续开发的核心顺序：迁移 Station 的 Orbit/RadioLink，接入 Icom Direct LAN/Rotator，搬迁 DSP 和完整音频 Media Service，随后把 WinForms 与 Web 的多台站窗口接到同一业务契约，完成实机及网络容错验收。

变更留痕：参考 PR #254 的提交 SHA、GitHub Actions 运行和 CI 失败日志；当前通过 API 提交代码，不能证实 Git 提交已加密签名。


## 本轮增量：单次轨道观测及执行

Station 直接引用原工程 SkyRoof.Orbit.Contracts / SkyRoof.Orbit.SgpNet，复用含地球自转速度修正的观测结果。PredictTracking 只读；ApplyTracking 由 Operator 授权，对地平以上（用户指定 mask）观测执行双向 CAT 和可选 rotctld P 命令，返回实际读回。普通 simplex rigctld 不假装支持独立上下行；使用 SkyCAT 独立 F/I 的路径完成双工。React 新增手工 TLE 单次观测及执行面板。该段描述的是早期单次跟踪实现；后续已有连续 Orekit/SGP.NET 跟踪，仍缺自动目标选择、ISS OEM、完整角度路径优化、RC-3040S 专用 Park 完成条件和全部 RadioLink 转发器逻辑。

新增 E2E 测试引用 Vallado 固定历元 TLE、几何距离有限差分（径向速度误差小于 3 m/s）、两方向 Doppler 符号、CAT F/I、rotctld P/p、Viewer 只读权限和不新增 PTT 命令。具体通过状态以本分支 CI 为准。

## 连续跟踪任务（本轮新增）

Station 通过独立 BackgroundService 执行单调时钟周期调度，使用台站当前 UTC 重新计算 TLE 观测、CAT 多普勒和 rotctld 指向；不补发历史坐标。共享台站任务提供 Start/Status/Stop，Operator 可启停，Viewer 只读；旧任务 ID 的停止请求不影响新任务。ResumeAfterRestart 默认由页面开启，任务以独立配置文件持久化（SKYROOF_TRACKING_JOB_FILE），仅恢复已授权的跟踪任务，不恢复 PTT。停止清除持久化记录，并向已使用的旋转器发送 STOP。设备失败会显示错误并按新历元重试。新增 E2E 模拟覆盖掉线、Station 强杀重启、恢复、停止及停止后无新设备写入。真实 Orekit 已接入；仍缺 ISS OEM、完整 RadioLink 转发器及旋转器自动路径优化迁移；CI 结论以具体运行记录为准。

Station 原生引用既有 Orekit/IKVM，默认选择 Orekit，使用 SKYROOF_OREKIT_DATA 或发布目录 orekit-data（随包复制原有 tai-utc.dat）。只有显式设置 SKYROOF_ORBIT_BACKEND=sgpnet 才使用对照后端；失败不隐式降级。复用同一 TLE track，单次计算周期只生成一次观测。E2E 在 Orekit 和 SGP.NET 两种实际后端执行，发布目录 E2E 默认 Orekit；尚需核实 CI 和完整 EOP 数据／ISS OEM。

Station Orekit EOP 数据：默认在用户数据目录缓存 IERS `finals2000A.all`，首次启动或文件超过 7 天时按原 SkyRoof 的 USNO/IERS 镜像顺序更新；校验数据行数/长度、SHA-256，先临时文件再原子替换。网络失败保留旧缓存并通过 Orekit DataQuality 显示数据覆盖范围。可以用 `SKYROOF_OREKIT_EOP_SOURCES` 指定镜像或本地文件，设置 `SKYROOF_OREKIT_EOP_AUTO=false` 关闭自动更新；`SKYROOF_OREKIT_DATA` 指向自定义目录。仅在 Orekit 进程初始化前更新，更新 EOP 后需要重新启动 Station 加载新数据。E2E 测试禁用互联网更新，使用发布目录固定 UTC 数据。

## P6 新增独立 Media Service

`SkyRoof.Remote.Media` 使用本机私有 gRPC Named Pipe 接收 48 kHz PCM16 单/双声道流，以共享内存 SignalBus 为站内 RX 音频总线，供 Host 认证 WSS 使用。接收端具备样本索引校验、缺口计数、后连接音源接管和新 StreamEpoch，禁止旧源覆盖新源。Windows CI 新增独立进程下的字节级 RX PCM、缺口、源接管及格式验证；开发包集成 Media 独立进程。该接口实际接收客户端送入的 PCM，但尚未连接 IC-9700 LAN 实机或 TX 音频管线，仍需单独验收。

## P5 音频谱 DSP 新增

建立独立 SkyRoof.Remote.Dsp 进程，读取 Media SignalBus PCM16，执行真实 Hann 4096 FFT，1600-sample hop，输出 475 个 dBFS AF bin，独立 StreamEpoch，在样本中断/代际变动时重建上下文。Host GET /ws/af-spectrum 是经认证的 WebSocket，网页另有 AF Spectrum Dockview 工作区。测试向真实 DSP 子进程注入 1 kHz 与切换后的 2.5 kHz 正弦波，检查峰值频率、电平和 Epoch；仅能声称 AF Spectrum 功能，不代表 IC-9700 RF/IF 频谱或 CW/FT4 解码器完成。

### 旋转器真实远控窗口

Operator 经 SignalR → Station gRPC → Device rotctld P/p/S 发送指向和 STOP；Viewer 仅允许 p 读回。Device 使用配置的 AZ/EL 机械上下限拒绝超范围指令，不干预界面其他功能。Web Rotator Dockview 显示设备读回和独立 STOP。E2E 验证实际模拟 TCP 传输、权限与超限拒绝。该读回不等于机械制动完成，现已追加 Remote 多路径点 Park、每个路径点连续两次位置反馈、抢占旧运动任务及失败后独立 STOP；原 SkyRoof 专用 RC-3040S 稳定驻留和 STOP 后读回校验、自动路径规划仍未迁移。

## 本轮质量与验收状态（2026-11-22）

独立 DSP 进程的 1/2.5 kHz 音频 FFT 与 dBFS 测试、既有浏览器及 SCM 安装/启停/退出验证已有成功的 Actions 记录。新增的独立 STOP 连接、延迟位置读回抢占、六进程打包、Web/Host/Station 联合回归和 aislop 必须以最新分支提交的最终 CI 结果为准。最新提交测试尚未完成时不得在 Release 中宣称通过；此前 SGP.NET 对照测试失败根因是工作流 DLL 路径错误，已改为正确的 win-x64 输出并加入文件存在检查。开发中尚缺实际电台、数字解码器、数字 TX、完整多工作区远控、RF/IF 频谱和正式安装器，不得合并为完整 V1 正式版。

## 2026-11-23 续开发（按具体提交验收）

- `e994edd`：Host SignalR 将单客户端并发调用数从默认 1 调整到 8。急停可越过同一连接中阻塞的旋转器位置反馈；Device 仍序列化普通运动指令并使用独立 STOP TCP 会话。另在 `03d683e` 抽离 Web PTT 输入监听 Hook，使原工作区持有的 TX 事务继续由原连接释放。该提交的 E2E、打包、浏览器和 aislop 已通过。
- `3eec8dd`：引入 Icom UDP 音频会话 SID/编解码器与 `MediaIngress.PublishIcomRx`，使用原始格式帧验证字节与序列并转入 SignalBus。该提交的 Media 实际多进程测试通过。
- `9658183`：增加 Icom 音频到 48 kHz 内部 PCM 时钟的窗函数 sinc 有状态 ASRC，跨 UDP 块保持相位，输入采样缺口重置滤波上下文；44.1 kHz 测试音和跨进程模拟验证通过。
- `a35be11` → `dba52ca` → `6bc9af2`：Remote Rotator 的多路径点 Park、实际读回等待、全路径预校验、手动 STOP/Point 抢占、中途 STOP 不能继续后续路径点；补齐每段连续两次新鲜读回。**这组最新代码须以 `6bc9af2` 的 E2E/打包/aislop 最终结果为准**，先前编译通过不能替代这一轮结果。

### 2026-11-23 Media RX 可靠性与运行期可观测性

- `6be1da0` 全部 GitHub Actions 已成功，包括 E2E、六进程打包、浏览器、主编译与 aislop；这是 P6 初始 RX 格式重排和 P3 Park 修复在当时 HEAD 的验证基线。
- `31c1f2a`：Icom UDP 包的 100ms 重排等待现在由 Media 独立计时检查，源 gRPC 保持打开也能释放缺包后的缓冲帧；迟到包被忽略并保留采样空洞。对应 Media 跨进程 CI 已通过。
- `f0f757c`：从 IcomRxStreamDecoder 提取 PCM16 SignalBus 写入模块，移除 aislop 的类长度告警；结果以该 SHA CI 为准。
- `6ba545d` → `a99ff87`：新增私有 IPC `MediaIngress.GetRxStatus`，返回源类型、StreamEpoch、运行状态、帧数、缺采样数，以及 UDP 接收数、乱序数、忽略数与缺包数。源切换/正常结束更新当前状态；Media 故障不再要求接收流终止才可观测。
- `cc1a446` → `f61fa2e`：Host 通过受身份认证的 `GET /api/v1/media/rx-status` 显示状态，Media 离线返回 503；React 音频页每两秒刷新可用数据，未连接的 Remote Workspace 不允许误播放本地台站音频。Host Smoke 检查匿名 401/Media 离线 503，浏览器模拟注入 UDP 统计检查呈现。**上述最新 HEAD 的 E2E/打包/aislop 尚需 CI 最终结果，不能替代实机验证。**

这条诊断链路用于识别实际音频故障，不能自行证明 Icom 认证或 UDP 数据来自真实电台。当前 Direct LAN 会话所有者仍在原 SkyRoof 工程，Remote Device 尚未完成全独立的电台认证/连接管理。

### 尚不允许对外宣称的能力

此处模拟输入使用正确的 Icom LAN 数据包格式，但**并非**已完成电台认证、UDP 实时接收或 IC-9700/RS-BA1 全链路测试。Remote Media 接口现可接收经过其他模块认证的 Icom 音频帧，仍需迁移独立的 Direct LAN Session 所有权、接通真实 RX/TX 音频及射频频谱。标准 rotctld Park 仿真不等同于 RC-3040S 的机械到位/制动确认。数字 TX、完整 WinForms IPC、WSJT-X Bridge、ISS OEM Hybrid、跨站点工作区及正式安装器亦未完工。

提交代码使用 GitHub API，未证实加密提交签名；实机验证未执行。

## 2026-11-23 P3/P6 Direct LAN 原生会话开发

- 新增 `IcomLanControlCodec`、`IcomLanRadioCapabilities`：迁移原 SkyRoof 的凭据替换、IC-9700 控制帧、Auth/Capability/Stream Grant 字节布局，校验完整的 SID、登录随机请求、事务序号与 6-byte AuthId。
- `IcomLanControlConnection` 以独立 UDP 进行控制 Hello→Login→Capabilities/Token→流授权，提供 Control Ping/Idle、相关 Token Renewal、故障重登。
- `IcomLanCivSocket` 与 `IcomLanAudioSocket` 在发起流授权**之前**各自绑定本地实际 UDP 端口，避免媒体授权时端口与最终 socket 不一致。完成各自 Greeting/Ready，CI-V 发送 native Stream Open，音频定期 Ping；控制或 CI-V maintenance 异常触发新控制授权。
- 可选 `IcomLanReceiveWorker` 在独立 Device 进程中处理远端 Audio UDP、音频 SID/Codec、外层包重排及 `FirstSampleIndex` 计算，将字节原样交给 Media `PublishIcomRx`，最终输出 48kHz PCM 到 SignalBus。不会自动切换原 CAT 源或干预既有 SkyCAT/RS-BA1 会话。
- Foundation Fake Radio 使用真实 `UdpClient` 执行双向 Login/Grant 和 Audio/CI-V 握手；Media 集成脚本新增模拟 UDP Radio→Device 独立进程→Media Named Pipe→SignalBus 的联动和 3 个 UDP 故障用例。
- 早期 `cc3d852` Foundation CI 曾因**模拟电台**的随机登录 Token 与后续 AuthId 前两个字节不一致而超时，现于 `d513a3c` 修复测试桩，不放宽真实协议校验。**最新 HEAD 必须以 GitHub Actions 的 Foundation/Media/E2E/aislop 成功结果为准，不得把已提交视作通过。**

### 开发环境参数

仅在需要启用原生 Icom 接收时为 **Device 进程** 设置：

| 环境变量 | 说明 |
|---|---|
| `SKYROOF_ICOM_LAN_HOST` | IC-9700 设备 IPv4，未设置即不启动 Direct LAN RX Worker |
| `SKYROOF_ICOM_LAN_PORT` | Control UDP 端口，默认 50001 |
| `SKYROOF_ICOM_LAN_USERNAME` | 电台网络登录用户名，不写入代码或仓库 |
| `SKYROOF_ICOM_LAN_PASSWORD` | 电台网络登录密码，不写入代码或仓库 |
| `SKYROOF_ICOM_LAN_RX_RATE` | 首选 RX 采样率，默认 48000；仍依据电台能力掩码选择有效值 |
| `SKYROOF_ICOM_LAN_RX_CODEC` | RX Codec：4=PCM16、2=PCM8、1=μ-law，默认 4 |
| `SKYROOF_MEDIA_PIPE` | Device 和 Media 进程必须一致，默认 `skyroof-station-media` |

当前为**接收方向的独立开发功能**。只使用已协商的 Audio UDP 接收，未实现 Direct LAN TX 及原 SkyRoof 的完整 CI-V/RF Spectrum 功能，且没有实机验证。真实 IC-9700 的 LAN AF/IF Output Select 由电台本身决定；软件不能据 UDP 内容假定一定是 AF 或 IF。生产部署时避免与独占电台远控会话的 RS-BA1 客户端同时争用设备授权，具体并发兼容性仍需实机确认。



## 2026-11-24 Remote V1.7 续开发优先级 P0–P5

这里的 P0–P5 是本轮工作顺序，不替代上文 P0–P18 的总验收矩阵。完整 V1.7 目标不变。

| 本轮 | 提交与实现 | 尚需验收/实现 |
|---|---|---|
| P0 | `510d50c` 原生 CI-V 写入 ACK/NAK、频率/模式读回、PTT、MAIN/SUB 选择、仪表原始 BCD 数据 | 实机 CI-V、完整 VFO 与 split 模式行为 |
| P1 | `b63d51d`、`40818dc` 原生卫星 CAT 后端、同一会话复用、DATA 模式、PTT 恢复与真实 UDP 模拟跨进程测试 | 断电及实机故障恢复；完整非 SAT CAT |
| P2 | `9414dc9` TX Grant、20 ms PCM 节拍、1364 字节最大 UDP 载荷、MOD Input 读写确认 | Device TX IPC、持有者与 PCM 代际绑定、浏览器麦克风、数字 TX、实际 RF |
| P3 | `bf9f432` 复用原 RF Scope 解析，MAIN/SUB SignalBus 和认证 WSS、浏览器 RF 绘图 | 完整 Device→Host→浏览器 RF 实测、频谱配置与瀑布图、独立 IF、仪表 UI |
| P4 | `00d21cf` SAP/1 按 sample index 处理短缺口、长缺口重新预充，Epoch 重置、Workspace 媒体隔离及断开后重新连接 | 正式 WebRTC Audio Track/RTC Data、ICE/FRP、麦克风 TX |
| P5 | `81ff758` 独立 DSP 复用原始 FT4 DLL/ABI，48 kHz PCM 文件解码与原生波形闭环 CI | 实时 UTC 对齐的 FT4 时隙接收/发射、CW/Q65/遥测、ISS OEM/RadioLink、ONVIF、QSO/WSJT-X 等模块 |

### 配置与边界

Device: `SKYROOF_CAT_DIALECT=icom-lan-satellite` 选用原生卫星 CAT；`SKYROOF_ICOM_LAN_RF_SCOPE=true` 显式开启原生 Scope。仍需前述 Icom LAN 登录参数。现有 SkyCAT、rigctld 路径保持。TX 底层组件尚未连接完整 PTT 音频事务；不能宣称原生 LAN TX 已端到端可用。RF Scope 使用 0–160 原始幅度，不是校准 dBm，也不是 IF IQ。

DSP 开发诊断：`dotnet SkyRoof.Remote.Dsp.dll --ft4-self-test` 执行原始 native encode/decode 回环（无电台发射）；`--ft4-decode path.pcm` 输入单声道 48 kHz PCM16 LE、290304 样本。当前不是实时 FT4 工作台。

### 已取得的 CI 证据

- `510d50c` Foundation 45304756833、Media 45304756487、aislop 45304756493 成功。
- `40818dc` Foundation 45305057656、Media 45305057818、浏览器 45305057793、Web build 45305057928 成功。主 Compile Check 45305057622 在合并测试版本的 EarthMapRenderer.cs 发生 CS0120；该文件在当前 master 已采用实例 vao.Unbind(gl)。aislop 45305057761 报告 score 99、10 个未基线化告警，涉及同期地图代码以及 IcomLanReceiveWorker 类长度；本轮进一步拆分 Worker。未修改 quality gate 或 baseline。
- 所有后续 SHA 必须独立核对 CI，不能继承前一提交的成功结论。当前无真实 IC-9700、旋转器、远端公网或实机 RF 测试。PR 保持 Draft，不发布正式 Release。


## 2026-11-24 继续实施：TX、实时 FT4、RTC 与主项目回归

本节是后续增量；上述较早描述以本节最新状态为准。

| 提交 | 已实现 | 验收边界 |
|---|---|---|
| `f7799a4` | 原生 Icom TX grant、PTT owner/epoch 绑定 PCM、Station/Device IPC、MOD input 读回、20 ms 发送和 ASRC | 仍需实际电台 RF/音频 |
| `4257cb0` | 真 UDP 模拟电台验证 TX PCM、PTT 抢占、重复/旧 owner/RX 拒绝 | Windows Media Integration 45307141101 和六进程包 45307140689 已成功 |
| `9fd272b` | 浏览器 AudioWorklet MIC 和 SignalR 20 ms PCM 上传，仅在确认 PTT 事务后发送 | Web build 45307205040、Media 45307205076 成功；尚未真实麦克风/RF 验收 |
| `3a0d61a` | 修复旧 HEAD 62224da 的 3 个 aislop 告警（连接参数、重复清理、转换循环） | 最新 gate 必须重新通过 |
| `603280f` | 实时 FT4 sample-clock UTC 时隙、epoch/gap 处理、后台原生解码、结果 ring 和 Web 面板 | Web build 45307421420 成功；实时解码新 CI 尚待完成 |
| `a724ab6`、`8f65506` | libdatachannel C ABI 原生 RX peer、认证 SDP signaling、SAP/1 DataChannel、浏览器 WSS 自动回退 | 新原生桥与实际 Chromium 待验收，Audio Track 未迁移 |
| `3e1dbab` | 原生 RTC Windows 运行库构建及打包、真正 Chromium/SignalBus 数据验收 | 不是既有双原生 peer 回环的同一测试；新 CI 尚未完成 |
| `e0c7dbb` | 20 ms TX PCM 有限重排及缺帧处理、重复与失去 owner 区分 | 新增顺序 0,2,1,4,3 测试，待 CI |
| `46e0247` | FT4 encode PCM CLI 和真实独立 DSP SignalBus→FT4→结果 ring 测试 | 尚未完成数字定时发射 |
| `eeb6d15` | 原 CAT Cycle 在真实写入锁内校验 ON 准备状态，禁止检查后新到 ON 绕过准备，同时保持 OFF 优先 | 最新 Compile Check 待完成 |

已核对 62224da 全部 21 项 CI 中 19 成功。主 Compile Check 当时 1811/1813 通过，失败为同期地图像素与中文覆盖；在 4257cb0 的后续合并测试中地图两项不再失败，1813/1814 通过，但 `IcomMicrowaveVirtualPttTests.TenGigahertzDopplerMayUpdateTxWhileKeyedAndReturnToRx` 的 `KeyedBeforeTuned` 断言失败。分析 `Cycle` 发现准备检查与实际 PTT 请求读取之间存在竞态，已修复，不能预先宣布通过。旧 HEAD 的 aislop 报告 3 项已针对性修正，没有改变 gate 或 baseline。

### 正式交付尚缺

- Native TX：实机 PTT/音频源切换与标定、浏览器实际麦克风和网络压力，定时数字 PCM。
- RTC：新原生 Windows bridge 与实际 Chromium 端到端结果、Voice/Opus Audio Track、FRP UDP/公网 ICE、断线与撤权/负载验收。
- DSP：FT4 实时时间标定、数字发射/QSO sequencer；原 CW/Q65/Telemetry 解码器、ISS OEM/RadioLink、SDR/ONVIF 等剩余模块。
- WinForms 完整 IPC、真正多台站、生产 ACL/数据库迁移/升级回滚、正式 installer、硬件和长时稳定性仍未闭合。
- 最新 SHA 的所有必要 CI 及 aislop 通过后才具备软件级发布前验收资格。未获得实机验收不冒称真实射频验证。当前 PR 继续 Draft，不创建正式 Release。
