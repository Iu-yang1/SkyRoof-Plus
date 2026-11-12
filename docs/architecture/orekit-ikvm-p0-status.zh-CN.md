# Orekit + IKVM P0 代码实施记录

开发分支：`feature/orekit-ikvm-orbit-engine`。此文记录**源码提交状态**，不代表已通过实际 Windows 编译。

## 第一批代码

- `SkyRoof.Orbit.Orekit`：独立 .NET 10 项目，通过 IKVM.Maven.Sdk 1.12.2 加载 `org.orekit:orekit:13.1.9` 的 Maven 依赖。
- `OrekitDataContext`：一次性加载独立数据目录。未提供 UTC 历史数据时抛出明确错误。
- `OrekitTleTrack`：TLE → TEME 位置/速度、UTC 转换、WGS-84 地面站 ITRF 方位角/仰角/距离/距离速率；输出为纯 .NET 数据类型。
- `SkyRoof.Orbit.Orekit.Smoke`：独立控制台烟测，固定 Vallado 卫星 00005 在 t=0、360 min 的 TEME 参考状态；验证 UTC/TAI 与时区处理；记录测量结果和加载程序集。
- `.github/workflows/orekit-ikvm-p0.yml`：Windows .NET 10 独立流水线，执行恢复、静态转换、构建、运行烟测和保存证据。

## 验证分界

- **已完成**：源代码和实验 CI 已添加到开发分支。
- **尚待流水线验证**：NuGet/Maven 传递解析、IKVM 生成程序集、编译、实际运行、性能/体积。
- **尚未实施**：生产 NuGet/Maven 锁文件、独立 SGP.NET 第三路对比、EOP 正式资料包、P1 契约项目接入现有 SatelliteTracker、P2–P6。
- 本实验项目未加入 `SkyRoof.sln`，未改变默认 SGP.NET、现有 JPL BSP、CAT、Rotator 和 Release。生产链路仍采用原来的锁定依赖。
- 实验数据只包含 UTC-TAI 历史；没有独立 EOP 数据时的 ITRF 观测仅是连通性验证，质量标签为 `P0_UTC_ONLY_EOP_UNVERIFIED`，不能用作高精度或 CAT 控制验收。
- Vallado 参考案例来自 AIAA-2006-6753 `SGP4-VER.TLE`、`tcppver.out`。初始流水线只报告两组数值差异，**不会把互相接近宣称为精度验收通过**。
- `tai-utc.dat` 来源为 USNO 历史数据的公开镜像 `eSpace-epfl/orekit-data`；每次烟测生成 SHA-256 并写入报告。正式分发之前单独核验授权与更新状态。

## 后续顺序

1. 确认 Windows Runner 实际通过恢复、转换、启动；失败按真实日志调整 IKVM 依赖/调用签名。
2. 固定锁文件与数据 hash，记录程序集清单和许可证。
3. 增加原有 SGP.NET 第三路参考比对、更多 Vallado 深空样本和严格阈值。
4. 进入 P1 的独立 Contracts、SGP.NET/Orekit Adapter，再由 SatelliteTracker 兼容门面接管；不直接导出 Java 类型到 UI。
