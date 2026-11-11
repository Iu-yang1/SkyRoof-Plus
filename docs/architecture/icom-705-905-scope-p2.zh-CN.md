# IC-705 / IC-905 CI-V 频谱接入（P2 协议阶段）

## 本阶段已经实现

在 **Settings → Icom LAN Spectrum → Scope radio model** 选择 `IC-705`、`IC-905` 或默认 `IC-9700`。这只决定 `27 00` 收到的数据如何解析，不会更改主 CAT 电台、发射权限或连接目标。请选择与实际 Scope 数据来源对应的机型。

- IC-705：单路 Scope，5 字节 BCD 中心频率/上下边界
- IC-905：单路 Scope，10 GHz CENTER 使用 **6 字节中心频率 + 5 字节 Span**，FIXED/SCROLL 使用两组 6 字节边界；下边界允许官方定义的 100 GHz 位 `F` 符号
- IC-9700：保留双接收机 MAIN/SUB、原有 5 字节格式及控制和 UI 默认行为
- LAN/WLAN 完整波形 475 点；USB/虚拟串口可按 11 段完成一次 Sweep，接收丢包后沿用原状态机重新同步
- 已支持通过 SkyCAT TCP Scope 流或被动监听已有 RS-BA1 会话进行波形接收

## 操作注意

对于 IC-705 和 IC-905，本阶段的高级 Spectrum 控件（MAIN/SUB 选择、REF、SPAN、VBW 等）**不复用 IC-9700 特有的写入协议**。本地频谱、瀑布图、叠加和解调显示可继续工作。必须由相应的 SkyCAT 驱动或电台设置启用 CI-V Scope 数据输出；只有收到电台真实波形后才显示。

通过 Embedded SkyCAT 操作电台时，CAT 的 Radio Model（IC-705 USB: 3085；无线模型 30850；IC-905: 3090）与本页 Scope radio model 必须一致。USB CAT 与本地声卡 AF/IF 是两条不同链路；本 PR 只交付 Scope 解码，不声称已完成新机型 AF/IF 接入。

**Direct LAN / WLAN（实验性 Scope）** 可以在选择相应的 Scope radio model 后连接 IC-9700 Ethernet、IC-705 WLAN 或 IC-905 Ethernet。建立认证会话后必须从设备能力响应读取具体型号，并与用户所选的解码器型号一致，才发送 `27 10 / 27 11` Scope 控制；CI-V 地址使用会话实际返回值，不硬编码 A2/A4/AC。保留原有会话认证、关联应答、超时重连和波形接收链路。**仅协议级/CI 验收，不等于已经通过 705/905 的实际 WLAN/LAN 连接测试。**

## 测试依据与剩余事项

测试覆盖 5/6 字节黄金帧、10 GHz 6+5 CENTER、6+6 FIXED/SCROLL、负下边界、475 bins 完整 LAN 帧、11 段 USB 帧、越界标记、非法 BCD、IC-705 SUB 拒绝及 IC-9700 回归。均为模拟协议帧，不能替代 IC-705/IC-905 实机端到端测试。

下一阶段需要以实机记录验证：USB CAT/Scope、IC-705 WLAN、IC-905 LAN、AF/12 kHz IF 音频，以及微波卫星多普勒和 PTT 回显。

资料：Icom IC-905 CI-V Reference Guide 第 27–29 页，`https://www.icomjapan.com/support/manual/3792/`；IC-705 CI-V Reference Guide，`https://www.icomjapan.com/support/manual/3064/`。
