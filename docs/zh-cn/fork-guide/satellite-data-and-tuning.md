# 卫星数据、星历、转发器与频率调谐

**语言：** [English](../../fork-guide/satellite-data-and-tuning.md) | 简体中文

## 多来源轨道数据（2026-11 星历导入审查）

进入 **Tools → Orbit / TLE Sources → Edit Source URLs** 或 **Orbit / Ephemeris Sources**。轨道要素优先级从高到低：

1. **手动导入的本地文件**：72 小时临时最高优先级，到期后恢复自动来源。
2. **CelesTrak OMM CSV/JSON**：最高自动优先级。默认官方 Amateur CSV，支持以 JSON 或地区镜像作为同一优先级的来源备选；支持六位 NORAD。
3. **用户配置的轨道 URL / 本地文件列表**：前面的条目优先级更高，支持 TLE 文本、SatNOGS JSON、OMM CSV/JSON。此类持久源**不是** 72 小时手动导入覆盖。
4. **AutoTLE** 后备来源。
5. **SatNOGS TLE** 最低自动优先级。

CelesTrak 官方 CSV 和 JSON 均通过 2026-11-17 在线 Action 实测（各 99 条，NORAD 集合完全一致）。为遵守上游两小时更新间隔，软件增加缓存、按源失败回退、gzip/Brotli 和无效数据拒绝。卫星/发射机数据库、JE9PEL、SatYAML 可以独立设置 HTTPS 镜像或本地文件，并保留有效缓存；它们不竞争上述轨道优先级。

**[完整星历来源、优先级和中国大陆镜像/离线部署说明](../../engineering/orbit-source-mirrors.zh-CN.md)**。

### UNNE-1B / HADES-E2 正式轨道与遥测身份

SatNOGS 可能仍提供临时编号 `98205`（以及旧的 `70002` 占位记录），而 CelesTrak 的正式 NORAD 编号是 `101101`（SO-131）。SkyRoof 在载入旧 `Satellites.json` 和更新 SatNOGS 目录时，将**确认为 UNNE-1B 的记录**合并到具有发射机资料的主卫星对象，保留原 SatNOGS `sat_id` 和已合并的历史 ID，统一使用 `101101` 进行轨道跟踪、发射机和遥测解码。

由于官方 CelesTrak Amateur GP 子集未必及时包含刚发射的目标，轨道更新还会独立获取 `CATNR=101101&FORMAT=CSV` 的单星 OMM，并沿用两小时缓存与自动源优先级。UNNE-1B 的 FM 上行 `145.925 MHz`、下行 `436.888 MHz` 不需要 CTCSS；同一频点的 200 Bd FSK/HADES 遥测可在选择 FM 转发器时自动识别并解码，无需将电台切到专用数据转发器。若已手动添加临时本地转发器，应自行核对重复 UUID/记录。

### 月球与太阳系天体

当前 **Orekit** 轨道引擎需要统一的 UTC、IERS EOP 和 **Orekit 原生 JPL DE430** 星历数据。默认设置支持 USNO/IERS EOP 双来源、固定 Git Blob SHA-1 校验的 DE430；可选择自建 HTTPS 镜像或本地文件，离线保存有效缓存。旧版所述 DE440s/DE421/Custom BSP 是不同的星历数据/接口，不可直接替换本引擎需要的 `lnxp1990.430`。

**注意：** 有星历和跟踪不等于完成 EME 链路预算、QSO 预测或极化旋转补偿。

## 持久化 Base 频率与滑动调谐（PR #2、#4–6、#33）

**Frequency Control** 可停靠面板将三个概念分开：

- **Database reference**：卫星转发器数据库中保存的上行/下行参考频率。
- **Saved Base**：数据库频率 + 为当前转发器分别保存的上下行基准校正；**不包含即时多普勒**。
- **实际电台调谐**：Base + 临时调谐位移 + 已启用的实时多普勒修正。

用 **Edit Base...** 修改持久基准；**Reset to Database** 会在确认后清除相应基准校正；**Reset to Base** 只撤销临时调谐量，不删除保存的 Base。频率滑条和“无多普勒”读数表示参考频率，而发给 CAT 的实际频率可能包含多普勒。滑条允许跨出转发器数据库标称带宽，但实际使用必须遵守设备频段和通信规定。

可分别启用/关闭上下行的多普勒及手动修正。对反向线性转发器应核对正确的上/下行与边带。使用变频器时，若频率不在已配置的 IF 映射内，SkyRoof 会跳过不安全的 CAT 写入。

## 本地自定义转发器（PR #29、#31、#34）

在 **Satellite Transmitters** 面板右键新建 **Local** 转发器；本地创建的记录可再次编辑或删除。可填写：

- 名称/描述；
- 下行 MHz、上行 MHz（两者至少填写一项）；
- 模式，包括 `FM_D`、`USB_D`、`LSB_D` 等支持的数据模式别名。

本地 UUID 保持稳定，编辑后不会丢失相关的 Base 修正、CTCSS 与模式记录。删除前有确认提示。自定义转发器记录位于 SkyRoof 数据目录中的 **`custom-transmitters.json`**，不会改写从 SatNOGS 下载的原始数据库。通过 **Help → Data Folder** 打开真实路径，常见目录是 `%APPDATA%\Afreet\Products\SkyRoof`；手动修改前请备份。

**限制：** 编辑/删除仅适用于本地创建的转发器，不能直接修改在线数据库下载记录。

进一步阅读：[Frequency Control](../../users_guide/frequency_control.md) · [Doppler Tracking](../../users_guide/doppler_tracking.md) · [Satellite Transmitters](../../users_guide/satellite_transmitters_panel.md) · [Data Folder](../../users_guide/data_folder.md)。
