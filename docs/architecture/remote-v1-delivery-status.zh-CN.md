# SkyRoof Remote V1 — P0–P18 实施与验收状态

分支：feature/remote-v1-multiprocess-foundation；Draft PR #254。当前**不能**认为 P0–P18 已全部完成，也不能将开发测试包当成 Remote V1 正式 Release。

| 阶段 | 已提交、已纳入自动化验证的实现 | 仍缺少的发布条件 |
|---|---|---|
| P0 | 核对原 SkyRoof RadioLink、CAT/SkyCAT、Direct LAN、CI，建立独立测试基线 | 全部旧版性能回归基线 |
| P1 | Protobuf、gRPC Named Pipe、共享内存 SignalBus、流 Epoch/Sequence/SampleIndex | 生产 IPC ACL、协议升级兼容 |
| P2 | Supervisor 普通后台/Windows Service 双模式、SCM 实际注册启停 CI、崩溃恢复、Device PTT 和已配置旋转器 STOP 退出处理 | 实际硬件在 Session 0 的账户/ACL 和升级迁移验收 |
| P3 | 独立 Device：SkyCAT/rigctld CAT/PTT、读回和异常恢复；rotctld AZ/EL/STOP、机械限位、STOP 独立连接、多路径点 Park。新增独立 Icom LAN UDP 控制登录、认证 Token、授权流、保活和 CI-V/Audio 端口预绑定的可选接收 Worker | IC-9700 Direct LAN 实机登录/CI-V 完整 CAT、RF/IF 频谱、TX 音频、自动路径优化、RC-3040S 专用 Park 校验、SDR/ONVIF |
| P4 | Station gRPC + Orekit/SGP.NET 实轨道传播、EOP 缓存、连续跟踪与 Doppler、重启恢复 | 共享 RadioLink 频率映射、Station 原格式缓存查询及网页选择已提交；仍缺在线数据库更新、OMM、ISS OEM 自动下载及真实星历验收、完整排程迁移 |
| P5 | 独立 Dsp：SignalBus RX PCM16、4096-point Hann FFT、实时 FT4 native 解码及结果 ring、FT4 编码 IPC | FT4 QSO sequencer、CW 最新跨进程测试、Q65、遥测和硬件时钟校准 |
| P6 | 独立 Media：PCM16 SignalBus、Opus 本机环回、认证 WSS/WebAudio、Icom UDP PCM16/PCM8/μ-law、8–192 kHz→48 kHz 有状态 ASRC、UDP 乱序/重复/缺包恢复及实时认证诊断；Device RX Worker 已连到 Media 私有 IPC | IC-9700 实机登录/RX/TX、外部网络压测、新 Opus Voice Track 的浏览器验收和模式自动切换 |
| P7 | 共用跨进程命令契约 | 原 WinForms 生命周期解耦及 IPC 功能整体替换 |
| P8 | Identity + EF SQLite、Cookie/Token/一次性码、站点管理员账号/角色管理与实时撤权测试 | 生产 EF Migrations、Windows 本机 SID/IPC 身份及多台站凭据管理 |
| P9 | SignalR MessagePack、CAT/旋转器实时控制和读回、认证 RX PCM/AF 频谱 WSS | 原电台频谱/状态的实际采集、Delta 与多客户端流控 |
| P10 | 固定 libdatachannel 双 Peer 环回、Windows 发布包 Chromium PCM DataChannel 实际接收通过；新增 Opus Voice Track 接入 | 最新 Voice Track Windows/Chromium CI、FRP UDP 联合公网验证 |
| P11 | frps/frpc v0.72.0 TCP/QUIC 控制+UDP Socket 真实本地代理双路径测试，手工部署示例 | 公网/CGNAT 实测、FRP UDP+ICE 以及延迟/丢包验收 |
| P12 | 两个独立账户的后按优先 PTT、过时释放无效、数字暂停、崩溃/心跳回 RX、实时角色授权 | 真电台 PTT/音源切换、TX 缓冲、旋转器/摄像机仲裁 |
| P13 | WorkspaceRouter 处理台站切换下的原 PTT 事务绑定 | WinForms 完整多台站连接与凭据存储 |
| P14 | React Dockview：CAT、PTT、TLE/连续跟踪、旋转器及多路径点 Park、WebAudio、真实 AF FFT 及模拟 RF Scope | 原 SkyRoof 全部 Panel、多远端主机、浏览器操作验收 |
| P15 | UTC/单调时钟预约、SHA-256 PCM 封存、设备定时播放与读回 RX（真 UDP 模拟通过）、FT4 单时隙 UI | 最新 Host→DSP→Station→Device FT4 E2E、WSJT-X CAT/PCM Bridge、真实 RF TX 及标定 |
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


