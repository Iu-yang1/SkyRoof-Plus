# Remote Q65 原生编解码 V1

## 依赖与范围

- 固定 [mfsk-core-tx5dr 8a9d74a](https://github.com/Iu-yang1/mfsk-core-tx5dr/tree/8a9d74a93100c5ddbc06d14887c5ea67f50f7e9f) 的 C ABI v3；原 Rust Q65 FEC、波形和解码器。不是 FT4 复用调制器，也不调用远端 WSJT-X UI。
- 当前 ABI 注册的十种子模式：15A、30A、60A/B/C/D/E、120D/E、300A。子模式几何由 DLL 读回，禁止静默混淆 slot / spacing。
- 独立 DSP 进程保留 native decoder handle，按 PCM sample clock 对齐 UTC，丢样、源 epoch、配置变化、跳过的 slot 重置连续平均。EME DELAY 和 AVG 通过 native 参数位传递。输入 UTC 暂按发布锚点估计，非 ADC 硬件时间戳；后续需校准实际 RX 延时。
- Web Q65 EME RECEIVE + SEND ONE Q65 SLOT。认证 GET /api/v1/dsp/q65，Operator/Admin PUT 配置与 POST /transmit。TX 由原生 48 kHz PCM、SHA256、Station 单调时钟和 Device 单次 claim 控制。支持显式取消，不自动重复旧时隙。300 秒 PCM 最大约 28.8 MB，站内 IPC 接收容量 32 MiB。
- 原生 DLL 缺失时明确返回不可用；关闭 Q65 接收不影响 FT4/CW/AF。
- 尚未完成：真实 EME 双向通联、Q65 自动 QSO、呼号 AP 上下文设置、Q65 Pileup / Max Drift / fading 参数、月面多普勒端到端联动、发射路径逐子模式实测。

## 构建与验证

独立 Windows CI checkout 固定源，生成 Cargo.lock，使用 Rust 1.98.1 和静态 CRT 构建 mfsk-ffi。验证 ABI、十种几何、30A/60B native encode-decode、15A 48 kHz PCM/UTC slot/gap/epoch。保存 DLL、GPL 许可证、生成的 Cargo.lock 和对应源码链接。开发包依赖通过的 Q65 runtime job，并从实际发布目录重新运行 self-test。首次运行因为源没有 Cargo.lock 而失败，已修改生成后 --locked；新的 CI 结论仍需确认。生成依赖锁不是预先固定的可重现锁，后续应提交经过验证的锁文件或固定其哈希。

编译、模拟验证不等同于真实 RF/EME 验收。
