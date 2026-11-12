# Orekit + IKVM P0 代码实施记录

开发分支：`feature/orekit-ikvm-orbit-engine`。此文记录**源码提交状态**，不代表已通过实际 Windows 编译。

## 第一批代码

- `SkyRoof.Orbit.Orekit`：独立 .NET 10 项目，通过 IKVM 8.16.3 和 IKVM.Maven.Sdk 1.12.2 加载 `org.orekit:orekit:13.1.9` 的 Maven 依赖。
- `OrekitDataContext`：一次性加载独立数据目录。未提供 UTC 历史数据时抛出明确错误。
- `OrekitTleTrack`：TLE → TEME 位置/速度、UTC 转换、WGS-84 地面站 ITRF 方位角/仰角/距离/距离速率；输出为纯 .NET 数据类型。
- `SkyRoof.Orbit.Orekit.Smoke`：独立控制台烟测，固定 Vallado 卫星 00005 在 t=0、360 min 的 TEME 参考状态；验证 UTC/TAI 与时区处理；记录测量结果和加载程序集。
- `.github/workflows/orekit-ikvm-p0.yml`：Windows .NET 10 独立流水线，执行恢复、静态转换、构建、运行烟测和保存证据。

## 验证分界

- **已完成**：源代码和实验 CI 已添加到开发分支。
- **首轮 CI 发现并修复**：Orekit 13.1 的 UTC 偏移返回 `TimeOffset` 而非 `double`（CS0023）；同时发现 IKVM 8.15.0 的 `CharBuffer.rewind()` 兼容警告，因此显式升级为 IKVM 8.16.3。\n- **已通过的第一轮 Windows P0**：[Actions #43216033629](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43216033629)：IKVM 8.16.3 与 Orekit 13.1.9 完成 Maven 恢复、JAR→IL、Build、UTC/TAI、TLE/TEME、ITRF 观测和烟测（2026-11-12）。
- **参考案例**：Vallado 00005 t=0：TEME 位置误差 0.000009 m、速度误差 0.000000638 m/s；t=+360 min：0.000005 m、0.000000384 m/s。烟测增加 0.1 m、0.001 m/s 断言，防止后续未检测到的回归；这些是同算法参考向量差异，不代表真实轨道绝对误差。
- **时间数据**：UTC/TAI 2020 年 37 s；测试 `tai-utc.dat` SHA-256 = `96017d361fda96801547dc7846c25d4a529ad48128a2aa849962966ad5b95eeb`，已加入验证断言。没有完整 EOP。
- **P0 性能样本（仅一台 GitHub Windows runner）**：初始化 33.0946 ms；1000 次单星传播 3.5531 ms；进程工作集 122560512 B。性能结果只是一次样本，不能作为跨平台/跨机器基准。
- **实际加载程序集**：`org.orekit`、`hipparchus.core/geometry/ode/optim/stat/filtering/fitting`、`IKVM.CoreLib`、`IKVM.Java`、`IKVM.Runtime`。
- **未解决的兼容性告警**：即使升级 IKVM 8.16.3，仍有 `IKVM0117: java.nio.CharBuffer.rewind()Ljava.nio.CharBuffer;`，出现于 `org.orekit.data.DataSource$ReaderBasedOpener$1.read()`。当前数据路径的烟测可运行，但 ReaderBasedOpener 路径未经独立覆盖，不能视为完全兼容。
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

## P1 非生产接口（第一批）

- 新增 `SkyRoof.Orbit.Contracts` 无第三方依赖的 .NET 10 类库，将 `OrbitState`、`GroundStationSpec`、`OrbitObservation` 从 Orekit 工程中独立出来。
- 新增 `IOrbitBackend` / `IOrbitTrack`，首批提供 `CreateTleTrack`、`PredictTeme` 与 `Observe`；接口只接收/返回 .NET 值对象，不泄漏 Java、IKVM 或 SGPdotNET 类型。
- `OrekitOrbitBackend` 实现首个后端；烟测通过接口创建轨迹，并执行传播与观测。SGP.NET 适配、OMM、过境预测和与 `SatelliteTracker` 的连接尚未完成，不能把这批接口当作整个 P1 验收。