## 2026-11-24 — 定时数字 TX、RTC Voice Track、ISS OEM

以下是本轮增量，优先于旧段落中尚未更新的描述。仍是开发分支，不是完整 V1.7 正式交付。

- `517b241` 21 项运行中 20 项通过，原工程 Compile Check [45308339644](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45308339644) 通过，实际独立 DSP FT4 [45308339505](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45308339505) 通过，Windows/Linux 原生 RTC [45308339668](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45308339668) 通过；发布包真实 Chromium SAP/1 [45308339731](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45308339731) 通过。aislop 唯一未通过项由后续重构处理。
- `dffb1e4`：原 CAT 按职责分成八个 partial 文件，拆 Host TX bridge，修 DSP 释放和 C++ RAII；[aislop 45313509803](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45313509803) 与 [Compile Check 45313509839](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45313509839) 均已通过。无 gate 降级。
- `ea9c244`、`b475d87`：SHA-256 封存与持久化单次 claim，设备本地单调时钟、800 ms PTT lead、20 ms native PCM、100 ms tail、释放后 PTT 读回；[Media 45313826179](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45313826179) 实际通过 9600 样本逐样本 UDP 对比、首包允许误差 80 ms、任务 ID replay 拒绝、结束 RX 验证。此为软件/模拟 UDP 首包时间，不是 RF 时间标定。
- `78eb975`、`8babd43`：DSP 私有编码 IPC，Host Operator 鉴权并持续检查 token，Web 单 FT4 时隙控制台、真实发布包 Host/Station/DSP/Device 的样本级测试。78eb975 [Media 45313920119](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45313920119) 及 [aislop 45313920255](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45313920255) 通过；整套新 HTTP FT4 发射测试待最新 package CI。尚非自动 FT4 QSO sequencer。
- `ab1326d`、`62931dd`、`bff5195`：原生 Opus 48 kHz mono 48 kbps、协商动态 RTP PT、RTCP SR、认证 WS SDP、浏览器 Voice Track 与原 PCM DataChannel 切换、WSS fallback；新增实际 Chromium RTP codec/sample 验收。新 Voice Track CI 尚未确认，公网 ICE/FRP 未测。
- `f7b8946`：Orekit CCSDS OEM KVN/XML 读取、ISS object ID 校验、按 usable segment 覆盖判定、TEME/ITRF/topocentric、超范围或无效文件显式 TLE fallback；Station 可设 `SKYROOF_ISS_OEM_PATH` 指向本地星历文件，更新文件后重新加载。新增合成 EME2000 PV、分段间隙与距离导数测试。没有使用真实 NASA 星历验证；自动获取及真实星历与独立基准比较尚缺。
- `9c708e9`：新增实际 gRPC/UDP 模拟定时发射取消及手动 PTT 抢占回归，检查旧任务清理不释放新手动 owner；待 CI。

配置：Host 与 DSP 的 `SKYROOF_DSP_PIPE` 必须一致（默认 `skyroof-station-dsp`）；Device 的 `SKYROOF_TIMED_TX_DIRECTORY` 保存封存任务；`SKYROOF_FT4_TX_DELAY_MS` 为校准项，真实 RF 延迟仍须测量。`SKYROOF_ISS_OEM_PATH` 为 Station 的本地 OEM 路径，仅 Orekit 后端、NORAD 25544 使用。勿在仓库写入实际电台凭据。

剩余主要阻断：完整 RadioLink/转发器数据库，CW/Q65/Telemetry，WSJT-X Bridge，SDR/ONVIF，原 WinForms 全部功能 IPC、多台站与生产身份/ACL/数据迁移，公网 RTC/FRP、设备实机、长时稳定性及正式安装器/升级回滚。所有最新 HEAD 的 CI 必须独立验收，不能继承历史提交的通过结果。不合并、不发布正式 Release，API 提交未证实加密签名。


