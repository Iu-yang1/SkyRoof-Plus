# SkyRoof-Plus v.1.72

SkyRoof Plus 1.72 — responsive cache-first startup and deferred Orekit/IKVM data maintenance.  
SkyRoof Plus 1.72 — **主界面启动白屏与耗时加载修复版**。

## 中文

### 本版修复

- **优先显示主界面：** 修复 v1.71 在 `MainForm_Load` 中先等待 IERS `finals2000A.all` 和 JPL DE430 网络下载/校验，最后才恢复 DockPanel 工作区，导致顶部工具栏显示后中央长时间空白的问题。现在优先使用本地缓存初始化 Orekit，并恢复已保存的停靠窗口。
- **全量过境预测延后：** 首次卫星群组/业余卫星过境重建在主窗口 `Shown` 后触发，允许界面完成首次绘制，随后仍完整生成预计 AOS/LOS、最高仰角和过境列表。
- **后台更新星历：** 首屏显示之后再在后台线程运行 IERS EOP/DE430 下载、文件校验与缓存管理；自动更新设置、原生 Orekit 引擎、月球/太阳/金星星历、多普勒及旋转器协议均不取消、不降级。
- **手动/自动下载事务串行化：** 统一 Orekit 缓存的自动刷新与设置页「更新 Orekit 星历」按钮使用下载互斥机制，防止同时改写同一临时文件。
- **卫星数据库周更新：** 对已有本地数据库，保留每周 SatNOGS 列表更新的提示，但在主界面完成第一帧后检查，避免布局尚未恢复时出现网络等待；也避免等待 EOP 网络超时数分钟后才弹出数据库刷新对话框。
- **首次安装：** 完全没有卫星数据库缓存时，仍需要完成首次 SatNOGS 列表下载/导入才能生成可用卫星列表。这属于数据依赖，而非人为功能锁定。
- **状态、诊断与精度：** 记录初始 DockPanel 布局恢复耗时。Orekit 使用已安装的 EOP 进行当前会话计算，新下载或刷新的 EOP 因进程全局时间尺度/坐标框架缓存需要重启后生效；在状态提示和日志中给出说明。第一次下载 DE430 后尝试立即添加 Moon/Sun/Venus，如果运行时还未发现新文件则重启。
  
### 验证

- [PR #198](https://github.com/Iu-yang1/SkyRoof-Plus/pull/198) 已通过 Windows .NET 10 x64 编译与测试、安装包内 Orekit/IKVM 运行验证、固定版本 `aislop` 质量检查、DocFX 中英文文档验证，已合并至 master。
- 保持 v1.71 的 Orbital backend、TLE/OMM、AZ/EL、距离变化率、单程多普勒和 CAT/Rotator 计算接口不变。本次修复仅修改启动时序与下载并发机制。
- **启动速度尚未在用户 Windows 设备上实测**，不能把源码调度优化或 CI 构建成功称为已实测的具体启动时间。可在 Serilog 日志中查看 `Initial SkyRoof docking layout restored in ... ms` 并比较修复前后。

### 发布说明

- 由用户授权的正式 GitHub Release；保留 `v.1.71` 原有标签和安装包，另建 `v.1.72`。
- 如果当前 CI 缺少 SignPath，则安装包 **Authenticode 未签名**。最终以发布后的工作流记录、Release 目标提交、SHA-256 核验结果及签名状态为准；不虚构签名或真实硬件测试结果。
- **尚无 IC-9700/CAT/GS232B 实机和真实启动耗时验收**，首次运行应检查实际 EOP 数据质量和卫星列表状态。

## English

- **Cache-first UI:** Restore locally cached satellite data and docking layout before fetching IERS EOP and Orekit-native DE430, removing network latency from the v1.71 blank central-workspace startup path.
- **Deferred pass rebuild:** Build initial satellite passes after the first visible docking frame; pass calculations, tracking, Doppler and rotator protocols remain enabled and numerically unchanged.
- **Non-blocking ephemeris update:** Refresh/cache/download Orekit files on a background worker; serialize automatic and manual updates to the shared staging paths. Newly downloaded EOP requires a restart because Orekit caches its time scales and frames.
- **SatNOGS data:** Preserve the initial import on a clean installation; with existing cached data, the weekly refresh occurs only after initial UI presentation.
- **Verification:** PR #198 Windows x64 compile/unit/integrity tests, Orekit/IKVM packaged runtime smoke, `aislop`, bilingual DocFX passed. No actual user-device first-frame benchmark or connected CAT/rotator hardware acceptance is claimed.
- **Authenticode:** User-authorized stable patch release; may be unsigned if SignPath credentials are absent, disclosed transparently with downloaded asset SHA-256 verification.

**Technical trace:** [PR #198](https://github.com/Iu-yang1/SkyRoof-Plus/pull/198) · [CI checks](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43318521845).

**Previous release:** [v.1.71](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.71), retained without replacement.
