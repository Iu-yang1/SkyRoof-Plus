# SkyRoof Remote V1.7 — FT4 自动 QSO 开发增量

本功能复用独立 DSP 的原生 FT4 编解码与 Device 定时 PCM 发射。它不是原桌面 FT4 工作台的完整迁移。

## 操作

在 Web Digital 工作台先设置 TX MHz、AF Hz、PCM LEVEL 和 TX SLOT，再填写 MY CALL、MY GRID。DX CALL 留空为 CQ；填写对方标准呼号则以网格消息开始呼叫。ATTEMPTS PER STAGE 为无进展重发次数；收到有效的下一阶段回复后重新计数。START AUTO QSO 启动单次自动通联，STOP AUTO QSO 取消。完成后不会自动开始下一次 CQ 或自动提交日志。

自动流程在 Host 内执行，以站端 UTC 的 7.5 秒时隙为基准；浏览器读取 NDJSON 进度，不承担精确定时。只消费对应接收时隙的解码结果，锁定对方呼号。无完整接收窗则跳过对应发射机会；当 Device 不再有足够准备时间时，不重放旧波形。授权撤销、HTTP 断连、Device 拒绝、音源代际变化会终止自动事务。手动单时隙发射保持原有功能。

## 验收

- Foundation：标准 CQ/定向交换、呼号锁定、无关消息、过时阶段、报告、RR73/73、尝试次数。
- 独立 DSP：原生编码/解码的定向网格、报告及结束消息经过自动交换使用的解析器。
- Windows 发布包：真实认证 HTTP、Host → DSP → Station → Device → 模拟原生 UDP；注入合成 decoder result ring，核对三次定时发射的逐样本 PCM 和最终 QSO 报告。该测试不是实际空口通联或完整 ADC 到 RF 闭环。
- Web：NDJSON 分片和异常断流。全部结果以对应提交的实际 CI 为准。

## 未完成

非标准/散列呼号、点击任意 CQ 自动选频、自动频率追踪、通联日志、AP 上下文优化、真实 ADC/UTC 和 RF 延迟校准、实际 RF/公网测试仍需后续实现或验证。V1.7 交付范围不因此缩减，仍为开发 Draft。