## 2026-11-24 — 原 CW 双解码器 / RadioLink / 台站缓存接入

本节为最新实施增量，尚不是完整 V1.7 的发布验收。

| 提交 | 实施结果 | 验收范围 |
|---|---|---|
| `af708c6` | 从原 WinForms 声卡控制器分离纯 PCM ingress 与音源枚举；独立 DSP 直接链接原 26 个 CW 源码文件和 FFTW、MathNet、ONNX Runtime，复用八路 Kalman/固定滞后关联、DeepCW、NewCW、增量文本；修复 worker 停止时漏等最后一个推理任务的竞态 | 最新主项目与 DSP CI 待确认 |
| `217c845` | SignalBus → 有界独立 CW 输入队列 → 原 CwReceiveWorker → 结果 ring；样本索引及源代际变化重建会话，丢弃旧推理结果；私有 IPC 支持启停及 DeepCW/NewCW 切换 | 未进行硬件音频验证 |
| `c2894d0`、`b2a4026` | 认证 CW 结果 API 与 Operator/Admin 配置，Web 八路频率/峰值/键控/文本面板；发布目录实际 DSP 注入双载波 CW 测试及 epoch 清除、停止测试 | 新 CI 排队；尚未证实实际 CW 双路解码、真实 ONNX 模型效果 |
| `488ca57`、`1e2791f` | 原 RadioLink 与 Station 共享频率计算，反向/同向映射、基准偏移、RIT/XIT、手工修正；不钳制数据库边缘。Device 支持仅上行调谐而不写 0 Hz 到 RX。Web 通带设置、主项目回归及认证 CAT E2E 新用例 | 最新 CI 待确认；未迁移 CTCSS/自动模式、完整实时调谐、排程与数据库在线更新 |
| `2c7059b`、`2d72de7` | Station 读取原格式 Satellites.json，按名称/别名/NORAD 检索及缓存更新，手工 TLE 过期后选择 AutomaticTle；返回精确通带、模式和状态，Web 选择卫星及转发器填入追踪表单；认证 E2E fixture | 仅本地缓存导入，不是完整 SatNOGS/CelesTrak 在线数据源；当前 OMM-only 可识别但不可直接启动 TLE 跟踪 |

CW：通过 Web 工作台显式启用接收处理，避免在未使用 CW 时持续占用 DSP 资源。Host 与 DSP 的 SKYROOF_CW_RESULTS_BUS 必须相同；DSP 可设置 SKYROOF_CW_MODEL_DIRECTORY（原 DeepCW model.onnx、model.onnx.json）、SKYROOF_CW_HISTORY_DIRECTORY。NewCW 不需要 ONNX 模型；DeepCW 依赖模型实际安装。现阶段 UI 返回最近八路的有限长度文本；完整历史浏览、CW TX/WPM/宏、全功能 Skimmer UI 仍需实现。

Station：SKYROOF_SATELLITE_CATALOG 指向由桌面版生成的 Satellites.json（建议 C:/SkyRoofRemote/data/Satellites.json）。该增量只读，不修改桌面缓存，不自动声称死活/轨道数据实时准确。用户可以手工输入或修正频率和 TLE。OMM 自动跟踪、轨道缓存源自动获取、当地自定义记录刷新与模式/CTCSS 联动尚待实施。

质量证据：`ab1326d` 的 [aislop 45314259461](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45314259461) 报告 6 项，其中 rtc_voice_track.cpp OfferedAudio::payloadType 无初始化已在 `488ca57` 修正；另 5 项来自同期 master 的 EarthViewPanel/Designer，当前 Remote 分支未改动该模块，不以降低门禁或回退地图功能处理。上一轮 `8babd43` 的 Media、DSP、aislop 已通过；完整 HTTP FT4 包与 Opus 浏览器测试仍需最终 CI。当前最新提交的 CI 排队，不能用历史成功代替。未执行 IC-9700 实机、真实公网、24h 稳定性验收；不合并 master，不发布正式 Release。


## 2026-11-24 — OMM、NASA OEM 与台站在线卫星目录

本节为最新增量，优先于旧段落中“OMM-only 不支持”“OEM 仅本地文件”的描述。仍未达到正式 V1.7 发布标准。

### 本轮提交与证据

