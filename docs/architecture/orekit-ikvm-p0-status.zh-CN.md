# Orekit + IKVM P0 代码实施记录

开发分支：`feature/orekit-ikvm-orbit-engine`。此文记录源码、独立 Windows 编译/运行结果及未完成项目；不能等同于正式 SkyRoof 集成验收。

## 第一批代码

- `SkyRoof.Orbit.Orekit`：独立 .NET 10 项目，通过 IKVM 8.16.3 和 IKVM.Maven.Sdk 1.12.2 加载 `org.orekit:orekit:13.1.9` 的 Maven 依赖。
- `OrekitDataContext`：一次性加载独立数据目录。未提供 UTC 历史数据时抛出明确错误。
- `OrekitTleTrack`：TLE → TEME 位置/速度、UTC 转换、WGS-84 地面站 ITRF 方位角/仰角/距离/距离速率；输出为纯 .NET 数据类型。
- `SkyRoof.Orbit.Orekit.Smoke`：独立控制台烟测，固定 Vallado 卫星 00005 在 t=0、360 min 的 TEME 参考状态；验证 UTC/TAI 与时区处理；记录测量结果和加载程序集。
- `.github/workflows/orekit-ikvm-p0.yml`：Windows .NET 10 独立流水线，执行恢复、静态转换、构建、运行烟测和保存证据。

## 验证分界

