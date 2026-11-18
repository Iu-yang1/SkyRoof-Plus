# SkyRoof Fork v.1.85 / UNNE-1B 200 Bd FSK, Telemetry UI, BIN+LOG and SatNOGS

## v1.85 — 2026-11-18

- 经过真实 UNNE-1B I/Q 回归，针对 NORAD 101101、436.888 MHz、200 baud 的误标 BPSK 遥测源自动解析为 FSK/HADES，并采用真实 Type 1、Type 14、CODEC2 CRC/序列验证过的 820 Hz 峰值频偏。用户手动覆盖优先；同频 FM 转发器可自动关联遥测源，不改动 CAT 选台和 CW/FM 分类。
- 降低停靠遥测窗口对主界面的影响：无活动解码器时跳过无用 IF→IQ 处理，合并 PCM 复制；Hilbert 滤波跳过零抽头、变频使用连续复数 NCO；突发统计全部保留但 RichTextBox 摘要按 1 Hz 合并更新。
- 增加可选 BIN+LOG 配对归档，保存解帧后的字节与 UTC/NORAD/CRC/FEC/CFO/SNR/纠错元数据；后台异步写盘，校验失败仍保留且标注。新增独立 SatNOGS 上传排队/成功/失败/HTTP 状态行，上传由异步工作线程处理，不占用 DSP 回调。
- 已合并 PR #246，master Windows 编译、单元测试及 aislop 0.18.1 通过；独立公开真实 I/Q 回放通过。无用户 IC-9700 当天完整原始 IQ PER/BER、停靠窗口实机 FPS/ETW 测试，不宣称现场问题全面解决。
- 创建全新 v.1.85 版本与安装包，保留 v.1.84 及更早 Release 资产。若 SignPath 不可用，则依原有签名策略明确标记未签名 Prerelease，验证安装包 SHA-256。

**English:** v1.85 corrects UNNE-1B's mislabeled 200-baud FSK/HADES telemetry, uses genuine-IQ-validated 820-Hz peak deviation, and improves the docked telemetry panel's DSP and repaint load. It also adds per-frame BIN/LOG archiving and asynchronous SatNOGS upload status. The real IC-9700 pass and docked-FPS behavior require field validation.

---

# SkyRoof Fork v.1.84 / Telemetry UI Liveness and FM Voice DSP P0–P3

## v1.84 — 2026-11-18

- 合并已通过代码审核的 FM P0–P3（PR #243）：将数字遥测 clean AF 与模拟语音后处理分离；增加可选 750 μs 去加重、250 Hz 高通、20 ms 语音衰落插值与鉴频后宽带噪声静噪。保留 48 kHz、12 kHz 实 IF 的 NCO/FIR 及数字 IQ 路径、旧设置枚举值和中英文说明。
- 修复遥测解码期间切换转发器、停止解码导致整个 WinForms 进程不响应的同步等待设计（PR #244）：UI 线程不再 `Join()` 正在处理 SkyTlm 的解码线程；PCM 数据入口与解码器退休同步，后台有序 `Purge/Flush/Dispose`。音频中断从同步 Invoke 改为异步合并，避免工作线程与 UI 互等。
- 遥测界面所有帧/图像/语音事件按 FIFO 分批让出消息循环（每批最多 24 条或约 5 ms），不丢 CRC-valid 数据；FM ASR 未完成文本按最新状态更新，ONNX 模型首次加载及晚完成清理不阻塞 UI。
- 新增 UI 活性与解码线程退役、FM 音频信号、流式分块及 UNNE-1B 真实 IQ 的回归验收。是否通过以当前 v.1.84 Actions 最终运行记录为准；无 IC-9700 现场冻结复现、实机 RF 性能与扬声器音质实测。
- 发布策略：新增独立 v.1.84 安装包和标签，不替换 v.1.83。缺少 SignPath 时明确标记未签名 Prerelease，核验下载哈希与安装后运行库。

**English:** v1.84 makes telemetry decoder retirement, UI dispatch and optional FM ASR model loading nonblocking, while including the previously merged FM voice P0–P3 upgrades. End-to-end radio stress testing remains pending.

---

# SkyRoof Fork v.1.83 / Telemetry Real-WAV and OMM Source Reliability

## v1.83 — 2026-11-17

- 合并已通过回归的遥测 PR #240：修正池化 IQ 有效长度；恢复 AO-73 差分 BPSK + AO-40 FEC 默认解码；为 FSK/GFSK/GMSK 增加已鉴频 FM-AF 可选输入；保留 SDR IQ / 12 kHz IF。
- 真实 WAV 回放：AO-73 1×256 B RS、AO-27 2×AX.25/FCS、AISTECHSAT-3 5×AX100/FEC、Suomi 100 2×AX100/FEC；SkyTlm fork 823 通过、3 跳过、0 失败。UNNE-1B 真实 IQ/CRC 通过原有独立回归；Suomi 100 其它三 burst 数据组仍为 2/3，未宣称修复。
- 合并星历 PR #241：支持官方 CelesTrak OMM CSV/JSON 内容自动识别与六位 NORAD；官方 amateur 在线对比 99/99 一致。
- 轨道要素优先级仍为 72 h 手动文件 > CelesTrak OMM > 自定义源（第一项优先） > AutoTLE > SatNOGS；区分独立的卫星/发射机元数据、IERS EOP 和 Orekit DE430。网络优化提供多镜像/本地文件、结构校验、原子缓存、源键 2 h CelesTrak GP 缓存与故障回退；无默认未经验证公共镜像。
- 发布基线：2026-11-17 合并后 master Windows x64 Release 编译、xUnit、Orekit Smoke、安装包依赖测试及 aislop 均通过。此次 v1.83 Release 的安装包签名、SHA-256 和发布完整性以后续 Actions 实际结果为准。
- 不声称完成 IC-9700 实机接收、旋转器/多普勒闭环、自动 AFC 实测或中国大陆 ISP 连通性实测。保留独立 v.1.82 资产；详细变更参见 `RELEASE_NOTES.md`。

**English:** v1.83 bundles recorded-audio-validated AO-73/AO-40 telemetry and pooled-IQ fixes with CelesTrak CSV/JSON OMM, source priorities, regional/offline mirrors, and Orekit data safeguards. RF hardware and mainland network verification remain outstanding.

---

# SkyRoof Fork v.1.82 / Telemetry IF-AF Reliability

## v1.82 — 2026-11-16

- P0: 遥测 12 kHz IF 接收不再依赖 IF→AF 的虚拟声卡成功启动。USB 可独立采集，Direct LAN 由媒体会话分发原始 PCM；新增数据断续事件并重置后续 IQ/解码状态。
- P1: 调整已鉴频 AFSK AF 的解调输入域，避免对解析音频直接重复 FM 鉴频。
- P2: 增加 IF 输入、RMS、丢样、断流、解帧 CRC、CFO/SNR 诊断；KISS 以端口 0/1/2 区分 RF 帧、内层业务包与校验失败帧。
- P3: Recorder 支持 .if.wav 经过统一 IQ 桥接离线遥测解码，回放 Seek 时重建解调状态，修复 AO-123 的失败 TM 帧后内层包连续性。
- 保留原有 SDR、IF→AF 和现有卫星协议栈。无 IC-9700 实机测试，真实 200 Bd/9k6 帧通过率仍需实录验证；自动验收结果以 v1.82 Actions 和发布安装包为准。