| 提交 | 结果 |
|---|---|
| `aef061b`、`6f2393b` | Station 按选定 catalog satellite ID 每次跟踪 tick 解析最新 TLE/OMM，OMM 原格式直接 CreateOmmTrack，保留六位 NORAD；Web FOLLOW STATION ORBIT 可关闭以手工输入 TLE；认证 SignalR fixture 对比独立 SGP.NET 几何距离和径向速度。修正 RadioLink 引入的 Tracking DTO 与音频注释 aislop 告警。 |
| `f80f410`、`739a3d6` | NASA ISS OEM 自动刷新、6 小时周期，失败 15 分钟重试；解析且存在未过期覆盖段后原子更新，旧有效文件保留；新增独立 OEM 非均匀采样、分段间隙、坐标转换、径向速度以及无效/过期/HTTP 失败保持缓存测试；手动 workflow_dispatch 可选真实 NASA 文本 OEM 源矢量验证。 |
| `33475d4` | 使用 GitHub 已验证两父节点的无冲突 merge preview tree，同步 master `e6a1c3c`（极区地图点击修复及 v1.89 基线）至 Remote 开发分支；不修改 master，不合并 PR #254。 |
| `37c2fef`、`6a3a590` | Station 独立 SatNOGS 卫星/转发器/TLE 与 CelesTrak OMM CSV 下载，分页处理、两套原子 sidecar 缓存，按真实轨道历元合并、保留本地自定义转发器及手动轨道优先级；模拟 HTTP 多页/CSV、旧轨道竞争、本地记录保留与认证 Station API 测试。 |

实际已验证的旧提交：`b2a4026` 的 [独立 DSP 45315929579](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45315929579) 已通过真 PCM 两载波 CW：日志记录 90 次跟踪周期、5 次 NewCW 推理窗口、0 次跳过，且 Epoch 重置通过；FT4 native round-trip、UTC live window、AF 1/2.5 kHz 也通过。这证明 CW 接收路径和推理调度，尚不代表八路实际文本准确率或全部 ONNX 实测通过。原 `8babd43` 20/21 项 CI 成功、发布包仍排队。

质量：`1e2791f` [Compile Check 45316275587](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45316275587) 主工程成功构建，1825 测试通过，唯一失败为当时 master 南极地图回环；已同步后续修正 master `e6a1c3c`。旧 `2d72de7` 的 [aislop 45316527712](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45316527712) 两项非基线告警（Tracking DTO 多参数、AudioPanel 注释）已在 `aef061b` 修复，未削弱规则或更新 baseline。最新 SHA CI 尚在排队，不得标为通过。

### 数据源配置

- `SKYROOF_ISS_OEM_AUTO=true`：启动后台自动刷新。默认 NASA JSC 公共 KVN 地址 https://nasa-public-data.s3.amazonaws.com/iss-coords/current/ISS_OEM/ISS.OEM_J2K_EPH.txt；可用 `SKYROOF_ISS_OEM_URL` 覆盖。未指定路径时使用 LocalApplicationData/SkyRoof/Remote/orbits；若显式指定手工文件路径而没有设置 AUTO，默认不覆盖它。自动刷新不会外推过期或分段间隙星历。
- `SKYROOF_CATALOG_AUTO=true`（默认开启）：SatNOGS 24 小时，CelesTrak 6 小时；`SKYROOF_SATNOGS_API` 和 `SKYROOF_CELESTRAK_OMM_URL` 支持配置来源。SatNOGS 公开 API 文档 https://docs.satnogs.org/projects/satnogs-db/en/stable/api.html；CelesTrak OMM 格式/使用政策 https://celestrak.org/NORAD/documentation/gp-data-formats.php。
- `SKYROOF_SATELLITE_CATALOG` 原桌面缓存为只读；在线 sidecar 分别为该路径加 .satnogs.json 和 .celestrak.json，独立刷新，失败不破坏上一有效文件。远端目录并非卫星实时转发器开关状态保证。
- 确定性 E2E 禁用真实外部刷新，使用 HTTP fixture 和实际跨进程 Station。真实 NASA 网络测试可在 Remote Authenticated Host Station Device E2E 的手动触发选择 verify_live_nasa；当前工具未成功取得 NASA 文件，不能标记真实源测试通过。