- **已完成**：源代码和实验 CI 已添加到开发分支。
- **首轮 CI 发现并修复**：Orekit 13.1 的 UTC 偏移返回 `TimeOffset` 而非 `double`（CS0023）；同时发现 IKVM 8.15.0 的 `CharBuffer.rewind()` 兼容警告，因此显式升级为 IKVM 8.16.3。
- **已通过的第一轮 Windows P0**：[Actions #43216033629](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/43216033629)：IKVM 8.16.3 与 Orekit 13.1.9 完成 Maven 恢复、JAR→IL、Build、UTC/TAI、TLE/TEME、ITRF 观测和烟测（2026-11-12）。
- **参考案例**：Vallado 00005 t=0：TEME 位置误差 0.000009 m、速度误差 0.000000638 m/s；t=+360 min：0.000005 m、0.000000384 m/s。烟测增加 0.1 m、0.001 m/s 断言，防止后续未检测到的回归；这些是同算法参考向量差异，不代表真实轨道绝对误差。
- **时间数据**：UTC/TAI 2020 年 37 s；测试 `tai-utc.dat` SHA-256 = `96017d361fda96801547dc7846c25d4a529ad48128a2aa849962966ad5b95eeb`，已加入验证断言。没有完整 EOP。
- **P0 性能样本（仅一台 GitHub Windows runner）**：初始化 33.0946 ms；1000 次单星传播 3.5531 ms；进程工作集 122560512 B。性能结果只是一次样本，不能作为跨平台/跨机器基准。
- **实际加载程序集**：`org.orekit`、`hipparchus.core/geometry/ode/optim/stat/filtering/fitting`、`IKVM.CoreLib`、`IKVM.Java`、`IKVM.Runtime`。
- **未解决的兼容性告警**：即使升级 IKVM 8.16.3，仍有 `IKVM0117: java.nio.CharBuffer.rewind()Ljava.nio.CharBuffer;`，出现于 `org.orekit.data.DataSource$ReaderBasedOpener$1.read()`。当前数据路径的烟测可运行，但 ReaderBasedOpener 路径未经独立覆盖，不能视为完全兼容。
- **尚未实施**：生产 NuGet/Maven 锁文件、更多 SGP4/SDP4 独立样本、EOP 正式资料包、P1 契约项目接入现有 SatelliteTracker、P2–P6。
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

## P1 SGP.NET 参考后端（第二批）

- 新增独立 `SkyRoof.Orbit.SgpNet` 项目并固定 `SGP.NET 1.6.0`；`SgpNetOrbitBackend` 实现同一份 `IOrbitBackend/IOrbitTrack` 契约。
- 将 SGP.NET ECI 位置/速度的 km、km/s 转换为合同规定的 m、m/s；地面站海拔高度米转 km，距离及范围速率统一 SI 单位；不泄漏 `SGPdotNET.*` 类型。
- P0/P1 烟测现有 Vallado 00005 参考解 + Orekit + SGP.NET 三路输出，另外输出两后端站心观测。不以两后端彼此一致代替独立精度验证，也不把地球自转模型差异误认为纯传播误差。
- **上述新增代码仍需本次提交后的独立 CI 验证**；暂不接线真实 SkyRoof 运行时，也不改变默认后端。

## P2 OMM 六位 NORAD（第一批）

- 双后端 `IOrbitBackend.CreateOmmTrack(OrbitMeanElements)` 使用统一 CCSDS OMM 值对象；完整保留 NORAD、国际代号、历元、BSTAR、轨道角度及平均运动导数。所有公开单位明确记录。
- Orekit 通过已解析元素构造 TLE/SGP4，**不从六位 NORAD 反向拼装传统 TLE 文本**；SGP.NET 使用既有 OmmData 构造。
- 增加六位编号 100001 的双后端 10 分钟传播对照（2 km 上限属于首轮一致性回归，不是独立物理精度标准）。
- 此时仅非生产适配，不代表 OMM/全部 SDP4 精度或上层 UI 连接完成。

## P2 过境预测契约

- 新增纯 .NET `OrbitPassPredictor`、`OrbitPass` 与 `IOrbitTrack.FindPasses`，最大仰角、AOS/LOS 和可见窗口不依赖 SGP.NET 的公共类型。
- 搜索边界位于过境途中时回溯/向前恢复真实地平线交点（时间二分至 1 s）；对于 72 h 恢复范围仍无交点的永久可见目标，`ActualAos/ActualLos=false`，明确不伪造真正升落点。
- 无 Orekit 的解析正弦轨迹验证跨搜索边界、相邻过境与最大仰角；在 Orekit 实际轨迹上执行 4 h 站心观测与过境烟测。后续继续覆盖 GEO/极区/跨午夜。
- 通过纯函数入口可增补独立参考的时间窗口，无需主程序或射频硬件。

## P3 ITRF 与性能改进（第一批）

- Orekit 的 ITRF 参考框架、WGS-84 参考椭球在创建轨迹时固定；同一地面站的 `TopocentricFrame` 缓存复用，避免每 30 s 地平线扫描反复创建 Java 对象。
- `TLEPropagator` 的 PV 查询使用每轨迹互斥锁，依据 Orekit 的 propagator 实例不支持多线程并发调用的官方说明；多卫星仍可分轨迹并行。
- 新增两后端 `PredictGeodetic`：WGS-84 大地位置与椭球高度的 SI 单位合同，避免覆盖图/星下点继续隐式混用 ECI 坐标和不同高度单位。
- CI 在独立 Windows Runner 从 USNO/IERS 官方 HTTPS 下载 `finals2000A.all` 到临时测试目录，记录数据哈希；不将动态的 EOP 数据混入生产锁文件。结果区分 UTC-only、EOP 受覆盖时间及预测/快速解。此项仍须实测加载并验证质量标签，不能声称已经达到外部精度要求。

## P1/P6 生产调用链接线（实验分支）

- SkyRoof 通过独立纯 .NET Contracts 项目引用，不引入 Java 类型到 WinForms、CAT 或 Rotator。Orekit 后端 DLL 使用按需加载；原有 SGP.NET 仍是默认。
- 新增设置 `OrbitSources.Backend`（SgpNet/Orekit）和 `OrekitDataDirectory`；选中 Orekit 后初始化数据、重建卫星追踪对象和群组/无线电/SDR 过境缓存。
- `SatelliteTracker` 的 `Predict/Observe/ComputePasses` 通过契约适配到 Orekit，外部仍返回现有 SGP.NET 值对象。真实 JPL Moon/Sun/Venus 后端不修改。
- 若某一元素的 Orekit 创建失败，保留原有效的 SGP.NET 传播能力并记录 WARN；整体引擎加载失败则显示错误并选择 SGP.NET。不会因无实机而关闭任何已实现的追踪控制。
- 用户数据默认位于 `orekit-data`，第一次使用时从安装目录复制 UTC-TAI 历史；EOP 可通过自定义目录导入。尚需完成独立产物打包、NuGet 锁定、发行版实时控制验证。

## P6 安装器集成（待 CI 复验）

- 正式 Compile Check 与主分支 Release 工作流新增独立的锁定 Orekit 恢复/编译步骤，调用 `scripts/stage-orekit-backend.ps1` 将 IKVM、Hipparchus、Orekit 转换程序集复制进 SkyRoof 构建目录。
- Inno Setup 纳入 `orekit-data/tai-utc.dat`；安装后 payload 测试核对主要 IKVM/转换 DLL、UTC 时间数据哈希和原有 Windows Runtime DLL，避免“仅实验程序能运行”的半成品。
- 当前仍需提交 regenerated lockfiles、确认第三方许可证、验证安装目录独立加载、UI 后端切换和路径恢复、CPU/内存。不能在这一步直接发布正式 Release。

## Windows 安装后 IKVM 独立加载回归

- 新增 `SkyRoof.Orbit.InstallerSmoke`，只依赖纯 .NET Contracts，模拟将应用安装在全新目录，再通过 `Assembly.LoadFrom` 加载已安装的 Orekit 插件。
- 覆盖安装目录的 UTC 数据初始化、TLE/TEME、WGS-84 站心观测和六位 NORAD OMM；不注册额外 AssemblyLoadContext 解析器，防止测试掩盖生产端缺失依赖。
- 原有安装器安装与 .deps.json RID 资产检查继续执行。该烟测不替代现场真实电台/旋转器确认。
