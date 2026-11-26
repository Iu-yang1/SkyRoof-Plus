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


## 2026-11-25 — CI 收敛及 Telemetry 输出与目录

- `454345c` [开发包 45515572745](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45515572745) 实际成功，22/22 全绿；FT4 自动 QSO 长会话修复完成。
- `f854204` 19/24；[Q65 45518012062](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45518012062) 原生 30A/60B、15A 48 kHz slot/gap/epoch 通过；[Telemetry 45518011776](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45518011776) 四份真实录音及 AO-73 发布版 DSP 通过。Media/E2E/Package 同一 ASP0016 错误，浏览器旧标题，aislop 3 warnings。
- `a899a84` 修复 GET delegate overload、标题回归、aislop 三告警；实际 [Media 45559459116](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45559459116)、[认证 E2E 45559459394](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45559459394)、[Browser 45559459144](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45559459144)、[aislop 45559459371](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45559459371) 全通过；整包待结果。独立 DSP 45559459128 的 CW 双载波测试超时，新增并行读取与最后状态诊断，保持双载波和实际推理门槛。
- `7e0935f` / `5cc9bad` BIN/LOG、KISS TCP、真实 AO-73 字节一致性、HTTP GET 结果体。 `9012304` / `e571417` Station catalog 元数据映射、CCSDS 参数、Web Profile 选择及真实 HTTP fixture。见 [Telemetry](remote-v1-telemetry.zh-CN.md)。
- Q65 60B AP / EME 延迟高级参数新回归（模拟 2.5s 月面往返延迟；非实际 EME）。原生解码与整包仍待最新 CI；不提前合并。


## 2026-11-25 — P0–P5 增量与验收边界

- **P0:** `7e0935f` 24/24 CI 成功，包括 [开发整包 45559585109](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45559585109)。`8301015` 21/24，Q65 pileup 缺 AP hint 导致 ABI/package 失败，另有归档拼接 quality warning。`dd83ac3` 修复真实 AP hint 并实际解码测试，不放宽门禁；[Native Q65 45565279879](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45565279879)、[aislop 45565280098](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45565280098) 与 [Telemetry RF 45565279848](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45565279848) 已通过。
- **P1:** `7478913` / `5420582` 可选 SatNOGS SiDS 站端上传、独立有界队列、CRC 失败帧及内包排除，真实 AO-73 帧的 loopback HTTP fixture（无公共服务上传）。`28d6db7` / `79ce079` 私有 DSP gRPC CF32 流与真实 UNNE-1B 录音回归，固定 50→48 kHz 重采样和 CFO、CRC/星上时钟验收；发布包改用同一 pinned SkySignals-Plus decoder。新 CI 待完成；尚无宽带 channelizer、硬件 SDR 采集、SSDV/SSTV/CODEC2 播放。
- **P2:** `e1c258f` Orekit/JPL DE430 月球跟踪、方位俯仰、单站一阶 CFOM/own-echo RX/TX/none Doppler、2r/c 延迟与已有 Station/Device CAT 联动。真实 DE430 E2E 待 CI。不代表双站精确月面多普勒或 Q65 自动 QSO。
- **P3:** `99ca6f3` / `98958cc` 站端 ONVIF discovery、Media1/2 profiles、GetSnapshotUri、同主机 HTTP JPEG/PNG 代理与认证 Web Camera 面板、真实 SOAP/HTTP fixture。未实现连续 RTSP 转码、PTZ、SoapySDR/RSP/Pluto 硬件控制。
- **P4:** `62d36c8` 站 ID 独立 pipes/rings、持久化目录、认证数据库/Cookie/DataProtection key；配置生成器保留模板设备参数，Web 可打开另一独立 Host。未实现单工作台多台站联控及原 WinForms 全量 IPC。
- **P5:** `1924a03` 完整文件 manifest SHA-256、并列版本暂存、SCM 路径切换及 readiness 失败回滚、脚本 fixture 检查隔离及篡改拒绝。未实施真实升级回滚/数据库 schema migration、发布者签名、安装器、公网 FRP/TURN/设备实测。
- 最终整包、最新 aislop、所有新增回归必须以当前 HEAD 的实际 CI 为准；不将历史绿色基线或已编写测试算作最新功能验收。保持 Draft PR #254，不合并 master，不发正式 release。


## 2026-11-25 — P0–P5 后续增量、真实数据与验收阻断

