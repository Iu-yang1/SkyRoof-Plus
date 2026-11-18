# SkyRoof-Plus v.1.86

### v1.86 同版本修复 — UNNE-1B 齿轮与工具栏

- SAVE 和设置齿轮统一 32×32，间隔 4 像素、顶部对齐；标题区留出完整按钮高度，避免盖住状态行。
- UNNE-1B FM 同频 FSK 遥测配对时保留齿轮，沿用原参数编辑、手动覆盖、Discover 和保存逻辑，修改实际 FSK 解码参数。没有独立数据条目时允许使用 FM UUID 保存遥测参数，仍保持 FM 选台。
- 保持 v1.86 不变，复用现有发布资产替换流程。

### v1.86 同版本安装包再次替换 — 2026-11-18：保存图标及 UNNE-1B 临时编号兼容

- 保持 SAVE 原按钮尺寸，改为磁盘保存图标；保存期间显示停止方块。取消独立 BIN+LOG 状态行，计数、错误和目录移至按钮悬浮提示，SatNOGS 状态保留。
- UNNE-1B 436.888 MHz 的 FM / BPSK 遥测识别不再仅依赖旧临时 NORAD 101101；精确卫星名及别名也可识别，自动调用 200 Bd / 820 Hz / HADES 专用解帧和业务解析。真实数据库编号不改写，其他卫星和频率不受影响。
- 本次仍使用 v.1.86 标签，安装包及签名/哈希由发布工作流验证；新增缺失、变更编号与错误卫星/频率的回归用例，实际用户台站实时接收仍需确认。

### v1.86 同版本安装包替换 — 2026-11-18：遥测 SAVE 按钮及 UNNE-1B 同频自动识别

这次是用户明确要求的 **v1.86 同版本安装程序替换**，不增加版本号、不创建 v1.87 Release。当前 v1.86 安装包重建后仍使用 `SkyRoofSetup-v.1.86.exe`，新 SHA-256 及 Authenticode 实际状态以本次 Build and Release 工作流为准。旧 v1.86 下载链接指向替换后的安装包，因此历史安装包哈希不再适用；不修改更早的版本。

- **遥测面板 SAVE / STOP：** 顶部解码参数齿轮旁新增独立 `SAVE` 按钮。点击选择保存目录，立即开始当前会话逐帧归档；再次点击 `STOP` 停止接收新归档并由后台写完已排队帧。直接在面板显示 `BIN+LOG` 保存状态、已存帧数和错误计数，SatNOGS 上传状态保留为另一行。原设置中的 `Save to File` 与 `Save Decoded BIN + LOG` 只保留旧 JSON 的兼容字段，不再显示或控制归档。
- 指定目录下按 `<NORAD>/<UTC日期>/` 保存每帧解码后的 `.bin` 与同名 UTF-8 `.log`（CRC、CFO、SNR、纠错、HEX、来源和时间），另写 `frames_YYYYMMDD.txt` 文本记录。所有实际写盘在单独后台队列完成，不阻塞遥测解码或 WinForms 重绘。保存目录由操作者在按钮中选择，软件不再擅自写入旧默认路径。
- **UNNE-1B FM 与 BPSK 两条入口均可进入 FSK：** 对 NORAD 101101 / 436.888 MHz 的错误 SatNOGS BPSK 行，不再硬性要求数据源提供 200-baud 字段；没有用户手动覆盖时自动选择 2-FSK、HADES Type-C、200 baud、820 Hz 峰值频偏。同频 FM 行保留 CAT/FM 模式，但其遥测面板关联正确的 BPSK 数据行；即使该行缺失，也使用相同的专用 FSK 参数作为后备，不再出现“暂不支持此遥测模式”。
- 用户手动参数覆盖、其他卫星识别、原有 AOS/LOS 门控、SatNOGS 上传凭据及授权行为不变。新增自动识别、缺字段、FM 同频与回退测试，以及自选文件夹 BIN+LOG/文本保存测试。
- 发布前执行 Windows Compile Check / xUnit、公开 UNNE-1B 原始 I/Q 的严格 200-baud / 820-Hz / ±800-Hz CFO 回归、aislop 与发行包回读哈希验收。整场用户 IC-9700 I/Q 在 SkyRoof GUI 的 PER 与实际停靠 UI FPS 仍不视为已完成实机验证。

**English:** In-place v1.86 installer replacement (no new tag/version): the telemetry panel gains a prominent SAVE/STOP button that asks for the destination directory and records decoded BIN+LOG pairs plus readable frame text asynchronously. BIN+LOG and SatNOGS upload each have their own visible status line. The mislabeled UNNE-1B 436.888-MHz BPSK row resolves to verified FSK/HADES 200 Bd, 820-Hz deviation even without a baud field; FM selection drives the same FSK telemetry receiver, with a protocol fallback when its database sibling is missing, while CAT/FM and AOS/LOS remain unchanged.

---


## v1.86 — 2026-11-18 UNNE-1B 默认接收链真实 I/Q / CRC / CFO 验收正式版

