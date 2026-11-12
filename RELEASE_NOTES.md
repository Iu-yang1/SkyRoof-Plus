# SkyRoof-Plus v.1.70

SkyRoof Plus 1.70 — Orekit / IKVM orbital-engine integration and verified Windows installation. / SkyRoof Plus 1.70 — Orekit / IKVM 轨道引擎集成与 Windows 安装验证。

## 中文

- **可选轨道引擎：** 在轨道数据源设置选择 SGP.NET（默认）或 Orekit 13.1.9 + IKVM 8.16.3，显示实际运行的后端与 EOP 数据质量状态。切换会重建轨道与过境缓存；初始化失败或单条元素运行异常时保留 SGP.NET 计算路径。
- **轨道数据与观测：** CCSDS OMM 原始数值映射并保留六位 NORAD，新增 TEME/WGS-84 位置、AZ/EL、距离与范围速率接口，独立 AOS/LOS 与过境最高仰角计算；保留现有 JPL/NAIF BSP 天体计算和 CAT/Rotator 协议。
- **多普勒修正：** 修复既有 SGP.NET 1.6.0 站心范围速率未扣除地球自转速度的问题，统一改为卫星与地面站的相对速度投影。该修复会改变旧版本多普勒数值，特别是在高频段；不更改 SGP4 传播模型。
- **UTC 与 IERS：** 安装器包括 UTC–TAI 数据；可通过 Orbit/TLE 数据源菜单下载官方 IERS `finals2000A.all`，校验内容、记录 SHA-256/来源并在重启后使用。
- **可重复验证：** Windows .NET 10 x64 完整构建、1,400 项 xUnit、固定版本 aislop、33 组 Vallado SGP4/SDP4 参考案例（667 条数据中 666 条成功传播，1 条预期故障）、IKVM 全运行时镜像安装、安装后独立 TLE/OMM 传播验收。28 组常规参考案例相对 Vallado 的 TEME 最大位置误差约 0.00198 m、速度误差约 2.27×10⁻⁶ m/s。
- **中英双语操作指南：** 后端选择、数据更新、已知精度边界和回退路径，见 `docs/users_guide/orekit-orbit-engine.md` 及 `docs/zh-cn/users_guide/orekit-orbit-engine.md`。

### 已知限制与验证范围

- SGP4/SDP4 相对 Vallado 参考输出的数值精度**不等同于真实轨道绝对定位精度**。5 组特殊压力/错误码/超长外推案例单独保留差异报告：极端长期 TLE 外推可能出现约 **27,924 km** 的参考差异，不应据此宣称所有场景达到亚米精度。
- IKVM 静态转换存在 `IKVM0117` / `CharBuffer.rewind()` 兼容性警告；已经测试的时间、TLE/OMM、ITRF/站心路径可运行，但不宣称所有 Orekit Java 路径均兼容。
- Moon/Sun/Venus 仍使用原 JPL BSP，尚未实现 Orekit 对应天体独立替代、CCSDS OEM/AEM 或真实姿态/极化计算。
- 已通过软件和干净安装包测试，**尚未使用真实 IC-9700/CAT/云台执行射频或运动实机验收**。功能不会因此被禁用，使用者应按设备实际状态进行验证。
- 如果本项目未配置 SignPath/Authenticode 签名，Release 工作流会将安装包标为**未签名预发布版**，不会冒称已签名正式版。

## English

- **Selectable orbit engine:** Keep SGP.NET as default or enable Orekit 13.1.9 via IKVM 8.16.3; show the active backend and IERS EOP data quality. Rebuild orbit caches when switching, with transparent fallback to existing SGP.NET calculations on a backend failure.
- **Orbit data and geometry:** Native-value CCSDS OMM with 6-digit NORAD IDs; TEME and WGS-84 positions, azimuth/elevation, range/range rate and actual AOS/LOS pass-boundary recovery. Existing JPL BSP celestial calculations and CAT/rotator protocols remain unchanged.
- **Doppler:** Correct the historical SGP.NET 1.6.0 line-of-sight velocity calculation by subtracting the ground station's rotation velocity; SGP4 orbital propagation is unchanged. This intentionally changes the numeric Doppler result compared with older SkyRoof releases.
- **Earth orientation:** Bundle UTC/TAI history, offer USNO/IERS `finals2000A.all` updates with source and hash records, and reload updated data on restart.
- **Evidence:** Windows x64/.NET 10 build, 1,400 xUnit tests, pinned aislop, all 33 Vallado diagnostic cases (666 valid propagation samples), 28 normal verification cases with strict 0.1 m / 0.001 m/s TEME limits, and a clean-install startup/propagation test with the complete IKVM runtime home image.
- **Limitations:** Extreme or pathological Vallado cases are still reported rather than falsely accepted, including long-horizon TLE extrapolation errors; an IKVM0117 ReaderBasedOpener warning remains. No real-radio or real-rotator certification has been performed. Unsigned builds are published as clearly identified prereleases.

**Technical evidence / 测试记录:** [Orekit PR #194](https://github.com/Iu-yang1/SkyRoof-Plus/pull/194) · [Windows installed runtime gate](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43285100812) · [Full compile check](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43285112291).

**Prior release:** [v.1.69](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.69). It is retained; this release must not overwrite or retarget the old tag.