- **P0：** `dd83ac3` 24/24 已通过（[整包 45565280162](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45565280162)）。`11dcb26` 的 [真实 IQ 45566800160](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45566800160) 实际通过 UNNE-1B Type 1/14、CRC、星上时钟 192304/192124；[Telemetry RF 45566799782](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45566799782) 和 [Host smoke 45566800009](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45566800009) 同样已通过。历史绿灯不能替代最新 SHA。aislop ONVIF 文件长度、URL placeholder、Tracking DTO 参数数目分别在 `d9cd30d` / `22c03f1` / `d23b452` 处理，没有改变 baseline 或 gate。`22c03f1` [E2E 45571526735](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45571526735) 编译失败，Q65 Auto 呼号的 CS8602 两处在本次提交修正，仍待 CI。
- **P1/P3：** `cdf525b` 增加 Windows SoapySDR 0.8 RX companion、设备枚举、实际采样率/频率读回、CF32 私有 DSP Named Pipe、连续 NCO/Blackman 多相 FIR 任意整数采样率→48 kHz（18 kHz 通带、24 kHz 阻带）。原生 overflow 关闭旧 epoch；FIR 重置，不将缺失样本拼成连续流。Foundation 测试 250 kHz→48 kHz 非整数比、分块一致性及折叠干扰抑制；本次修改真实 UNNE IQ 回归也复用该生产 channelizer。新增发布包 ABI probe；尚未实际连接 RSP/Pluto，尚未完成 GUI 管理、SSDV/SSTV/CODEC2 播放、远程历史下载。
- **P2：** `0936763` ICRF 双站 EME 光行时前/后求解、固定 DX 频率的 RX/TX 频率比与对端仰角，新增 DE430 数值时延导数独立校验；月心反射近似，不是月面散射积分或物理 RF 验收。`22c03f1` / `d23b452` Q65 标准呼号自动交换、-50..+50 报告、完整 RX slot、发射准备时间不足时同奇偶下一周期、原生波形与设备定时 TX；三消息模拟 QSO 精确 UDP PCM、末帧 padding、-40/-42 报告测试已接入，待实际 CI；自动任务与月面跟踪 job 的发射时刻联动尚未完成。
- **P3：** `82b884a` ONVIF RelativeMove/Stop、独立 Operator/Admin 权限、标准归一化空间和真实 SOAP fixture，Web 小步位移/Zoom/STOP。未完成连续 RTSP 视频转码或实际摄像头位置读回。
- **P4/P5：** `d7186b9` 两个独立 Host 实际启动，共享 Cookie 容器验证 A/B 登录、交叉 Bearer 拒绝、重启后 key ring 与会话保留；配置生成器 station ID 小写规范化。新增整包实 SCM 升级/不可达新配置回滚测试，保留原身份库与登录 session，测试 readiness 采用 /health/live 因无硬件；生产仍应使用 /health/ready。尚未完成统一工作台多台站、WinForms 全量 IPC、正式安装器、数据库 schema migration、签名、公网、长时与实机验收。
- 最新 CI 排队阻止正式交付判断。不修改质量门槛，不合并 master，不发布正式 Release；提交密码学签名未独立验证。


## 2026-11-25 — Q65/SCM 整包实证、归档与媒体增量

- `7f5d514` **24/25** CI 已通过，唯一失败为 ONVIF PTZ 空 catch 的 aislop；`bac1131` [aislop 45581348874](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45581348874) 修正并通过，未调整质量规则。旧 SHA 的通过不得替代新 SHA 的验收。
- [开发整包 45573277773](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45573277773) 完成并上传产物：六进程、原生 Q65 15A 定时 PCM/末包 padding、三报文自动 QSO（-40 dB、奇偶 slot）、SignalBus Q65 RX、双站 DE430、SoapySDR 0.8 ABI、真实 UNNE IQ、真实 SCM 升级/失败回滚及登录持久化均通过。QSO 使用协议模拟电台和注入 RX 结果，不是月面 RF 通联。 [E2E 45573277811](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45573277811)、[真实 IQ 45573277588](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45573277588)、[Host 45573277623](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45573277623) 实际通过。
- `bac1131` BIN/LOG 原子提交、按 NORAD/UTC 日分页及鉴权下载，Host smoke 已执行并通过下载断言，但测试进程退出后的 SQLite 文件删除遇 Windows sharing violation；`4c24d37` 仅对该锁冲突增加有限清理重试，不放宽业务断言。桌面 [Compile Check 45581348781](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45581348781) 1856/1857 通过；NativeRotatorWorkerTests.StopBypassesPendingRemoteManagementVerification 的 false 断言失败，根因待定位，不标记通过。
- `beb1f51` Q65 单次与自动 TX 可显式绑定当前月球跟踪任务，按目标 UTC slot 计算上行频率，保留手动频率；[E2E 45581736527](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45581736527) 已验证指定 job、目标 UTC RX/TX 计算与拒绝已停止任务，无 PTT。不是已武装时隙中逐样点频率更新或实时 STOP 取消证明。
- `572aec7` 复用 native SkyTlm SSDV/JPEG、Codec2 及 SkySSTV，完成/部分图像和 WAV 可通过认证 HTTP 取回。HADES-SA media factory 限定实际 NORAD 68446，不把 UNNE 当 HADES-SA；Web 支持媒体预览。新增固定真实 SSDV 参考 JPEG、人工 Codec2 子帧缺失、合成 Robot36 IQ 自检。 [aislop 45582230615](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45582230615) 和 Web 已通过；[Native Q65 45582230550](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45582230550) 与 DSP 编译因自检 Frame 缺少必需 CrcValid 字段失败，本次修复为 null（保留协议无帧级 CRC 的语义），仍需新 CI。
- `4c24d37` ONVIF GetStreamUri 连续 H.264 RTSP/TCP→FFmpeg copy-remux→fragmented MP4→浏览器 MSE。台站提供 FFmpeg，不捆绑、不重编码、无摄像头音频，原快照/PTZ 不变。真实本地 RTSP/RTP 测试流与 FFmpeg 7.1.5 产生 87507 字节 MP4，包含 ftyp/avcC/moof；这是传输与 remux 验证，未证明完整 Host/MSE/物理摄像头。新 Host smoke 与完整 CI 待验收。
- 剩余：新媒体自检和最新 CI、桌面旋转器反馈事务回归、远程媒体历史目录/渐进更新、SDR 硬件控制、统一多台站工作台与 WinForms 全量 IPC、正式安装器/schema migration/ACL/签名、公网和硬件/长时验收。保持 Draft，不合并、不发正式 Release。API 提交签名未独立验证。


