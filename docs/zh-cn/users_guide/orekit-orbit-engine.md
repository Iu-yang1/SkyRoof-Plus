# Orekit / IKVM 统一轨道与天体计算引擎

SkyRoof Plus 的生产轨道传播器统一采用 **Orekit 13.1.9 + IKVM**。TLE 和 CCSDS OMM 仍使用 Orekit 自带的 SGP4/SDP4，并不将 TLE 强行转换为精密数值积分轨道。月球、太阳与金星统一由 Orekit `CelestialBodyFactory`、`TopocentricFrame` 和 WGS-84 站心观测计算，不再使用旧的独立 JPL BSP 解码路径。

## 数据目录和自动下载

在**设置 → 轨道与星历来源 → Orekit ephemeris** 中统一配置：自动下载开关、太阳系目标显示、数据目录、IERS EOP 下载源及 JPL DE430 星历下载地址。选中该设置项后，工具栏显示 **更新 Orekit 星历**，可手动刷新 EOP 并检查/下载 DE430，替代之前工具菜单中独立的 EOP 下载项。

**Orekit Data Directory** 留空时采用 SkyRoof 用户数据目录下的 `orekit-data`。默认开启 **Auto-download Orekit Data**。

程序启动时先准备数据，再初始化 Orekit 的时间与坐标框架：

- `tai-utc.dat`：内置的 UTC/TAI 闰秒历史，首次运行复制到数据目录。
- `finals2000A.all`：从 USNO / IERS 下载地球定向参数（EOP），本地文件超过七天时尝试更新。下载后校验文件结构、保存 SHA-256 来源记录。
- `lnxp1990.430`：下载 Orekit 原生格式的 JPL DE430 行星星历，供月球、太阳和金星使用；采用固定 Git Blob SHA-1 校验并保存 SHA-256 记录。

下载失败时保留已有可用数据；**不会悄悄切换回 SGP.NET 传播**。注意传统 NAIF `.bsp` 文件与 Orekit 所需的 `lnxp1990.430` 不是相同数据格式。

Orekit 对全局数据目录和时间尺度有缓存，因此更新 EOP 或切换目录后应重启程序。**Orbit Data Quality** 是数据覆盖情况说明，不等同于测量过的指向误差。

## 跟踪与多普勒

上层沿用现有 CAT/旋转器接口和轨迹缓存管理。Orekit 提供站心 AZ（真北顺时针）、EL（几何仰角）、距离（米）、距离变化率（米/秒，远离站点为正）；单程下行一阶频移 `Δf = -f0 × ṙ/c`。当前仍存在用于兼容旧 UI、过境 DTO 的 SGP.NET 类型定义，但**不再调用其传播器作为运行回退**。

## 验证边界

代码接线不能代替运行验收。发布前仍需通过 Windows x64 构建与单测、Orekit 天体星历冒烟测试、安装器验证，以及多普勒和旋转器端到端回归。旧版 JPL SPK 解码器已经删除；SGP.NET 数据类型仍暂时用于旧 UI、过境列表和观测 DTO 的兼容适配，未作为数值传播后端。完全清理这一依赖仍需独立的接口迁移与回归测试。

相关变更见 [Orekit/IKVM 架构](../../architecture/orekit-ikvm-orbit-engine-v1.zh-CN.md) 和 [PR #195](https://github.com/Iu-yang1/SkyRoof-Plus/pull/195)。
