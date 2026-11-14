# IC-9700 LAN TX 音频已发送但 ALC / PWR 偏低：诊断流程

## 观测含义

SkyRoof 状态栏 `LAN TX N pkts · MIC −XX dBFS` 只表示本机已向电台的 LAN 音频 UDP 会话发送了 PCM 数据包。这里的 MIC 是**软件发送链的 PCM RMS**，不代表 IC-9700 的射频发射电平。

点击状态栏 **LAN TX** 字样可查看只读诊断，包含：

- CAT PTT 状态（不等于独立电台 RF PTT 读回）；
- 实际发送的 48-kHz PCM RMS 与峰值、电台 LAN 音频连接状态；
- IC-9700 的 CI-V 寄存器读回：`DATA OFF MOD`、`DATA MOD`、`LAN MOD Level`；
- PWR、ALC、SWR 的 CI-V 原始数值和最近读回的新鲜度；陈旧或缺失的读回显示为不可用，不擅自填 0。

如果处于 SSB 发射、MIC RMS 约 −24 dBFS、PWR 0% 和 ALC 4%，优先检查**电台实际发射状态、所用 DATA ON/OFF 的 MOD 来源和 LAN MOD Level 是否为 0%**。虽然 PCM 可以到达 LAN 会话，但电台未必用作当前调制源。如果 LAN MOD Level 已验证为非零，且处于实际 RF 发射状态，再检查音频输入峰值与当前增益。FM 模式的 ALC 变化不能按 SSB 语音的表现推断。

## 推荐步骤

1. 点击 SkyRoof 状态栏 **LAN TX** 打开只读诊断，看 `DATA OFF=...`、`DATA=...`、`LAN MOD Level=...` 的**实际读回**。如果三项未读到，检查 Direct LAN CI-V 连接及电台响应，不假定其值。
2. IC-9700 `MENU → SET → Connectors → MOD Input`：确认**当前实际模式**使用的 `DATA OFF MOD` 或 `DATA MOD` 是 `LAN`。两项独立存在。
3. 检查 `LAN MOD Level`。如果为 0%，先恢复到 50%（官方默认），再通过电台 PWR/ALC 表短时间测试；不在软件中无条件自动修改这个电台设置。
4. 检查电台是否真正进入 TX，以及是否为 SSB、FM 或数据模式。仅 CAT PTT 软件状态不能证明 RF 已发射。
5. 当确认以上事项无误后，再核对 SkyRoof 发射声卡与音频峰值，并适当调节 `Microphone gain`，避免削顶。对于 FT4/FT8/Q65，保持 `Voice AGC` 关闭。

## 代码修正边界

修正了本地 PCM 数据虽已处理、但 PTT 在 UDP 数据包发送前中断时，仍将其标记为“最近已发射音频”的问题。新诊断的 RMS/峰值只反映**至少发送过一个 UDP 包**的帧。未更改电台 PTT、CAT 控制、调制输入、LAN MOD Level、自动音频增益或 RF 保护逻辑。

参考：IC-9700 基本说明书 Connectors/MOD Input、CI-V Reference Guide 命令 `1A 05 0114/0115/0116`，以及 wfview 官方 FAQ（TX 音频/MOD 输入/发射增益）。