剩余关键缺口：最新 CI、完整线上数据源覆盖（AutoTLE、JE9PEL、satyaml、当地自定义配置实时编辑、全部模式/CTCSS 联动）、ISS 真星历及实机跟踪验证、WSJT-X CAT/PCM Bridge、自动 FT4 sequencer、CW TX/WPM/宏/完整历史、Q65、Telemetry、SDR/ONVIF、WinForms 全量 IPC、多站点和生产 ACL、安装器及升级/回滚、公网和长时测试。仍保持 Draft，不提前正式发布。API 提交未验证密码学签名。


## 2026-11-24 — OMM/OEM 验收闭环、WSJT-X 客户端桥接

- `265ca85`：补齐 Orekit OEM Earth body 初始化需要的已固定校验 DE430 数据，开发包和 CI 共同使用同一数据源；拆 CW worker、RadioLink 及空值处理；浏览器 Opus 明确媒体传输与 SRTP 初始化、增加接收诊断。实际 [Compile Check 45331175463](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45331175463)、[DSP 45331177522](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45331177522)、[认证 E2E 45331175540](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45331175540) 已通过。E2E 日志明确 PASS：合成 ISS OEM EME2000 非均匀插值、分段间隙、坐标/径向速度；HTTP 刷新先验证再原子替换，失败/过期保留缓存；Orekit 与 SGP.NET 两套 OMM、目录分页与自定义记录、RadioLink/rotator。真实 NASA 下载、实机和 Chromium 新 Voice Track 尚未验收。
- `7b929bc`：新增客户端 Windows WSJT-X Bridge，loopback NET rigctl 默认 4534；认证 SignalR CAT 与 WebSocket RX/SignalR TX PCM，NAudio 虚拟设备。Device 外部数字 TX 独立租约、心跳和断连释放，手动优先；定时内部任务不依赖外部心跳。
- `8354fec`：恢复手动 heartbeat 原状态兼容（旧 [CAT 45331620996](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45331620996) 因该状态断言失败）；新增独立数字状态、Device 显式模式读回和 Hamlib 模式映射，拆 RadioLink 剩余长函数。
- `46b7021`：真实 TCP NET rigctl 握手/CAT/PCM 经认证 Host、Station 和模拟原生 Device UDP 的测试，另加数字租约超时回归；打包客户端 companion 到 tools/WsjtxBridge。新 CI 尚待完成。
- 本次质量补丁按 aislop 报告将 RemoteCatStatus 转为属性 DTO（保留 JSON 字段）并拆 RigctlSession 命令实现，未降低门禁。见 [旧告警 45331620804](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45331620804)。最新 SHA 需独立确认。
- 客户端说明： [WSJT-X Bridge](../../deploy/remote/windows/wsjtx-bridge.zh-CN.md)。真实 WSJT-X 程序和虚拟音频线缆、RF、FRP/ICE、公网、完整 QSO sequencer、CW TX/Q65/Telemetry、SDR/ONVIF、WinForms IPC/多台站及生产安装器仍未完整验收；维持 Draft，不合并、不发布正式 Release。

## 2026-11-24 — FT4 自动 QSO、CW TX 与历史增量

- `214d423`：22/22 CI 全通过，包含 [WSJT-X Media 45341046289](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45341046289)、[开发包 45341046295](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45341046295) 与 [aislop 45341046393](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45341046393)。模拟通过不等于实机或公网验收。
- FT4 自动 QSO 已接入 Host UTC slot、标准呼号状态机、Web START/STOP、报告、原生 DSP 编解码。见 [FT4 自动 QSO](remote-v1-ft4-auto.zh-CN.md)。`dd46685` 的 [DSP 45342939419](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45342939419) 实际通过原生定向报文 grid/report/RR73/73 与 CW 历史、两载波跟踪及 NewCW 推理。三次发射的自动 QSO 整包 fixture 仍需最终 CI 验证。
- CW TX 已实现 SkyCAT keyer、WPM 读回、F1–F8 浏览器宏、Host/Station/Device 事务仲裁与 CW 历史 API。`dd46685` 的 [CAT 45342939188](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45342939188) 日志通过真实 TCP 模拟 keyer、取消 STOP、跟踪 TX defer、数字仲裁及手动抢占。
- `9371f50` 修复 E2E 链接 FakeRigctld.Cw.cs 缺失，拆分 FT4 session；[aislop 45352691633](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45352691633) 已通过，规则未改变。`5f26b6f` 修复断连测试在 CAT T 0 与 journal 清理之间的观察竞态，并增加认证 CW API 测试。
- `8ca3287`、`7af0e2a`、`05a9537`：Native IC-9700 SAT CW 使用现有 CI-V socket，WPM/BK-IN/Command 17/STOP，持久 journal 恢复、真实 UDP fixture 及启动孤儿恢复。见 [CW keyer 实现](remote-v1-cw-keyer.zh-CN.md)。Native 新测试和最终整包 CI 尚未取得完整结果，真实 IC-9700 SAT Command 17、BK-IN 和重连尚需实机验证。
- 剩余：FT4 非标准呼号、日志及完整自动化，CW 宏跨设备、TX 历史、Q65/Telemetry、SDR/ONVIF、WinForms IPC、多台站、生产 ACL/安装器/升级迁移/公网及长时实测；继续 Draft。

