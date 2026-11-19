# Icom LAN Scope 渲染性能审计与实施 V1

基线：eb71fe6e5e45145d921b557aff41891d2d86adcc。仅针对实际频谱画面响应、绘制成本、测量透明度；不降低原始 475 点采样、扫频速度、瀑布历史或其他解码功能。

## 已确认问题

- CI-V 27 00 单包或多分片组帧经 UI mailbox 交给 WinForms；多分片可产生多次显示更新，但不等于多个独立完整 sweep。
- 原 DisplayFps 由 ScopeUpdateCount 算出，不是 OnPaint 或 DWM 呈现。WaterfallDeliveryFps 是交付数据行，不是实际显示刷新。
- GDI+ OnPaint 原先每次分配两个采样数组、平滑数组和折线 List/ToArray，同时重绘背景、波形、文字与瀑布。
- Doppler 跨 bin 会移位整个历史并在 UI 线程重新颜色映射。800 行为 380000 个原始样本；原先每个像素执行浮点亮度与自适应映射。
- Center 模式中心频率的持续变化不改变工具栏的半跨度，却触发完整控制同步。
- CW 的 FFT/HamNoise 已在后台线程，但 GDI+ 画面和 PCM 触发的 BeginInvoke 仍与主频谱共享 UI；遥测 mailbox 每批至多 24 个事件或约 5 ms，单事件与连续重投递仍可能产生竞争。

## Phase 1

- 同一 mailbox 批次仅一次 Invalidate，完整 sweep 的数据仍依序进入历史；中心频率变化不重复更新工具栏。
- 复用画图采样及折线缓冲，去掉重复背景填充；平滑算法及频率映射不变。
- 161 级颜色 LUT 代替逐像素浮点映射；隐藏瀑布保留数据但延期位图重建。
- Diagnostics 单独记录实际有新数据的 OnPaint 完成率、p95/p99 绘制墙钟耗时、帧间隔 p95/p99/max、组帧后画面年龄。不是 DWM Present 或屏幕光学测量。
- 测试覆盖 LUT 等价、原有平滑行为与帧时间统计。等待 Windows CI。

## Phase 2/3 — 尚未实施

1. 实测基线和 phase 1：固定同一源/跨度/速度，空载、CW、遥测及两者同时开启；静止和多普勒跟踪，1080p / 4K，比较真实完整 sweep 交付、新数据绘制率、帧间隔、页面响应与 CPU/GC。
2. 将平均、峰值、频移历史映射、瀑布颜色纹理准备移出主 UI；明确帧序、保留有效历史、无阻塞发布及设备切换代次，避免给 DSP 增加无界队列。
3. 优先考虑已有 SharpGL/OpenGL 基础设施的 GPU 环形纹理和曲线；如 Windows D3D/Direct2D 更符合驱动及独立渲染线程需求，则先作对照原型。保留 GDI+ 兼容后端。
4. 原始 475 点保持不变；缩小采用峰值保持，放大使用亚像素坐标；可选时间插值只作用于显示且不能虚报独立采样帧。避免新增高开销插值或默认改变弱信号峰值。
5. 接入 PresentMon/ETW 或等效工具验证真实呈现、CPU 与 GPU 时间。目标在原始完整 sweep 足够时 p95 帧间隔不高于约 40 ms（30 Hz），p99 不高于约 67 ms，且没有长时间冻结。与 RS-BA1 的比较需同一电台、相同 span/speed、分辨率及记录源；没有 RS-BA1 实测不宣称超过。

不自动发布版本；现有射频/CAT、音频接收、遥测归档行为保持不变。

## Phase 2：实际接入（开发分支）

- 采集端在 session router 内选择 MAIN/SUB/AUTO、检查 HOLD 与代次，直接投递后台 scope worker。独立 UI mailbox 只处理 CAT/工具栏确认；UI 阻塞不会再直接丢弃完整的原始扫描行。过载时后台有界队列优先丢 partial，完整 sweep 丢失单独计数。
- 后台线程独占 AVG、PEAK、噪声底、原始 ring、频移计算和 GDI+ 回退颜色准备；发布不可变快照，UI 无需持有数据处理锁。
- 频移由逐行 offset/valid range 表示；反向调谐不会恢复已裁掉的样本。475 点原始数据不重采样。GDI+ 只复制变更的颜色行。
- Settings > Icom LAN Spectrum > GPU waterfall (experimental) 提供独立 native OpenGL 3.3 专用呈现线程；默认 false，保留 GDI+ 兼容路径。GPU 直接读取原始 475 点环形纹理、每行元数据及 161×161 LUT；正常 sweep 单行增量上传，Doppler 只更新元数据，颜色参数变化更新 LUT。GPU 显示压缩时保峰值，放大时线性插值；并不增大 RF 分辨率。
- GPU 上下文创建或绘制失败触发日志及 GDI+ 自动回退。Native HWND 由 WinForms 管理；上下文、纹理、交换缓冲由呈现线程管理；在 HWND teardown 前释放上下文。当前曲线、交互和文字仍为 GDI+，尚未完成全图 GPU 化。GPU buffer swap 计数不是 DWM/光学显示证明。
- 已新增模拟完整扫描/partial、Doppler 换向边缘裁剪、平均与峰值、CPU/GPU LUT 等价和 UI 不消费时仍处理全部 sweep 的回归。Windows GPU 实际呈现、RS-BA1 A/B 及并发 CW/遥测端到端性能仍需实际运行验证。

