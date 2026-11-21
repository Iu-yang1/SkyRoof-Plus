# SkyRoof Remote V1 — P0–P18 实施与验收状态

分支：feature/remote-v1-multiprocess-foundation；Draft PR #254。当前**不能**认为 P0–P18 已全部完成，也不能将开发测试包当成 Remote V1 正式 Release。

| 阶段 | 已提交、已纳入自动化验证的实现 | 仍缺少的发布条件 |
|---|---|---|
| P0 | 核对原 SkyRoof RadioLink、CAT/SkyCAT、Direct LAN、CI，建立独立测试基线 | 全部旧版性能回归基线 |
| P1 | Protobuf、gRPC Named Pipe、共享内存 SignalBus、流 Epoch/Sequence/SampleIndex | 生产 IPC ACL、协议升级兼容 |
| P2 | Supervisor 普通后台和 Windows Service 兼容宿主，故障重启，停止前尝试释放 Device PTT | 真实 SCM/Session0 账号与权限实测 |
| P3 | 独立 Device：SkyCAT/rigctld TCP、RX/TX 独立频率、模式/PTT、读回、故障事务日志和心跳过期 RF 释放 | IC-9700 Direct LAN、原旋转器路径规划/反馈策略、SDR/ONVIF 和实机验收 |
| P4 | Station Core 独立 gRPC 进程，Host→Station→Device 命令链 | RadioLink 转发器模型、Orekit、连续跟踪和调度迁移 |
| P5 | Shared Memory Ring 多进程读写、慢读者丢帧测试 | FT4、CW、Q65、遥测等 DSP 迁移 |
| P6 | C++ libopus 48 kHz PCM 编解码、SAP/1、跨进程 PCM16→认证 WSS、WebAudio Worklet 接收 | 真正 Media Service 的 IC-9700 音频 I/O、Opus/PCM 自动切换与 ASRC |
| P7 | 共用跨进程命令契约 | 原 WinForms 生命周期解耦及 IPC 功能整体替换 |
| P8 | Identity + EF SQLite、Cookie/Token/一次性码、站点管理员账号/角色管理与实时撤权测试 | 生产 EF Migrations、Windows 本机 SID/IPC 身份及多台站凭据管理 |
| P9 | SignalR MessagePack、实 CAT 命令与状态读回、开发频谱、真实 SignalBus PCM16 WSS | 原电台频谱/状态的实际采集、Delta 与多客户端流控 |
| P10 | 固定 libdatachannel 的双 Peer ICE/SDP/DTLS/SCTP DataChannel 与 Opus RTP Audio Track 本机回环 | 与正式 Media、浏览器和 FRP UDP 的联合公网验证 |
| P11 | frps/frpc v0.72.0 TCP/QUIC 控制+UDP Socket 真实本地代理双路径测试，手工部署示例 | 公网/CGNAT 实测、FRP UDP+ICE 以及延迟/丢包验收 |
| P12 | 两个独立账户的后按优先 PTT、过时释放无效、数字暂停、崩溃/心跳回 RX、实时角色授权 | 真电台 PTT/音源切换、TX 缓冲、旋转器/摄像机仲裁 |
| P13 | WorkspaceRouter 处理台站切换下的原 PTT 事务绑定 | WinForms 完整多台站连接与凭据存储 |
| P14 | React/Dockview 工作台、实际 CAT 调谐和 PTT、WebGL2 模拟频谱、WSS Audio Worklet、布局持久化 | 原 SkyRoof 全部 Panel、多远端主机、浏览器操作验收 |
| P15 | UTC/单调时钟预约模型、SHA-256 PCM 封存和恢复 | 外部 WSJT-X CAT/PCM Bridge、真实 Timestamped RF TX 及标定 |
| P16 | 原生媒体、FRP、SignalBus、E2E 多进程、凭据、断线、崩溃等 CI | 大规模网络故障注入、24h 运行和无线电实机验收 |
| P17 | Windows x64 四进程+Web 开发包 CI，要求从发布文件启动并重跑 E2E | 正式签名安装包、升级/回滚、Windows Service 实验 |
| P18 | 独立 CI + 原项目 Compile Check、Docs 与 aislop Quality Gate | 所有阻断条件消除，许可证审查，正式 Release |

## 当前已闭合的模拟链路

认证用户（Administrator/Operator）→ SignalR/MessagePack → Host → Station Named Pipe/gRPC → Device → SkyCAT/rigctld TCP 模拟设备。覆盖 RX/TX 频率区分、PTT 抢占、用户撤权、断线释放、硬件确认、异常重启等。

另一条真实跨进程路径：Windows SignalBus PCM16 生产者 → Host 认证 WebSocket SAP/1 → 浏览器/独立客户端消费。Opus Audio Track 与 DataChannel 已在原生两 PeerConnection 上通过本地测试，但尚未连接真实电台。

## 禁止混同的结论

SkyCAT/rigctld 模拟测试不是 IC-9700 Direct LAN 全功能。Native Opus 与 WebRTC 本机环回不是公网 FRP UDP 浏览器实测。Web/Dockview 构建不是原 SkyRoof 全部停靠窗口的功能回归。开发 ZIP 不是正式 Release。

继续开发的核心顺序：迁移 Station 的 Orbit/RadioLink，接入 Icom Direct LAN/Rotator，搬迁 DSP 和完整音频 Media Service，随后把 WinForms 与 Web 的多台站窗口接到同一业务契约，完成实机及网络容错验收。

变更留痕：参考 PR #254 的提交 SHA、GitHub Actions 运行和 CI 失败日志；当前通过 API 提交代码，不能证实 Git 提交已加密签名。


## 本轮增量：单次轨道观测及执行

Station 直接引用原工程 SkyRoof.Orbit.Contracts / SkyRoof.Orbit.SgpNet，复用含地球自转速度修正的观测结果。PredictTracking 只读；ApplyTracking 由 Operator 授权，对地平以上（用户指定 mask）观测执行双向 CAT 和可选 rotctld P 命令，返回实际读回。普通 simplex rigctld 不假装支持独立上下行；使用 SkyCAT 独立 F/I 的路径完成双工。React 新增手工 TLE 单次观测及执行面板。当前不是连续轨道跟踪服务；未迁移现有的自动目标选择、ISS OEM、Orekit 数据、角度路径规划、Park 和全部 RadioLink 转发器逻辑。

新增 E2E 测试引用 Vallado 固定历元 TLE、几何距离有限差分（径向速度误差小于 3 m/s）、两方向 Doppler 符号、CAT F/I、rotctld P/p、Viewer 只读权限和不新增 PTT 命令。具体通过状态以本分支 CI 为准。

## 连续跟踪任务（本轮新增）

Station 通过独立 BackgroundService 执行单调时钟周期调度，使用台站当前 UTC 重新计算 TLE 观测、CAT 多普勒和 rotctld 指向；不补发历史坐标。共享台站任务提供 Start/Status/Stop，Operator 可启停，Viewer 只读；旧任务 ID 的停止请求不影响新任务。ResumeAfterRestart 默认由页面开启，任务以独立配置文件持久化（SKYROOF_TRACKING_JOB_FILE），仅恢复已授权的跟踪任务，不恢复 PTT。停止清除持久化记录，并向已使用的旋转器发送 STOP。设备失败会显示错误并按新历元重试。新增 E2E 模拟覆盖掉线、Station 强杀重启、恢复、停止及停止后无新设备写入。仍缺真实 Orekit/ISS OEM、完整 RadioLink 转发器及旋转器路径规划迁移；CI 结论以具体运行记录为准。