## 2026-11-26 — P0/P3 回归、P4 多台站工作区、P5 身份库与 ACL

- 基线 `477fb54` 23/25 通过；[整包 45583543215](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45583543215) 和 [桌面 Compile Check 45583543175](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45583543175) 已通过。Host smoke 因 Windows Runner 缺 FFmpeg 失败；质量门禁因 CameraVideoPlayer 嵌套深度失败。
- **P0/P3:** `3864824` CI 显式安装并检查 FFmpeg；[Host smoke 45730808879](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45730808879) 在 `f051034` 基线上已通过，包括真实本地 RTSP remux 测试步骤；[aislop 45730808842](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45730808842) 仍有一项嵌套深度告警。 `57fff1c` 将播放器、挂载和 MSE 流处理拆分，等待新质量门禁，不修改配置或 baseline。未实测物理摄像头。
- **P0:** `f347b1c` / `5bf8c3b` 修复原桌面旋转器 STOP 使管理反馈事务失效的代际检查，并仅记录实际接受的反馈查询；回归改为等待真实 C2 丢包、验证 STOP TCP 延迟小于 3 秒且事务返回 false。当前新增回归待 CI，非机械 STOP 实测。
- **P4:** `54743db` / `5a2fa1a` Web 可保存、增加、切换和关闭独立台站工作区；各 Host 独立 origin、认证、SignalR、Dockview，不交换登录凭据。远端 Host 通过 `SKYROOF_WORKSPACE_ORIGINS` 配置允许嵌入的控制端 origin；切换时原工作区释放 PTT，避免跨文档丢失按键释放事件，非活动工作区保留页面连接但限制音频和操作。新增真实双 Host Chromium 测试，不是双物理设备测试。跨站 Cookie、麦克风权限与公网 HTTPS 仍需实测；可使用 OPEN STATION 独立窗口。尚非跨 Host 面板任意混排，也未完成 WinForms 全量 IPC。
- **P5:** `4d91a5a` SQLite `user_version` 0→1 事务式增量迁移，新增 token owner/revoked 索引，不重建用户表；启动拒绝未知更高版本。新增旧 schema 重启与 Cookie/Token 保留测试；完整备份恢复、未来破坏性 schema 回滚仍未实现。 `ab9be06` 可选 `Protect-StationState.ps1` / `New-StationConfiguration -ProtectState -ServiceAccount` Windows 文件目录 ACL，实际 SID/继承回归；不等同于 Named Pipe/SignalBus ACL 或 SCM 服务账户配置。
- 上述新增功能均以最新 CI 为验收依据，不能用历史绿色基线替代。P1/P2 已有整包实证不代表真实月面 RF 通联；P3 SDR 实机、P4 WinForms、P5 正式安装器/签名/IPC ACL、WAN/长时和硬件仍有缺口。保持 Draft PR #254，不合并 master，不发布正式 Release。API 提交未签名。


## 2026-11-26 — 收敛 P4 测试与继续 P4/P5

