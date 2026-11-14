# SkyRoof CW Console V2 — P4 验收报告（未发布）

**基线**：SkyRoof-Plus v1.74；P0 #214、P1 #215、P2 #216 已合并。P3 #217 与本次 P4 的准确 SHA/CI 以对应 GitHub PR Checks 为准。  
**交付方式**：在 `master` 合并源代码后执行 Compile Check 并归档未签名测试安装器；不修改 `Directory.Build.props`，不运行 `Build and Release`，不创建 Release/Tag。

## 1. 功能验收覆盖

| 内容 | 测试证据 | 边界 |
| --- | --- | --- |
| 八路独立槽位 | `CwConsolePresentationTests`、`CwConsoleV2AcceptanceTests` | 含 1/3/5/8 路合成 Track/Transcript；非真实八路录音 |
| Track ID、频率交叉 | `CwConsolePresentationTests`、`CwConsoleSlotTextMemoryTests` | H/T 身份重关联与不同 AF 频率 |
| 保留/人工 Clear | `CwConsoleSlotTextMemoryTests` | Grace、过期、复用、timeline reset、解码器重置与清空水位 |
| HOLD / RELEASE | `CwConsolePresentationTests` | 仅固定显示槽位身份，不锁定电台 |
| Selected Lane / Copy | `CwSkimmerUiTests` | Committed/Provisional 全文及颜色；不发射 |
| Use Call | `CwConsoleSlotTextMemoryTests` 中确认呼号提取 | 仅确认文本；插入编辑框须用户点击；无自动发送 |
| 深浅主题与 DPI | `CwSkimmerUiTests`、`UiDpiVisualAcceptanceTests` | 模拟视口与 DrawToBitmap，100/125/150/200%，不等于实机跨屏 |
| RX 音源 / 模型 | `CwAudioSourceControllerTests`、`CwReceiveWorkerTests`、`DeepCwPipelineTests` | 复用 v1.74 已有逻辑 |
| TX / STOP / WPM / 宏 | `CwTransmitTests`、`CwMacroBankTests`、`CwSatelliteTransmitInterlockTests` | 不替代 IC-9700、SkyCAT 实机 |
| 独立 FFT 显示链 | `CwDisplaySessionTests`、`CwDisplayFrameProcessorTests` | ONNX/HamNoise 不在 UI 绘制线程执行 |

## 2. 运行要求与 Action

必须核对最新 PR HEAD 的四类检查：
- **Compile Check**：Windows x64 Release、完整 xUnit、FFT benchmark、UI DPI 图像工件、Orekit runtime smoke 和未发布的测试安装器；
- **CW Pileup Real Model Benchmark**：已有模型基准流程；不将其等价于多台站真实 RF 录音；
- **aislop Quality Gate v0.18.1**：以相对 `master` 的新增问题为质量门禁；
- **Docs Validation**：中英文用户指南与文档构建。

GitHub Actions 的精确 Run ID、结果、commit SHA 和工件链接以仓库最新运行记录为准；不得把“已请求”写成“已通过”。独立测试安装器只保留为 CI Artifact，**不发布 GitHub Release**。

## 3. Pileup 基准与录音限制

已有 `CwRecordedCorpusBenchmarkTests` 和 `.github/workflows/cw-recorded-corpus.yml` 可以处理带清单的 WAV 录音，但后者需要可验证的 `corpus_url` 与 SHA-256。目前没有在本次 P4 提供覆盖 1/3/5/8 路、5/10/15/25/40 Hz 邻频间隔、QSB、Doppler 漂移及功率动态范围的完整、固定 hash 的同批实录语料，因此 **CER/WER、完整呼号率、Track ID Switch 不能报告为已完成的真实录音比较**。合成轨迹单测不等于声学解码 CER/WER 实验。

`CwPileup Real Model Benchmark` 若成功，仅说明对应工作流覆盖的模型输入和配置在 CI 上运行成功；必须以其上传的实际报告为依据，不能外推至 IC-9700 的 RF 收发性能。

## 4. 明确的未实现 / 未完成事项

- `Tune RX`：未加入。需要经过测试的 CW/CW-R、IF/AF、RF VFO、卫星多普勒映射后才可接受操作请求；槽位普通点击依旧只选择。
- `Clear Tracks`：未新建会触及 tracker/audio ring 的操作按钮；保持独立逐槽位 Clear，不将“清空文字”误写成重置解码算法。
- IC-9700 / SkyCAT Command-17、真实八路录音、多显示器 100–200% DPI 与 Dock 移动：尚无实机证据。模拟验收不得宣称已覆盖。
- 逐字符实际发报进度、自动 CQ/自动应答、未定义消息队列：依 P3 非强制范围不实现。
- 由于本次要求暂不发布，发布身份、签名、正式安装器分发和版本号调整均不实施。

## 5. 发布决定

本次只接受 **CI 编译和测试工件 + 经核对的合并代码**。如个别 CI 项失败，则保留 PR 并记录故障；不得宣称 P4 完成或创建 Release。真实录音和硬件验收留作后续单独任务，不借此撤销已验证的功能。