**English:** Telemetry IF/AF reliability release with direct USB/LAN raw IF receive, state reset on discontinuities, corrected AFSK audio domain, channel-separated KISS output, telemetry diagnostics and offline IF WAV replay.

---

# SkyRoof Fork v.1.81 / Direct LAN 连续 PTT 稳定性

## v1.81 — 2026-11-16

- 修复 v1.80 的 12 秒音频心跳误判：只有本机发送 TX 包、却没有电台回显 Audio Ping，并不代表媒体授权失效；不再因此触发整个 LAN 会话自动重建，造成下一次 PTT 黄灯。
- 心跳继续提供诊断统计，但与媒体就绪判断解耦。明确的电台断开/Socket 错误/鉴权失败仍可触发恢复。
- 添加多轮 PTT 仿真回归测试，保留既有三路 UDP 关闭、Logout、TX Audio 热切换与 CAT/PTT 安全语义。
- 未执行 IC-9700 实机，Windows 自动测试和 aislop 结果以 v1.81 GitHub Actions 为准。

**English:** Stop resetting healthy negotiated audio media on missing optional remote ping echoes. Maintain heartbeat counters for diagnostics and preserve evidence-based reconnection.

---

# SkyRoof Fork v.1.80 / Direct LAN TX 音频会话恢复

## v1.80 — 2026-11-16

- 增加 Control / CI-V / Audio 三个 RS-BA1 UDP 会话各自的断开通知、关联令牌注销确认及未确认故障日志。
- 将重连前控制 UDP 的悬挂接收任务收回，阻止新旧接收事务竞争注销应答，延长正常释放窗口以减少强制关闭残留。
- Audio UDP 远端心跳依据 SID、随机数和序列号核对，不再把本机 SendAsync 计数视作电台活性；12 秒失活会在 PTT 释放后请求完整媒体重建。
- 底栏 TX 诊断添加远端响应时间/次数/失效状态，并增加独立协议测试；保留现有 CAT/PTT、发射使能与轨道/频谱功能。
- TX 开关热切换：已有有效 TX 媒体授权时仅重启本地 WASAPI 采集端点，不再断开控制/CI-V/频谱；首次启用或调整协议参数仍重新协商。
- 无 IC-9700 实机测试；自动化验收与签名状态以 v1.80 Actions/Release 为准。

**English:** Explicit 3-stream disconnect, correlated logout, remote-audio heartbeat monitoring and post-PTT authenticated recovery.

---

# SkyRoof Fork v.1.79 / SkyRoof Plus 1.79 过境匹配异步与可选 UTC 窗口

## v1.79 — 2026-11-15

- DX 网格输入改为纯编辑，不再逐字符调用过境预报；新增“开始计算 / 取消计算”、UTC 起止时间和进度状态。
- 轨道传播、双方窗口求交、仰角和天空轨迹采样在 WinForms UI 线程外执行；完成后原子更新列表，支持取消与输入失效，避免过期结果。
- 交集严格裁剪自选 UTC 范围，保持 ≥30 秒门槛；匹配采用有序区间扫描，并修正地球静止轨道固定一日的结果范围。
- 新增中英文资源、四档模拟 DPI 与时间窗口测试，配套中文/英文操作文档。
- 本次为独立 v.1.79 迭代，不覆盖 v.1.78；缺少签名凭据时遵循仓库未签名预发布策略。最终质量门禁与 SHA-256 以对应 Actions 运行记录为准。

**English:** v1.79 adds explicit UTC-range pass matching, a start/cancel workflow, background computation with progress, and stale-result rejection. It no longer recalculates while typing grid squares.

---

# SkyRoof Fork v.1.78 / SkyRoof Plus 1.78 专业术语优化

## v1.78 — 2026-11-15

- 将卫星发射机列表专业表述为“转发器与信标”，保留 Transmitter/Transceiver/Transponder 的分类语义差别。
- 将 QSO Scheduler 改为“过境匹配”，新译共同可见窗口的时间、最大仰角和详情视图；不把可见性等同转发器实际可用。
- 统一旋转器跟踪、频谱 REF/VBW、CW WPM、SSTV RSV、Recorder 音频/IQ 术语，修正日志州/省字段。
- 本轮新增 315 组中英文资源并修订 36 项旧译文，补全顶部 RX/TX、CAT/PTT/CTCSS 菜单、频率控制、卫星分组与天空视图、过境匹配、自动选择、AMSAT、实体 PTT、ONVIF 视频、PARK/Yaesu G-5500 旋转器、SSTV 降噪/图库/WAV/ADIF 以及 Recorder/遥测/CW 提示；不修改电台、轨道、遥测、旋转器行为。
- 中英文资源键一致性与术语专门测试由 CI 验证；发布构建、哈希与未签名披露以对应 Release 工作流为准。
- 详细变更与术语依据：[RELEASE_NOTES.md](RELEASE_NOTES.md)。

**English:** v1.78 improves precise Chinese ham-radio terminology and covers remaining pass-matching and transponder/beacon editor interactions. Existing CAT/PTT, rotator, SDR and DSP behavior is unchanged.

---

# SkyRoof Fork v.1.77 / SkyRoof Plus 1.77 中文资源安装修复

## v1.77 — 2026-11-15

- 修复已安装的 v.1.76 在选择简体中文后主窗口仍为英文：Inno Setup 遗漏 `zh-CN/SkyRoof.resources.dll`，导致 .NET UI 资源回退英文。
- 将中文卫星资源 DLL 打包到正确的 `{app}/zh-CN`；在安装后测试中检查文件 SHA-256 以及菜单和面板的中/英文实际 `ResourceManager` 查找。
- 仅修改安装包、安装后冒烟测试与版本发布元数据，保留 CAT/PTT、旋转器、遥测与录音功能；保留独立 `v.1.76` 标签和安装包。
- 本次新版本 Release 的完整测试状态以 GitHub Actions 为准；无实机、物理 DPI 和全部 WinForms 窗口的图像验收。
- 缺少 SignPath 时安装包为明确披露的 NotSigned，继续核验 SHA-256。

**English:** v1.77 fixes missing Chinese satellite resource assemblies in the v1.76 Inno Setup payload. Installed-runtime resource resolution and per-file hash checks now block regressions. The patch does not alter radio, DSP or rotator logic.

---

# SkyRoof Fork v.1.76 / SkyRoof Plus 1.76 遥测与双语迭代

## v1.76 — 2026-11-15

- AO-123 TM-short KISS 分帧重组；UNNE-1B Type-C GENESIS 同步、CRC、工程遥测；修复 SatYAML 下行关联、TLE epoch 偏移及 RF 帧上传语义。
- Recorder 新增原始 IF、IF 派生 IQ 录制与 IQ WAV 遥测回放，隔离实时 SDR 流与回放流。
- English / 简体中文运行时切换主要页面，不重新建立 CAT/PTT 或 GS-232 会话。
- Windows CI 新增双语模拟 100/125/150/200% DPI 的真实控件 40 张截图以及虚拟通信会话保持验证；修复 Settings/Recorder 空白截图误判。
- 代码：PR #224、#225、#226；发布：PR #227；详细说明：`RELEASE_NOTES.md`。
- 验证：合并前 1609/1609 xUnit 通过，合并后的 master Compile Check 和 aislop 通过。版本化安装包必须在 Release 工作流中进一步验证；不声称已进行 IC-9700、实际云台、物理 DPI 或 Settings PropertyGrid 的原生绘图测试。
- 签名：缺少 SignPath 时安装包为未签名（NotSigned）正式版，必须通过 Release 附件 SHA-256 校验。