- `505995e` 24/25 CI 成功。[浏览器 45731693740](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45731693740) 日志为 Playwright 5 passed、2 skipped（原生 RTC 专项条件），实际失败是 EXIT trap 的 GNU tail 多文件旧参数语法；`a5f34b4` 改为 tail -n 50，不改测试断言。
- 已确认 [Host 45731693815](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45731693815) 实际通过旧 SQLite schema 原地迁移、双 Host Cookie/token/重启、授权 BIN/LOG/media 下载及真实本地 ONVIF RTSP/RTP→FFmpeg→fMP4。[整包 45731694205](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45731694205) 实际通过 Windows 文件 ACL、SCM 升级回滚、真实 HADES-SA SSDV 精确 JPEG、Codec2 WAV 缺帧计数及合成 Robot36 IQ→PNG 239/240 行。[Compile Check 45731693719](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45731693719) 1857/1857；[aislop 45731693831](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45731693831) 通过。
- **P5:** `51afb17` Device/Station/Media/DSP 服务器明确采用同用户 Named Pipe 默认策略；可按独立环境变量指定受信任 Windows SID 读写授权，创建服务端实例权限仅授予服务账号。新增真实 Windows DACL 检查与使用自定义 ACL 的跨进程 RPC probe。此测试不是使用两个真实 Windows 登录账户的授权/拒绝验证。共享内存 SignalBus ACL 和跨 Session 0 仍待完成。
- **P5:** `644dd0c` 首次安装入口 Install-RemoteStation.ps1，验证 ZIP/manifest、版本暂存、运行库、受限 LocalSystem 站点配置、SCM 注册及可选 readiness/失败移除。新增实际最终 ZIP 的注册、重复安装拒绝、保留数据卸载测试。不是正式签名 MSI/EXE，首次启动失败清理尚未由新测试执行。
- **P4:** `6979841` 原 SkyRoof 新增 `SkyRoof.exe --remote-station skyroof-alpha-station` 独立 WinForms IPC 工作台；直接使用 Station gRPC 而不初始化本地硬件，支持 CAT 读回、RX/TX 频率/模式、AZ/EL POINT/STOP 和跟踪状态。新增 E2E 复用同一适配器，验证模拟 CAT 实际 TCP 频率写入并恢复、旋转器 POINT/STOP 及 tracking status。此为首批桌面 IPC 接入，不是原 MainForm 全面迁移或 WinForms UI 自动化验收。PTT、DSP/媒体、完整 RadioLink 等原有面板仍须迁移。Named Pipe SID 授权是可信本地操作者访问该端点的全部 RPC，不是 Host 角色鉴权。
- 最新代码 CI 需要独立验收，保持 Draft PR，不合并、不发正式 Release；本轮 API 提交未签名。公网 HTTPS/ICE、真实 IC-9700/SDR/rotator/camera、长期稳定性无新实测。


## 2026-11-26 — WinForms PTT、跨账户 IPC 与依赖锁定

- `64c9969` 已确认 [Named Pipe IPC Probe](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45744812278) 实际 DACL 检查通过，128 次跨进程 RPC P50 0.608 ms / P95 0.924 ms；Host、Chromium、aislop、IQ/RF、SignalBus 等已通过。Compile Check 与 CW Benchmark 因新增桌面 gRPC 依赖使 NuGet lockfile 不一致（NU1004），不是算法性能或 C# 编译失败；新增独立锁文件刷新工作流，保留 LockedMode，使用 Windows Runner 实际 restore、再 locked restore，上传产物并仅在分支未移动时提交锁文件。
- `1fae064` 桌面独立 IPC 工作台增加按住 PTT、释放、捕获丢失/窗口失焦回 RX、事务心跳、关闭时释放当前 session。Device 仍负责硬件及超时回收；双 IPC 客户端模拟抢占、旧事务释放及新所有者释放新增 E2E 检查。仅是 CAT PTT，尚未接入桌面麦克风 PCM。
- `f9aeda3` 命名管道客户端显式请求 ReadWrite + Synchronize，避免 GENERIC_WRITE 隐含 CreateNewInstance 导致跨账户被拒绝。新增标准 Windows 用户授权及拒绝探针，安装器可显式传入 DesktopOperatorSid，仅授权 Station 管道，不授权文件目录。该 SID 有完整 Station RPC 权限，不是 Host Web 角色。新测试尚待运行。
- SignalBus ACL、跨 Session 0 共享内存、原 MainForm 全量迁移、真实射频/远程公网实测和正式签名发布仍未完成；保持 Draft，不发正式 Release。


## 2026-11-26 — lockfile 恢复与 Global SignalBus

- `a4aa985` 的 [Windows IPC Probe](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45746524521) 已实际通过标准用户 SID 授权/拒绝、DACL 和 128 RPC（P50 0.610 ms，P95 0.882 ms）。
- `e71c4e9` 修复 aislop 新增唯一告警（WinForms close async void 改 Task）；锁文件候选生成加入现有 Compile Check 独立 job，实际 locked restore 验证、上传 artifact，原 build job 的 LockedMode 不变。独立 refresh workflow 不再自动写分支，避免竞争；取得真实 artifact 后审查并提交锁文件。
- `9fbc8f1` 可选 Global SignalBus：默认 Local 保留，Global 生产者创建页文件共享映射及命名 mutex，明确 Windows SID DACL；消费者只打开已存在的只读映射与同步 mutex，不创建、不写入。安装脚本可传 GlobalSignalBus / SignalBusReaderSid，所有托管进程使用同配置；授权仅为可信读者，不能跨用作生产者。
- 本轮增加 Session 0 SYSTEM scheduled task 生产者、独立进程授权读取、写入拒绝、未授权读取拒绝及配置传播测试。日志明确记录实际 session ID，若 CI 客户端也在 Session 0，不宣称已跨 Session 验收。新代码 CI 待确认；未接入桌面 PCM/频谱读取，未完成正式安装器/签名。

- 后续提交：WinForms 同一 Station IPC 适配器新增 CW 状态、WPM 设置与硬件读回、发送及所属 session 停止；新增真实 Station/Device/TCP SkyCAT 模拟器 E2E，验证 WPM 量化、完整消息及中途停止。未引入新依赖；CW RX 解码与完整 MainForm 迁移仍未完成。


## 2026-11-26 — P0–P5 集中交付增量

