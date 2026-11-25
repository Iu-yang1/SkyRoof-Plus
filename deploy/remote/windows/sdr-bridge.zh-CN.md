# SDR IQ companion（开发版）

在台站 Windows x64 本机启动，可选工具；不增加强制常驻进程。先在遥测面板将输入选择为原生 IQ (iq-cf32)，选择正确卫星、modulation、framing、baud 等并启用接收。工具连接同台站 DSP 的私有 Named Pipe，不开放公网原生设备控制。

```powershell
dotnet tools/SdrBridge/SkyRoof.Remote.SdrBridge.dll --abi
dotnet tools/SdrBridge/SkyRoof.Remote.SdrBridge.dll --list
dotnet tools/SdrBridge/SkyRoof.Remote.SdrBridge.dll --rx skyroof-alpha-dsp "driver=plutosdr" 435000000 2400000 435100000 default
dotnet tools/SdrBridge/SkyRoof.Remote.SdrBridge.dll --replay capture.iq skyroof-alpha-dsp 50000 -4489
```

`--rx` 参数依次为 pipe、Soapy 设备参数、硬件中心频率 Hz、采样率 Hz、目标频道 RF Hz、增益 dB 或 default；设备及驱动是否支持该采样率和调谐频率由 SoapySDR 硬件返回决定。读取实际采样率与中心频率后做数字混频及抗混叠 FIR 重采样，输出 48 kHz little-endian CF32。目标完整 36 kHz 通带必须位于设备 Nyquist 区间内。输入与输出时钟采用连续样本计数；硬件 overflow 结束原 epoch 并重新初始化 FIR。该通路不控制发射，不是宽带多频道频谱服务器。

`--replay` 使用原始 little-endian interleaved float32 I/Q 文件，参数为采样率与待混频的基带频偏 Hz。无需 SDR 硬件。文件以处理速度回放，不是 RF 墙钟时序验证。实际 SDRplay API 服务/驱动等仍需用户正确安装。ABI 依据 [SoapySDR 0.8 Device C API](https://pothosware.github.io/SoapySDR/doxygen/0.8.0/Device_8h.html)。

当前尚未取得新的 CI 与 RSP/Pluto 实机证据，不作为正式发布功能验收。
