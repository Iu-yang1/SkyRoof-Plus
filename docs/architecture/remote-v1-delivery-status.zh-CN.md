# SkyRoof Remote V1 开发进展（按实际代码与 CI 判定）

此文件记录 feature/remote-v1-multiprocess-foundation 的实际进展。此前架构计划的 P0–P18 全部为完整交付条件，不允许将本文件中的协议原型称为正式 Remote V1。

| WP | 当前实现/证明 | 缺口 |
|---|---|---|
| P0 | 已阅读 Context/RadioLink/Direct LAN 及现有 CI；独立测试基线可运行 | 音频、DSP、频谱旧版全量可复现性能记录 |
| P1 | PTT/媒体/Time 纯逻辑原型；gRPC Named Pipe 双进程通过；共享内存 Ring Windows 跨进程通过 | libdatachannel/FRP UDP ICE、C ABI、真正 DSP 音频总线 |
| P2 | Supervisor 普通后台、Windows Service 集成代码及进程崩溃测试 | SCM/Session0 实测、ACL、安全关闭及设备生命周期 |
| P3 | 未实施 | Device 的 Icom LAN/CAT/Rotator/SDR/ONVIF 迁移 |
| P4 | 未实施 | 轨道和调度真正搬迁至 Station |
| P5 | 共享内存原型 | 全部 DSP 独立运行/原始样本回归 |
| P6 | SAP/1 Header、媒体选择器 | libopus、ASRC、真实 Media Service/PCM 路径 |
| P7 | 未实施 | WinForms IPC 全面改造 |
| P8 | Identity/SQLite、密码 Cookie、Token、登录码可运行原型 | 数据库迁移、Windows SID、角色细分与生产安全验收 |
| P9 | SignalR MessagePack、Development 模拟 Binary WSS | 真实设备 Snapshot、Delta、频谱和音频 |
| P10 | 未实施 | libdatachannel Audio Track / DataChannel 原生与浏览器联通 |
| P11 | 配置与部署文档草案 | FRP TCP/QUIC/UDP 实际连通和质量比较 |
| P12 | 纯逻辑 PTT 数字抢占和旧事务释放测试 | 真实硬件 readback、TX 缓冲清理及 Rotator |
| P13 | 未实施 | WinForms 多台站 Remote Client |
| P14 | 未实施 | React/Dockview 全 UI 功能 |
| P15 | Timestamped TX 状态/时间模型 | 波形存储、授权、CAT Bridge、真实 RF 调度和时间标定 |
| P16 | 新增无硬件模拟及崩溃测试 | 端到端音频/网络压力和真实故障注入 |
| P17 | 未实施 | 安装包集成、用户/服务双模式升级回滚 |
| P18 | 使用原仓库 Compile Check/Docs/aislop，新增子项目 CI | 全部模块集成、许可证审计、正式 Release |

每次正式推进应记录：提交 SHA、CI Run ID、测试实际通过情况、未测设备与网络、失败分析和回滚方式。新增项目尚未纳入完整主程序的运行时依赖，原有 SkyRoof 主流程仍保持不变。