- **已验证的历史基线：** `cfdc4d9` 22/25；[Global SignalBus 45748069529](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45748069529) 实际 SYSTEM Session 0 → Session 2，授权只读、写拒绝和未授权拒绝。桌面 CW E2E 已通过。旧整包 SCM 升级/失败回滚业务断言通过，结束时 DLL 清理失败；不能将旧整包标记成功。
- **P0：** `47391cc` 提交 Windows CI 实际生成并通过 locked restore 的三个 lockfile，Git blob 与 artifact 完全一致。随后旧 NuGet/MSBuild 的 IPC Contracts assets 缺失由 `a800271` 纳入 solution 修复；保留 LockedMode 与 aislop gate，删除临时生成 job。 `3d3f8ba` Supervisor 等待子进程退出后释放句柄并进行有限清理重试。
- **P1：** `a800271` SSDV/SSTV 实时预览、稳定媒体 ID、revision、final/complete 区分、持久化 NORAD/UTC 日索引和鉴权分页，保留最终参考 JPEG 与 Robot36 检查；`4345103` PNG 渐进预览限频，最终帧不受限。
- **P2/P4：** `cee739a` Station 单一 IPC 入口转发 DSP 配置/编码和实时信号；`2b54e42` 桌面 NAudio RX 播放、原生 TX PCM、AF/RF 频谱、实时 CW/FT4/Q65/遥测结果；`4b5824c` CW RX/history、FT4/Q65 单次定时 TX、取消、卫星目录、RadioLink、EME 预测、持久化跟踪、PARK。新增真实 Station 流测试。仍是独立 WinForms 工作台，不是原 MainForm 所有控件迁移；桌面自动 QSO 和原面板 UI 等价性未完成。桌面端时隙/延时配置须与 Station 一致；物理音频设备未测。
- **P3：** `4345103` 增加真实 RTSP/RTP、ONVIF SOAP、Host remux、Chrome 视频解码帧计数端到端测试，不 mock 视频内容。`6e23f09` Station 管理固定配置 SoapySDR companion，起停、重调谐、采样率/增益、实际读回状态、Host 角色授权；设备选择来自站端配置，重调谐结束旧采集并重建 IQ epoch。设置 Station 进程的 SKYROOF_SDR_DEVICE_ARGS 后启用；需要实机和驱动测试。
- **P5：** `3b25c43` Inno Setup 首次安装固定 alpha 站，验证 ZIP、注册但不启动未配置电台，保留版本/数据，实际 SCM 安装/卸载回归已接入 CI。更新继续使用 Switch-RemoteServiceVersion.ps1。可选 Sign-RemoteInstaller.ps1 使用操作者已安装的代码签名证书，验签失败即报错；未提供真实证书，当前产物仅无签名开发版。安装路径须管理员控制。公网 HTTPS/ICE/FRP、物理 IC-9700、SDR、摄像头、旋转器和长期运行均未实测。
- 所有新代码需要最新 SHA 的实际 CI，不能用旧 SHA 的成功替代；保持 Draft #254，不合并、不正式 Release。API 提交未签名。


## 2026-11-26 — 最新整包实证、桌面自动 QSO 和在线跟踪调谐

- `0044154` 最终 CI **23/25**；[Compile Check 45763215844](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45763215844) 1857/1857，[整包 45763216040](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45763216040) 成功，真实 SCM 回滚、最终 ZIP 安装/卸载以及 Inno Setup EXE 实际注册/状态保留均通过，并上传无签名 EXE 和 ZIP。[E2E 45763215917](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45763215917) 已通过桌面 CAT/ROT、PCM 索引/epoch、CW/PTT、双站月面光行时以及 SDR 未配置/鉴权测试。
- `0044154` aislop 报告 10 项新增问题；浏览器测试真实 H.264 解码尺寸 160×120 和 decoded=true，但持续播放断言未通过。 `daa1b28` 处理空 catch、nullable、Trace 输出等全部新告警，并延长实时 RTSP fixture、修正初始缓冲寻址、增加播放时间继续前进断言；[aislop 45773194313](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45773194313) 已实际通过。浏览器结果仍待确认，不删除或跳过播放断言。
- `a0df4fd` / `d585276` 桌面 FT4/Q65 AUTO QSO 使用 Host 原生授权 NDJSON 状态机，Bearer token 仅内存保留，显示实际 Host identity、可复用连接、取消、终态检查、Moon job 选择。新 packaged E2E 直接复用桌面 HTTP 适配器，继续精确三报文 UDP 波形及报告验证。允许 HTTPS 或 loopback HTTP，禁止自动重定向，避免 token 泄露；不是新建第二套自动 QSO 引擎。
- `d0d09cb` 增加 StationTrackingJobs.Tune RPC 和 Host SignalR TuneTrackingJob，按原 JobId 串行校验/持久化修改 RIT、XIT、转发器位置、手动及基频偏移，不替换任务、不清空计数、不限制数据库通带外调谐。仅处理卫星 RadioLink 偏移，EME Doppler 模式/频率调整仍按既有 Moon 任务流程。Web 和桌面均接入；E2E 检查实际 CAT 读回、旧任务 ID 拒绝、进程重启恢复，RIT 在 RX Doppler 前，XIT 在 TX Doppler 后。
- 桌面新增 QTH/频率/Moon/DX 站点结构化字段与高级 JSON 可选模式，默认未填写 QTH 不提交预测，旋转器指向由操作者选择；增加 STA WinForms 构造/控件 smoke。原 MainForm 的 Context 仍直接持有 CAT、SDR、音频、DSP，不冒充全量迁移。原本地模式不变。真实设备、公网、长时、受信任签名和原面板全量迁移仍是验收缺口；本轮 API 提交未签名，保持 Draft PR #254，不合并或正式 Release。最新 SHA 仍需独立 CI 验证。


