# SkyRoof Direct LAN：频谱启停与音频会话生命周期修复

**场景**：IC-9700 Direct LAN 频谱反复启动/停止，或修改 LAN 音频设置后，频谱登录长时间无法完成，数次重试才恢复；SkyRoof 能读取麦克风 PCM，却不能确认电台实际完成 LAN 发射调制。

## 源码确认的三个耦合点

1. 旧版 `IcomLanSpectrumPanel.StopCapture()` 同时释放频谱采集对象、已认证 Icom Control/CI-V/Audio UDP Session 与 TX/RX WASAPI 声卡。频谱只是显示窗口，不应负责中断已明确启用的电台音频链路。
2. 旧版 `IcomLanDirectSession.RunAsync()` 会吞掉音频 UDP 握手异常，继续运行正常 CI-V 频谱，音频会话保持离线，直到人工重新登录。
3. 旧版 WASAPI `IcomDirectLanAudioDevices.Dispose()` 把声卡释放放到异步 Task，却未跟踪完成。修改设置后立刻启动新声卡可能与旧的设备释放竞争。另一方面，旧认证会话尚未注销时新认证会话可能并发开始。

以上解释软件中存在的具体状态失配，不等于证明这就是某一台 IC-9700 无 RF 输出的唯一原因。LAN UDP 发包与 MIC RMS 不等价于射频实际发射。

## 修复后的规则

- **频谱 START/STOP**：当 Direct LAN 接收或发射音频启用时，停止按钮只关闭频谱的显示订阅并清除频谱图，不会释放仍在工作的电台 LAN 认证、CAT/CI-V 及音频流。再次 START 直接重新绑定现有会话，无须登录。此前已经启动音频会话的用户关闭/重开频谱停靠窗口，也不会丢失音频。单纯不启用任何 LAN 音频时，STOP 仍正常结束频谱会话。
- **会话所有权**：新 `IcomDirectLanMediaOwner` 与应用 Context 同生命周期。主窗体关闭时释放它，音频不依赖 DockPanel 窗口的可见性。
- **设置变化**：只有修改 Direct LAN 音频参数或真正的频谱连接参数时才执行整体重新配置。即使关闭频谱窗口，也能对已保留的 LAN 音频会话应用新设置。
- **音频恢复**：音频 UDP greeting / ready 握手失败会传播明确的 `IOException`，使捕获层退出当前 Control/CI-V/Audio 认证流，随后重建相关会话。不能再出现“Scope 看起来连接成功，音频握手其实永久失败但不会重试”的状态。
- **登录串行化**：同一电台 IP 和 Control UDP 端口的认证使用可取消的 `SemaphoreSlim` 进行事务级串行化，上一会话完成关闭才允许下一次认证。不同电台可独立运行。
- **WASAPI 串行化**：旧麦克风/扬声器端点释放完成后，才初始化新一代设备。网络握手和 UI 线程不需要同步等待声卡的长时间关闭；关闭/重新开频谱时复用现有音频设备，避免反复触发独占占用冲突。

## 安全与非目标

- 不引入新的 TX 门限，不自动改电台 `DATA OFF MOD`、`DATA MOD`、`LAN MOD Level`、CAT PTT 或麦克风增益。
- 保留已有真实 PTT 状态检查及 UDP 发包的事务关联。
- 不恢复已经撤销的 PR #221 LAN TX 弹窗诊断功能。
- 维持 `IcomLanSpectrumSource.SkyCat` 和 `RsBa1` 各自现有的频谱接收方式；新媒体所有者只负责 `DirectLan`。
- 可从既有 Spectrum Diagnostics 中查看 `Direct LAN TX radio MOD input`、`TX UDP PCM packets`、`TX audio level`、连接状态；若电台实际发射仍无 ALC，需要实机检查 PWR、LAN MOD Level、所处 DATA/非 DATA 模式。

## 测试

`IcomDirectLanMediaLifetimeTests` 验证：(1) Direct LAN 带 RX/TX 的频谱窗口停止仅解绑显示；(2) 同一电台会话互斥、不同电台不干扰、取消时能退出等待；(3) 旧 Windows WASAPI 设备释放未完成时，新设备不会抢先初始化。

CI 通过只代表构建、自动模拟测试、音频接口协议不回归，并不能代表 IC-9700 实机 LAN/ALC 或 RS-BA1 并发登录行为已被硬件验证。

**发布**：只合并源码与 CI 构建；不改版本、不发布 Release。