## 2026-11-25 — Native CW 验收、长会话续期、Q65 原生工作台

- `d58ea6a` 实际 21/22 CI 通过。 [Media 45353242807](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45353242807) 通过 Native CW orphan recovery、CI-V CW-R、WPM BCD、BK-IN、Command 17/STOP/RX；[认证 E2E 45353242706](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45353242706)、[aislop 45353242732](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45353242732) 成功。唯一失败 [开发包 45353242893](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45353242893)：初始登录后 fixture 没有继续回应 control token renewal，自动 FT4 期间 TX Audio Grant 丢失。单次原生 FT4、WSJT-X bridge、CW UDP 已通过。
- `454345c` 给模拟 radio control 增加长会话 Ping、续期、logout 和 token 关联验证；仍保留生产端 timeout。整包强制验证至少一次续期。独立 aislop 45515572114、DSP 45515572045 成功；自动 FT4 整包尚待结果。
- `e7ca041` / `1b33f60` / `3a3244c`：固定 mfsk-core-tx5dr C ABI v3，十种 Q65 子模式，原生 12/48 kHz 编解码、UTC PCM 完整窗口、独立 native decoder、AVG/EME delay、认证 RX API 和 Web。
- `ddc5322` / `76e6870`：Q65 单时隙 PCM TX、奇偶 UTC 周期、SHA256/一次性 claim、32 MiB IPC，开发包依赖原生 Windows 构建并带 DLL、许可证、锁文件与源版本。首轮 native CI 45515729888 因源无 Cargo.lock 在编译前失败，已修复；首次 Q65 aislop 45515730153 评分100/0 errors/1 warning（索引循环），本次改为 foreach，不改变门禁。
- `474711a`：新增发布包 Q65 15A 跨进程 RX、认证 TX 精确 UDP、UTC parity 与末帧 padding 验收，未取得最终运行结果。
- [Q65 实现说明](remote-v1-q65.zh-CN.md)。待完成 Q65 AP 详情、自动 QSO、真实 EME/多普勒、Telemetry、SDR/ONVIF、多台站、WinForms IPC、生产安装器、迁移与公网实测。保持 Draft；不提前发布。API 签名未验证。

## 2026-11-25 — Q65 advanced options 与 Remote Telemetry PCM

- `89fe991` 将 Q65 AP 呼号、阶段、Max Drift/Pileup/fading 参数接入 native ABI。未完成 Q65 自动 QSO、月面多普勒。
- `67a6798` / `98e1aed`：Telemetry 独立 DSP、真实音频 IQ 前端、SkyTlm、UNNE-1B/FC1/ASRTU 业务解释、认证 API、Dockview 工作台与真实录音 CI。
- 新增真实 AO-73 SignalBus 进程回归及配置代际隔离，详见 [Telemetry PCM](remote-v1-telemetry.zh-CN.md)。历史 FT4 修复基线 `454345c` 已 21/22 通过，整包仍 queued；最新 Q65 / Telemetry CI 未验证，不视为发布完成。
- 剩余：Q65 自动 QSO 和 EME Doppler；Telemetry 自动 catalog/原始 SDR IQ、归档/KISS/上传/图像语音；SDR、ONVIF、多台站、WinForms IPC、生产安装与公网。保留 Draft。