**English:** v1.76 adds AO-123/UNNE-1B engineering telemetry, IF/IQ recording and telemetry replay, bilingual live WinForms UI switching and simulation-based DPI/session continuity tests. Actual radio/rotator and physical-monitor DPI tests remain outstanding. If SignPath is unavailable, the installer is explicitly unsigned, with SHA-256 verified by release CI.

---

# SkyRoof Fork v.1.75 / SkyRoof Plus 1.75 LAN TX / CW / Audio

## v1.75 — 2026-11-14

- 持久化 Direct LAN UDP TX/RX 音频会话，配置热应用、握手失败恢复；TX UDP LED 与音频诊断入口。
- CW Console V2 改进八槽位、资源生命周期与 Skimmer UI；增加 IF→AF 录音、遥测多音源以及设置项中文化。
- 详见 `RELEASE_NOTES.md` 历史版本；保留独立 `v.1.75` 标签和安装包。

---

# SkyRoof Fork v.1.74 / SkyRoof Fork 1.74 UI 适配版本说明

## v1.74 UI P1–P4 — 2026-11-14

- P1：工具栏、频率条、卫星选择与旋转器控件和字体使用固定基线同比例缩放；UI Scale 可选 75/85/100/110/125%。
- P2：Dock 窗口与 G-5500/RC-3040S 右键控制卡片同步缩放；不重排 Dock，不新增滚动条，保持原设备控制回调。
- P3：多显示器拔插后的窗口恢复与负坐标保留；SystemAware 为默认值，PerMonitorV2 为显式可选测试模式。
- P4：WinForms 模拟渲染与交互测试覆盖浅色/深色主题、96/120/144/192 DPI、Sky View、Satellite Passes、Icom Spectrum、CW Skimmer、ROTATOR STOP 等。
- 代码：PR #209、#210、#211、#212；验收报告：`docs/architecture/ui-dpi-acceptance-20261114.zh-CN.md`。
- 测试：xUnit、Windows 编译、Orekit/IKVM 运行时、aislop 与 CI 的模拟截图；暂无真实双显示器、Wine、IC-9700 或旋转器实机验收，不将 PerMonitorV2 默认启用。
- 本版发布为独立 v.1.74 标签，不覆盖既有 v.1.73 Release。

**English:** v1.74 completes the P1–P4 uniform no-reflow UI/DPI adaptation, dock/popup scaling, multi-monitor recovery with optional PMv2, and synthetic Windows CI screenshot/interaction coverage. Physical monitor and transceiver/rotator tests remain unverified.

---

# SkyRoof Fork v.1.73 / SkyRoof Fork 1.73 版本说明

## v1.73 原位重构 / In-place performance rebuild (2026-11-13)

- 前台首帧前不执行 Orekit 初始化及大量卫星数据库读取；启动载入状态先绘制，缓存与轨道跟踪器后台初始化。
- 卫星列表来源并发下载，数据库解析与导入后台化；手动 TLE 更新使用暂存数据库、原子合入已选对象并保持人工星历优先级。
- Ham / 群组 / SDR 批量预测采用独立工作线程和每个卫星至多四路并行，UI 定时器不再同步执行整批 Orekit 传播。
- 仅在原 v.1.73 标签/Release 身份和旧安装包哈希精确匹配时允许一次性覆盖，经发布工作流重新验证 SHA-256 和签名状态；旧安装包校验值失效。
- English: Responsive startup, concurrent feeds, worker-owned imports and orbit snapshots, bounded multi-core propagation, and a guarded single-use v.1.73 replacement. Software CI is not physical CAT/rotator or end-user latency acceptance.

# SkyRoof Fork v.1.73 / SkyRoof Fork 1.73 版本说明

Release date / 发布日期：2026-11-13。响应式卫星切换、群组/SDR 后台预测、Orekit 手动数据更新线程分离以及短期校验缓存。作为独立正式修复迭代，不覆盖 v1.72。

