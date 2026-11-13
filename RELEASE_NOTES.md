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
