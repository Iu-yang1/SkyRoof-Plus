# SkyRoof 下一代轨道与天体动力学引擎：Orekit + IKVM 实施方案 V1

- 状态：**架构设计草案；尚未实施、尚未通过 IKVM/Orekit 实际编译**
- 基线：SkyRoof-Plus `master`，2026-11-12，起始提交 `1be25637969fb34ad77b36fa1151a46ddb8361ba`
- 研发分支：`feature/orekit-ikvm-orbit-engine`
- 目标平台：Windows x64、.NET 10、WinForms；不改动现有电台/旋转器控制协议
- 技术决策：**首选 IKVM 静态转换 Orekit Maven JAR 为 .NET 程序集**；SGP.NET 作为保留的有效后端
- 范围界限：本文件是设计，不是已实现功能的声明；未经独立比对不更改默认轨道传播器

## 1. 目标与非目标

### 1.1 目标

1. 在 SkyRoof 中实现可切换的轨道计算后端，支持当前 SGP.NET 和新 Orekit，同时尽量不让 WinForms、卫星数据库、CAT、多普勒、Rotator 直接依赖某个后端的专有类型。
2. 将 TLE/OMM、统一时间尺度、TEME/GCRF/ITRF 参考框架、地面站拓扑观测和空间目标运动状态的接口清晰分开。
3. 为未来的 EME 高精度月球/太阳观测、CCSDS OEM 状态星历、卫星姿态/极化计算提供扩展基础，而不是把所有高级功能捆绑到首版。
4. 保持 SkyRoof 现有的真实能力：自动/手动轨道元素优先级、6 位 NORAD 编号、AOS/LOS、地球同步卫星、足迹图、SkyCAT/IC-9700 多普勒与已有旋转器路径控制。
5. 采用可重复、离线可重放的基准测试，严格区分“两个库结果接近”和“相对于独立参考解正确”。

### 1.2 明确不做

- **首版不移除 SGP.NET**，也不将新的计算结果直接下发到正在运行的 CAT/旋转器。
- 不把 TLE 输入的 SGP4 更换成没有相应拟合初始状态的高精度数值积分；单纯改成数值积分不能自动提高 TLE 的轨道精度。
- 不宣称 Orekit 自动提供真实姿态、天线安装矩阵、卫星 TLM 或电离层法拉第旋转数据。
- 不直接删除 `JplSpkKernel`；当前 SkyRoof 使用的 `.bsp` (NAIF DAF/SPK) 与 Orekit 默认 JPL 数据加载格式必须单独验证兼容，不能默认为同一个文件接口。
- 不对其他既有功能新增基于“可能风险”的功能锁定；新后端是否可用由明确的依赖、数据与运行能力决定。

## 2. 核实的现有架构与改动位置

| 现有模块 | 当前行为 | V1 计划 |
|---|---|---|
| `SkyRoof/SkyRoof.csproj` | `SGP.NET 1.6.0` NuGet 依赖；.NET 10 Windows x64 | 初期不删；新 Orekit 隔离于独立类库项目 |
| `SkyRoof/Satellites/SatelliteTracker.cs` | `new Tle(OMM/TLE)` → `new Satellite(tle)` → `Predict` / `GroundStation.Observe`；JPL 另有旁路 | 逐步由适配器代理；保留原外部调用签名直到兼容层完成 |
| `SkyRoof/Satellites/SatellitePass.cs` | 直接存储 `SatelliteVisibilityPeriod`、返回 `TopocentricObservation` | 为高层构造独立通行数据 DTO，过渡期保留 SGP.NET 数据适配 |
| `SkyRoof/Satellites/SatellitePasses.cs` | 统一创建地面站、预测、选择可见窗口 | 分离调度/缓存与传播算法 |
| `SkyRoof/Satellites/SatnogsDbTle.cs` | 保留原始 TLE 和 CelesTrak CSV/JSON OMM 对象 | 增加内部元素映射；不得损失 BSTAR、历元、NORAD、单位和 TLE 原文 |
| `SkyRoof/Satellites/SatnogsDbSatellite.cs` | 自动/手动轨道元素优先级与过期逻辑 | 原逻辑保持；后端变更只重建相应轨迹实例 |
| `SkyRoof/Satellites/JplSpkKernel.cs` | 自研 DAF/SPK Type 2/3、J2000/日期坐标转换、站心观测 | 单独作为现有天体后端，后续与 Orekit 天体计算比对 |
| `SkyRoof/Rotator/OptimizedRotationPath.cs` | 消费 `SatellitePass.GetObservationAt` 的 AZ/EL | 维持既有路径优化与控制边界，不直接传递 Orekit Java 类型 |
| `VE3NEA.Dsp.Tests/OrbitElementTests.cs` | OMM 六位 NORAD、手动元素过期等回归 | 继续复用 |
| `VE3NEA.Dsp.Tests/JplEphemerisTests.cs` | SPK 解码与天体轨迹回归 | 继续复用，补第三方独立参考 |