- **UI 响应性：** 卫星切换使用 Orekit 优先后台预测，缓存按 Tracker 身份区分并支持无过境负结果；快速切换不接纳旧结果。
- **耗时计算与缓存：** 多卫星群组、SDR 频段过境、工具提示、地图与卫星详情均从 WinForms 事件/周期绘制中剥离；按 UI 代次提交和缓存失效，不使用同步 Task.Result。
- **手动星历更新：** 复制本次数据源设置，后台 HTTP、DE430/EOP 文件校验与 I/O；已验证的 DE430 在路径、大小和修改时间一致时复用进程内缓存。保留自动更新与手动更新互斥。
- **测试：** [PR #200](https://github.com/Iu-yang1/SkyRoof-Plus/pull/200) Windows x64 构建、全量单测、Orekit/IKVM 安装器烟测、固定 aislop 和双语 DocFX 均通过。旋转器模拟握手超时测试使用适当等待时间，不更改控制逻辑。
- **English:** Responsive, bounded, tracker-aware next-pass worker queues; group/SDR snapshot predictions, Earth-view and tooltip caching; off-thread manual Orekit ephemeris downloads. Retains Doppler, CAT and rotator behavior. Actual hardware and user-PC UI latency remain unmeasured.

完整双语说明 / Full bilingual details：[`RELEASE_NOTES.md`](RELEASE_NOTES.md)。

---

# SkyRoof Fork v.1.72 / SkyRoof Fork 1.72 版本说明

Release date / 发布日期：2026-11-13。首次加载缓存优先及 Orekit 网络更新异步化；正式修复迭代，不覆盖 v.1.71。

- **启动优化：** DockPanel 优先恢复缓存窗口；IERS EOP/DE430 自动下载与校验移至界面显示后的后台线程。卫星全量过境预测延后到首次绘制之后，避免原先的空白工作区等待。
- **一致性：** 保持自动更新、TLE/OMM、Moon/Sun/Venus、Doppler、CAT/Rotator 功能；手动和自动更新共享串行缓存写入，取消后台刷新时不会写入损坏文件。
- **用户体验：** 有缓存时数据库按周提示更新会在主界面显示后触发；无数据库的新安装仍需首次导入。EOP 更新后提示重启以应用新的 Orekit 全局数据上下文。
- **验证限制：** Windows x64 构建/单测/安装器烟测、aislop/DocFX 检查均通过；没有用户实机启动耗时或旋转器/CAT 联动实测。没有 SignPath 时可按本次明确授权发布未签名正式版，完整性以 SHA-256 校验。
- **English:** Cache-first docking UI, delayed initial pass prediction, deferred IERS/DE430 background refresh, safe serialized manual/automatic downloads. Same satellite, Moon and Doppler algorithms. Verified Windows CI; no hardware or user-device startup-time measurement. This authorized stable hotfix may be unsigned.

完整双语说明 / Full bilingual details：[`RELEASE_NOTES.md`](RELEASE_NOTES.md)。

---

# SkyRoof Fork v.1.71 / SkyRoof Fork 1.71 版本说明

Release date / 发布日期：2026-11-13。正式发布统一 Orekit/IKVM 卫星与月地星历引擎；保留旧 v.1.70 标签与安装包。

- **中文：** 全部 TLE/OMM 轨道统一使用 Orekit SGP4/SDP4 传播，移除运行中的 SGP.NET 后备计算；月球/太阳/金星统一 Orekit DE430 站心位置、方位角/仰角、径向距离变化率。旧版 SGP.NET DTO 仅用于 UI 兼容，不执行后备传播。
- **数据与 UI：** 「轨道与星历来源」合并 IERS EOP 和 JPL DE430 下载来源、自动下载、目录与可视状态，并提供「更新 Orekit 星历」入口；中英文支持，改动后重启加载新数据；校验 DE430 Git Blob 和缓存完整性。
- **质量与性能：** 通过 Windows x64 构建、xUnit、Vallado/DE430 专项、多普勒方向/距离变化率回归、安装后 IKVM 运行验证与固定版本 aislop。预热后中位耗时：TEME 1000 次 3.90 ms，站心 1000 次 118.25 ms，月球 200 次 7.68 ms；仅为共享 CI 主机测量。
- **发布与限制：** v.1.71 根据明确授权发布为正式 Release，缺少 SignPath 时可能是**未签名**安装包并明确披露，保留 SHA-256/来源验证。未进行 IC-9700、CAT、GS232B 实机联动；特殊病态轨道、长期外推和 IKVM 非测试路径不作全面精度承诺。
- **English:** One Orekit/IKVM engine for TLE/OMM SGP4 propagation and DE430 Moon/Sun/Venus observations. Bilingual consolidated ephemeris settings, verified IERS/DE430 downloads, local cache and status, restart-to-reload, tested Windows installer with IKVM dependencies. No SGP.NET runtime propagation fallback; old DTO adapters retained. CI numerical/performance tests and pinned code quality pass. This owner-authorized stable release may be unsigned if SignPath is not configured; no physical IC-9700 or rotator validation is claimed.

Read full bilingual notes / 详细双语说明：[`RELEASE_NOTES.md`](RELEASE_NOTES.md)。

---

# SkyRoof Fork v.1.70 / SkyRoof Fork 1.70 版本说明

Release target / 计划发布：2026-11-13. Orekit 13.1.9 / IKVM 8.16.3 集成；此前 v.1.69 不覆盖。

- **中文：** 新增 SGP.NET/Orekit 后端选择、原始 CCSDS OMM 六位 NORAD、TEME/ITRF/WGS-84 观测、实际 AOS/LOS 恢复、官方 IERS EOP 资料更新和只读状态显示。IC-9700/CAT/云台控制协议和 JPL BSP 后端保持。
- **多普勒修复：** 原 SGP.NET 1.6.0 未扣除地面站自转速度的径向速度计算已修正；改变先前的数值结果但不修改轨道传播模型。
- **验收：** 1400 项 xUnit、aislop、Vallado 33 个案例、完整 Windows 安装目录 IKVM 启动及 TLE/OMM 计算路径通过。28 个标准参考案例与 Vallado 解的误差符合 0.1 m / 0.001 m/s 门槛。
- **已知限制：** IKVM0117 静态转换警告仍在；特殊深空/病态 TLE/极端长期传播案例分别保留差异，真实 IC-9700/CAT/Rotator 未实机验证；P4/P5 精密天体和姿态/极化不是本版已实现功能。签名不可用时将按既有 CI 自动标识为未签名预发布。
- **English:** Optional Orekit/IKVM orbital backend, native CCSDS OMM with six-digit catalog numbers, correct range-rate and pass geometry, IERS EOP data updates and a clean-install verified Windows bundle. SGP.NET stays default and is available as fallback. Fixed the legacy SGP.NET observer-rotation omission affecting Doppler. Verified 1400 xUnit cases, all Vallado diagnostics, 28 standard accuracy cases, and IKVM installed-runtime startup. Stress-case accuracy, IKVM0117 and untested real radio/rotator hardware remain explicitly documented.

Read full bilingual notes / 详细双语说明：[`RELEASE_NOTES.md`](RELEASE_NOTES.md).
Architecture & test evidence / 架构与验证记录：[`docs/architecture/orekit-ikvm-p0-status.zh-CN.md`](docs/architecture/orekit-ikvm-p0-status.zh-CN.md).

---

# SkyRoof Fork v.1.69 / SkyRoof Fork 1.69 版本说明

### v.1.69 同版本安装包补充修正 / Installer replacement

- **自动更新来源：** 从原作者网站的 HTML 版本解析切换到本 Fork 的 `Iu-yang1/SkyRoof-Plus` GitHub Releases API。检查已发布版本（包括带安装包的预发布版），更新链接指向 Fork 的版本发布页。Windows 安装器的更新和支持链接也指向本仓库。
- **About 双署名：** 在 `Help → About` 内保留 Alex Shovkoplyas VE3NEA 的原始版权及官网链接，新增 `Fork modifications © 2026 Iu-yang1 BG5JSU` 和 Fork 仓库链接；不修改原始 LICENSE。
- **发布完整性：** 同版本替换仅允许在原始 v.1.69 标签未被移动的情况下执行，构建、测试和新安装包哈希匹配后才移动标签；最终验证标签、下载包及 SHA-256。

- **Updater source:** The app now queries the fork's GitHub releases (including prereleases with matching installers) instead of scraping the upstream HTML page; the installer update/support links point to this fork.
- **Attribution:** Help → About retains VE3NEA's original copyright and website, while identifying Iu-yang1's fork modifications and repository.
- **Release integrity:** The one-time in-place installer replacement validates the new binary's digest before retargeting v.1.69, followed by release verification.


Release date / 发布日期: 2026-11-12. Chinese settings localization and IC-9700 native Direct LAN TX/RX reliability maintenance, based on v.1.68; previous release tags and installers remain unchanged.

## 中文

- **新增音频/频谱设置汉化：** 在现有「简体中文 / English」设置界面中补齐 RX 扬声器播放缓冲、TX 网络预缓冲、麦克风采集缓冲、抖动预填充/上限、麦克风增益、语音 AGC（目标 RMS、最大增益、噪声门限、启动/恢复时间、峰值上限）、自动选择 LAN MOD 输入及独立 SkyCAT 频谱控制 CAT TCP 端口的中文名称和中文帮助文字，折叠摘要也可随语言切换。
- **保持配置兼容：** 所有 JSON 键名、默认值、频率/PTT 协议以及英文菜单均未改变，新增中英切换/序列化测试。音频软件增益仅改变本地 PCM，不代表电台 RF 输出。
- **原生 LAN 频谱控制（PR #187）：** 独立的 SkyCAT CAT 控制连接支持 MODE/SPAN/REF 与真实寄存器读回，频谱数据 TCP/4535 与控制 CAT TCP/4532 分离；减少频谱周期性查询对多普勒和 PTT 的影响。
- **发射语音连续性（PR #188）：** Direct LAN TX 以固定 20 ms PCM 节奏发送，使用 60 ms 软件预填充及 250 ms 最大抖动缓冲，新增欠载、丢样、PTT 状态和时钟迟滞诊断。
- **可选 TX 语音 AGC 与缓冲拆分（PR #189）：** RX 播放 120 ms、TX 网络 150 ms、麦克风采集 150 ms、软件抖动预填充 60 ms/上限 250 ms 各自独立；AGC 默认关闭，语音目标 −18 dBFS、最大增益 +12 dB，含噪声门限、时间常数与峰值限制。FT4/FT8/Q65 等数字调制建议保持关闭。
- **电台调制源读回和选择（PR #190）：** 经 CI-V 读取 IC-9700 DATA OFF MOD、DATA MOD、LAN MOD Level；仅在 CAT PTT 正在发射且最近存在 PC TX PCM 时才按设置自动切换 LAN，并独立读回核验。提示 MOD≠LAN、LAN MOD 0%，不将本机麦克风 dBFS 误认为电台已调制。电台 MOD 选择可能在程序退出后保留，使用原装麦克风时请检查并切回。
- **仪表与调度（PR #184）：** 发射时优先查询 PO/ALC、SSB 功率峰值短暂保持，CAT PTT 与多普勒写频优先。全链路实际 RF 功率、音质及帧率仍需 IC-9700 实机核验，自动测试不等于完成射频验收。
- **PR 整理：** 发布前仓库已无未关闭 PR；本次汉化 PR 合并后再次核对。签名是否存在由 CI 自动判断：无签名时发布明确标识的未签名预发布版，不冒称签名正式版。

## English

- Fully localized the newly added Icom Direct LAN RX/TX buffer, voice AGC, microphone gain, radio LAN MOD selection and SkyCAT scope-control PropertyGrid labels, help text and collapsed summaries in Simplified Chinese; English labels and persistent JSON keys remain unchanged.
- Included scope-only SkyCAT CAT control and verified MODE/SPAN/REF register readback (#187), 20-ms paced TX PCM and jitter diagnostics (#188), independent native network/capture/jitter buffers and optional speech AGC (#189), and actual IC-9700 LAN modulation input CI-V readback/auto-selection (#190).
- Automatic LAN MOD selection only runs with current CAT PTT and recent PC TX PCM; it does not silently change front-panel microphone preferences during RX-only scope operation. The setting may remain changed after SkyRoof exits.
- Default AGC remains OFF for calibrated digital modulation. MIC dBFS/UDP send count is not hardware ALC, transmitter power or a proof of RF modulation. Actual radio TX/RX, voice quality, meter readback and Doppler compatibility require on-air validation.
- Added bilingual documentation and PropertyGrid localization/serialization regression tests. The release workflow tests Windows x64, runs pinned aislop and installer integrity checks, and publishes an unsigned **pre-release** when SignPath signing is unavailable.

Updated operator guides: [中文频谱与 Direct LAN](docs/zh-cn/fork-guide/spectrum.md) | [English](docs/fork-guide/spectrum.md) | [Direct LAN 音频技术说明](docs/architecture/icom-direct-lan-audio-meters.zh-CN.md).

---

# SkyRoof Fork v.1.68 / SkyRoof Fork 1.68 版本说明

Release date / 发布日期: 2026-11-11. Direct LAN CI-V network settings readback release, superseding v.1.67 without modifying its tag or installer.

## 中文
- **SkyRoof-Plus PR #179**：Direct LAN 设置增加 `READ FROM CI-V`，通过已连接的 SkyCAT 复用 CAT/CI-V 会话，不单独建立 Icom LAN 登录。
- **SkyCAT PR #37**：支持 IC-9700 网络参数只读 CI-V 查询。读取 IP、Network Control 开关、Control / Serial / Audio UDP 端口；IP、Control / Serial 端口按有效字段回填，Audio 和开关状态只供显示；账户及密码原样保留。
- 在 CAT 周期中逐个查询参数，保持正常频率、多普勒和 PTT 操作流程；对响应来源、命令和字段进行匹配，拒绝畸形 BCD 数据。缺失字段不重置设置，点击 Apply / OK 才持久化。
- SkyCAT Windows 编译与测试、SkyRoof-Plus Windows x64 构建、全量 xUnit、Settings 四行工具栏布局回归及固定版本 aislop 门禁已经通过。
- **实机边界**：尚未在 IC-9700 上直接完成网络参数回读实测，不声明真实电台读回兼容性已经验收。

## English
- **SkyRoof-Plus #179** adds `READ FROM CI-V` to Direct LAN settings using the existing SkyCAT CAT transport, without another Icom LAN login.
- **SkyCAT #37** exposes read-only IC-9700 network IP, Network Control, and Control/Serial/Audio UDP port registers. Valid IP and Control/Serial ports populate pending settings; audio port/status are informational, and credentials remain unchanged.
- One CI-V query is executed per CAT cycle, with strict response matching and BCD validation. Missing fields preserve existing settings and values are saved only on Apply/OK.
- SkyCAT Windows build/tests, SkyRoof-Plus Windows x64 build, full xUnit tests, settings-layout regression, and pinned aislop gate passed.
- **Hardware caveat:** Real IC-9700 network readback has not been validated on hardware; automated tests do not prove real-radio interoperability.

---

# SkyRoof Fork v.1.66 / SkyRoof Fork 1.66 版本说明

Release date / 发布日期: 2026-11-05. P0–P3 risk-assessed code-quality and lifecycle maintenance release.

## English
- Isolated rotator child: preserve IPC, startup rollback, transport teardown and **physical STOP unverified** failure evidence without changing motion commands, STOP priority, feedback interlock or hardware retry timing.
- SkyCAT/CAT lifecycle: distinguish expected process exit from an unconfirmed child termination; preserve CAT/PTT ownership and serial protocol behavior.
- FT4/SSTV: keep mandatory PTT release when audio-buffer cleanup fails; report temporary-WAV deletion and malformed QSO log rows while retaining valid records.
- Improve WASAPI device-loss handling, ONVIF camera lifecycle diagnostics, voice playback disposal, SatNOGS upload shutdown, ephemeris cache cleanup, and installer log fallbacks.
- Run the CW R2C FFT benchmark on every master push, with Windows x64 build, full xUnit and pinned aislop v0.18.1; independent CI, not a cross-CPU throughput claim.
- P1: make AMSAT status submissions cancellable and non-blocking on the WinForms UI thread (#164); reject incomplete satellite/transmitter customization state explicitly before tuning (#165), while retaining the existing stored values and frequency formulas.
- P2: refactor AFC observation/polarity/servo responsibilities (#154) with regression coverage for independent FIR/DSP references, AFC CAT-timeline/polarity and CW R2C/complex FFT phase and amplitude.
- P3: require pinned aislop and six-size CW R2C FFT microbenchmark inside Build and Release itself, refuse an existing release tag, serialize releases, verify Authenticode before publishing, and mark a build without SignPath as a prerelease. Never claim signing from workflow configuration alone.
- Full-source pinned aislop after #165: 64/100, 781 findings (1 intentional native callback exception error, 779 warnings and 1 info). Historical debt is documented, not declared fixed or silently rebaselined.
- The SoapySDR native log callback retains one deliberately reviewed non-propagating catch to avoid exceptions crossing the unmanaged callback boundary; see `docs/quality/aislop-native-callback-exception-20261105.md`.
- **Hardware acceptance remains separate:** physical rotator STOP/C2 feedback, IC-9700 CAT/PTT, WASAPI unplug and ONVIF devices require field verification; Windows CI does not certify them.

## 中文
- 旋转器独立子进程：保留 IPC、启动失败回滚、传输关闭和**物理 STOP 未确认**的诊断证据，不改变运动命令、STOP 优先级、反馈互锁或重试时间。
- SkyCAT/CAT 生命周期：区分正常退出与子进程终止未确认，保留 CAT/PTT 控制权和串口协议。
- FT4/SSTV：音频缓冲清理失败仍保证既有 PTT 释放；记录临时 WAV 删除和损坏 QSO 日志行，保留有效记录。
- 修复与加强 WASAPI 断开、ONVIF 生命周期、语音播放资源清理、SatNOGS 后台任务、星历缓存和安装程序日志的诊断。
- master 每次推送都运行 CW R2C FFT 基准，并保留 Windows x64、全量 xUnit 与 aislop v0.18.1 门禁；基准仅作为同环境回归检查。
- P1：AMSAT 上报改为可取消的异步操作，避免阻塞界面（#164）；RadioLink 在读取或修改调谐配置前明确验证配置对象已初始化（#165），不改变已有频率公式和持久化值。
- P2：按 #154 拆分 AFC 观测、极性和伺服职责；由独立 FIR/DSP、AFC CAT 回读时序及 CW R2C/复数 FFT 相位幅度回归测试覆盖核心数学约束。
- P3：Build and Release 自身强制通过固定版本 aislop 与六组 CW R2C FFT 性能基准，拒绝已存在的 Release Tag、串行化发布、上传前核查 Authenticode；没有 SignPath 时作为预发布，签名情况以实际安装包验证为准。
- #165 之后固定版本 aislop 报告：64/100，781 项发现（1 项原生回调例外 error、779 warnings、1 info）。历史技术债持续保留，不声称全部清零或自动重新生成基线。
- SoapySDR 原生回调保留一项有依据的安全例外，避免异常跨越非托管边界；详见 `docs/quality/aislop-native-callback-exception-20261105.md`。
- **实机验收仍独立进行：** 旋转器物理 STOP/C2、IC-9700 CAT/PTT、WASAPI 热拔插及 ONVIF 设备均不能以 CI 成功替代。

---

# SkyRoof Fork v.1.64 / SkyRoof Fork 1.64 版本说明

Release date / 发布日期: 2026-11-02. Settings dialog layout correction.

## English
- Fixed top VB-CABLE and language toolbars covering the first four Settings sections: Orbit / Ephemeris Sources, User, Audio, OutputStream.
- Replaced overlapping Dock layouts with independent TableLayoutPanel rows; kept all existing settings values and serialized JSON names.
- Added Windows bilingual PropertyGrid, nonoverlap and settings persistence regression tests.
- No changes to IC-9700 IF→AF, AFC, PTT or satellite Doppler.

## 中文
- 修复 VB-CABLE 工具栏及语言选择栏遮挡设置列表前四项（轨道与星历来源、用户信息、音频、音频输出流）的问题。
- 改为独立三行布局，保留原设置内容、序列化键名和中英标签。
- 增加 Windows 真实 PropertyGrid 显示、工具栏不重叠及配置保存测试。
- 未修改电台 IF→AF 解调、AFC、PTT 或卫星多普勒。

---

# SkyRoof Fork v.1.63 / SkyRoof Fork 1.63 版本说明

Release date / 发布日期: 2026-11-02.

## English

- Production IC-9700 48-kS/s real 12-kHz USB IF → SSB/CW/FM AF → Windows virtual cable for WSJT-X; raw IF is shared with AFC/SSTV where possible.
- FM 6.25-kHz (900-Hz deviation) startup fixed; decoder failures surfaced to the link status.
- Removed experimental dual USB IF/LAN AF comparison recorder and verbose per-block diagnostics; normal recording and four-stage IF/DDC/AF/V-MIC health display remain.
- Preserved bounded queues, peak protection, slow SSB/CW AGC, FM gain, weak-FM evidence gate, HoldLast and RX-only CAT/PTT safety.
- Optional original basic VB-CABLE Donationware package, verified for publisher and integrity. No experimental SysVAD kernel driver is shipped.

## 中文

- 正式提供 IC-9700 12 kHz 实数 IF → SSB/CW/FM → 虚拟声卡 → WSJT-X 接收功能，AFC/SSTV 可共享原始 IF。
- 修复 6.25 kHz FM 900 Hz 频偏导致的静默无声与解调异常诊断。
- 移除实验性双路 IF/LAN AF 对照录音和冗长 DSP 诊断，保留普通录音及 IF/DDC/AF/V-MIC 链路状态。
- 保留受限音频增益、削峰、缓冲、SO-50 弱 FM 置信度验证、AFC HoldLast 与 RX/TX 安全互锁。
- 正式安装包可选安装原厂基础版 VB-CABLE Donationware；不分发未正式签名的内核驱动。

---

# SkyRoof Fork v.1.62 / SkyRoof Fork 1.62 版本说明

Release date / 发布日期: 2026-10-31. SSTV Console integration release.

## English — changes since v.1.61

- AFC FM fix: repeated WASAPI IF audio timestamps no longer clear the longer frequency-measurement buffer while the independent spectrum remains active.
- AFC distinguishes no measurement from a true zero residual, and displays tentative FM IF offset estimates plus quality without unsafe CAT correction.
- Added +820 Hz FM offset and repeated-timestamp regression tests and measurement pipeline diagnostics.

- Official v1.62 update: up to eight draggable SSTV caption layers with add/duplicate/remove, editable text, size, color and template persistence.
- Added picture variable insertion for MY/DX calls, RSV sent/received, QTH/name/notes, UTC time and TX/RX MHz, preserving older RST variable aliases.
- Optimized large photo loading, bitmap undo/redo, gallery thumbnail caching, fast picture drag, and background saving of received images.
- SSTV QSO reports display RSV; ADIF exports standard RST_SENT and RST_RCVD field names.

- v1.62 gallery hotfix: selecting a thumbnail no longer duplicates its entry; right-click now opens a context menu.
- Gallery/History adds image import, export and delete (including a confirmation prompt).
- RX and TX use the same MMSSTV-style orange/black AF/IF reference with numeric 1200/1500/1900/2300 Hz labels only; RX retains a live trace within the lower strip.

- v1.62 replacement build: RX mode dropdown now includes all 43 TX profile names (Robot/PD remain native auto-decoded).
- TX editor has separate MY/DX callsign and custom caption layers, independent font colors/sizes and mouse dragging.
- Existing user templates can be deleted; saved layouts persist per-layer positions, color, size and callsigns without breaking older template JSON.

- New dockable SSTV Console: receive, transmit, image gallery, UTC QSO log and ADIF export.
- 43 transmit modes with standard and extended VIS/N-VIS, selectable WASAPI audio output, station ID, PNG/template editing and 48-kHz mono WAV export.
- AF capture, RS-BA1/Windows loopback and 12-kHz IF SSTV reception; VIS AFC, manual fine tuning and slant correction.
- Satellite uplink CAT/PTT uses current Doppler frequency during a picture, preserving STOP across an in-flight CAT request. Esc stops the SSTV operation.
- Virtual IC-9700 CAT TCP simulator exercises key/stop, interrupted PTT ACK and reconnect; a five-minute virtual satellite pass exercises Doppler updates.
- Corrected MC110-N scan timing and optimized cached CAT-state sampling and SSTV STOP feedback.

## 中文 — 相对 v.1.61 的变化

- 修复 AFC FM 频偏测量：连续 IF 音频块使用重复时间戳时不再清空测量缓冲，避免出现频谱正常但无频率数据。
- 未测量不再显示为真实零频偏，低置信度 FM IF 估计以“候选”形式显示，同时保持 CAT 自动修正限制。
- 添加 +820 Hz FM 偏移、重复时间戳回归测试和测量链路状态诊断。

- 正式版 v1.62 更新：最多八个独立 SSTV 图层，支持添加、复制、删除、拖动、文字颜色/大小及模板持久化。
- 增加呼号、RSV、QTH、姓名、备注、UTC 时间和上下行 MHz 变量的选择与插入，兼容旧 RST 宏。
- 优化大图加载、撤销重做内存、图库缩略图缓存、拖动渲染与接收图片异步保存。
- SSTV 通联报告界面改为 RSV，ADIF 继续使用标准 RST_SENT/RST_RCVD 字段。

- v1.62 图库修复：选中已有缩略图不再重复添加，右键弹出操作菜单。
- 图库／历史增加导入、导出与删除图片功能（删除需确认）。
- RX/TX 采用同一橙黑色 MMSSTV 音调刻度，只有 1200、1500、1900、2300 Hz 数字，RX 下方黑色区显示 AF/IF 活动。

- v1.62 替换构建：RX 模式下拉框补全全部 43 种 TX 制式，Robot/PD 仍使用原生自动 VIS 解码。
- TX 独立己方呼号、对方呼号及自由文字三图层，支持分别设置字体大小、颜色和鼠标拖动定位。
- 增加自定义模板删除，保存呼号和每层位置、字号、颜色，兼容已有模板文件。

- 新增可停靠 SSTV 控制台，包含接收、发射、图库、UTC 通联日志和 ADIF 导出。
- 支持 43 种 SSTV 图片发射模式、标准与扩展 VIS/N-VIS、指定 WASAPI 发射声卡、台站识别、图片模板编辑及 48 kHz 单声道 WAV 导出。
- 提供 AF 声卡采集、RS-BA1/Windows 回环、12 kHz IF 接收，支持 VIS 自动校频、手动微调与倾斜校正。
- 卫星 CAT/PTT 发射期间跟随当前多普勒频率，修复 CAT 指令尚未返回时 STOP 丢失的问题；Esc 可停止 SSTV。
- 增加虚拟 IC-9700 TCP CAT 测试，覆盖按键/停止、PTT 应答丢失与重新连接，并进行五分钟多普勒模拟。
- 修正 MC110-N 行扫描时间，降低冗余缓存状态检查频率，优化停止操作反馈。

## Release process / 发布流程

源代码版本为 `Directory.Build.props` 中的 `1.62`；使用 Windows x64 自动构建、测试及安装包发布流程。

---

# SkyRoof Fork v.1.61 / SkyRoof Fork 1.61 版本说明

Release date / 发布日期: 2026-10-31. Corrective AFC calibration release following v.1.60.

## English — changes since v.1.60

- Corrected CAT request/readback attribution: a command is never treated as acknowledged by a readback captured before it, and each calibration phase is independently fenced.
- Made the IF timing explicit: the FM estimator averages a 2-second window, so calibration now evaluates settled measurements after the real CAT request time instead of treating the window center as a new command timestamp.
- Updated the conservative RX-only probe to 150 Hz for FM (40 Hz for CW/SSB pilot), requiring two stable IF samples on each of the probe and restore movements; the same measured direction must be verified on return.
- Added more precise calibration failure details, bounded deadlines and a brief hold tolerance for momentary FM reference instability. PTT and lost tracking continue to abort and restore the temporary RX probe.
- The model's predicted RX Doppler rate is subtracted from observed CAT movements, so the probe is not falsely rejected while a satellite is moving. No TX, persistent transponder correction or orbit/ephemeris changes. IC-9700 real-radio validation is still required.

## 中文 — 相对 v.1.60 的变化

- 修正 CAT 写频请求与真实读回的时间归因，杜绝用下发之前的读回确认写频；每一阶段只接受该阶段下发后的硬件反馈。
- 考虑 FM 频偏算法约 2 秒 IF 测量窗口，仅在命令生效后获得稳定 IF 观测，才判断调谐方向。
- FM 采用 150 Hz 临时 RX 探针，CW/SSB 导频为 40 Hz；一次调谐和返回须各有两次稳定 IF 观测及真实 CAT 读回，完成返回验证后才启动闭环修正。
- 增加失败原因、阶段超时诊断和短暂失号保持，不放宽 PTT 安全保护。所有校准只作用于 RX 临时修正，不保存频偏、不改 TX。
- 针对卫星运行时 CAT 正常跟踪多普勒的情况，在方向校准判断中扣除模型给出的预期频率变化，不再把正常多普勒位移作为探针误差。
- IC-9700 实机时序及 AFC 收敛仍需现场复测。

## Release process / 发布流程

Release version is `1.61` in `Directory.Build.props`. A successful Windows build, xUnit regression suite and installer payload verification are required before merging. Unsigned artifacts remain pre-releases.

---

# SkyRoof Fork v.1.60 / SkyRoof Fork 1.60 版本说明

Release date / 发布日期: 2026-10-30. Based on VE3NEA/SkyRoof v.1.56; AFC stabilization release.

## English — changes since v.1.59

- Fixed AFC's zero-correction lock by decoupling live RX CAT readback from Ignore Dial Knob; operator dial feedback remains disabled when requested.
- Added bounded, self-restoring RX-only CAT/IF polarity calibration (two probe/restore cycles) before automatic frequency writes when polarity is unknown.
- Prevented unverified Doppler-derived polarity from opening the AFC servo. Added PTT, stale IF, lost-lock and CAT timeout fail-safe handling and a deferred RX restoration path.
- Added CAT readback freshness and polarity-probe states to the bilingual AFC console, along with unit tests and updated operator documentation.
- No changes to TX frequency, persistent transponder/manual offsets, RIT/XIT, or the underlying satellite ephemeris model.
- Automated synthetic/CAT-state tests do not replace IC-9700 real-radio IF/CAT timing verification.

## 中文 — 相对 v.1.59 的变化

- 修复 AFC 信号已锁定但修正仍为 0 Hz 的控制前置条件：将 RX CAT 实际频率读回从 Ignore Dial Knob 功能中独立出来。
- 新增限时、可恢复的 RX-only RF/IF 方向自动标定，两组小幅调谐/回退均得到 CAT 读回和 IF 观测确认后，才开放自动 AFC 写频。
- 卫星多普勒引起的普通频率变化不再单独作为开放 AFC 写频的方向证明；保留 PTT、失号、超时和 CAT 异常保护，并补充延迟回退路径。
- AFC 中英界面新增 CAT 读回新鲜度和方向标定状态；加入回归测试与使用说明。
- 不修改 TX、永久转发器修正、手动频偏、RIT/XIT 和卫星轨道模型。
- 合成数据及 CAT 状态机通过测试不代表 IC-9700 实机完成验收，仍需 IF 与 CAT 联合测试。

## Release process / 发布流程

唯一版本来源：`Directory.Build.props` → `1.60`。合并至 master 后自动完成 Windows x64 构建、xUnit 测试与安装包发布；有签名能力时按原签名流程处理，否则依现有工作流发布未签名 pre-release。保留 v.1.59 以便回退。

---

# SkyRoof Fork v.1.59 / SkyRoof Fork 1.59 版本说明

Release date / 发布日期: 2026-10-30. Source: `Iu-yang1/SkyRoof`, based on VE3NEA/SkyRoof v.1.56.

## English — changes since v.1.58

- The IC-9700 LAN spectrum toolbar now uses compact English operator buttons: START/STOP, CLEAR, SYNC and SETS, while retaining REF, VBW, AVG, SMOOTH and ZOOM.
- Initial bilingual Settings PropertyGrid interface, with an in-dialog 简体中文 / English selector. Language is saved to `UiSettings.Language` and does not change radio parameters or JSON field names.
- AFC Live Tracking, control states, diagnosis messages and Advanced Settings labels support Chinese/English presentation; technical details and numerical settings keep their original semantics.
- Includes recent responsive spectrum-toolbar and DPI improvements, and safe reusable MORE context-menu lifecycle handling.
- Localized names and core controls are covered first; less common technical setting descriptions retain their original text until reviewed.
- No change to CAT protocol, Doppler logic, IF demodulation or AFC correction algorithms.

## 中文 — 相对 v.1.58 的变化

- IC-9700 LAN 频谱工具栏采用紧凑英文操作按钮：START/STOP、CLEAR、SYNC、SETS，保留 REF、VBW、AVG、SMOOTH、ZOOM。
- 设置窗口增加简体中文/English 语言选择，首批汉化设置分类、常用设置项和参数标签；语言设置可保存，不更改 JSON 字段或电台控制配置。
- AFC 实时追踪页、控制状态、诊断说明和高级设置属性支持中英双语呈现。
- 包含近期 DPI 响应式两行工具栏及 MORE 菜单生命周期异常修复。
- 较少使用的技术设置说明文字仍逐步完善，本版不宣称全部设置说明均已完成翻译。
- 不修改 CAT 协议、多普勒算法、IF 解调或 AFC 频率控制算法。

## Release process / 发布流程

唯一版本来源为 `Directory.Build.props` → `1.59`。合并至 master 后，Build and Release 自动完成 Windows x64 编译、测试、Inno Setup 安装包和 `v.1.59` Release。
如未配置 SignPath，依照项目现有策略发布未签名的 **pre-release**，不得标为已签名正式版。

---

# SkyRoof Fork v.1.58 / SkyRoof Fork 1.58 版本说明

Release date / 发布日期: 2026-10-24. Source: `Iu-yang1/SkyRoof`, based on VE3NEA/SkyRoof v.1.56.

## English — changes since v.1.57

- RC-3040S native LAN control: streamlined quick AZ/EL speed controls, device readback, and motion settings with Direct, Precision stop, Trapezoidal, and S-curve software reference planners.
- Speed configuration is sent only after explicit user approval. Removed the cumbersome typed confirmation dialog, retaining a single Yes/No safety confirmation.
- Reduced management TCP/60030 connection churn: one bounded WebSocket session per speed save (C2 preflight → a single AT command → fresh C2 readback), device identity pinning, and no implicit write retries.
- Fixed the motor-control state handling after a successful speed readback: recovery is conditional on fresh independent TCP/60020 position telemetry. Explicitly distinguish physical feedback loss, management safety lock, and ready status.
- Cleaned up obsolete RC-3040S DEMO UI/settings, reorganized motion settings and button layout, and simplified hardware-control wording.
- Preserve CW Console, IC-9700 LAN spectrum, Doppler/CAT/transponder workflow and previous v.1.57 improvements.
- The reported raw speed setting may not represent calibrated mechanical angular speed; firmware EEPROM retention and physical S-curve compliance have not been independently proven. The application remains safety-conservative.

## 中文 — 相对 v.1.57 的变化

- 完善 RC-3040S 原生 LAN 控制：AZ/EL 速度快捷控制、配置读回，以及 Direct、Precision stop、Trapezoidal、S-curve 四种软件参考轨迹规划模式。
- 去除输入指定文字的二次确认框，保留一次 Yes/No 安全确认，绝不后台自动写入 AT 速度参数。
- 优化 TCP/60030 WebSocket 管理流程：每次保存使用单个会话完成读取、一次性写入及写后重新查询，核对设备身份，不自动重发配置命令。
- 修复速度保存后无条件锁定云台导致状态栏误显示为断连的问题；需取得 TCP/60020 独立的新位置反馈后方可恢复，安全锁与真实通信断开明确区分。
- 删除无用 DEMO 入口及设置，整理 Motion Settings 布局及界面提示。
- 保留 CW Pileup、IC-9700 LAN 频谱、卫星频率/多普勒及上版功能。
- 速度显示及配置回读不等于实测机械速度，也不代表 EEPROM 断电保持或固件实际执行了 S 曲线；真实运动安全仍应实机确认。

## Release process / 发布流程

Version source of truth: `Directory.Build.props` → `1.58`.
The release workflow triggers on this file's change on `master`, validates release invariants,
builds and tests Windows x64, packages the installer, and publishes `v.1.58`.
Unsigned builds are published as **pre-releases**; a signed release requires valid SignPath configuration.

---

# SkyRoof Fork v.1.57 / SkyRoof Fork 1.57 版本说明

Release source: `Iu-yang1/SkyRoof`; upstream baseline: `VE3NEA/SkyRoof` v.1.56 (`bc7befff64fb0dc8e83d207259313e27b834b92f`).

## English

- Integrated the upstream v.1.56 changes: AO-123 SSDV decoding, SSTV auto-save filter selection (None / Wiener / NLM), and refreshed vendored telemetry/DSP libraries.
- Retained the fork's satellite operations workflow: SkyCAT/RS-BA1 CAT integration, IC-9700 LAN spectrum and passband UI, manual transponder records, Doppler-aware frequency control, and rotator control.
- Includes the recent CW Console / Pileup receive and transmit workflow, eight-lane UI/decoder continuity improvements, and removal of obsolete FPS diagnostics (PR #77).
- Preserved the fork's GitHub/light/pink-blue themes and bilingual extension guides.
- Unsigned builds are published as **pre-releases**. Signed releases require the SignPath repository secret and a passing Windows release workflow.

## 中文

- 合并上游 v.1.56：AO-123 SSDV 图像解码、SSTV 自动保存滤波器选择（None / Wiener / NLM）及遥测/DSP 依赖更新。
- 保留 Fork 的 SkyCAT/RS-BA1 CAT 控制、IC-9700 LAN 频谱、手动转发器、多普勒频率控制和旋转器控制。
- 包含最近的 CW Console / Pileup、八路槽位显示与解码连续性修复，并删除旧 FPS 诊断功能（PR #77）。
- 保留 GitHub 明暗主题、粉蓝白主题与中英文扩展功能文档。
- 未签名安装包仅以 **预发布版** 发布；稳定签名发布需要配置 SignPath 凭据并通过 Windows Release 工作流。

## Release process / 发布流程

Version source of truth: `Directory.Build.props` -> `1.57`.
Merge this release branch after CI validation. The `Build and Release` workflow is automatically triggered only when `Directory.Build.props` changes on `master`, or via `workflow_dispatch`.
It builds and tests the application, packages the installer, then publishes the `v.1.57` tag and GitHub Release. Do not create the tag manually before the workflow.

版本唯一来源：`Directory.Build.props`。合并前检查 CI；合并后自动触发安装包构建、测试和 Release。不要提前手动创建同名标签。
