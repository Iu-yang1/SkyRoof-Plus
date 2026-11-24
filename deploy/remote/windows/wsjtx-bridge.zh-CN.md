# WSJT-X 客户端桥接器（Remote V1.7 开发版）

此工具运行在操作者的 Windows 电脑上，不属于远端 Supervisor 的六个服务进程。使用认证 SignalR CAT 与 48 kHz PCM16 音频链路；不需要将远端电台的 CAT TCP 端口暴露到公网。

## 配置

1. 在远端 Host 创建 Operator 权限的访问令牌，使用 HTTPS 连接。将令牌置于客户端进程环境变量 `SKYROOF_REMOTE_TOKEN`，不要写入仓库、日志或公开配置。
2. 安装两个独立的虚拟音频通道（例如虚拟音频线缆），使 RX 与 TX 不互相反馈。使用 `dotnet SkyRoof.Remote.WsjtxBridge.dll --list-audio` 查看设备索引。
3. 在开发包的 `tools/WsjtxBridge` 目录运行：

```powershell
dotnet SkyRoof.Remote.WsjtxBridge.dll https://station.example 4534 0 1
```

替换示例 URL、端口和设备索引。第一个索引为桥接器播放输出（连接 WSJT-X 输入），第二个为桥接器录音输入（连接 WSJT-X 输出）。双向 PCM 工作时需要远端 Device 原生 LAN TX 音频能力。省略两个索引可使用 CAT-only，音频需自行连接正确的电台路径，不能将 CAT-only 当作完整数字发射。

4. WSJT-X Settings / Radio：Rig 选 Hamlib NET rigctl，Network Server 填 `127.0.0.1:4534`，PTT Method 选 CAT。卫星场景需确认 MAIN/SUB、split 及多普勒由哪一侧控制，避免两个软件同时独立改写频率。Audio 使用上述对应的虚拟录音与播放端点。

## 事务和验收边界

- CAT 兼容子集支持频率、模式、PTT、split、VFO 和协议握手。电台模式使用设备读回，不将请求值当作实际硬件状态。尚未声明完整 Hamlib 命令集或所有 VFO/滤波器控制兼容。
- 数字 PTT 使用独立 owner/epoch，2 s 心跳续租，Device 默认 15 s 租约超时；断线释放，手动操作可抢占，旧 PCM 或释放不作用于新 owner。不会自动重放旧 TX。
- PCM 每帧 20 ms，48 kHz mono，RX 检查 SAP/1 header、样本索引和 epoch；TX 队列溢出会停止对应数字事务，避免持续发送陈旧样本。
- 实际 WSJT-X/Hamlib 程序、虚拟声卡、远端 RF 延迟与公网压力尚须验收。外部客户端流式 TX 不等同于内部 UTC 定时 FT4 封存波形发射。
- 新 CI 测试使用真实 localhost TCP 命令与认证 SignalR/Station/Device 模拟原生 UDP，验证 CAT、PCM 样本和断连返回 RX；以对应 SHA 的实际 CI 结果为准。
