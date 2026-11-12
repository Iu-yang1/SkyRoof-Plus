# SkyRoof-Plus v.1.71

SkyRoof Plus 1.71 — **Unified Orekit/IKVM satellite and celestial ephemeris engine**.  
正式迭代版：**Orekit/IKVM 统一轨道、月地计算与数据管理**。

## 中文

### 主要更新

- **统一数值引擎：** 卫星 TLE/CCSDS OMM 统一走 Orekit 13.1.9 + IKVM，按 TLE 适用的 SGP4/SDP4 算法计算。不再在运行异常时悄悄回退至 SGP.NET 传播引擎；旧版配置中的后端选择迁移至 Orekit。
- **太阳系星历：** 月球、太阳、金星统一通过 Orekit `CelestialBodyFactory` 和 ITRF/WGS-84 `TopocentricFrame` 计算方位角、仰角、距离、径向距离变化率，删除原独立 JPL BSP 解析引擎。旧卫星 DTO/过境列表仍保留部分 SGP.NET 兼容类型，**但不再作为计算后备引擎**。
- **统一设置页：** 在「轨道与星历来源 → Orekit 星历与数据」配置自动下载、数据目录、IERS EOP 下载源、DE430 原生星历来源及太阳系目标显示。选中对应分组显示「更新 Orekit 星历」按钮；移除工具菜单中重复的 EOP 更新入口。中英双语及 JSON 设置兼容性已测试。
- **自动更新与校验：** 首次运行自动准备 UTC/TAI、IERS `finals2000A.all` 与 Orekit 原生 JPL DE430 `lnxp1990.430`，EOP 超过七天尝试更新；DE430 对照固定上游 Git Blob SHA-1 校验并记录 SHA-256，下载异常时保留缓存。**NAIF `.bsp` 不能直接代替 Orekit 原生文件**。
- **数据重载：** 因 Orekit 全局时间尺度与框架缓存，切换数据目录、更新 EOP/星历或调整太阳系目标选项后须重启 SkyRoof 生效。系统不会在运行中切换到另一数值引擎。

### 已通过的软件验收

- Windows .NET 10 x64 构建、xUnit 测试、Orekit/IKVM 安装后运行验证、固定版本 aislop 质量门禁。
- TLE/OMM、TEME/ITRF 变换、AOS/LOS 过境、站心 AZ/EL、距离和距离变化率、单程多普勒方向与 Vallado 参考数据回归。
- Orekit DE430 月球/太阳/金星冒烟测试通过。示例：2026-11-12 12:00 UTC、测试站点 28.5°N/115.9°E，月球距离 405,644,782 m、径向速度 +358.441 m/s，432.080 MHz **单程下行**一阶频移 −516.608 Hz，月球距离变化率与数值差分一致。
- GitHub Actions Windows 共享运行器的**预热中位数**参考：Orekit TEME 1000 次约 3.90 ms（SGP.NET 基线 0.62 ms）、Orekit 站心观测 1000 次约 118.25 ms、月球观测 200 次约 7.68 ms。此数据不能直接视为实时控制时延或实机运动误差。
- 本版本的正式发布工作流额外检查固定哈希 DE430 原生星历、安装器内部 Orekit/IKVM 依赖、已发布文件 SHA-256 和签名状态；详见对应 Release CI。

### 限制与签名

- Vallado 参考数值的一致性不代表真实轨道绝对定位精度；极端长期 TLE 外推和病态案例不保证常规精度。已有 IKVM 静态转换兼容性警告仍需要按具体 API 路径评估。
- **未做 IC-9700 / CAT / GS232B 旋转器实机射频与运动验收。** 不以软件模拟结果冒充硬件验证，正常功能不因此锁定。
- 按用户本次明确授权，此版本发布为**正式 Release（非 prerelease）**。如构建环境未配置 SignPath，安装包可能**未进行 Authenticode 数字签名**；此时以 GitHub Release 的来源、SHA-256 和发布工作流作为完整性检查依据，不声称已签名。后续其他版本仍遵循原签名/预发布策略。

## English

- **Single Orekit engine:** Orekit 13.1.9 via IKVM performs TLE/CCSDS OMM SGP4/SDP4 propagation and Moon/Sun/Venus ephemerides through CelestialBodyFactory, ITRF and WGS-84 topocentric observations; legacy runtime fallback to SGP.NET propagation and the standalone JPL BSP parser have been removed.
- **Unified settings and data:** Automatic UTC/TAI, IERS finals2000A.all and validated Orekit-native DE430 acquisition, configurable source URLs, seven-day EOP refresh, offline caching, data-health display, and a bilingual in-settings update action. Restart after changing ephemeris data or target visibility.
- **Verification:** Windows x64 builds, tests and installed IKVM runtime; Vallado TLE/OMM, pass prediction, azimuth/elevation and range-rate/Doppler consistency; DE430 lunar/solar/Venus observation; warmed throughput benchmarks and pinned aislop. See [PR #195](https://github.com/Iu-yang1/SkyRoof-Plus/pull/195) and [CI evidence](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43301778724).
- **Boundaries:** SGP.NET data-transfer type adapters remain for UI compatibility but do not provide an alternate numerical propagator. Vallado numerical agreement is not satellite ephemeris absolute accuracy; no real-radio or real-rotator field acceptance has been performed.
- **Signature status:** This version is explicitly authorized as a **stable GitHub Release**. If SignPath signing is unavailable, the installer is **unsigned**, not falsely described as Authenticode-signed; release provenance and SHA-256 remain checked. Future releases retain the existing signing/prerelease policy.

**Release validation evidence / 发布验收记录:** [PR #195](https://github.com/Iu-yang1/SkyRoof-Plus/pull/195) · [PR #196](https://github.com/Iu-yang1/SkyRoof-Plus/pull/196) · [Orekit build & tests](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43301778724) · [installed runtime](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43301768477) · [aislop](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43301778457).

Previous tag / 上一版本：[v.1.70](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.70) (retained unchanged).