## 2026-11-26 — 浏览器闭环、在线 RadioLink 读回、旋转器 STOP

- `3a6529d` 最终 24/25 CI；[Compile Check 45773776788](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45773776788) 1858/1858；[整包 45773776789](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45773776789) 已验证桌面 Bearer/NDJSON 客户端复用 FT4 三时隙及 Q65 三报文自动 QSO，模拟接收结果、实际本地 UDP 及原生编码 PCM 一致，最终报告和终态确认；安装器实际 SCM 注册和保留配置卸载。 [E2E 45773776445](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45773776445) Orekit 和 SGP.NET 均通过 RIT/XIT 在线调整、过期 job ID 拒绝、重启恢复。aislop 通过。
- 唯一失败浏览器现场下载实际截图及 trace：视频被小 Dockview 面板滚动区裁出视口。独立 Chromium + FFmpeg fMP4 复现：不可见时 paused=true, currentTime=0, decodedFrames=4；滚入视口 1.8 s 后 paused=false, currentTime≈1.794 s, decodedFrames=22。 `5996a7d` 视频预览移到 PTZ 前、启动时滚到预览、仅在打开时请求一次播放，尊重手动暂停。 [Browser 45781293901](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45781293901) 6 通过/2 条件跳过，真实 H.264 解码/播放/暂停/恢复均通过；[aislop 45781293752](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45781293752) 已通过。不降低断言或关闭质量门禁。
- `112146f` TrackingJobStatus 新增 active_radio_link、celestial_target 和 point_rotator；Web 与桌面显示 Station 当前有效偏移（未调整字段不覆盖），桌面新增 RX/TX base Δ、读取有效偏移，调谐和停止绑定已展示任务 ID 而非重新读取可能已被替换的任务；E2E 验证配置读回与重启持久化。
- `db2df08` 修复明确的故障模式：手动旋转器 STOP 后自动跟踪下一周期可能重新 POINT。Station 立即抑制当前任务的新自动指向并直达 Device 紧急 STOP，随后排空已进入的跟踪周期，持久化 PointRotator=false，发送最终 STOP；保留原 job ID、射频 Doppler 和所有 RadioLink 偏移。客户端 STOP 截止时间覆盖后续持久化/收尾（初次紧急 STOP 不等待任务 gate）。停止指向后可通过显式 START TRACK + Point rotator 重新启用。E2E 新增实际 TCP POINT 数量、Doppler 持续及 Station 重启后不恢复 POINT 的断言。最终 STOP 响应只确认指令发送/读回，不表示独立机械停止确认。
- 以上新 SHA 验收需独立 CI。原 MainForm Context 全量迁移、物理硬件、实际公网和可信 Authenticode 仍未完成。保持 Draft，不合并或正式 Release；提交未签名。


## 2026-11-26 — 原生停靠工作区、台站绑定、过境与天空轨迹

- [db2df08 E2E 45781634773](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45781634773) 已在 Orekit 和 SGP.NET 两套后端实际通过：紧急 STOP 只停止自动指向、无线电多普勒继续运行、Station 重启保留停止状态；[b0d3f5f 浏览器 45781742638](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/45781742638) 已通过。b0d3f5f aislop gate 新增一项 TrackingJobRunner 文件主构造器长度告警；拆出 Snapshot 到 partial，不修改门禁或 baseline。
- 112146f Compile Check 出现单项 NativeRotatorWorkerTests.StopBypassesPendingRemoteManagementVerification 失败（管理租约为 null，1857/1858）。6fe5452 测试等待实际 fresh position、为真实 TCP fixture 增加读超时预算，原 STOP <3 s 断言保持不变；生产控制协议未改。不能据此宣称最新测试已通过。
- 6fe5452 远程 WinForms 复用原 MainForm 的 DockPanelSuite 及主题；文档工作区可停靠、浮动、隐藏、VIEW 菜单恢复；仍是单独的 Station IPC 模式，原本地 MainForm 及硬件 Context 不改。不声称所有原面板功能对等。
- f98b5cc StationRuntime.Identify 返回进程级实例标识，Host 经实际 Station pipe 获取同一身份；桌面连接及自动 FT4/Q65 请求携带期望实例，避免误将本地 CAT / Moon job 用到另一台 Host。Web 单 Host 工作流不要求新 header。新 E2E 检查同实例、匿名拒绝和错误实例 409 / 无 PTT；整包自动 QSO 使用有效实例 header。
- 9e4a5c2 增加只读 PlanPasses RPC，复用 OrbitPassPredictor（30 秒扫描、交点约 1 秒细化、原 72 小时边界恢复），搜索输入最长 72 小时、支持取消、每窗口 31 个等间隔样本并包含峰值。使用同一 Station backend，每历元按既有 ISS OEM coverage 选择，不缓存旧轨道结果或使用客户端独立计算。Moon common visibility 以两站仰角较低值和同一 mask 判定；不是链路预算或地形遮挡模型。原生 PASSES / SKY 停靠窗、UTC AOS/LOS、峰值、backend、极坐标路径。新真实 Station E2E 检查已知 TLE 过境、峰值与 Predict 一致和无设备写入；尚未取得新 SHA 的 CI 结论。极短掠边窗口仍受采样步长限制。
- 最新代码未实机验证；保留 Draft PR #254，不合并、不发布，API 提交未签名。剩余原 MainForm 专属面板/交互（EarthView、录制、QSO 日志、AFC 等）、真实硬件和 WAN/长期稳定性、可信发布签名需要后续独立验收。