本版基于已合并的 [PR #246](https://github.com/Iu-yang1/SkyRoof-Plus/pull/246)（UNNE-1B FSK 自动识别、停靠遥测 UI 性能、BIN+LOG 与 SatNOGS 状态）和 [PR #248](https://github.com/Iu-yang1/SkyRoof-Plus/pull/248)（严格生产链模拟验收）。**创建新的 v.1.86 程序、标签和安装包，不覆盖 v.1.85 或任何历史 Release 资产。**

### 协议及自动解码默认参数——已复核

- 仅对 **UNNE-1B NORAD 101101、436.888 MHz、200 baud 数据发射机条目**修正 SatNOGS 的错误 BPSK 元数据，默认选 **2-FSK、HADES/GENESIS Type-C、200 baud、FSK 峰值频偏 820 Hz**。手动覆盖值仍优先。
- 同频 FM 转发器行保持 FM/CAT 选台，遥测面板使用其同频 200 Bd 遥测 sibling；不将 FM/CW 误改为 FSK。原有卫星 AOS/LOS 门控不变。
- UNNE 帧按 **0xBF35 同步 → Type-C 地址/类型/长度 → 对加扰字节执行 CRC-16/CCITT-FALSE → 解扰**；不能将 HADES-SA 39-byte Type-3 帧当作 UNNE-1B 的标准 Type-3。生产版使用固定版本 SkyTlm 原生 UNNE 专用解帧器；缺少新 DLL 时保留独立后备解析路径。
- 用户提供的 `2026-11-18_11_48_48_UNNE-1B.iq.wav` 是 **48 kHz、双声道 float32 I/Q、525.5 s**。独立接收器从中恢复两帧无纠错 CRC 有效遥测：约 8.337 s Type 8，FCS `0x3549`；约 98.343 s Type 1，FCS `0x9611`。两帧 31 字节原始加扰线帧与 29 字节解扰负载均作为精确 xUnit 测试向量，要求帧结构、CRC 和实际字节完全一致。
- 用户两帧有效 FSK 的中心约 −817/−788 Hz，相对于 I/Q 零频；双音间隔约 1623/1611 Hz、对应峰值频偏约 812/806 Hz。**820 Hz 是 FSK 峰值频偏，不是载波 CFO；**已验证 CFO 搜索 ±2000 Hz 足以覆盖观测偏移。48-kHz、12-kHz 中心实数 IF 的 Hilbert/NCO 验收覆盖这两个 FSK 音调及此前 −1211 Hz 偏移假设。

### SkyRoof 生产级模拟回归——已通过

- 过去的 real-IQ 测试有参数组合容错：任何手动增益、关闭频谱门控或候选偏移成功即可验收。本版新增**严格默认测试**：使用 SkyRoof `TelemetryDecocder.BuildUnne1bStreamingOptions` 与实机运行时**同一构造器**，不再另写一个近似的 SkyTlm 默认配置。
- 保持 **200 baud、820 Hz、GateByShape=true、DecodeRejected=false、Hangover=250 ms、Guard=160 ms、MaxBurst=16 s**；以真实公开 UNNE-1B I/Q 样本在 **CFO = −800 Hz 及 +800 Hz** 各运行独立端到端解调/软判决/CRC：**Type 1 CRC OK、Type 14 CRC OK、Type 15 CODEC2 连续 0–5 六子帧全部恢复**。
- [真实 I/Q 工作流 #44245064126](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44245064126) 通过。固定 SkyTlm 测试结果 **823 pass、3 skip、0 fail**；[Windows Compile Check #44245064161](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44245064161) 与 aislop v0.18.1 同步通过。此次只把已确认的生产默认选项提取为统一构造器、补回归；**未擅自更换正确的调制/频偏默认值或取消功能**。

### 与 v1.85 继续包含的接收功能

- 保留在无活动解码器时跳过无用 IF→IQ DSP、Hilbert 非零抽头/NCO 优化、遥测 UI 突发摘要合并刷新。
- 保留可选解码后每帧 `.bin` + UTF-8 `.log` 后台存盘（CRC/FEC、UTC、CFO/SNR、来源和 HEX），以及 SatNOGS 上传队列/成功/失败/HTTP 状态栏。保存功能默认关闭，SatNOGS 不会因新增状态栏自动启用。
- 保留 v1.84 的 FM 语音链 P0–P3、遥测工作线程后台退休、其余卫星协议和 CAT/PTT/旋转器功能。

### 发布验收与边界

- v1.86 采用独立的 `Build and Release`，必须重新通过发布专用 aislop、Windows Release / xUnit、Orekit IKVM、Inno Setup 安装程序、回读 SHA-256 与 Authenticode 状态校验；发行包的实际状态以该次 Actions 和 GitHub Release 为准。
- **该版本是用户明确要求的正式 Release。** 若仓库的 SignPath 凭据不可用，允许此次单独版本标记为 Stable，但会在 GitHub Release 中**明确注明安装程序未签名**并提供 SHA-256；不对旧版本扩大例外。
- 原始 525.5 s 用户录音已经独立验证两帧；**GitHub Windows SkyRoof 的真实 I/Q 测试使用固定公开 UNNE-1B 过境样本，不是用户整段录音本身**。本版未宣称完成全程用户 IQ 的软件 PER、IC-9700 射频实机或 WinForms 停靠窗口 FPS 压力验收。此前用户报告的整场一包现象仍需要现场完整回放进行最终量化。

**English summary:** Stable v1.86 ships the v1.85 receiver fixes with stricter production SkyRoof streaming-option regression. The 200 Bd/820 Hz FSK/HADES Type-C configuration and native UNNE deframer were audited against exact 2026-11-18 user packet CRC vectors. Genuine pinned off-air UNNE IQ achieves CRC-valid Type1 and Type14 plus CODEC2 6/6 with +800 and −800 Hz residual CFO using the real production gate/hangover/guard/burst values. IC-9700 end-to-end PER and GUI FPS remain unverified; unsigned status is explicitly disclosed if SignPath is unavailable.

---

# SkyRoof-Plus v.1.85

## v1.85 — 2026-11-18 UNNE-1B FSK 遥测、停靠界面性能、BIN+LOG 与 SatNOGS 上传回执

此版本整合已合并的 [PR #246](https://github.com/Iu-yang1/SkyRoof-Plus/pull/246)，从 v.1.84 独立升级。**不修改、覆盖或重新指向任何旧版 Release/Tag/安装包。**原有 AOS/LOS 卫星地平线解码器门控保持不变；误判门控根因的 PR #245 已关闭且未合并。

### UNNE-1B 200 baud FSK/HADES 自动选择与真实录音回归

- 修正 UNNE-1B（NORAD 101101）436.888 MHz 的 200 baud 遥测发射机数据库误标：针对**这一具体卫星、下行频率及 200 baud 数据行**，缺少手动覆盖时自动使用 **FSK、HADES、200 baud、820 Hz 峰值频偏**。保留用户手动修改的调制、成帧、符号率和频偏优先级。不会把同频 FM 转发器或 CW 行错误地识别为 FSK，也不影响其他卫星。
- 选择同频 V/U FM 转发器时，遥测面板会关联其 FSK 遥测发射机；CAT 仍维持用户选中的转发器及收发模式。
- 820 Hz 配置来源于独立、已公开的 UNNE-1B 原始 I/Q 样本回归：成功恢复 CRC 有效 Type 1 / Type 14 与连续 CODEC2 子帧 0–5；同一记录按 562.5 Hz 假设未恢复对应 Type 1/14 有效帧。该实测结论不代表已在用户 2026-11-18 的整场过境上完成 PER/BER 测量。
- 对已知 820 Hz 双音采用验证过的形状门控，减少噪声与不匹配突发的重复解调；没有强制禁止手动修改频偏，未知频偏仍走盲搜索。针对截图 CFO −1211 Hz 的预期两个音调（相对 12 kHz IF 中心 −2031 / −391 Hz）新增 IF→IQ 回归用例，验证前端可保留两音。

### 遥测面板与 IF→IQ 性能

- 对未建立活动解码器的遥测面板，采集仍统计样本/RMS，但不执行没有消费者的 Hilbert/NCO 处理与 I/Q 分配；保留原先 SDR/IQ、IF/AF 输入及地平线逻辑。
- 去除 PCM 重复复制；129-tap Hilbert FIR 跳过恒为零的抽头；混频使用连续相位复数振荡器代替逐样本计算 sin/cos，并通过原有分块/频率回归确保相位连续。
- 针对停靠窗口的频繁重排，突发统计仍完整递增，但所选过境摘要最多每秒重绘一次；不改变有效帧、音频/图片消息的顺序及接收途径。状态标签仅在实际内容变化时更新，避免重复设置 Tooltip。
- 面板诊断 Tooltip 增加 **DSP work / ingress realtime (%)**：高于 100% 表示解码工作量超过输入流实时长度，是定位 CPU 争用/漏包的指标，不是主界面 FPS 的替代值。

### BIN+LOG 保存及 SatNOGS 状态

- 设置新增 **Save Decoded BIN + LOG（保存遥测 BIN + LOG）**，默认关闭；打开后在用户数据目录的 `TelemetryDecodes/Binary/<NORAD>/<UTC日期>/` 每帧写入独立的 `.bin`（`Frame.Bytes`）与同名 UTF-8 `.log`（UTC、卫星/发射机、类型、HEX、字节数、CRC/FEC、纠错、擦除、CFO、SNR）。校验失败的帧可保留并标记 FAIL 供排障。**BIN 是解帧后的有效载荷，不是原始 IQ 或空中编码流。**原有 Save to File 功能仍独立可用。
- 保存操作进入后台串行队列，不在解码回调中同步写硬盘；面板悬停信息可查看已保存/失败统计。
- 遥测面板增加独立 **SatNOGS 上传状态行**：未启用、配置缺失、等待/排队、成功、失败、最近 HTTP 状态。将请求构造和 HTTP 传输放到后台工作线程；回放模式、未确认的参数覆盖仍不上传，内层重组包不会被当作原始射频帧提交。中英文 PropertyGrid 帮助与文档同步更新。

### 构建验证与限制

- PR #246 的 Windows 编译、xUnit 单测、UNNE-1B 独立真实 I/Q 回放和 aislop 0.18.1 均已通过；合并后的 `master` Compile Check 与 aislop 再次通过。发布工作流仍必须独立完成 x64 Release、安装程序、运行依赖、签名/哈希及 GitHub Release 校验，**实际结果以对应 v1.85 Actions/Release 为准**。
- 未进行用户 IC-9700 的现场过境录制重放、停靠布局 FPS/ETW 实机压力测试；此前 10 分钟文件是转码为单声道的 MP3，不能代替原始 48 kHz I/Q 来测定误包率。因而本版不承诺“所有过境均能解出每个 UNNE-1B 帧”或已全面解决所有 UI 卡顿。若 CI 无 SignPath 能力，按现有策略发布未签名 Prerelease。

**English summary:** v1.85 repairs the mislabeled 436.888 MHz UNNE-1B 200-baud BPSK metadata by resolving the telemetry entry as FSK/HADES with the 820-Hz deviation independently validated on genuine IQ fixtures. FM repeater selection can use a co-channel telemetry sibling without changing CAT. Real-IF DSP, PCM allocations and docked RichTextBox updates are optimized. Optional per-decoded-packet BIN+UTF-8 LOG archiving and a live SatNOGS upload status row are included. Historical release assets remain immutable; on-air IC-9700 PER and real docked-FPS testing are not claimed.

---

# SkyRoof-Plus v.1.84

## v1.84 — 2026-11-18 遥测接收 UI 无响应修复 + IF→AF FM 语音 P0–P3

本版包含已合并的 [FM P0–P3 PR #243](https://github.com/Iu-yang1/SkyRoof-Plus/pull/243) 和本次遥测 UI 生命周期/卡死修复 [PR #244](https://github.com/Iu-yang1/SkyRoof-Plus/pull/244)，基于 v.1.83 新增 Release。**不替换或重写 v.1.83 的安装包、标签或历史记录。**

### 遥测：修复切换转发器时的整进程无响应风险

- 根因：WinForms UI 切换转发器、重选音频、结束解码和关闭面板会同步调用 `TelemetryDecocder.Dispose`，其上游 `ThreadedProcessor.Dispose` 使用 `Thread.Join()`；如果 SkyTlm 正在进行 FEC/软判决、音频处理、SSDV/语音组装或 Flush，整条 WinForms 消息循环都可能停顿。原音频不连续回调又使用同步 `Control.Invoke`，在切换资源时存在互等风险。
- 先原子撤下旧解码器入口，后台串行执行 `Purge → Join → Flush → Dispose`，保留部分图像/语音自动归档及 FFTW 内存释放顺序。IQ 输入与退役通过短锁关联，避免对已释放唤醒对象继续写入；遥测 DSP 线程优先级回到 Normal，不再不必要地抢占绘图消息循环。
- 移除音频中断回调的同步 UI Invoke，重同步请求异步合并并检查音频源配置代数，保持原有 IF/AF 解调状态清空语义。
- 遥测 burst、已解码帧、图像、Codec2 语音和 FM 文本等界面事件使用有序批处理，每个 UI 批次最多 24 项或约 5 ms，让频谱绘制、窗口拖拽、转发器切换等消息获得执行机会。**不丢弃成功解码的帧**；FM 语音识别的临时文字合并为最新状态，已完成文本仍完整保留。
- 可选 Sherpa/ONNX FM 语音识别模型首次加载移到后台，避免同步加载约 71 MB 模型阻塞 UI；停止/关闭时保证共享模型在对应解码器退休后释放。
- 真实故障为“**进程整体不响应**”而非单独频谱冻结。修改的是确定存在的同步阻塞链路；若没有当时的线程 dump、CPU ETW 与实机复现，不能把每一次真实冻结都归因于单一锁循环。

### 48 kHz / 12 kHz IF→AF FM 链 P0–P3

- **P0 — 分离语音和遥测：** 新 `CleanDemodulatedAfAvailable` 事件提供未经过语音去加重、静噪、音频增益的 48 kHz 干净 AF；`TelemetryAudioReceiveSession.IfAfDemodulatedAf` 改用该事件。原有主输出事件仍用于虚拟声卡和模拟语音。宽带 GMSK 必须继续选原始 SDR IQ / 12 kHz IF，而非经过 3 kHz 低通的语音 AF。
- **P1 — FM 语音后处理：** 可选 250 Hz 二阶 Butterworth 高通、模拟 FM 的 750 μs 去加重；原有 50/75 μs 枚举 ID 与设置迁移兼容。全部选项默认不改变既有接收行为。
- **P2 — 衰落与静噪：** 可选 20 ms 短衰落插值（超过窗口的长衰落原样保留）、宽带鉴频后 3.5 kHz 以上噪声 quieting 检测与原载波功率条件联合判定；20 ms 延迟同时作用于语音静噪门控，遥测 AF 不经过这些处理。
- **P3 — 回归和性能：** 合成 12 kHz 实数 IF 的 48 kHz 流式分块一致性、音频与遥测隔离、750 μs 频响、67 Hz CTCSS 抑制、20 ms 延迟/静噪对齐、短/长衰落、噪声 quieting、配置 JSON 往返与软件耗时检查；本版另增加后台退役与 WinForms 分批队列的确定性回归。

### 构建、验证和限制

本版通过 GitHub Actions 进行 Windows x64 Release 编译、全量 xUnit、独立 UNNE-1B 真实 IQ/CRC 回放、Orekit IKVM 星历冒烟、Inno Setup 安装及安装后依赖校验、aislop 门禁。**具体通过项目、运行编号及安装包 SHA-256 以实际 v.1.84 Actions/Release 结果为准**，不能凭此说明已完成实机冻结压力测试。

待在 IC-9700 USB/LAN IF 下实际验证：持续卫星遥测解码时快速切换转发器/结束接收，观察进程 UI 延迟、频谱 FPS、CPU 与队列延迟、未闭合 SSDV 文件、遥测 CRC/PER；FM 音质和静噪门限也需要同一原始 IF 的 A/B 录音对比。缺少 SignPath 凭据时按现有仓库策略发布 **未签名 Prerelease**，同时保留 SHA-256 和下载回读校验。

**English:** v1.84 removes the synchronous WinForms decoder-thread join and blocking audio reset, serializes decoder retirement off the UI thread, dispatches telemetry events through bounded FIFO UI batches, and initializes the optional large FM ASR model in the background. It also includes FM IF→AF P0–P3: a clean telemetry AF tap, optional 750-µs de-emphasis/250-Hz high-pass, 20-ms voice fade blanker and noise-quieting squelch. CI and recordings do not substitute for an actual radio-side freeze reproduction.

---

# SkyRoof-Plus v.1.83

## v1.83 — 2026-11-17 遥测真实样本修复与星历导入回归

基于 `v.1.82` 的两项已合并变更：[#240](https://github.com/Iu-yang1/SkyRoof-Plus/pull/240)（遥测）和 [#241](https://github.com/Iu-yang1/SkyRoof-Plus/pull/241)（星历）。本版不改写以前 Release 的 tag 或安装包。

### 遥测链路：SkyRoof + 源码固定的 SkyTlm fork

- SkyRoof 遥测输入严格按照 `DataEventArgs.Count` 处理有效 IQ 样本；不再将池化缓冲区的无效尾部样本送入接收链。
- 修复 AO-73 / FUNcube-1 AO-40 FEC 的差分 BPSK 默认元数据解释，保留显式手动覆盖。提供已鉴频 FM 音频的 FSK/GFSK/GMSK 可选输入，保留 SDR IQ、12 kHz IF、模拟音频的既有路径。
- FM/AF 输入支持连续解调状态及不等长块；AO-73 经过 RS 验证的 256 字节帧显示实时类型/序号/EPS 原始字段，不伪造校准数值。
- 软件回放回归（[Actions #44045074478](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44045074478)）：AO-73 1 帧 AO-40 FEC/RS、AO-27 2 帧 AX.25/FCS、AISTECHSAT-3 5 帧 AX100/FEC、Suomi 100 2 帧 AX100/FEC；SkyTlm 离线测试 823 通过、3 跳过、0 失败。UNNE-1B 保留已合并的真实 IQ/CRC 回归及源码固定集成。
- 注意：Suomi 100 的**独立三 burst 样本**仍为 2/3 成功，是已知 burst 分割缺陷；不得宣称已经修复。以上回放不是 IC-9700 实机、实时 AFC 或过境测试。

### 星历：CelesTrak OMM、来源优先级、镜像及离线恢复

- 认可 CelesTrak **同一官方** `gp.php` 的 `FORMAT=CSV` 与 `FORMAT=JSON`：依照正文识别，保留六位及更长 NORAD catalog ID；默认保留官方 `GROUP=amateur&FORMAT=CSV`。
- 在线独立核对（[Actions #44053914187](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44053914187)）：官方 CSV 99 条、JSON 99 条；NORAD **99/99** 一致，含 AO-27 (22825)；说明核对当时的接口状态，不是中国大陆连通率证明。
- 优先级维持为：**72 h 手动文件 > CelesTrak OMM > 用户自定义来源（列表第一项优先） > AutoTLE > SatNOGS**。SatNOGS 卫星/发射机、JE9PEL、SatYAML 元数据及 IERS EOP/JPL DE430 不混入轨道来源排序。
- 每个来源支持校验后的区域 HTTPS 或本地镜像候选、独立超时与原子缓存；无效 HTML/JSON/ZIP 不能覆盖有效数据。CelesTrak GP 的源键隔离 2 h 缓存及 24 h 有限故障回退，支持压缩与官方错误退避；实时双格式验证 Action 改为手动执行。
- IERS EOP 可用本地/官方备用来源；Orekit-native DE430 仍严格验证固定哈希，禁止将其它 SPK 误认为匹配文件。部署参见 [中国大陆星历源与镜像指南](docs/engineering/orbit-source-mirrors.zh-CN.md)。

### 发布验收边界

合并后的 `master` 在 [Compile Check #44057474744](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44057474744) 已通过 Windows x64 Release 编译、xUnit、Orekit Smoke 和安装包依赖测试；[aislop #44057474657](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44057474657) 通过。**v1.83 正式构建、SignPath 签名状态、安装程序 SHA-256、Release 完整性**仍以本次实际 GitHub Actions 构建及发布后校验为准；没有签名则现有流水线自动标为预发布。未完成真实 IC-9700 接收、云台/多普勒联动与中国大陆运营商链路实测，不能把模拟或 GitHub Runner 结果等同于实机验证。

**English:** v1.83 combines real-recording-verified SkyRoof/SkyTlm telemetry ingress and AO-73/AO-40 FEC recovery with content-validated CelesTrak OMM CSV/JSON, six-digit NORAD handling, ordered orbit sources, resilient regional/offline mirrors and Orekit data checks. Real RF/hardware and mainland ISP connectivity remain unverified.

---

# SkyRoof-Plus v.1.82

## v1.82 — 2026-11-16 遥测 IF/AF 接收可靠性修复（P0–P3）

本次针对 IC-9700 USB/LAN 12 kHz 实数 IF 和 FM 鉴频后 AF 的遥测接收链路，修复来自两轮源码审查的问题。

### P0 · 原始 IF 与数据连续性

- Telemetry Shared12kIf 不再强制依赖 IF→AF 虚拟播放设备（VAC）成功启动：可直接采集 48 kHz USB IF，或订阅经过认证的 Direct LAN 音频流。
- 为共享 IF 增加可携带不连续性状态的 PCM 事件，保留原有 IF→AF、Recorder、SDR 入口。
- Direct LAN IF 独立使用 UDP 外层序号重排与 48 kHz 重采样，丢包、重同步及缓存掉块会通知遥测 Hilbert/NCO 与上层解码器重置。
- 保留原始 IF 正常接收功能，未添加人为的发射/接收权限限制。

### P1 · AF/AFSK 调制域

- 为已进行 FM 鉴频的 AFSK 音频提供兼容 SkyTlm RF-IQ 解调入口的 FM-IQ 桥接，避免将解析 AF 误当射频 FM 信号再次鉴频。
- 依据当前选中下行调制类型选择 AFSK 桥接或现有解析信号下变频；切换调制时重建流状态。
- 保留 12 kHz Real IF 的 Hilbert + NCO 转换和已有 FSK/BPSK 解调路径。

### P2 · 可观测性及输出分层

- Telemetry 状态标签工具提示增加累计输入样本、PCM 丢块、数据中断、输入 RMS、已解码帧数、CRC 成功/失败、内层包数及最后一帧 CFO/SNR。
- 只有 CRC 校验成功的遥测帧才标记当前过境的有效遥测及手动解调参数确认。
- TCP KISS 使用虚拟通道区分：0=原始链路帧，1=TM/KISS 重组的内层包，2=CRC 失败的原始帧。原始链路帧仍按既有规则上报 SatNOGS，内层包不上报。

### P3 · 录音、回归与数据安全

- Recorder 可将 48 kHz 单声道 `.if.wav` 使用实时同一 IF→IQ 转换器读入，作为 IQ 进行离线遥测回放；任意实时输入模式下回放均暂时优先于实时信号。
- 回放 Seek 后会重置遥测解码状态，避免历史样本跨不连续时间拼接。
- AO-123/ASRTU-1 CCSDS TM 校验失败时清除正在重组的 KISS 包，避免跨缺失片段误拼。
- 增加 AF→FM IQ 音频一致性、KISS 虚拟通道、IF 流式/离线一致性测试；修复已有 AMSAT UTC 测试由于 JSON 自动解析导致的文化区格式差异。

**限制与验收边界：** Windows x64 编译、全部 xUnit、aislop、安装包及签名的最终结论以 v1.82 GitHub Actions 工作流实际结果为准。模拟/软件回放不等于 IC-9700 实机 USB/LAN IF 验收，仍需在真实 UNNE-1B 200 Bd FSK、AO-123 9600 Bd BPSK 信号上测量解码成功率、误码与丢包后的恢复时间。未经来源认证的样本不能据此宣称已经完成真实卫星整链路验证。

**English:** v1.82 restores independent USB/LAN raw-IF telemetry ingress without mandatory VAC playback, propagates stream discontinuities to stateful demodulation, separates FM-discriminated AFSK audio from raw IF handling, exposes telemetry ingress and CRC diagnostics, separates KISS frame layers, and enables raw IF WAV offline replay. Hardware receive performance remains to be verified with actual IC-9700 captures.

---

# SkyRoof-Plus v.1.81

## v1.81 — 2026-11-16 Direct LAN 连续 PTT 误重连修复

用户在 v1.80 观察到：第一次 PTT 绿灯，下一次 PTT 变黄（会话重新连接）。代码根因是 v1.80 将未确认的 Audio UDP 心跳回复错误地等同于远端撤销媒体授权。发送至少 20 个 TX UDP 数据包，且连接建立/最后心跳起经过 12 秒后，即使没有收到电台的明确断开响应，程序也会在 PTT 结束后重建整条 Control/CI-V/Audio 会话。

- **不再因缺少 Audio Ping 回应而断开媒体。** 已完成的音频握手与协商不会被单方面的 12 秒计时器撤销；正常的 PTT OFF→ON、长时间待机及多次发射不再仅因此变黄或断开频谱。
- **真正故障仍恢复。** IC-9700 发出的对应 Session ID 的明确 UDP Disconnect、音频工作线程异常、Control 鉴权失败等有独立证据的故障继续按原有事务恢复。
- **心跳降级为诊断项。** 保留 SID/随机数/序号的远端音频响应统计。超过 12 秒未收到可关联回包只标注为“心跳应答未确认（仅诊断，不重置会话）”，不作为音频已失效、发射调制失败的证明。
- **新增回归测试。** 模拟成功媒体授权、没有心跳回复、已经发送 TX 包以及连续三次 PTT 开关，验证媒体就绪状态保持一致；确保只有明确断开才进入未就绪状态。
- 保留 v1.80 的正常 Logout、Audio/CI-V/Control 三路 UDP Disconnect、WASAPI TX ON/OFF 热切换、PTT 与 MOD 输入校验，未增加发射限制。

**验收说明：** Windows CI、完整 xUnit、aislop 与发布安装包的实际结果以 v1.81 工作流为准。此测试不等同于 IC-9700 实机音频调制验证；需要在不同 TX 空闲间隔下测试实际发射。

**English:** v1.81 fixes spurious TX media reauthentication between PTT bursts. Missing optional Audio UDP ping echoes are diagnostic only and must never revoke a negotiated TX grant. Explicit disconnect, failed transport workers and authenticated errors still recover normally. Repeated PTT regression tests added.

---

## Historical / 历史版本 v1.80

# SkyRoof-Plus v.1.80

## v1.80 — 2026-11-16 ICOM LAN TX 发射音频重启恢复修复

- 为 Control、CI-V、Audio 三条 UDP 流补齐协议 `0x05 Disconnect` 报文。正常关闭时先停止 TX 音频，关闭 CI-V 媒体流，再发送令牌注销（Auth `0x01`），对关联注销应答等待最多 400 ms；未确认与失败会明确记入 UTC 日志，不再把本机 UDP 发送成功当作电台端已注销。
- 停止过程中先取消并收回正在等待的控制/CI-V UDP 接收任务，避免注销响应被另一个 `ReceiveAsync` 消耗；仅在正常清理未及时完成时保留原有强制关闭作为异常恢复。
- 为独立 Audio UDP 添加带双向 SID、随机数和序列号验证的 ping/echo 心跳跟踪；日志记录远端最近有效控制报文及累计应答次数。只有本地已经发出实际 TX PCM 且连续至少 12 秒没有可信远端音频控制响应时，才将 TX 链路标记失效并请求恢复。
- **不打断 PTT：** 处于 PTT ON 时先警告并标记故障，PTT 释放后才重建经认证的 Control/CI-V/Audio 媒体会话。保留 TX 编码器、20 ms 帧节拍、MOD 输入检查、PTT 验证与默认 48 kHz LPCM 协商，不禁用发射功能。
- 底栏 TX UDP 诊断窗口增加远端音频控制响应时间、应答次数及心跳失效状态；新增协议 Disconnect、SID/随机数关联、过期判断及重放拒绝的 xUnit 测试。
- **TX Audio ON/OFF 无需断开频谱：** 只有当前 Direct LAN 会话已获有效 TX 媒体授权且仅切换发射音频使能时，才只重启 Windows 麦克风端点并暂停/恢复本地 PCM。若从未协商 TX 或媒体已失效，仍按协议重新申请完整媒体授权；修改 Codec/采样率仍正常重协商。

**发布边界：** 本次已完成源码级修改。Windows CI、aislop 与安装包验收需以对应 v1.80 流水线实际结果为准；未执行 IC-9700 实机测试，因此不能声称所有固件均已验证。设备端是否实际调制仍必须使用独立射频测量确定。

**English:** v1.80 improves authenticated Icom LAN session teardown by sending explicit per-stream UDP disconnects and correlating token logout replies. TX audio now tracks verified remote UDP pings and recovers authenticated media after stale heartbeat detection without interrupting an active CAT PTT transmission. Diagnostics expose remote liveness independently of local PCM sends. Hardware TX modulation is not inferred from local UDP delivery.

---

## Historical / 历史版本 v1.79

# SkyRoof-Plus v.1.79

## SkyRoof Plus v1.79 — 2026-11-15 过境匹配后台计算与 UTC 自选时间范围

针对“输入网格立即同步预测导致界面卡顿”的修复。该版本作为 **v.1.78 之后的独立发布**，不覆盖此前 Release。

- **手动开始：** 修改 DX 网格、卫星、轨道/站点数据或时间范围不会自动计算；只有点击“开始计算”才启动过境搜索。
- **UTC 时间区间：** 两个日期/时间选择器分别指定 UTC 开始与结束，默认当前 UTC 分钟至 14 天后；共同窗口严格裁剪该范围，保留至少 30 秒的匹配门槛。
- **不卡主界面：** Orekit 双地面站轨道预报、共同时间窗口查找、天空轨迹和最大仰角采样在后台 `Task.Run` 完成；使用 `IProgress<T>` 回到 UI 更新分阶段进度条，并且仅在完整成功时一次性发布匹配列表。长耗时调用不占用 WinForms 渲染线程。
- **可取消：** 计算按钮运行时切换为“取消计算”；修改任何查询条件自动取消旧任务，但不会自动启动下一个任务。底层单次轨道传播调用暂时不能直接中断，取消会在该调用返回后生效；过期结果永不覆盖新输入。
- **计算性能：** 双方过境按开始时间排序，以有序时间区间扫描匹配，替代旧版全量双层笛卡尔积；地球静止轨道结果覆盖用户选择的区间，不再局限于当前起算的 24 小时。
- **双语及文档：** 13 组新增对等的英文/中文资源，加入中文 [过境匹配使用指南](docs/zh-cn/fork-guide/pass-matching.md) 与更新后的英文用户手册。

### 验收与限制

- Windows 编译、全量 xUnit、双语言模拟 DPI（100/125/150/200%）、文档构建、aislop，以及安装包构建和发布后 SHA-256 校验均以 v1.79 的 GitHub Actions 结果为准。
- 软件自动化验证不等于实体显示器 DPI/鼠标交互或真实 TLE、旋转器和电台联机验证。
- 若仓库未提供 SignPath 凭据，将按现有发布策略提供明确标示为 **未签名的预发布版**，而非擅自放宽签名要求强制设为正式版。

### English summary

v1.79 replaces synchronous per-keystroke pass matching with an explicitly started, cancellable background calculation. The operator selects UTC start/end dates, watches staged progress, can cancel without freezing WinForms, and sees only a completed result snapshot. Edits invalidate old results but never restart automatically. The previous 30-second overlap criterion remains; search windows are clipped to the requested UTC interval. This is a separate v1.79 release after v1.78, and unsigned packages follow the existing prerelease policy.

---

## 历史版本 / Historical: v1.78

# SkyRoof-Plus v.1.78

## SkyRoof Plus v1.78 — 2026-11-15 业余卫星通信专业中文术语优化

本次是继 v.1.77 汉化资源安装修复后，针对业余无线电操作语境的一次独立语言完善版本。**不改变通信、轨道、旋转器、音频与解码逻辑，也不覆盖 v.1.77。**

### v.1.78 同版本构建修复（2026-11-15）

- 卫星选择栏“卫星 / 载荷”使用同宽、右对齐标签列，让中文和英文标签的末端与各自行的下拉框保持一致。
- 下行和上行箭头使用一致的纵向基线，频率方向标签保留固定间隙，不挤占模式选择器。
- 修复 `ToolTipEx` 多行频率提示的绘制边界：弹窗尺寸先增加内边距，再按同样的内边距渲染文字；消除首行文字贴顶以及下方裁切风险。
- 新增中英双语、四档模拟缩放（100%/125%/150%/200%）的顶部工具栏几何与截图测试，以及提示窗尺寸回归检查。
- 本次经授权**原位替换当前 v.1.78 安装包及其标签来源**，保留 Release 身份和历史 v.1.77；严格校验旧标签、Release ID、旧资源 ID/SHA-256，构建后核对新 SHA-256 和签名状态。不存在设备协议、发射使能、频率计算或业务功能的更改。

### 专业用语修订与实际页面

- **卫星载荷分类：** 原“卫星发射机”页面改为“卫星转发器与信标”，列表及新建/编辑本地条目同步调整。卫星分组筛选器仍严格区分“发射机（Transmitter）/收发信机（Transceiver）/转发器（Transponder）”，不将单向信标错误归类为双向通信转发器。保留 SatNOGS/JE9PEL 数据来源、UUID、上下行频率及 CAT 关联。
- **过境匹配：** 原“通联预约（QSO Scheduler）”改为“过境匹配”；其本质是计算本台与 DX 台的共同卫星可见窗口，不是预约服务。补齐详情窗口和过境列表中的共同可见时段（UTC/本地）、最长仰角、等待时间、地球静止轨道等中英文内容。避免将“共同可见”直接断言为“转发器已开机/可以完成 QSO”。
- **电台操作语汇：** “手动旋转器”改“旋转器手动控制”，“实时追踪”改“实时跟踪”，“位置异常”明确为位置反馈异常；键速标示为 WPM，频谱 REF、VBW、扫频帧平均等提示统一术语。Recorder 改为“音频与 I/Q 录制”以覆盖其实际音频和 IQ 功能。
- **数据记录和 SSTV：** 将日志 STATE 字段修订为“州 / 省”；SSTV 信号报告标签统一为“发出 RSV / 收到 RSV”（R：可读度、S：信号强度、V：图像质量）；原英文资源将 RSV 误写为 RST；本台/对方台标记与链路术语相统一。
- **用户交互补齐：** 补齐过境匹配列表自绘文字和详情、转发器与信标编辑、主 RX/TX 工具栏、Doppler/CAT/PTT/CTCSS 选项、频率基准/修正控制、卫星分组过境状态、卫星详情和天空/地球视图、自动选择计划、实体 PTT 绑定，以及 ONVIF 视频与设备设置。新增 PARK 路径点、优化旋转路径、Yaesu G-5500 状态与手动操作提示，SSTV 降噪弹窗及图库/WAV/ADIF 操作提示，Recorder、遥测和 CW 异常消息以及中文日期格式。AMSAT 状态可显示中文，但向外部服务提交时仍使用原始英文 API 代码。语言切换只刷新文字和列表重绘，不重新创建 CAT/PTT、云台、SDR、遥测、录音会话或重置已选条目。
- 本轮修订 36 个现有中文译文，新增 315 组中英文对等资源键（中文资源总数从 453 增至 768）；继续运行资源键与格式化占位符一致性检查，以及术语分类回归测试。

### 术语依据

- 中国无线电管理（SRRC）：[《全球业余卫星介绍（四）》](https://www.srrc.org.cn/article24576.aspx)，将卫星业余无线电载荷区分为遥测信标机与通信转发器。
- [《全球业余卫星介绍（五）》](https://www.srrc.org.cn/article.aspx?id=24578)，采用线性转发器、U/V 模式等术语。
- [AMSAT Amateur Satellite Database](https://satdb.amsat.org/)，明确将 Uplink、Downlink 与 Beacon 列为不同字段。
- 工业和信息化部：[《业余无线电台管理办法》（工业和信息化部令第67号）](https://www.srrc.org.cn/article31728.aspx)，将卫星业余业务、信标台、呼号及通联日志等术语作规范性区分。
- IARU：[《业余无线电爱好者的道德和操作守则》中文版](https://www.iaru-r1.org/wp-content/uploads/2019/10/业余无线电爱好者的道德和操作守则.pdf)，II.10.3.4 明确 SSTV 使用 RSV（非 CW 的 RST）信号报告。

### 验收范围

- 通过 Windows CI 的全量 xUnit、受控合成 UI 布局、资源键/占位符以及 aislop 质量检查后进行独立发布；最终计数与打包/签名结果以本版本 Build and Release 的实际记录为准。
- 不声称已覆盖所有上游硬编码文本或已完成实机 CAT/PTT、物理 DPI、真实转发器 QRV 检测。遗留第三方源数据描述仍保留原始名称。
- 若缺少 SignPath，Windows 安装包为明确披露的未签名（NotSigned）正式版；用远端 Release SHA-256 回读核对文件完整性。

### English summary

v1.78 professionally refines Simplified Chinese ham-radio satellite terminology: satellite transponders and beacons rather than treating every transmitter as a transponder, common-pass matching rather than an actual reservation system, precise rotator/tracking and spectrum vocabulary, and correct RSV signal-report labels. It also translates the RX/TX toolbar, frequency-control page, satellite group and sky view, pass matching, local transmitter/beacon editor, automatic selection, physical PTT setup, AMSAT status display, and ONVIF camera dialogs. Categorization (transmitter, transceiver, transponder), saved settings, frequencies, UI state and radio/DSP/rotator protocols are unchanged. All new resources have English equivalents and terminology tests. Existing v1.77 remains available. The release build, installed-payload and SHA-256 results are recorded separately; no actual hardware/physical DPI acceptance is claimed.

---

## 历史版本 / Historical: v1.77

# SkyRoof-Plus v.1.77

## SkyRoof Plus v1.77 — 2026-11-15 简体中文未生效安装包修复

### 修复内容

- **修复 v.1.76 安装后“选择简体中文但主窗口仍为英文”的实际问题。** 构建输出包含 `zh-CN/SkyRoof.resources.dll`，但旧版 Inno Setup 只将顶层 `*.dll` 打包，遗漏了文化区域子目录；`ResourceManager` 因此回退到默认英文。新版本明确将中文资源程序集安装至 `{app}/zh-CN/SkyRoof.resources.dll`。
- **防止同类误判：** 在 Windows CI 中，安装器冒烟测试新增资源文件存在性与 SHA-256 一致性检查；独立的 .NET 10 安装后运行时测试从实际安装目录用 `ResourceManager` 解析中文和英文资源，检查 `文件/工具/语言/卫星过境/频率控制` 等关键条目。此检查能识别“源码 xUnit 测试通过、实际安装包缺少语言程序集”的发布缺陷。
- **保持运行逻辑：** 不修改 CAT/PTT、SkyCAT、GS-232、遥测、DSP、Recorder、SSTV、Orekit 或 UI 语言状态机。修复仅影响安装内容和安装验收，不需要重置用户配置或删除现有工作区。
- 保留已发布 `v.1.76` 的标签、安装包和 Release；这是独立修复版本。

### 验证与限制

- 发布验收以本版本 Build and Release 的 Windows x64 编译、1609 项既有 xUnit（实际计数以工作流为准）、安装器安装与中文/英文 `ResourceManager` 测试、Orekit/IKVM、aislop、远端 SHA-256 回读的实际结果为准；提交或启动工作流不等于通过。
- 本次验证语言资源在真实安装目录的存在及资源查找，不声称已验证全部窗口文字、硬件 DPI 或 IC-9700 / 旋转器实机。
- 没有 SignPath 凭据时安装包仍为 `Authenticode: NotSigned`，明确披露并校验 SHA-256。

### English summary

v1.77 fixes the real-world v1.76 issue where selecting Simplified Chinese still displayed English. The Inno Setup installer previously copied only root-level DLLs and omitted `zh-CN/SkyRoof.resources.dll`, silently triggering the ResourceManager neutral-English fallback. The installer now explicitly includes the Chinese satellite assembly. The installed-payload smoke suite checks presence, byte-identical SHA-256 and runtime retrieval of Chinese/English menu and panel strings from the installed root. This is a packaging and release-validation fix; CAT/PTT, telemetry, DSP and hardware control logic remain unchanged. Previous v1.76 is preserved. Hardware/UI physical validation remains outside this scope. Without SignPath the installer is unsigned.

---

## 历史版本 / Historical: v1.76

# SkyRoof-Plus v.1.76

## SkyRoof Plus v1.76 — 2026-11-15 遥测、IF/IQ 录音回放与中英双语实时切换

本版本基于 `v.1.75` 以来已合并到 `master` 的代码，是新的独立迭代 Release。原 `v.1.75` 标签及安装包保留，不原位覆盖。

### 更新内容

- **AO-123 / UNNE-1B 遥测（PR #224）**：改进 SatYAML 发射机资料与具体下行频率的关联，支持 AO-123 TM-short KISS 分包重组；增加 UNNE-1B Type-C GENESIS 同步、CRC 与工程遥测字段解析，并修正 TLE epoch 偏移和其他解析问题。重组的 KISS 数据不再误当作原始 RF 帧上传。
- **Recorder IF/IQ（PR #224）**：录制原始 IF 或 IF 派生的复数 IQ，保留选择的 IF 通道；IQ WAV 回放可接入遥测处理链，实时 SDR 输入和回放输入分离并正确重置解调状态。原有 IF→AF PCM 录音继续可用。
- **运行时双语（PR #225）**：以统一语言资源支持简体中文与 English 实时切换，覆盖主要工作台、设置、遥测、SSTV、Recorder、CW、频谱和旋转器控件；语言切换不需要重连电台和旋转器，不改变现有配置键或 CAT/PTT 协议。
- **模拟视觉及会话回归（PR #226）**：Windows CI 使用真实 WinForms 控件生成 **40 张**双语言截图，覆盖模拟 100%、125%、150%、200% DPI；修复 Settings / Recorder 的空白截图误判和 Orekit 英文默认提示残留中文。使用虚拟 CAT/PTT、GS-232、KISS 传输、IF/IQ、Recorder、SSTV 验证语言切换前后会话与数据保持。

### 测试、发布与已知限制

- PR #226 同一验收提交的 Windows CI 成功：**1609 项 xUnit 通过、0 失败、0 跳过**；合并后 `master` 的 Compile Check、pinned aislop Quality Gate 全部成功。新发布安装包的 Build and Release 工作流仍须独立通过 xUnit、Orekit/IKVM 冒烟、Inno Setup、运行时依赖、远端 SHA-256 回读；最终状态以该次运行记录为准。
- DPI 截图采用模拟缩放而非物理显示器 DPI；无桌面 CI 未完成 Settings 原生 PropertyGrid 本体绘图验收，资源和对象验证已与截图测试分离；未入选截图矩阵的页面仍需人工视觉验收。
- CAT/PTT、GS-232、遥测、IF/IQ、录音、SSTV 均属虚拟会话或软件模拟验证，**不等于 IC-9700、真实旋转器、声卡和无线电链路的实机验证**。
- **签名声明：** 仓库没有 SignPath 凭据时将发布 Authenticode **NotSigned** 安装包；基于本次迭代发布授权标记为正式 Release，并验证远端安装包 SHA-256。不得将其描述为已签名安装包。

### English summary

v1.76 is a separate release after v1.75, preserving its existing tag and installer. Updates include AO-123 TM-short KISS and UNNE-1B GENESIS engineering telemetry, raw IF / derived complex-IQ recording and IQ WAV telemetry replay, and live English/Simplified Chinese switching across major WinForms workspaces. The acceptance suite captures 40 real-control screenshots at four **simulated** DPI scales and exercises virtual CAT/PTT, GS-232, telemetry/IF-IQ, Recorder and SSTV state preservation. The merged master passed Compile Check and pinned aislop; the release installer is subject to its own Windows CI and SHA-256 readback. Native PropertyGrid rendering, real physical DPI and connected hardware tests remain outside this acceptance. Without SignPath, the Windows installer is unsigned (NotSigned).

---

## 历史版本 / Historical: v1.75

# SkyRoof-Plus v.1.75

## SkyRoof Plus v1.75 — 2026-11-14 CW、Direct LAN 音频会话与遥测音频迭代

基于已发布的 v.1.74 进行独立版本升级；保留既有 v.1.74 Release/Tag/安装包，不做原位替换。本版本纳入截至本次版本更新前已合并至 master 的改动，包括 PR #222 和 #223。与轨道、CAT、PTT 和旋转器相关的既有控制协议不因本次发布而额外增加功能门禁。

### 本次主要更新

- **Direct LAN UDP TX 会话生命周期（PR #222）**：将已认证音频会话提升至应用 Context 生命周期，反复关闭或停止频谱显示不会意外释放仍启用的 RX/TX 音频。音频 UDP 握手失败后重新认证 Control/CI-V/Audio，避免出现仅频谱可用但音频永久失效的会话。同一电台的认证会话与旧/新 WASAPI 声卡端点按正确顺序关闭、重建。
- **Direct LAN 设置热应用（PR #222）**：麦克风增益、TX AGC、计量表和扬声器音量可以运行时调整而不重登电台；更换本地音频设备只重新打开 WASAPI；涉及 TX/RX 流、采样率或 Codec 的配置才重新协商无线电媒体会话。
- **LAN TX UDP 三色状态灯与诊断（PR #223）**：在底栏 S/PWR/ALC/SWR 表前显示与 CAT/IF→AF 风格一致的 TX UDP LED。绿色代表已完成音频握手且 TX UDP 流就绪，PTT OFF/没有 PCM 包时仍常绿；黄色代表正在连接或重连；红色代表未建立、已断开或 TX 流不可用。点击 LED 或 `LAN TX … pkts · MIC … dBFS` 文本可打开详细诊断窗口，查看发包、发送电平、PTT、MOD 输入读回等；关闭计量表不隐藏 TX 链路指示。
- **CW Console V2 P0–P4**：改进八槽位 Pileup 消息与显示保留、逐槽清空、HOLD/RELEASE、Selected Lane、复制与呼号提取交互，以及横向 Skimmer 显示、独立渲染链和不同 DPI 下的合成 UI 验收。既有 CW TX/WPM/快捷宏链路保持。请注意 Tune RX、真实多台站录音 CER/WER 完整验证等不属于本次完成范围。
- **IF→AF 解调音频录音与遥测多音源**：Recorder 可直接录制 IF→AF 解调后 48 kHz 单声道 PCM；Telemetry 可选择 SDR IQ、共享/独立 12 kHz IF、AF Capture、AF Loopback 和 IF→AF 解调后 AF 等音源。AF AFSK 的中心频率可配置；这不等于自动识别卫星帧协议。
- **设置双语完整性**：补充音频、IF→AF、遥测、Icom LAN、CW、旋转器、频谱及其它设置的中文字段/说明和枚举显示；仅修改属性表显示层，不更改现有 JSON 配置键、串口/网络协议或设备 ID。

### 发布验证与边界

- Release 工作流采用 Windows x64 编译、xUnit、pinned aislop 质量门禁、Orekit/IKVM 独立冒烟、Inno Setup 打包、安装包依赖验证和 GitHub 附件 SHA-256 回读。**各项最终是否成功，以本版本对应 GitHub Actions 运行结果为准；启动构建不等于通过验收。**
- 本次没有接入 IC-9700 进行 LAN MOD/ALC/PWR 实机调制测试，没有进行实机 CW 多目标解码录音统计或真实多显示器 DPI 测试。**绿色 TX UDP LED 只证明本地认证网络会话与 TX 音频流就绪，不证明电台已选择正确 MOD 输入，也不证明有射频调制输出。**
- 签名与 Release 稳定/预发布分类由仓库既有 SignPath 检测及发布工作流决定；如缺少签名凭据，将按工作流发布未签名预发布版本并明确标识，绝不宣称存在有效 Authenticode 签名。

### English summary

v1.75 is a new version based on v1.74, not an in-place replacement. It includes persistent Direct LAN authenticated RX/TX audio across spectrum toggles, serial media-session recovery, live/local/radio settings-apply separation, and a TX UDP session indicator with click-to-open diagnostics. A green indicator means the UDP TX session is negotiated and ready even without PTT or PCM activity; it does not verify radio-side modulation or RF output. The update also incorporates CW Console V2 UI/slot improvements, direct IF-to-AF recording, selectable telemetry IF/AF sources, and broader bilingual settings coverage. Windows CI and installer validation are reported by the v1.75 release workflow; no IC-9700 RF or real multi-station CW hardware acceptance is claimed.

---

## 历史版本 / Historical: v1.74

# SkyRoof-Plus v.1.74

## SkyRoof Plus v1.74 — 2026-11-14 UI 适配 P1–P4

本次为全新版本，保留 v.1.73 安装包及此前轨道、CAT、PTT、Rotator 协议代码不变。P1–P4 源代码合并提交记录分别见 PR #209、#210、#211、#212。

- **P1 顶部工具栏：** 以系统 DPI 已适配的 WinForms 控件树作为几何快照基线，提供 `Tools → UI Scale` 75/85/100/110/125% 设置；频率、增益、卫星选择、云台及按钮按比例统一缩放，避免累积漂移。窗口狭窄时按同一倍率缩小，不采用按钮自动重排。
- **P2 Dock 与弹出窗口：** `Tools → Dock & Popup Scale` 独立保存比例；沿用既有 DockPanelSuite 位置、窗口结构与 XML，不添加滚动条或自动换行；同步 Satellite Passes 自绘行高与轨迹线宽、Sky View 标签字体、RC-3040S / G-5500 右键操作面板。
- **P3 多屏与 DPI：** 热插拔或屏幕移除后将不可见窗口恢复到有效工作区，保留合法负坐标与跨屏窗口。保留 SystemAware 默认值和 Wine 兼容性；可选 `--per-monitor-dpi` 或环境变量 `SKYROOF_DPI_MODE=PerMonitorV2` 试验动态 DPI，`--system-dpi` 可恢复默认行为。
- **P4 模拟 UI 验收：** 1280/1920 px 顶部工具栏、G-5500 75/100/125% 面板、Satellite Passes、Sky View、Icom Spectrum、CW Skimmer 以及深/浅主题模拟渲染。检查按钮入口、字幕裁切、尺寸比例、缩放往返一致性、无自动换行和交互回调保持。
- **测试证据：** Windows Compile Check、完整 xUnit、aislop 0.18.1、DocFX 和 Orekit/IKVM 安装运行时检查。模拟渲染截图归档在 GitHub Actions 的 `ui-dpi-layout-matrix`，并附文档 `docs/architecture/ui-dpi-acceptance-20261114.zh-CN.md`。
- **验收边界：** 尚未进行 Windows 双显示器不同 DPI 的真实桌面与 Wine 实机检查，也未连接 IC-9700、G-5500 或 RC-3040S；不把模拟控件绘制当作真实 CAT/PTT/旋转器行为验证。PerMonitorV2 不默认开放，仍可自行选择使用。

**English:** Versioned v1.74 release for uniform, no-reflow toolbar/Dock/popup scaling, monitor topology recovery, opt-in PerMonitorV2 and synthetic WinForms screenshot plus interaction regression fixtures. Previous v1.73 remains unchanged. SystemAware stays default. No real multi-monitor, Wine, or transceiver/rotator hardware test is claimed. The verified build and published SHA-256 are recorded by the v1.74 GitHub Release workflow.

---

## 历史版本 / Historical: v1.73

# SkyRoof-Plus v.1.73

SkyRoof Plus v1.73 — responsive satellite switching and manual Orekit ephemeris updates.  
SkyRoof Plus v1.73 — **切换卫星及手动星历更新无响应修复版**。

## 中文

### 主要修复

- **切换卫星不卡 UI：** 原卫星选择事件直接执行未来一天 Orekit 过境预测；现在优先复用已经计算的过境，未命中时经过短暂防抖后使用后台任务计算。以实际 Tracker 为缓存键，缓存有效期两分钟；新的 TLE/OMM 不会复用旧轨道预测。最新选择优先处理，旧卫星任务不得覆盖当前选择或用户人工选定的过境。
- **限制并发并处理快速切换：** 当前选择采用专属有界后台执行队列，群组列表使用独立最多两条预测任务，跳过失效请求，避免连续选择引发大量全日计算并发堆积。
- **群组/SDR 预测快照：** 未来两天的群组过境、SDR 频率范围改变触发的预测在工作线程执行。结果只在最新代次、目标一致时提交主线程；更改台站、TLE、数据库时使群组缓存失效，过时频段不再显示旧过境。
- **卫星列表的周期性刷新：** 每秒刷新不再逐颗同步执行 Orekit 搜索，悬浮提示中的观测角度等数据以最多两条后台工作者计算，保留完整提示内容，短期缓存 15 秒。
- **地球视图与卫星详情：** EarthView 的卫星地心地理位置、覆盖区边界、轨道路径采样，以及详情页卫星高度、足迹计算和缓存图片解码/缩放，不再阻塞正常选择 UI。地图结果按选择代次更新，不允许旧卫星的任务覆盖新卫星。
- **手动下载星历不卡设置窗口：** 点击「更新 Orekit 星历」后，对本次数据源配置进行快照，IERS EOP/DE430 网络请求、文件读取/校验/写入全部在工作线程进行。自动下载与手动下载沿用互斥写入，不会争用临时文件；关闭对话框会取消尚未完成的操作。
- **DE430 缓存校验：** 首次完成固定来源文件的哈希校验后，在同一进程内若本地路径、文件长度和修改时间均未变化，可复用验证结果，减少反复下载和读取；文件变化后仍执行完整校验。
- **旋转器模拟测试稳定性：** 适度增加模拟 rotctld 握手等待时间以减少高负载 Windows CI 偶发超时；生产 CAT、PTT、GS232B/rotctld、ONVIF 和旋转器安全逻辑没有被改写。
- **既有 Orekit 引擎保持：** TLE/OMM 传播及月球、太阳、金星仍使用 v1.71 起统一的 Orekit/IKVM；不修改 AZ/EL、距离变化率、Doppler 和 Tracking 数据接口。

### 验证与限制

- [PR #200](https://github.com/Iu-yang1/SkyRoof-Plus/pull/200) 已完成 Windows x64 Release 方案构建、全部 xUnit、CW FFT 检查、独立 Orekit/IKVM 运行时与安装器冒烟、固定版本 `aislop` 质量检查、中英文 DocFX，并合并到 `master`。合并提交为 `4d24423577096f755837592d93360c30d6b57a45`。
- 具体用户设备上的 UI 首次响应延迟、连续切星延迟、手动网络下载时的响应性，以及 IC-9700/CAT/旋转器联动仍需实际安装验证。CI 不能替代这些实机测试；不声称测得具体毫秒数。
- 本次升级从 v1.72 发布为独立 `v.1.73`，**不移动旧标签或覆盖旧安装包**。签名能力以正式发布的工作流记录为准：如没有 SignPath，按照本次已有正式迭代发布授权保留 Stable 分类并公开声明 Authenticode 未签名；通过 SHA-256 比对发布附件。

## English

### Improvements
- **Non-blocking satellite selection:** Reuse already computed passes or compute a 24-hour Orekit next-pass result on a background worker after a 60 ms debounce. Short-lived tracker-identity cache avoids stale TLE/OMM use. Ignore late results for an older selection or operator-selected pass.
- **Bounded calculation queues:** Current satellite predictions take priority; old queued selections are skipped and cannot create unbounded full-day propagation work. Group lists use separate bounded worker pools and two-minute pass-result caches.
- **Detached group and SDR snapshots:** Compute multi-day group passes and frequency-window passes off the WinForms UI thread; commit only the latest generation. Invalidate cached groups on location, TLE and database changes.
- **Periodic UI:** Group pass list reads completed results, with original detailed hover tooltips calculated off-thread and cached briefly.
- **Earth-view rendering and satellite details:** Move geodetic position, footprint polygons, pass mini-paths, satellite details and photo cache decoding to workers. Replace UI state only for the current satellite.
- **Manual IERS/DE430 refresh:** Snapshot settings, execute HTTP, SHA validation, disk I/O and downloading on a worker instead of the WinForms synchronization context. Automatic/manual downloads remain serialized, with cancellation when the dialog closes.
- **Correctness:** Retain Orekit tracking, CAT, Doppler and rotator protocols. Stabilize one flaky mock rotctld test handshake under Windows CI load without changing hardware motion policy.

### Acceptance boundary

Merged [PR #200](https://github.com/Iu-yang1/SkyRoof-Plus/pull/200) passed full Windows x64 solution build, xUnit, Orekit/IKVM smoke and packaged-runtime validation, pinned `aislop` and DocFX validation. No real-world Windows first-response benchmark, IC-9700 test or hardware rotator acceptance has been performed. This independent release does not replace v1.72. If Authenticode signing is unavailable, the installer remains unsigned and its SHA-256 release-asset validation is mandatory.

**Previous stable:** [v.1.72](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.72).

## 2026-11-13 guarded in-place installer replacement / 原版本安装包原位替换

本次属于已发布 **v.1.73** 的性能修复重构，不创建新的版本号。此前 v.1.73 安装包及标签将由经过 GitHub Actions 验收的新构建替换；**此前保存的 SHA-256 校验值届时不再适用，请以最终发布工作流生成的新值为准。**

- **首屏响应：** 先绘制可交互的初始化状态，随后在后台进行 Orekit/IKVM 初始化、卫星缓存 JSON 反序列化和太阳系轨道对象探测，最后回到 WinForms 线程绑定 DockPanel；避免在 `MainForm_Load` 首帧之前同步初始化。
- **卫星数据更新：** SatNOGS 卫星、发射机、主 TLE、JE9PEL 和 gr-satellites satyaml 独立源并发下载；数据库解析、融合与写盘移出 UI。下载进度仍由 UI 线程呈现。
- **TLE/OMM 更新：** 使用隔离的暂存数据库执行网络获取与星历合并，成功后一次性将轨道层提交至现有卫星对象，保留仍有效的人工 TLE 优先权、卫星选择以及 CAT/旋转器运行引用；已选群组的 Orekit 跟踪器提前在后台准备。
- **多核轨道传播：** Ham、群组和 SDR 的过境批量预测均不在 UI 定时器同步运行；按独立卫星并行，每项最多使用四个工作线程，通过取消/代次判定防止过时快照提交。
- **验证边界：** CI 编译、模拟/单元测试、隔离 Orekit/IKVM 安装包和完整性校验与用户电脑上真实的 UI 最长阻塞时间是不同层级的证据。无 IC-9700、旋转器实机连接和 Windows 交互延迟测量；不声称实机已经无卡顿。
- **签名状态：** 如无 SignPath 能力，安装包仍可能为 Authenticode `NotSigned`；发布工作流必须回读验证安装包的 SHA-256 和现有 Release 身份。

**English:** This is a one-time, guarded in-place replacement of the existing v.1.73 installer. Orekit initialization and cached database parsing are deferred until after the first WinForms paint and executed on worker threads. Independent satellite sources download concurrently; parsed orbit data and TLE layers are prepared in staging snapshots and committed on the UI thread. Ham/group/SDR pass computations use bounded multi-core parallelism with stale-request rejection. Existing numerical orbit, Doppler, CAT, PTT and rotator algorithms are not restricted or replaced. Validation is via CI and installed-runtime smoke tests, not physical radio or measured end-user UI latency. Previously recorded v.1.73 installer hashes must be replaced with the new published hash.



## 2026-11-13 Maidenhead 精度及右侧面板修复 / locator and panel hotfix

- 台站网格配置统一接受 4、6、8、10 位 Maidenhead；坐标转换按完整位数计算单元格中心，不再截断至 6 位。台站经纬度由配置字段转换并通过现有 GroundStation → Orekit/IKVM 观测接口传递，用于过境、方位俯仰、距离变化率及 Doppler。
- Sky View 的整轨迹计算、观测标签刷新和 Tooltip 移至后台，并缓存轨迹。实时卫星位置在已有轨迹上插值，不在绘制事件中同步运行 Orekit。修复过小 Dock 窗口零半径绘图导致的异常循环。
- Satellite Passes 的缩略轨迹与悬浮提示在工作线程生成，旧请求可取消，UI 始终使用已就绪的轨迹快照；避免滚动、切换卫星及窗口重绘期间的同步轨道采样。
- 新增独立经纬度参考值、精度与输入格式单元测试；Windows 构建/测试和 pinned aislop 结果以本次实际 GitHub Actions 运行记录为准。未开展真实 Windows 交互白屏复现和连接 IC-9700/旋转器的实机测试。
- 继续基于已发布 v.1.73 原位替换，不修改版本号。替换前检查当前 Release、安装器 SHA-256 和 tag commit；发布后再次回读校验，旧版安装器哈希不再适用。

**English:** Accept full 4/6/8/10-character station Maidenhead locators and propagate the true cell-center coordinates to the Orekit observer. Prepare Sky View orbit tracks, labels and Satellite Passes mini-paths/tooltips off the UI thread, draw only cached data and fix zero-radius dock-panel painting. Guard the existing v.1.73 asset and tag identities for replacement, then independently verify the published installer. Hardware testing remains outstanding.


## 2026-11-13 Satellite Passes 间歇性空白二次修复 / atomic pass refresh

- 修复已定位的周期性白屏：每分钟 Group 过境刷新在旧缓存失效时无条件调用 ClearPendingGroupPasses，导致旧表立即清空，并在 Orekit/IKVM 后台重新计算期间出现数秒空列表。现同一个群组保留**最后一次完整预测快照**，新结果计算完成后一次性替换；**实际切换群组**才清除旧群组数据，以免混淆卫星。
- 当预测列表的 SatellitePass 引用序列没有发生变化时，保留原有 WinForms 虚拟列表项、滚动位置及缩略轨迹缓存；不再次安排轨迹计算。需要更新时优先使用预先计算完毕的轨迹。
- 每秒倒计时仅调用 RedrawItems 刷新当前可见行，避免对所有虚拟行进行无谓的背景重绘。使用 Volatile.Read 接收后台完整发布的 MiniPath 数组。
- 新增固定组/换组缓存决策、相同/不同列表身份、滚动区域行刷新单元测试；Windows CI 和 aislop 的实际执行结果见对应 PR/Action。无真实 Windows 长时间白屏复现设备，本次修复不主张已完成实机验收。

**English:** Preserve the last published GroupSatellitePasses snapshot during the same group's minute refresh instead of blanking the virtual list pending Orekit computation. Clear stale data only on a real group switch. Skip redundant full list redraws and thumbnail workers on identical pass references; invalidate visible countdown rows only. Hardware/UI soak remains unverified.


## 2026-11-13 十位网格设置校验修复 / 10-character Maidenhead settings hotfix

- 修复 Settings → User Information → Grid Square 仍拒绝合法 10 位 Maidenhead 的问题。根因是 C# 逐字正则字符串 `@"..."` 中的十进制字符 `\\d` 被多写了反斜杠，误当作文字 `\d` 匹配；原先的完整精度转换器和 Orekit GroundStation 坐标入口并非拒绝原因。
- 4/6/8/10 位设置校验现在与 Maidenhead 解析器一致。新增用户复现值 `OL79WQ96HJ` 的输入、经纬度中心 (29.6933159722°N, 115.9109375°E)、10 位逆向转换和 GroundStation 传递的回归测试，同时覆盖无效长度和非法字母组合。
- 正式版维持 v.1.73，并采用现有 Release 身份、原标签提交、原资产 ID 与 SHA-256 的严格比对进行一次受控安装包替换。Windows 编译、xUnit、aislop 和 Orekit 运行时校验状态以本次 GitHub Actions 执行结果为准。尚未在用户 Windows 安装环境中复现操作过程。

**English:** Fix the escaped decimal-digit regex in the Settings property-grid validation path, which rejected valid Maidenhead locators despite the high-precision geodetic converter already supporting them. The user-reported `OL79WQ96HJ` now has regression assertions for accepted input, its exact locator-cell center, reverse conversion and the actual orbit ground-station coordinate. Guard the replacement of the existing v.1.73 installer by tag and published SHA-256 identity; hardware/UI testing remains outstanding.
