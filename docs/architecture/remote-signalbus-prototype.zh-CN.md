# SkyRoof Remote：P1 跨进程 Shared Memory SignalBus 原型

## 原型说明

该模块使用 Windows Named Memory-Mapped File + 命名 Mutex，验证单生产者/多读者的数据流协议：帧序号、StreamEpoch、SampleIndex、采样率、容量溢出和生产者重启。

- 帧头 48 字节、共享区 Header 64 字节；帧槽容量由配置固定；小端 ABI 与 x64 Windows 对齐。
- 每个消费者保存自己的 SignalCursor；慢速消费者不阻塞正常采集，只会报告 LostFrames。
- 一次运行跨真实 OS 进程验证 3 个样本帧，随后注入慢读者溢出、StreamEpoch 重置及 10000 帧循环覆盖。
- 原型用短临界区 Mutex 保证共享帧完整；不是零拷贝锁无关数据总线，不应宣称已满足 2.4 MSPS IQ 吞吐或 2 ms IPC 延迟目标。
- Windows Service Session 0 ACL、跨用户 Named Mapping 权限与读者崩溃仍待后续测试。

## 测试

    dotnet run --project tests/SkyRoof.Remote.SignalBus.Probe/SkyRoof.Remote.SignalBus.Probe.csproj -c Release

## 后续工作

将数据源注册、跨进程流式状态和正式 Shared Memory 数据路径接入 Device/DSP/Media，在同一机器上做持续采样、负载、时延及读写一致性测试，再迁移真实 IC-9700 Direct LAN。
