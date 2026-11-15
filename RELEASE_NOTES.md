# SkyRoof-Plus v.1.78

## SkyRoof Plus v1.78 — 2026-11-15 业余卫星通信专业中文术语优化

本次是继 v.1.77 汉化资源安装修复后，针对业余无线电操作语境的一次独立语言完善版本。**不改变通信、轨道、旋转器、音频与解码逻辑，也不覆盖 v.1.77。**

### 专业用语修订与实际页面

- **卫星载荷分类：** 原“卫星发射机”页面改为“卫星转发器与信标”，列表及新建/编辑本地条目同步调整。卫星分组筛选器仍严格区分“发射机（Transmitter）/收发信机（Transceiver）/转发器（Transponder）”，不将单向信标错误归类为双向通信转发器。保留 SatNOGS/JE9PEL 数据来源、UUID、上下行频率及 CAT 关联。
- **过境匹配：** 原“通联预约（QSO Scheduler）”改为“过境匹配”；其本质是计算本台与 DX 台的共同卫星可见窗口，不是预约服务。补齐详情窗口和过境列表中的共同可见时段（UTC/本地）、最长仰角、等待时间、地球静止轨道等中英文内容。避免将“共同可见”直接断言为“转发器已开机/可以完成 QSO”。
- **电台操作语汇：** “手动旋转器”改“旋转器手动控制”，“实时追踪”改“实时跟踪”，“位置异常”明确为位置反馈异常；键速标示为 WPM，频谱 REF、VBW、扫频帧平均等提示统一术语。Recorder 改为“音频与 I/Q 录制”以覆盖其实际音频和 IQ 功能。
- **数据记录和 SSTV：** 将日志 STATE 字段修订为“州 / 省”；SSTV 信号报告标签统一为“发出 RSV / 收到 RSV”（R：可读度、S：信号强度、V：图像质量）；原英文资源将 RSV 误写为 RST；本台/对方台标记与链路术语相统一。
- **用户交互补齐：** 补齐过境匹配列表自绘文字和详情、转发器与信标编辑、主 RX/TX 工具栏、Doppler/CAT/PTT/CTCSS 选项、频率基准/修正控制、卫星分组过境状态、卫星详情和天空/地球视图、自动选择计划、实体 PTT 绑定，以及 ONVIF 视频与设备设置。AMSAT 状态可显示中文，但向外部服务提交时仍使用原始英文 API 代码。语言切换只刷新文字和列表重绘，不重新创建 CAT/PTT、云台、SDR、遥测、录音会话或重置已选条目。
- 本轮修订 36 个现有中文译文，新增 214 组中英文对等资源键（中文资源总数从 453 增至 667）；继续运行资源键与格式化占位符一致性检查，以及术语分类回归测试。

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