## 2026-11-26 — 原 MainForm 四类核心专属工作区迁移

- e2b5650 已获得 Station E2E、aislop 和浏览器通过结果；整包、Compile 等仍排队，不能将其视为全绿。8a678c9 的 Compile Check 明确出现 RemoteEarthMap 缺少 VE3NEA.Utils 命名空间，a9ca955 已补 import，最新编译尚未确认。
- 8a678c9：StationTracking.GroundTrack 使用实际 OrbitTrack.PredictGeodetic / ISS OEM 每历元选择，提供 WGS-84 经纬度、椭球高与来源；WinForms EARTH VIEW 复用原 dxcc 地图纹理，实时星下点、过去/未来轨迹、平移缩放、跨日界线不错误连线。原 GPU/在线 XYZ 瓦片、足迹圈、原 EarthView 全部操作尚未移植；新 E2E 验证采样数和无硬件写入。
- ac1941f：StationLogbook 本地 IPC、站点受保护目录、原子 JSON 保存，GUID 幂等重试（相同内容返回已保存结果，冲突拒绝）、查询分页、原 AdifFile 序列化复用、FT4/Q65 SUBMODE、SAT/EME、收发频率/报告/UTC 等。桌面 QSO LOG 支持填写、CAT 预填、查询/导出；新增同呼号历史辅助、完成自动 QSO 后预填可审阅草稿（不伪造通联频率、不自动保存）、同一分页响应输出数据和下一 cursor、临时文件后替换。外部 LoggerInterface.dll、WSJT-X UDP QSO、旧 ADIF 导入/LoTW 同步仍未迁移。
- 48c3802：RECORDER 读取 Station AF PCM16 与 DSP Telemetry CF32 I/Q tap，保存桌面所选 WAV，sample index/epoch/格式断点独立分段，不将缺失样本伪造成连续信号。支持 AF 回放/MP3、配置后的 DSP CF32 重放、QSO 时间/样本标记。真实 IQ tap 只覆盖当前 48 kHz telemetry ingress，并非任意宽带 Soapy IQ；桌面关闭会停止录音，不是无人值守站端录制。原 UI 的完整录音事件/时间轴、原始 SDR 全速 IQ、AF 离线 DSP 回放仍未迁移。加入 exact-byte WAV 测试和 Station IQ IPC 测试。
- a9ca955/aebcdb7：AFC 使用原 AfcIfInput WASAPI/loopback 真 12 kHz IF 测量，复用原 AfcSpectrumView 瀑布图；AfcController/模型/证据门及 CAT chronology 源码链接到 Station。独立暂态 RX residual 与原 RIT/XIT 组合，job ID 生命周期及客户端会话 8 秒失联回收，不持久化 AFC bias；Device 锁内实际 PTT 选择保持值，独立 RX/TX 后端才允许 AFC Auto 修正（单 VFO 仅辅助，避免改 TX），实际 CAT RX 读回时刻记录于 Device。测试发送合成 12 kHz IF 原算法观测，验证 RX 偏置、TX 不变、关闭后移除。真实硬件 IF 极性、精确时间对齐、自动探针校准、AFC 站端无人值守采集、UI 运行和最终 CI 仍待确认。
- 所有新实现位于远程 Station IPC 工作区，原本地 MainForm 未替换。不能称原 MainForm 功能完全对等。未签名、保持 Draft PR #254，未发布。


## 2026-11-26 — MainForm 功能等价性继续迁移（ADIF / EarthView / Raw IQ / AFC）

