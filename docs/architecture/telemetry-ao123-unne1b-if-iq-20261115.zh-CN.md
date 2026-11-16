# SkyRoof 遥测修复：AO-123、UNNE-1B 与 IF/IQ Recorder（P0–P3）

> **历史设计记录（2026-11-14/15）：** 本页保留当时方案与测试假设，不作为 v1.82 现行操作说明。v1.82 已更新 `Shared12kIf` 的独立 USB/LAN IF 接收、AFSK 处理、连续性恢复及 `*.if.wav` 回放，见[当前中文遥测手册](../zh-cn/fork-guide/telemetry-if-af.md)或[English guide](../fork-guide/telemetry-if-af.md)。

实施日期：2026-11-15。分支：`fix/telemetry-ao123-unne1b-recorder-if-iq-20261115`。

## 任务范围及独立验收边界

- P0：SatYAML 接收发射机按 NORAD + 下行频率（±5 kHz）+ 波特率关联，并在已有卫星数据库缓存加载时从已下载的 SatYAML 重新解析，避免历史配置将数字遥测关联到另一频率的 FM 发射机。
- P1：UNNE-1B / NORAD 101101：对已恢复的 FSK 软符号识别 `0xBF35`、地址 `C`、类型化包长，先校验加扰态的 CRC-CCITT-FALSE，再使用与 AMSAT-EA GENESIS 兼容的 x17+x12+1 解扰。不要复用预编译组件中仅支持 HADES-SA 的包长表与 Codec2 构造器。TYPE15 的 CODEC2 原始子帧保留，不虚构能够播发语音。
- P2：AO-123 / ASRTU-1 / NORAD 61781：435.400 MHz BPSK 9600 Bd，CCSDS Concatenated、differential precoding、RS conventional 223。参考 gr-satellites 的 `TM short KISS`，从通过 RS 验证的 TM frame 中按 VC0、VC2 重组 KISS，输出 CCSDS Space Packet 的版本、APID、序号和长度等字段。无公开设备量纲定义的 Space Packet 数据保留原始 HEX，不臆造工程标定。
- P2：UNNE-1B 温度、瞬时功率、状态、太阳传感器、星历及时间序列等，根据 AMSAT-EA v1.04 说明书显示明确标注单位或原始数值。NEBRIJA 官方公开的 15 字节 TYPE10 示例用作业务层回归样本。
- P3：原始 IF 的采集与完整写入。Recorder `Record Audio → Raw 12-kHz IF`：48 kHz 单声道 Float WAV，`.if.wav`；`Record I/Q → I/Q Source → 12-kHz IF → complex I/Q`：48 kHz 双声道 Float WAV，`.iq.wav`，左 I/右 Q。两者优先使用运行中的 IF→AF `RawIfAvailable`，否则通过指定的 USB IF 录音端口采集；声卡声道沿用 IF→AF L/R 设置，绝不能把反相双声道相加。
- Recorder 中的 `SkyRoof SDR I/Q` 保持兼容。IQ WAV 从 Recorder 回放时直接送到遥测输入，不依赖实时卫星高于地平线。回放帧禁止自动向 SatNOGS 上传；从 TM 解析的 KISS 包不会被再次上传成原始射频帧。

## 电台及操作步骤

1. IC-9700 使用 USB 声卡接收 IF 时将 USB AF/IF Output 设为 IF；在 Windows 声卡和 SkyRoof 中选择 48 kHz，确认所选左/右声道含真实 12 kHz IF。若选 Direct LAN 的 IF→AF，应先确认认证 LAN IF 服务启动。
2. 打开 `Settings → 12 kHz IF → AF / WSJT-X`，检查 IF 输入设备及声道。已运行 IF→AF 时 Recorder 与 Telemetry 可共享原始 PCM，不重复占用设备。
3. 录原始 IF：`Recorder → Record ▼ → Audio Source → Raw 12-kHz IF`，之后点 Record Audio，保存为无损 `*.if.wav`。原始 IF 不能输出为 MP3。
4. 录 IQ：`Recorder → Record ▼ → I/Q Source → 12-kHz IF -> complex I/Q`，点 Record I/Q，保存为 `*.iq.wav`。这不是原生射频 SDR 的宽带 IQ，而是以 12 kHz IF 为中心数字变频后的复数信号，采样率保持 48 kHz。
5. 离线遥测回放：在 Telemetry 选择对应卫星与数字遥测发射机，并使 Telemetry RX Input 为 `SdrIq`；Recorder 载入 `*.iq.wav` 并播放。播放数据走与 SDR IQ 相同的流水线，支持下行窗口外回放，不进行 SatNOGS 上传。

## 测试要求与限制

- 自动测试：SatYAML 的 ASRTU/传输层解析，UNNE 同步/CRC/扰码与报文长度、TM short-KISS 流跨帧/跨 VC 重组、WAV IQ 浮点双通道读写，以及设置项中文显示。
- 在拥有真实 436.888 MHz UNNE-1B 和 435.400 MHz AO-123 IQ/WAV 语料之前，不报告 BER、PER、实测接收灵敏度或“实机已完成”的结论。原始 IF 与复数 IQ 的文件内容可由用户机上录制后与 UZ7HO / gr-satellites 逐帧比较。
- UNNE-1B 2026-02-11 官方协议写 FSK tone separation 为 1125 Hz，Nebrija 官网写约 1.64 kHz；二者存在差异。当前可保持盲 FSK 估计或由用户调整偏差，不擅自固定未经实录验证的数值。
- **不更改版本号、不自动创建 Release。**

参考：
- https://github.com/daniestevez/gr-satellites/blob/main/python/satyaml/ASRTU-1.yml
- https://github.com/daniestevez/gr-satellites/blob/main/python/components/transports/tm_kiss_transport.py
- https://www.amsat-ea.org/app/download/14192756/AMSAT%2BEA%2B-%2BTransmissions%2Boverview%2Bfor%2BUNNE-1B.pdf
- https://www.nebrija.com/unne1b/en/datos.html