要解决的技术债是：`SGPdotNET.Observation.TopocentricObservation`、`SatelliteVisibilityPeriod` 和 `Tle` 目前穿过多个层级。不能直接把 `SatelliteTracker.Satellite` 换成 Orekit 对象而假设消费端仍兼容。

## 3. 版本与集成决策（截至 2026-11-12）

| 依赖 | V1 候选版本 | 依据 |
|---|---|---|
| Orekit | **13.1.9** | 2026-10-03 已发布的 13.1 补丁版，Maven `org.orekit:orekit` |
| Hipparchus | **4.0.3（由 Orekit Maven POM 解析）** | Orekit 13.1.9 上游属性 `orekit.hipparchus.version` |
| IKVM | **8.16.3** | 最新已核实稳定 NuGet 版本；支持 .NET 6+ 和 Java SE 8 类库兼容 |
| IKVM.Maven.Sdk | **1.12.2** | 已核实的稳定 NuGet 版本；原生支持 `MavenReference` |
| 运行框架 | **net10.0 / net10.0-windows7.0** | 与 SkyRoof 现有技术栈对应 |

Orekit 13.1.9 POM 的 Java bytecode 目标是 1.8，**因此初步满足 IKVM Java 8 兼容门槛**，但这不等于全部反射、依赖加载和运行时路径已通过验证。必须先成功完成完整转换、启动、传播和数据加载冒烟测试。

首选 **`IKVM.Maven.Sdk` + `MavenReference`**，由 MSBuild 解析 Maven 传递依赖并执行 JAR→IL 转换。不要一开始手工维护每个 Hipparchus DLL 的版本；如果 Maven POM 在 IKVM 场景下缺失必需的运行依赖，再用显式 `Dependencies` 元数据修复并记录依据。

候选*配置形态*（**仅示意，尚未编译**）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="IKVM.Maven.Sdk" Version="1.12.2" />
    <MavenReference Include="org.orekit:orekit" Version="13.1.9" />
  </ItemGroup>
</Project>
```

实验时先检查实际解析出的 IKVM 运行时版本、Hipparchus 转换输出、NuGet/Maven 锁定行为和静态程序集闭包。仅在有明确兼容需求时追加直接 `IKVM 8.16.3` 引用，避免造成隐式版本漂移。CI 使用项目现有 `nuget restore -LockedMode`，更新受影响的 `packages.lock.json` 后才启用新项目的常规构建。

**不要求终端用户另装独立 Java JVM**；这不代表可以不带 IKVM Java 类库兼容运行时。实际部署应包含转换后的程序集和 IKVM 运行组件，并以干净 Windows 沙箱验证。

## 4. 建议的分层设计

```text
SkyRoof WinForms / Widgets / CAT / Rotator / AutoSelection
                |
        SatelliteTracker compatibility facade
                |
        OrbitApplicationService (cache, scheduling, backend selection)
                |
          IOrbitBackend / IOrbitTrack
            /                 \
 SgpNetOrbitBackend       OrekitOrbitBackend (IKVM)
    SGP.NET 1.6.0           Orekit + Hipparchus
            \                 /
           Orbit observation / PV / Pass DTOs
                        |
        Shared Time / Frame / Ground Station conventions
                        |
      Ephemeris Providers (TLE/OMM, OEM later, JPL later)
```

建议文件与项目布局（计划，不代表已创建）：

```text
SkyRoof.Orbit.Contracts/
  OrbitObservation.cs, OrbitState.cs, OrbitPass.cs
  OrbitElements.cs, GroundStationSpec.cs
  IOrbitBackend.cs, IOrbitTrack.cs
