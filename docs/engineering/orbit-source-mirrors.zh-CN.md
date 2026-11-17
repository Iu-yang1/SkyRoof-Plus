# 星历来源、导入优先级与中国大陆镜像部署

**适用版本：** SkyRoof-Plus 星历源修复 PR #241。本文区分了轨道要素（TLE/OMM）、卫星/发射机元数据（SatNOGS/JE9PEL/SatYAML），以及 Orekit 的地球定向参数（IERS EOP）和行星星历（DE430）；它们的优先级**不是**同一个维度。

## 1. 数据格式、官方接口与本软件的解析方式

| 数据族 | 官方默认接口/格式 | SkyRoof 行为 |
|---|---|---|
| CelesTrak amateur GP **OMM CSV** | \`https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=CSV\` | 默认最高自动优先级；按 \`NORAD_CAT_ID\` 关联 |
| CelesTrak amateur GP **OMM JSON** | \`https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=JSON\` | 同一轨道族的可选备用格式；按 JSON 顶层数组和 OMM 字段自动识别 |
| 单星 OMM | \`https://celestrak.org/NORAD/elements/gp.php?CATNR=39444&FORMAT=JSON\` | 可作为自定义 URL 来源，保留整型 NORAD |
| SatNOGS 轨道源 | \`https://db.satnogs.org/api/tle/?format=json\` | 原生 SatNOGS JSON，自动优先级最低 |
| SatNOGS 卫星及发射机 | \`/api/satellites/?format=json\`、\`/api/transmitters/?format=json\` | 元数据，不与 OMM 竞争轨道要素优先级 |
| AutoTLE | \`https://raw.githubusercontent.com/BI4PYM/AutoTLE/refs/heads/master/AutoTLE.txt\` | 传统 TLE 文本 |
| 本地 TLE / OMM | 操作者指定 \`.txt/.tle/.csv/.json\` | 2/3 行 TLE、OMM CSV/JSON、SatNOGS JSON；内容优先于 URI 扩展名识别 |

**实测依据：** [CelesTrak 正式在线 GP 对比 Action #44053914187](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44053914187) 于 2026-11-17 从官方 GP PHP API 得到 amateur CSV **99** 条、JSON **99** 条、NORAD 编号重合 **99/99**、AO-27（22825）存在、OMM 核心字段齐全。该 Action 证明接口此时有效；并不意味着将来永远不需处理网络故障。

CelesTrak 官方说明 \`GROUP\`、\`CATNR\` 查询与 \`FORMAT=CSV/JSON\` 都有效。**2026-07-11 起 NORAD 超过传统 5 位 TLE 的范围，六位及未来九位编号必须以 OMM 等新格式表示。** 当前 SkyRoof 将 OMM 原样传递到轨道引擎，不转换为可能丢失编号的旧 TLE。

- 官方格式文档： https://celestrak.org/NORAD/documentation/gp-data-formats.php
- 官方使用策略（2026-11-16 更新）： https://celestrak.org/usage-policy.php
- 官方 Amateur Radio 轨道列表： https://celestrak.org/NORAD/elements/table.php?GROUP=amateur&FORMAT=csv

## 2. 轨道优先级与镜像顺序

**轨道要素（最高 → 最低）：**

1. 操作者手动导入本地文件：72 小时临时最高优先级，期满自动回到自动来源。
2. CelesTrak OMM CSV/JSON：统一最高自动优先级。
3. 用户自定义 URL/本地文件列表：第一项比第二项高，应用时反向排序。
4. BI4PYM AutoTLE。
5. SatNOGS TLE/JSON。

**一个“来源”中的镜像候选**与**多个不同优先级的来源**不是同一回事。例如，CelesTrak 槽位可填
\`本地/区域镜像.csv;celestrak.org 官方 CSV\`，这两个候选属于**同一优先级**，第一个经结构校验的成功响应被采用。如果两个不同类型的来源同时有同一 NORAD，要遵守前面的轨道优先级。

软件每个 CelesTrak 来源配置维护隔离的源键缓存：官方 GP 每 **2 小时**更新一次，2 小时内只使用缓存；跨境网络短时间中断时，可在 24 小时内重用已经成功解析的数据并记录警告，之后让其他新鲜来源接管。源列表、组别或镜像顺序变化会改变缓存键，不混用旧数据。

## 3. 推荐中国大陆配置

**直接连接（无需自建服务器）**：先保留官方 CelesTrak CSV。各自提供多个可配置的 HTTPS 或本地文件候选：
- \`CelesTrak OMM CSV/JSON Sources\`：默认官方 CSV；JSON 为格式验证/备用，且 **JSON 与 CSV 共享同一官方主机，并不提供跨境链路冗余**。
- \`AutoTLE URL\`：默认 BI4PYM GitHub Raw URL；可追加已校验的本地 \`AutoTLE.txt\`。
- \`Satellite List URL\`、\`Transmitter List URL\`：官方 SatNOGS JSON；网络无法连通时本地缓存允许离线启动。
- \`JE9PEL CSV Sources\`：如果日本页面无法访问，保留最后成功下载的 \`JE9PEL.csv\`；没有这个可选表也不会阻断卫星数据导入。
- \`SatYAML ZIP Sources\`：可把 \`gr-satellites\` 的完整仓库 ZIP 放在本地，安装后仅更新有效的 SatYAML 定义，不会先删除已工作的 YAML。
- \`IERS EOP Download Sources\`：USNO 和 IERS 双官方来源；也可指定本地 finals2000A.all。
- \`JPL DE430 Download Sources\`：官方 Orekit-native DE430 以及可信本地备份；每个镜像都严格按**已固定的 upstream Git blob SHA-1**验证文件。不可拿 NAIF DE440/DE430 BSP/SPK 直接冒充 \`lnxp1990.430\`。

**区域共享缓存（多台台站推荐）**：在你自己控制的大陆服务器上周期性同步官方数据，确保 HTTP 200 且按格式验证后才原子更新：
- \`https://orbit.example.cn/feeds/amateur.csv\`（示例占位域名，**并非已存在的公共镜像**）
- \`https://orbit.example.cn/feeds/satellites.json\`
- \`https://orbit.example.cn/feeds/transmitters.json\`
- \`https://orbit.example.cn/feeds/AutoTLE.txt\`
- \`https://orbit.example.cn/feeds/gr-satellites.zip\`

将主镜像地址写在第一位，将原始官方地址写在第二位，分号或换行分隔。例如：

\`\`\`text
https://orbit.example.cn/feeds/amateur.csv
https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=CSV
\`\`\`

这个镜像必须由使用者部署，域名、TLS 和文件内容均需真实验证；不对第三方未经验证的“GitHub 加速”“CelesTrak 加速”域名做默认信任。

### Nginx 缓存 CelesTrak 的示例

在自行配置 TLS 的 \`orbit.example.cn\` 站点中，可通过反向代理只映射一个预定义的 \`/feeds/amateur.csv\`，避免用户任意查询上游地址：

\`\`\`nginx
proxy_cache_path /var/cache/nginx/orbit levels=1:2 keys_zone=orbit:16m
                 max_size=256m inactive=24h;

server {
    listen 443 ssl;
    server_name orbit.example.cn;
    # 使用部署者自己的有效 TLS 证书配置。

    location = /feeds/amateur.csv {
        proxy_pass https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=CSV;
        proxy_ssl_server_name on;
        proxy_set_header Host celestrak.org;
        proxy_cache orbit;
        proxy_cache_key "celestrak-amateur-csv";
        proxy_cache_valid 200 2h;
        proxy_cache_lock on;
        proxy_cache_use_stale error timeout http_500 http_502 http_503 http_504;
        add_header X-Orbit-Cache $upstream_cache_status always;
    }
}
\`\`\`

不要无条件把上游的 \`403\`、\`404\` 或 HTML/错误 JSON 页面缓存成星历内容。部署时建议另加一个周期同步器，下载完成后验证 \`NORAD_CAT_ID/EPOCH/MEAN_MOTION\` 等字段，记录更新时间和 SHA-256，再原子替换静态文件。严格控制上游请求量。

## 4. 上游访问频率与错误行为

CelesTrak 于 2026-11-16 更新的官方 Usage Policy 明确要求：
- **GP 两小时更新一次，只在内容更新后获取一次**；官方明确偏好 CSV，JSON 体积约为 CSV 三倍。
- 支持 gzip/brotli 压缩，客户端应支持压缩。
- 非 200 HTTP 响应必须停止重复请求；503/500 应至少等待 15 分钟。不要通过不断重试或随机切换代理 IP 规避限流。
- 多人共用 NAT 出口的台站特别适合共享缓存，避免每个人都去访问同一个官方接口。

SkyRoof 的下载器现会请求 gzip/brotli、逐个尝试**不同镜像源**，并记录哪个镜像成功；CelesTrak GP 最近缓存避免过度重复下载。用于实时校验 CSV/JSON 同一数据的 GitHub Action 已改为**手动执行**，而非在每次提交上重复访问上游。

## 5. 验证清单

- 文件内容优先：\`FORMAT=CSV\` 的 OMM CSV 识别、\`FORMAT=JSON\` 的 OMM JSON 识别，JSON 不受旧配置字段名中的 “CSV” 限制。
- 六位 NORAD 样本 \`100001\` 不可转回传统五位 TLE。
- SatNOGS / JE9PEL / SatYAML 的无效响应不得覆盖已经成功导入的数据库。
- 自定义源列表中第一来源高于后续来源；临时手动导入严格按 72 小时到期回退。
- 下载镜像从 200 返回 HTML、JSON 错误对象、空数组、残缺 ZIP 时拒绝并尝试下一**独立**源。
- EOP 数据按 IERS finals 文件结构验证；DE430 必须通过 pinned 文件 hash；独立 Orekit 轨道运行时 Smoke 与安装包测试通过。
- 没有位于中国大陆的真实网络探针时，不能把 GitHub Actions 所在地区的官方站点成功结果等同于“已证实中国大陆所有运营商均可直连”。