- **QSO：** `223bcdf` 累积实现长度感知 ADIF 解析、明确 STATION_CALLSIGN 或 UI MY CALL、UTC 日期/时间（分钟或秒）、Hz/MHz 转换、SAT/EME 字段、第三方 FT4/Q65 → MFSK/SUBMODE；新导出保留 APP_SKYROOF_QSO_ID 和 APP_SKYROOF_UTC_MS，原记录精确 round-trip，第三方记录映射后生成稳定 ID。全文件解析后逐条使用既有 Station Save RPC，按已确认数报告中断；不把部分导入称为数据库原子批事务。新增真实 Station E2E（重复导入、字面 <EOR> 备注、错误长度、毫秒 UTC）。旧导出没有 APP ID 时不保证与此前不同 ID 的手工记录自动去重；目前仅保留 Station QsoRecord 支持的字段，未完成外部 logger / WSJT-X、完整 ADIF 扩展字段与 LoTW。
- **EarthView：** `7b4bd2a` 累积实现 WGS-84 椭球零仰角地平线足迹、日期变更线与地图中心接缝跨越绘制、实线过去/虚线未来轨迹、显式足迹开关及地图重置。独立 Python 数学复算四组场景（400 km 赤道、1200 km 高纬/日期变更线、400 km 极点、35786 km 南高纬），724 个边界点法向投影最大绝对残差约 1.79e-7 m；C# xUnit 以独立 ECEF 表面法向验证切线约束及赤道解析值，尚待 Windows CI。仅几何可见范围，不是 RF 链路覆盖、地形或大气折射；原 GPU/在线 XYZ 瓦片仍未迁移。
- **Recorder：** `bb09015` 累积增加 SoapySDR 实际 read 后、信道化前的 CF32 RawIqTap（站点独立 SignalBus，256 × 65536-byte payload，真实采样率/样本索引，overflow 重置 epoch），Station IPC 新枚举保持原值，WinForms 可选 AF、48 kHz Telemetry IQ、原始 SDR IQ。WAV 使用实际 sample rate，按 epoch/缺失索引/格式分段，录制时长按实际采样率累计。新增生产 RawIqTap → 实际 Station IPC 的 2 MS/s 字节、索引及 epoch 断言，以及原生采样率 WAV exact-byte 测试。这不是 Soapy 硬件吞吐验收；桌面 IPC/磁盘性能可能造成显式分段缺口，无人值守 Station 本地无损录制、宽带 IQ 重放与 AF 离线解码仍待完成。
- **AFC：** `5e8dded` 累积增加同一会话中的 waterfall 选目标和 APPLY TARGET（采集 preset、pilot、lock here/center），AfcTargetChange 使用 IF generation，Station 在既有跟踪 gate 内 rebase，旧 generation 的测量不能改变新目标，保留 Device PTT/RX-only 语义。新增真实 Station E2E：建立非零 RX 修正后重新选目标、发送旧 generation 的错误测量、新 generation 重新捕获并检查偏置；UI smoke 增加入口断言。尚未迁移真实站端无人值守 IF 采集、自动 probe/restore 极性校准及硬件时间对齐；RadioLink 手动 RF 调谐仍按原远程实现使 AFC 会话失效。
- 四项新功能均在独立 Station IPC 工作台中实现；未替换原本地 MainForm。所有新代码须以最新 SHA 的 Compile Check、E2E、开发整包和 pinned aislop 实际结果验收。没有本地 Windows/.NET 实机测试环境；不降低门禁，不合并 master，不发布正式 Release，API 提交未验证签名。


## 2026-11-26：服务端设置与用户测试构建

- PR #256 已将 master 的 12 个新增提交（9b18e81）合入 Remote 开发分支（a78d983）；保留 PR #254 草稿，未将 Remote 合入 master。
- 桌面 Settings 增加 Remote / 服务端设置入口。独立 RemoteStation.json 保存 Standalone / ServerWorkspace、Station ID、IPC pipe、Host origin 和 Supervisor 路径；正常退出后重新启动生效。在创建原 MainForm 硬件 Context 前决定模式，默认仍为 Standalone。
- ServerWorkspace 内 Settings 支持 IPC 连通性测试、打开 Web、查询/启动/停止已安装服务。Supervisor 配置编辑按进程列出环境变量，支持新增/删除、敏感值遮盖、原子保存、显式授权当前用户 SID；需要系统权限，保存后重启服务生效。不是新的安装器或远程服务注册向导。
- Recorder 新增宽带 CF32 WAV 重放：实际采样率与显式相对频偏，复用 IqChannelizer 转至 48 kHz；已有 48 kHz 零偏移直接路径保留。站端无人值守录音及 AF 离线 DSP 未完成。
- QSO 增加 127.0.0.1 UDP Logged ADIF（WSJT-X schema 2/3 type 12，默认 2237，可配置），明确忽略其他消息，复用 ADIF 导入和 Station 幂等保存。需要手动启用监听。LoggerInterface.dll、LoTW 尚未迁移。
- 旋转器并发 STOP 旧基线 8bbf937 Compile Check（45786463535）1859/1860 通过；失败为测试等待期间验证已结束。新增 worker 实际验证开始事件和 TCP fixture 同步，不改变 STOP <3s 及验证失败断言。新 CI 未确认，不宣称 P0 已通过。
- Compile Check 新增成功编译后的 Remote desktop preview artifact，不绕过后续测试或质量门禁。服务端 ZIP/安装器仍通过 Remote package workflow 构建；没有已确认的新构建产物时不发布。
- 仍未完成：站端无人值守 AFC 与自动探针校准、站端录制、原 GPU EarthView 在线 XYZ 在 Remote 窗口的完整接入、扩展 ADIF/外部 logger/LoTW、其他原 MainForm 专属功能及新 SHA CI、实机验证。上述同步不等于功能对等。