SkyRoof.Orbit.SgpNet/
  SgpNetOrbitBackend.cs, SgpNetObservationMapper.cs
SkyRoof.Orbit.Orekit/
  SkyRoof.Orbit.Orekit.csproj
  OrekitDataContext.cs, OrekitFrameAdapter.cs
  OrekitTleMapper.cs, OrekitOrbitBackend.cs
SkyRoof/Orbit/
  OrbitApplicationService.cs, OrbitBackendSettings.cs
  SatelliteTrackerAdapter.cs
VE3NEA.Dsp.Tests/
  OrbitContractTests.cs, OrbitParityTests.cs, OrekitInteropSmokeTests.cs
  OrbitReferenceVectorsTests.cs, OrbitPerformanceTests.cs
docs/architecture/
  orekit-ikvm-orbit-engine-v1.zh-CN.md
```

如初期项目拆分导致部署复杂，可先把 SGP.NET 适配器留在 `SkyRoof/Orbit/`，但 Orekit 的 IKVM 引用与类型必须隔离到独立项目。建议先做无 UI 依赖的 .NET 10 控制台或 xUnit 最小实验，再集成 WinForms。

### 4.1 核心接口草案（语义契约，非编译代码）

- `OrbitElements`：`ElementType`（TLE、OMM、OEM 等）、来源与历元、原始输入、唯一目标标识、可选 NORAD；**不能通过重新拼接 69 列 TLE 丢失 OMM 六位目录号**。
- `GroundStationSpec`：WGS-84 大地纬度、经度（度）、椭球高（米）；与地球半径高度、海拔正高区分。
- `OrbitState`：所用 UTC、参考框架（TEME/GCRF/ITRF 等）、位置（米）、速度（米/秒）、数据源和质量元信息。
- `OrbitObservation`：UTC、AZ（从真北起顺时针，度）、EL（几何仰角，度）、`RangeMeters`、`RangeRateMetersPerSecond`（**远离接收站为正**）、可选光行时/大气折射策略、来源和质量元信息。
- `OrbitPass`：真实 AOS/LOS UTC、最大仰角及历元；窗口起点已经在可见状态时，正确回溯 AOS，而非将搜索边界伪装为 AOS。
- `IOrbitBackend.CreateTrack(elements)` → `IOrbitTrack.Observe(station, utc)` / `PredictState(frame, utc)` / `FindPasses(station, start, end, mask)`。轨迹实例绑定特定元素和后端版本，不在每秒 UI 刷新中重新构建整套传播器。

对 Java/`org.orekit.*`、IKVM/`java.*`、`SGPdotNET.*` 类型实行**边界隔离**：对外契约只暴露纯 .NET 值对象；避免 Java 异常和静态数据上下文穿透 UI 线程。

### 4.2 时间、坐标和射电频率的约定

1. 外部 API 统一传入明确的 UTC 时间值；Orekit 内部通过其时间尺度系统处理 UTC/TAI/TT/UT1。不要把 `DateTimeKind.Unspecified` 静默当 UTC。
2. TLE/OMM 的 SGP4 结果以 **TEME** 为基准；参考框架转换统一经 Orekit，只有明确的参考框架才允许比较状态矢量。
3. 地面观测建议 WGS-84 + **ITRF/IERS 2010**；选择无 EOP 的退化模式时必须有数据源标识，不将其冒充高精度结果。
4. 角度约定：AZ = 0° 北、90° 东；EL 正值为地平线上。天顶附近比较采用空间方向单位向量夹角，不直接比较跳变的方位角数值。
5. 范围速率正方向为“远离地面站”；一阶单程接收频移按 `Δf ≈ -f0 × rangeRate/c` 定义。上行和下行 CAT 补偿符号应与既有系统逐场景核对，不简单复用同一个符号。
6. `DateTimeOffset` 侧的数据结构与 Orekit `AbsoluteDate` 转换应单独编写测试，覆盖闰秒边界；不得靠本地时区字符串解析。
7. 两种后端若对地球自转、折射、光行时、EOP 或帧定义不一致，测试应先统一假设再比较；**轨道误差与观测坐标变换误差分别报告**。

## 5. Orekit 数据管理（不得遗漏）

Orekit 本体并不等于完整数据集。至少需要 UTC-TAI 历史与对应时间范围的 EOP 才能可靠执行部分时间/ITRF 转换；天体计算还需要匹配格式的行星星历。按照 Orekit 官方 `DataContext`/`DataProvidersManager` 管理。

设计：

- `OrekitDataContext` 在应用启动或后台首次调用时按已配置的数据包目录初始化，禁止每个卫星创建时重复全局初始化。
- 用用户数据目录内的独立 `orekit-data` 数据集，允许离线导入、自动更新和选择版本；元数据包括来源 URL、SHA-256、更新时间、EOP 起止历元、闰秒表版本及星历覆盖期。
- 测试固定小型资料包的内容哈希，CI 不依赖“今天在线下载最新 EOP”才能重现历史结果。
- 正式分发前审查每份 EOP、闰秒与 JPL 数据的授权及再分发条件。避免无审查地整包放入安装程序。
- 观测结果显示所用 EOP 历元/状态；缺少数据的异常应精确报告，不编造“高精度”输出。普通 SGP4 工作流可以按原有可用后端继续操作，不额外锁定用户功能。
- `JplSpkKernel` 的 NAIF SPK/BSP 读取继续保留；比较 Orekit 自带行星星历加载与现有 BSP 格式时，分别核实对应数据文件解析器与系数格式，不能直接假定互通。

## 6. 分阶段交付计划

| 阶段 | 范围与交付物 | 验收 |
|---|---|---|
| **P0 / 集成实验** | 创建独立 `SkyRoof.Orbit.Orekit` 最小项目，使用 `IKVM.Maven.Sdk` 解析 `org.orekit:orekit:13.1.9`，执行 JAR→IL 转换；完成 UTC/时间尺度、TEME/PV、TLE/SGP4 的最小烟测 | Windows .NET 10 x64 Restore/Build/Run 通过，依赖和许可证清单可追溯，记录冷启动、生成物大小、内存/CPU |
| **P1 / 稳定观测接口** | 引入无 Java 类泄漏的 DTO、SGP.NET Adapter、Orekit Adapter；重构 `SatelliteTracker` 内部，不改外部 UI 控制行为 | 同一输入两后端均输出 AZ/EL/距离/距离速率/坐标状态；旧单元测试全部通过 |
| **P2 / 轨道根数和过境** | 完整 TLE、CelesTrak OMM CSV/JSON 映射、六位 NORAD、近地/深空/GEO、AOS/LOS、手动/自动元素刷新 | Vallado SGP4 样例独立比对；过境边界、跨午夜、极区、长时间传播与动态元素切换回归 |
| **P3 / 时间与坐标精化** | 接入 `DataContext`、UTC/UT1/TT、EOP、TEME/ITRF/GCRF、地面站 WGS-84；对 Range Rate 多普勒与轨道跟踪交叉核验 | 多站、多历元、极移、闰秒与正负 Doppler 结果可重复；显示数据来源和质量状态 |
| **P4 / 天体星历及 EME** | 对 Moon/Sun/Venus 比对自研 `JplSpkKernel` 与 Orekit 的独立天体星历路径，考察双站可见窗口及一程/双程多普勒定义 | 同源星历、相同时间和框架下的差异分析；不因接入 Orekit 自动删除 BSP 后端 |
| **P5 / 姿态与精密轨道** | 增加 CCSDS OEM/AEM 与真实姿态来源的适配、极化基底与可选空间天气模型 | 使用真实/官方样本验证；“没有姿态资料”明确标记为模型假设，不伪称真实姿态 |
| **P6 / 发布与切换** | 后端选择设置（SGP.NET/Orekit）、遥测状态、回归、安装器与文档；在完成验证后评估默认后端 | 正式 CI、独立精度基准、干净 Win x64 安装、实时 UI/CAT/Rotator 回归和可逆切换通过 |

首个实际代码交付建议只做 **P0 + P1 的非生产路径**。P2-P6 按独立 PR 推进；完整链路验收前不改变默认后端。用户可主动选择已实现的 Orekit 计算功能，不设与故障模式无关的功能门禁。

## 7. 验证与测试基线

### 7.1 三路基准和差异报告

- **参考解**：Vallado 等，AIAA 2006-6753 《Revisiting Spacetrack Report #3》公开案例及其参考代码/输出。包含 LEO、SDP4 深空、Molniya、GEO、近地/极轨和特殊根数案例。
- **旧后端**：SkyRoof 当前 `SGP.NET 1.6.0`。
- **新后端**：Orekit 13.1.9 + IKVM。
- 同一元素历元以 `t0-24 h, -6 h, 0, +6 h, +24 h` 等时间点采样，分别记录 TEME 位置/速度、ITRF 状态、AZ/EL、距离/径向速度、AOS/LOS。误差统计包括 RMS、p95、最大值和失败样本的输入/传播器版本。
- 不能将两个后端的**一致**当成物理真值；TLE 的精度上限与算法一致性是不同问题。独立参考优先于互相对照。

### 7.2 首版验收量化原则

以下为**提议的工程验收目标，不是已经达成的实测结果**，首次基线跑通后据事实调整具体数值：

1. SGP4 标准案例：先严格对比相同参考系的 PV 状态；若大于参考输出/数值精度容限，记录和定位常数、解析与变换差异，不“增加容限直到通过”。
2. 上层控制一致性：相同场景的 TRACK/AOS/LOS/CAT 数据流与既有 SGP.NET 正常工作流程兼容；多普勒符号、AOS/LOS 窗口跨越、赤道/极区和天顶附近均要覆盖。
3. 首次初始化、单星逐秒传播、100/1000 星批量两日预测、内存峰值、CPU p95、冷启动和安装包体积形成**基线报告**；是否达到实时性能以 SkyRoof 当前实际使用场景为验收标准。
4. 对静态转换的 Orekit 运行时检验：启动、批量传播、对象释放、多个线程并行调度、数据资源更新、安装后运行和卸载；不将“编译成功”冒充“端到端可用”。
5. 失败模式：旧 TLE、缺 EOP、损坏数据包、无法解析 OMM、外部文件路径变化、异常退出后重启。明确错误原因，保证后台预测异常不破坏 UI/CAT/Rotator 当前状态。

### 7.3 CI 方案

- P0 的 IKVM/Orekit 试验先作为独立、可复现实验流水线：Restore 锁定依赖 → 转换/编译 → 小型烟测 → 测量 DLL/内存/速度 → 生成 JSON + Markdown 基准报告。
- 稳定后再整合到主 `Compile Check` 和发布流程。保持 SkyCAT 子模块、WinDivert、HamNoise、完整 xUnit、`aislop` 与安装器依赖核对。
- 提交前执行 `aislop` 并查验新增警告；如果 IKVM 生成代码或第三方依赖触发误报，只对生成物建立具有来源依据的排除规则，不扩大到人工维护源码。
- GitHub Actions Windows Release x64 构建与签名状态如实记录；有无实机分别注明，不以缺少实机为理由关闭功能。

## 8. 发布、兼容性和回滚

- 生产默认保持 `SGP.NET`，直到 P6 评估通过。引入新后端**不更改配置 JSON 原有字段**；新增后端设置采用清晰、向后兼容的枚举值。
- 新后端由用户选择时，保留“当前后端、轨道元素历元、数据集版本、参考系、观测数据时间戳”诊断；后端切换需要替换轨迹/缓存，不需要取消卫星列表、天线跟踪功能。
- 修复策略优先针对真实故障模式处理；对确定可工作的场景不引入额外功能限制。
- 回滚操作：选择 SGP.NET 后端并重建 `SatelliteTracker`、清除新后端轨迹缓存；若安装器需回退，原有版本的 Release 仍可独立使用。任何新增格式的元素数据都应可继续由旧路径读取或保留原始内容。
- 本计划不修改当前 `master`、已有 Release 或用户设备的物理运动行为。

## 9. 风险记录与决策

| 风险 | 触发条件 | 对策 / 验证 |
|---|---|---|
| IKVM 兼容 | Orekit/Hipparchus 类加载、反射或 Java 类库 API 与 IKVM 不一致 | P0 逐个运行的 TLE/时间/EOP 烟测，记录实际堆栈和版本 |
| Build/部署体积与时间 | Maven 转换和 Java 兼容运行库增加构建成本 | 单独工程与缓存、锁定依赖；量化大小和冷启动后决定打包方案 |
| 数据覆盖不完整 | 缺少/过期 EOP、闰秒表或 JPL 星历 | 数据 manifest、覆盖期检测、显式质量标记和允许用户更新/导入 |
| 参考框架混用 | TEME/J2000/ITRF 被视为同一坐标系 | 输出强制带 Frame ID，差异测试先做同框架对齐 |
| Doppler 方向错误 | 观测范围速率符号或上/下行频率关系翻转 | 一组接近/远离已知场景与现有 CAT 命令回归 |
| OMM 字段损失 | CelesTrak CSV/JSON 映射变为传统 69 列 TLE | 原始 OMM 保留和六位 NORAD/UTC/单位测试 |
| 天体星历文件不兼容 | 将 BSP/SPK 文件直接交给 Orekit 默认 DE loader | 各自独立加载与对照，不宣称互通直至验证 |
| 后端切换影响控制 | UI 同步线程阻塞或缓存跨源混淆 | 后台轨迹生成、版本化缓存、明确切换事件与现有 TRACK 冒烟测试 |

## 10. V1 决策记录

- **选择 IKVM 方向**：用户明确倾向将 Java 库直接转换为 .NET 程序集；首版先验证此路径，不默认用 Java 微服务替代。
- **保持 SGP.NET 生产可用**：用于基准、回滚以及现有用户继续运行；不是因为理论风险而锁定 Orekit 功能。
- **先实现轨道传播与观测，再扩展天体和姿态**：避免把独立的 EOP、JPL BSP、极化、电离层模型问题混在一起。
- **先将 SGP.NET 类型从公共调用链中隔离**：这是让 Orekit 真正成为可替换引擎而非局部附加库的关键技术节点。

## 11. 主要依据与官方资料

1. Orekit 13.1.9 变更记录：https://test.orekit.org/site-orekit-development/changes.html
2. Maven Central Orekit 13.1.9 / Hipparchus 4.0.3 / Java 8 编译目标：https://central.sonatype.com/artifact/org.orekit/orekit
3. IKVM.Maven.Sdk 使用说明：https://github.com/ikvmnet/ikvm-maven
4. IKVM 运行时与静态 JAR 转换：https://www.nuget.org/packages/IKVM/
5. IKVM.Maven.Sdk NuGet：https://www.nuget.org/packages/IKVM.Maven.Sdk/
6. Orekit DataContext 官方文档：https://test.orekit.org/site-orekit-latest/data/contexts.html
7. Orekit 数据初始化与所需外部资料：https://test.orekit.org/site-orekit-development/data/default-configuration.html
8. Orekit Frames 参考框架：https://test.orekit.org/site-orekit-latest/architecture/frames.html
9. Vallado et al. 《Revisiting Spacetrack Report #3》AIAA-2006-6753 及测试数据：https://celestrak.org/publications/AIAA/2006-6753/
10. SkyRoof 现有源码：
    - `SkyRoof/Satellites/SatelliteTracker.cs`
    - `SkyRoof/Satellites/SatellitePass.cs`
    - `SkyRoof/Satellites/SatellitePasses.cs`
    - `SkyRoof/Satellites/JplSpkKernel.cs`
    - `VE3NEA.Dsp.Tests/OrbitElementTests.cs`
    - `VE3NEA.Dsp.Tests/JplEphemerisTests.cs`

## 12. 第一轮实施工作单（P0）

- [ ] 新建 `SkyRoof.Orbit.Orekit` 独立项目，固定 Orekit/IKVM/Maven SDK 版本，恢复传递依赖
- [ ] 构建 Orekit JAR→IL，列出转换输出、实际加载的程序集与运行时清单
- [ ] 创建最小 `orekit-data` 测试样本并写明来源/哈希/覆盖时段
- [ ] 实测 `AbsoluteDate`、UTC/TAI、TEME/ITRF、`TLEPropagator`、`TopocentricFrame`
- [ ] 实现至少一份 SGP4 参考案例的三路输出对比（Vallado / SGP.NET / Orekit）
- [ ] 记录恢复依赖耗时、首次调用耗时、每次传播延迟、内存和生成物体积
- [ ] 形成 P0 验收报告；再开始 P1 接口适配

> **实施状态：** 本文档落库只代表设计交付。IKVM/Orekit 工程、数据包、运行时转换、基准结果和安装包均尚未创建或验证。