## Phase 3：独立整图 GPU 呈现（开发分支）

同一个 native OpenGL surface 贯穿频谱 plot 与瀑布图；Win32 region 留出原有轴文字和分隔条。GPU 线程使用原始 475 点 trace/peak 纹理及固定四边形 VBO，通过亚像素线段距离场抗锯齿，压缩时保峰值；网格、RX/TX 参考、估计通带阴影、游标线及小型文字纹理均在独立线程呈现。文字坐标保留既有频率映射及调谐手势冻结几何。新数据无需等待 UI paint 即可触发 GL swap；UI 忙时轴标签或布局更新可能暂缓。设置界面名称为 GPU spectrum (experimental)，仍默认关闭并自动回退。真实显卡、docking/焦点交互尚待验证。

## v1.87 默认后端及模拟验收

GPU 默认开启（旧配置未含该字段时也为 true；显式 false 保留）。R8 纹理保留 475 点原值，DWORD 对齐每行 476 字节；普通扫频上传变化行和必要元数据，频移不搬移旧原始采样。GDI+ 使用固定像素缓冲区及 SourceCopy 环形绘制。`IcomScopePerformanceAcceptanceTests` 测试实际 router/worker、实际 4K 离屏 GDI+ 瀑布绘制及无显卡依赖的 GPU 上传准备模拟；以 30/60 Hz 定时输入、800 行历史、两个合成计算负载线程、连续多普勒及换向检验扫描完整率、P95/P99 间隔。GPU shader 执行和 Windows 实际呈现未包含在模拟指标内。

第一轮 4K 离屏 GDI+ 双线性缩放仅 3.58 FPS（150 扫描全部处理但只呈现 19 帧），不满足验收。以原生 StretchDIBits COLORONCOLOR 直接绘制固定 top-down DIB 两段环形历史，保留源像素，避免 GDI+ 高成本软件插值；DrawImage 最近邻作为兼容路径。增加环形顶部、源频率方向与像素回读测试；重新执行同一 30 Hz 4K 性能阈值。

## v1.87 原生 GPU 窗口闪烁修复（待 Windows CI / 用户实测）

用户报告 GPU 频谱及瀑布持续闪烁。原本 worker 每次发布快照都触发父窗口整窗 Invalidate + Graphics.Clear，而子 HWND 的独立 WGL 线程同时交换缓冲；原实现未显式隔离父缓冲 blit 的子区域。修复通过 WS_CLIPCHILDREN / WS_CLIPSIBLINGS、精确父区域裁剪、仅失效 chrome、显式 WM_ERASEBKGND 消费及 GL_BACK 绘制建立唯一像素所有者。WGL 上下文与 DC 在绘制/交换前核对。测试在 Windows 上验证 parent/child styles、150 次模拟父区域重绘不覆盖 plot/waterfall 像素及瀑布隐藏时的裁剪。该测试不代表物理 GPU / DWM 闪烁已实测消除。

## 录屏复核：GPU 与 GDI+ 共用频谱数据闪烁（v1.87）

11.262 秒、278 帧录屏，AVG 4、CENTER、25 kHz。第 50–53 帧曲线从原始噪声起伏逐渐变平，第 54 帧中心频率从 436.792911 MHz 变至 436.792909 MHz 后再次变粗。每次 1–2 Hz 变化远小于 25000/474≈52.74 Hz 的 bin 间隔。源码 AdvanceGeometry 原先每次 LowerFrequencyHz 变化都 ResetAverage，而且整数 bin 平移也清空 AVG。结果是周期性 AVG 4→1→2→3→4；无需图形驱动即可复现。保持亚 bin 平均历史，整数 bin 平移按有效 RF 区间移动每条历史，缺测边缘不计入除数，真实模式/跨度/大频移仍按原规则重置。瀑布各历史行保留采集时的 Floor，避免频移和软件后端重新栅格化时统一套用最新噪声底，亮度/对比度/调色板手动控制仍生效。视频未证明旧数据序列倒退，也不能单独证明物理 GPU 绘图无闪烁；PR #252 的父子窗口隔离作为独立修复保留。
