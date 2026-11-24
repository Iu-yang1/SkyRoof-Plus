# SkyRoof Remote — CW 硬件发射开发增量

两条后端共用 Host / Station / Device CW RPC。SkyCAT 使用原 4538 持久 keyer TCP 会话；独立 IC-9700 LAN 使用当前已认证 CI-V socket，无第二套登陆。Web 支持 WPM 读回、文本发送、停止及 F1–F8 本地宏。

## Native SAT 路径

按照 [IC-9700 CI-V Reference Guide](https://www.icomjapan.com/support/manual/2161/)（命令表和 CW message contents），SUB 读取 CW/CW-R、BK-IN（16 47）、WPM（14 0C，0000–0255 packed BCD，6–48 WPM）、实际频率和 TX 状态。Command 17 发送最多 30 字符，17 FF 停止。SUB 事务结束恢复 MAIN 焦点。实际发射仍由电台内部 keyer 产生，不使用 USB-D 音频。

Native CW 事务在发出非幂等消息前保存 PTT journal；停止后确认 RX 才清除。连接丢失时取消当前事务，不重发旧文本。下次连接恢复先尝试 17 FF 并确认 RX。手动 TX 可抢占 CW，数字事务不可抢占当前 CW；跟踪暂缓 TX 调谐而 RX 继续。命令 ACK 及 watchdog elapsed 不代表已测得空口文本完整。

## 验证

- SkyCAT 独立进程真实 TCP keyer fixture：WPM、STOP、TX 仲裁。
- Native UDP fixture：CI-V SUB、CW-R、BCD、BK-IN、原始文本、STOP 和 RX；后续仍需实机校验实际 SAT keyer 行为、断线重连及 Semi/Full BK-IN。
- Host 授权 E2E：匿名拒绝、WPM、错误模式拒绝。
- CW 历史由 DSP 从已有 JSONL 归档重建，独立 session / generation / mode / lane identity，Web 暂展示最近两个文件中最多 100 lane 的最后 4096 字符；跨设备宏同步、TX 历史仍未实现。

所有 CI 结果以具体 commit 为准；本增量不改变 V1.7 完整交付范围。
