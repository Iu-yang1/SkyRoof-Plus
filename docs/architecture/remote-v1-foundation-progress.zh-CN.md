# SkyRoof Remote V1：P0/P1 基础代码与模拟测试

本分支为 feature/remote-v1-multiprocess-foundation。首批实现独立 .NET 10 类库和可重复模拟测试，不改动现有业务代码。

## 已实现

- TransmitCoordinator：共享 Operator PTT 后按优先，配置一致保持 RF TX，配置不同请求硬件重配置；旧事务释放不得影响新事务；人工接管数字 TX 后暂停自动发射。
- TransportSelector：Opus 首选标准 WebRTC Audio Track；ICE 失败优先 WSS，Track 故障可转 RTC DataChannel；Raw PCM 不自动变更为有损编码。
- SAP/1：52 字节 Little Endian 二进制帧头，包含 Stream Epoch、Sample Index、Sequence 和 UTC 时间戳。
- TimedTxScheduler：定时任务 Seal、Arm、Start、Complete 及恢复数学模型，冻结 UTC 到 monotonic 映射。
- 16 组无需实机的确定性模拟，覆盖多操作员并发、抢占、过时 PTT 释放、自动发射暂停、网络回退、音频帧连续性和定时任务异常恢复。
- Windows / Linux 两平台的独立构建与模拟 CI。

## 当前边界

尚未接入 Station、Device、DSP、真实媒体和现有 IC-9700。未实现真正的 gRPC Named Pipe、Shared Memory、WebRTC ICE、FRP UDP 或 Windows Service。Timestamped TX 仅实现任务状态与时间换算，尚无波形上传、内容哈希验证、授权持久化、音频缓冲控制、硬件读回或 RF 时间标定；不得将该原型当作已完成的精确发射功能。

## 验证方式

在具有 .NET SDK 10 的环境执行：

    dotnet run --project tests/SkyRoof.Remote.Foundation.SimTests/SkyRoof.Remote.Foundation.SimTests.csproj -c Release

CI 两平台构建与模拟通过后再推进跨进程通信及与现有业务功能集成。不能仅凭源码审查宣称 .NET 构建成功。
