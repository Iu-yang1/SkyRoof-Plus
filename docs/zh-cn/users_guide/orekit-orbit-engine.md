# Orekit / IKVM 轨道引擎使用说明（实验性）

SkyRoof Plus 的轨道计算可使用 **SGP.NET（默认）**，也可主动切换至通过 **IKVM 8.16.3** 转换的 **Orekit 13.1.9**。两者都可通过 SGP4/SDP4 传播 TLE 和 CCSDS OMM。**切换计算库并不会使 TLE 自动具有精密轨道积分的精度。**

## 后端选择

1. 打开 **Tools → Orbit / TLE Sources → Edit Source URLs...**，进入轨道数据源设置。
2. 在 **Orbit engine** 分类选择 **Orbit Propagation Backend**：`SgpNet` 或 `Orekit`。
3. 应用后程序会重建卫星轨迹和过境缓存。通过只读的 **Active Orbit Backend** 与 **Orbit Data Quality** 确认实际启用的后端及数据状态。
4. 如果 Orekit 初始化失败，程序显示具体错误并保留可工作的 SGP.NET；某一轨道元素计算失败也会记录回退。

集成版不需要用户单独安装 Java JVM，但必须完整携带 IKVM 的 .NET 运行库、Windows 本地 DLL、`ikvm/win-x64` 类库镜像及 `ikvm.properties`。仅编译通过不代表独立安装包可运行。

## UTC、EOP 与坐标精度

Orekit 需要 UTC–TAI 历史，例如 `tai-utc.dat`。安装器包含 UTC 历史数据，首次启用时复制到用户数据目录。为提高 ITRF 地固系转换的可信度，还应导入覆盖计算历元的 IERS `finals2000A.all` 数据。

通过 **Tools → Orbit / TLE Sources → Update Orekit IERS EOP Data...** 从官方源更新 EOP，程序会在校验格式后保存 SHA-256、下载 URL 和时间。**更新后重启 SkyRoof** 以使新数据生效。也可填写 **Orekit Data Directory** 指向自行管理的数据目录；不要假定运行中的全局 Orekit 时间上下文可以热替换。

**Orbit Data Quality** 标明是否缺少 EOP、数据是否覆盖指定时段及来源，但不表示已经通过绝对跟踪精度实测。

## 与电台、旋转器和多普勒的兼容

现有 SkyRoof UI、CAT 和旋转器调用接口保留。新后端使用方位角从真北顺时针、几何仰角、距离以米计、**远离地面站时范围速率为正**；兼容层会将其转换为原先 SGP.NET 的 km / km·s⁻¹ 数据对象。单程下行一阶多普勒近似为 `Δf = -f₀ × rangeRate/c`。

可随时选择 SGP.NET 作为回退。月球、太阳、金星目前仍保留原有 JPL/NAIF BSP 天体计算后端；Orekit 不会自动提供真实卫星姿态、极化或 EME 的全套数据。

## 验收范围

软件验证包含 Vallado SGP4/SDP4 参考向量、六位 NORAD 的 OMM、AOS/LOS 边界、安装目录的 IKVM 运行时加载；**没有实机 CAT/旋转器或真实 EME 的自动化验证，不应称为已完成该类验收**。IKVM 静态转换的 `CharBuffer.rewind` 警告仍在单独跟踪。

完整技术方案见 [Orekit + IKVM 架构 V1](../../architecture/orekit-ikvm-orbit-engine-v1.zh-CN.md)。发布验收完成前，请将开发版与已知稳定安装分开保存。
