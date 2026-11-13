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
