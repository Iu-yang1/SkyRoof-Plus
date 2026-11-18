# 遥测接收导致 SkyRoof 整个进程无响应：生命周期与 UI 性能修复（v1.84）

## 问题、源码证据及故障模型

用户反馈：遥测接收/解码期间，频谱明显卡顿；选择其他转发器以终止遥测时，不止频谱停止，**整个 SkyRoof WinForms 进程都不再响应用户输入**。

v1.83 的 `TelemetryPanel.CreatDestroyPipeline`、`SetIqReplayActive`、
`ApplyAudioSettings`、参数编辑及关闭面板路径，直接在 UI 线程调用
`TelemetryDecocder.Dispose`，而继承的 `ThreadedProcessor.Dispose`
执行 `processingThread.Join()`。如果解码器正执行 SkyTlm StreamingPipeline
的检测/解帧、SSDV/语音组装或末尾 Flush，主 UI 消息循环就被阻塞。
另一个互等风险是 `AudioReceiver_IqDiscontinuity` 从 PCM 后台工作线程调用同步
`Invoke`，等待 UI 处理，同时 UI 线程可能在停止接收或等待其依赖资源。

频谱由同一 WinForms 消息循环绘制，故频谱卡住是**进程 UI 卡顿的一部分**，
并非必须假设 OpenGL 或绘制线程崩溃。上述是由源码确定的阻塞路径；
没有现场线程 dump，尚不能证明每一次实际冻结都有相同根因。

## 修改后的线程与事件契约

- **停止/切换：** 先在 UI 上从当前接收器解除 decoder 引用并标记停止新 IQ；
  `TelemetryDecoderRetirement` 在后台串行执行 `Purge → Join → Flush → Dispose`，
  保持原来对 FFTW 释放顺序和部分图像/语音 flush 的保证，而不阻塞窗口。
- **并发入流：** `TelemetryDecocder.EnqueueIq` 和 `StopAcceptingIq` 通过短锁关联，
  避免生产者在唤醒句柄 Dispose 后调用 `StartProcessing`。
- **音频重同步：** 不再使用 `Control.Invoke`；重置事件异步且合并重复事件，
  通过音频源配置 generation 忽略旧源重置请求，保留滤波器/PLL 清零语义。
- **帧/图像事件：** `TelemetryUiMailbox` 对解码器事件保持 FIFO，不丢弃有效
  解帧结果；每次 WinForms UI 回调最多处理 24 项或约 5 ms，然后让出事件循环。
  退出面板后丢弃**未呈现的 UI 回调**，磁盘上的解码/图片保存仍在退役线程完成。
- **FM 语音识别：** 首次 Sherpa/ONNX 模型加载从 UI 线程移走；完成后仅对
  当前选中的转发器重新核对并建立 FM 分支，退出时等待任务结束并释放模型。
  实时识别的未完成文字只保留最新 UI 刷新，已完成行仍按顺序保存。
- **CPU 调度：** 遥测工作线程降为 `Normal`，不修改射频采集、FM P0–P3
  解调路径、旋转器及 CAT/PTT 的线程/控制策略。

## 自动化验收与测试

`TelemetryUiLivenessTests` 验证：
- 1000 项模拟解码事件 FIFO 完整性，分多次 UI tick 处理，不一次霸占事件循环；
- UI drain 期间追加事件不丢失；
- 面板关闭后不执行剩余 UI 回调；
- 在模拟长时间解码 worker 挂起时，退役方法**立即返回调用方**，
  背景完成按原顺序执行；
- FM 语音识别过渡文字 1000 次更新合并为最新值，已完成文字不被丢弃。

验证时仍须执行：
1. Windows x64 Release 编译、全量 xUnit、aislop；
2. 独立 UNNE-1B 真实 IQ/CRC 测试、现有卫星遥测回放与 Orekit 烟雾测试；
3. 测试安装包和安装后的 DLL/运行库核对；
4. 实机 IC-9700：在连续接收期间切换转发器、停止重启遥测、切换 AF/IF，
   同时观察 WinForms UI 响应、频谱 FPS、CPU、队列长度及已解码帧完整性。

**边界：** 没有在用户设备上获取到冻结现场的 WinDbg UI 线程栈，也未进行
IC-9700 与真实天线的实时过境压力测试。本补丁修复了已确认的同步阻塞设计，
不应把 CI 模拟顺序和真实 IQ 协议回归等同于现场冻结百分之百消失的证据。
