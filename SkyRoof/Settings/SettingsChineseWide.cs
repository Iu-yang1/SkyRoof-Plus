using System;
using System.Collections.Generic;

namespace SkyRoof
{
  /// <summary>
  /// Remaining operator-facing settings beyond the IF/AF receive workflow.
  /// Kept separate from the PropertyGrid provider so its size stays bounded.
  /// </summary>
  internal static class SettingsChineseWide
  {
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
      ["Microwave Tuning Step (Hz)"] = "微波段 CAT 调谐步进 (Hz)",
      ["Microwave CAT Cycle (ms)"] = "微波段 CAT 控制周期 (ms)",
      ["Visible Fields"] = "显示的通联日志字段",
      ["New file every"] = "新建 ADIF 日志周期",
      ["Download Enabled"] = "下载卫星工作状态",
      ["SoapyRemote Host"] = "SoapyRemote 服务器主机",
      ["SoapyRemote Port"] = "SoapyRemote 服务器端口",
      ["RF Low (Hz)"] = "射频频段下限 (Hz)",
      ["RF High (Hz)"] = "射频频段上限 (Hz)",
      ["LO Offset (Hz)"] = "本振频率偏移 (Hz)",
      ["SDR Offset Enabled"] = "启用 SDR 变频偏移",
      ["RX CAT Offset Enabled"] = "启用 RX CAT 变频偏移",
      ["TX CAT Offset Enabled"] = "启用 TX CAT 变频偏移",
      ["SDR Bands"] = "SDR 变频器频段表",
      ["CAT Bands"] = "CAT 变频器频段表",
      ["Camera name"] = "摄像头名称",
      ["ONVIF Device Service URL"] = "ONVIF 设备服务地址",
      ["Camera user"] = "摄像头用户名",
      ["Preferred profile token"] = "首选媒体配置令牌",
      ["Preferred media version"] = "首选 ONVIF 媒体版本",
      ["mpv executable"] = "mpv 播放器程序路径",
      ["Connect on open"] = "打开窗口时连接",
      ["Play on open"] = "打开窗口时播放",
      ["Reconnect after interruption"] = "中断后重新连接",
      ["Rotator Model"] = "旋转器硬件型号",
      ["Ap"] = "先验解码标记 Ap",
      ["SNR"] = "信噪比 SNR",
      ["New file every"] = "新建 ADIF 日志周期",
      ["Transmitter"] = "发射配置",
      ["Receive"] = "接收配置",
      ["Transmit"] = "发射配置"
    };

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
      ["Settings.User"] = "设置呼号、梅登黑德网格定位和海拔高度，供日志、卫星和远端上传功能使用。",
      ["Settings.Cat"] = "配置收发信机 CAT 控制、PTT 热键及内嵌 SkyCAT。",
      ["Settings.Waterfall"] = "设置 SDR 瀑布图显示质量及性能计数器。",
      ["Settings.Rotator"] = "配置旋转器连接、反馈、跟踪、运动参数和停靠位置。",
      ["Settings.IcomLanSpectrum"] = "配置 Icom CI-V/LAN 频谱来源、参考电平、显示方式与瀑布图。",
      ["Settings.IcomDirectLan"] = "配置 Icom LAN 接收音频、发射音频和 CI-V 仪表读取；频谱与登录信息单独管理。",
      ["Settings.SoapyRemote"] = "通过 SoapyRemote 使用其他计算机上的 SDR 设备。",
      ["Settings.QsoEntry"] = "配置通联日志字段及 ADIF 文件切分周期。",
      ["Settings.Ft4Console"] = "配置 FT4 控制台的接收音频、发射音频、瀑布图、WSJT-X UDP 和消息显示。",
      ["Settings.CwConsole"] = "配置 CW 接收来源、解码、SkyCAT CW 发射及宏。",
      ["Settings.Transverter"] = "为 SDR 与收发信机 CAT 控制定义射频变频器本振频率偏移与支持频段。",
      ["Settings.Amsat"] = "自动下载 AMSAT 卫星工作状态资讯。",
      ["Settings.OrbitSources"] = "轨道和星历数据来源、更新设置及 Orekit 数据质量诊断。",
      ["Settings.OnvifCamera"] = "配置 ONVIF 远端摄像头的地址、身份验证、媒体配置与播放器。",
      ["UserSettings.Call"] = "业余无线电呼号，例如 BG5JSU；用于通联日志和需要标记地面站身份的服务。",
      ["UserSettings.Square"] = "梅登黑德网格定位（Maidenhead Grid Square），用于卫星可见性和地面站位置。",
      ["UserSettings.Altitude"] = "地面站海拔高度，单位为米。",
      ["AmsatSettings.Enabled"] = "启用从 AMSAT 网站获取卫星当前工作状态信息。",
      ["SoapyRemoteSettings.Enabled"] = "启用连接另一台计算机上的 SoapySDRServer，远程使用 SDR 硬件。",
      ["SoapyRemoteSettings.Host"] = "远程 SoapySDRServer 的主机名或 IP 地址。",
      ["SoapyRemoteSettings.Port"] = "远程 SoapySDRServer 的服务端口，默认 55132。",
      ["WaterfallSettings.Quality"] = "SDR 瀑布图纹理与频谱的内部显示精度。降低等级可减轻较老 GPU 的负担，不会改变 SDR 采样率。",
      ["WaterfallSettings.ShowPerformanceCounters"] = "在瀑布图滑块窗口显示 FPS、纹理上传及绘制性能计数器。",
      ["CatSettings.Delay"] = "普通 CAT 命令循环间隔，单位毫秒。",
      ["CatSettings.LogTraffic"] = "在日志中记录 CAT 命令通信，供连接与协议排障。",
      ["CatSettings.SendTimeout"] = "CAT TCP 发送超时，单位毫秒。",
      ["CatSettings.ReceiveTimeout"] = "CAT TCP 应答接收超时，单位毫秒；远程或高延迟链路可适当增加。",
      ["CatSettings.ReconnectDelay"] = "CAT 连接中断后的重试等待时间，单位毫秒。",
      ["CatSettings.IgnoreDialKnob"] = "由软件独占频率调谐，忽略电台实体调谐旋钮产生的频率变化。",
      ["CatSettings.TuningStep"] = "发往电台的普通频率量化步进，单位 Hz。",
      ["CatSettings.MicrowaveTuningStep"] = "1 GHz 及以上 CAT 频率量化步进；默认 1 Hz，可保留 1296/2400/5760/10368 MHz 多普勒精度。",
      ["CatSettings.MicrowaveCycleDelayMs"] = "1 GHz 及以上 CAT 控制循环目标间隔，默认 100 ms；不会改变 1 GHz 以下的普通 CAT 循环延迟。",
      ["CatSettings.PttHotkey"] = "全局键盘或 HID 瞬时 PTT 按键：按下发射，松开结束发射；USB 脚踏可选 F13–F24 等专用按键。",
      ["CatSettings.SuppressPttHotkey"] = "是否阻止已绑定的实体 PTT 按键再传递给当前前台程序。",
      ["CatSettings.Embedded"] = "在 SkyRoof 内运行固定版本 SkyCAT；同一个 CI-V 串口只能由一个服务占用。",
      ["CatSettings.RxCat"] = "接收机 CAT 配置，可使用外部 Hamlib rigctld 或内嵌 SkyCAT。",
      ["CatSettings.TxCat"] = "发射机 CAT 配置，可使用外部 Hamlib rigctld 或内嵌 SkyCAT。",
      ["CatRadioSettings.Host"] = "用于 CAT 控制的 rigctld 主机名或 IP 地址。",
      ["CatRadioSettings.Port"] = "rigctld CAT TCP 服务端口，常用 4532。",
      ["CatRadioSettings.Enabled"] = "启用此接收或发射 CAT 连接。",
      ["CatRadioSettings.UseEmbeddedSkyCat"] = "使用内嵌 SkyCAT 的 CAT 服务，忽略外部 Host/Port；需先启用内嵌 SkyCAT。",
      ["CatRadioSettings.ShowCorrectedFrequency"] = "显示包含多普勒等校正量的实际频率（True），或显示名义频率（False）。",
      ["EmbeddedSkyCatSettings.Enabled"] = "在 SkyRoof 内运行 SkyCAT 服务；不要让外部 skycatd 同时打开相同串口。",
      ["EmbeddedSkyCatSettings.Model"] = "选择 SkyCAT 设备定义中的收发信机型号；也可以输入自定义型号 ID 或名称。",
      ["EmbeddedSkyCatSettings.SerialPort"] = "选择电台 CI-V 串行端口，允许手动输入 COM 端口；同一串口不能同时被两个 CAT 服务占用。",
      ["EmbeddedSkyCatSettings.BaudRate"] = "内嵌 SkyCAT 与电台串行通信的波特率。",
      ["EmbeddedSkyCatSettings.CatPort"] = "内嵌 SkyCAT 主 CAT TCP 服务端口，默认 4532。",
      ["EmbeddedSkyCatSettings.WsjtXEnabled"] = "开启提供给 WSJT-X 等程序的 CAT TCP 代理。",
      ["EmbeddedSkyCatSettings.WsjtXPort"] = "WSJT-X CAT TCP 代理端口，默认 4534。",
      ["EmbeddedSkyCatSettings.SpectrumPort"] = "提供 Icom 频谱数据的 SkyCAT TCP 端口，默认 4535。",
      ["EmbeddedSkyCatSettings.SwitchEnabled"] = "启用供 Remote Control Switch 使用的独立 TCP 控制服务。",
      ["EmbeddedSkyCatSettings.SwitchPort"] = "Remote Control Switch 的 TCP 端口，默认 4537。",
      ["EmbeddedSkyCatSettings.CwEnabled"] = "启用 SkyCAT CW 键控服务。",
      ["EmbeddedSkyCatSettings.CwPort"] = "CW 键控 TCP 服务端口，默认 4538。",
      ["Ft4ConsoleSettings.Receive"] = "配置 FT4 接收音频源、声卡与解码带宽。",
      ["Ft4ConsoleSettings.Transmit"] = "配置 FT4 发射音频声卡、增益、看门狗与 XIT。",
      ["Ft4ConsoleSettings.Waterfall"] = "配置 FT4 音频瀑布图的亮度和对比度。",
      ["Ft4ConsoleSettings.UdpSender"] = "将 FT4 解码消息以 WSJT-X 格式通过 UDP 提供给其他程序。",
      ["Ft4ConsoleSettings.Messages"] = "FT4 解码消息的保存、字体大小及颜色设置。",
      ["Ft4ReceiveSettings.AudioSource"] = "选择 FT4 接收来源：内部 SDR 解调音频或外部声卡录音。",
      ["Ft4ReceiveSettings.RxSoundcard"] = "从 Windows 录音设备读取 FT4 音频时使用的声卡。",
      ["Ft4ReceiveSettings.Bandwidth"] = "FT4 音频解码器与瀑布图使用的带宽，单位 Hz。",
      ["Ft4TransmitSettings.Enabled"] = "启用 FT4 控制台的音频发射功能。",
      ["Ft4TransmitSettings.TxSoundcard"] = "FT4 发射音频输出的 Windows 播放设备，可选择数字模式专用虚拟声卡。",
      ["Ft4TransmitSettings.TxGain"] = "FT4 发射音频的增益或衰减，单位 dB。",
      ["Ft4TransmitSettings.TxWatchDog"] = "持续发射超过设定分钟数后停止，防止 PTT 意外保持。",
      ["Ft4TransmitSettings.XitEnabled"] = "FT4 发射时启用收发频率偏移 XIT。",
      ["Ft4WaterfallSettings.Brightness"] = "FT4 音频瀑布图显示亮度。",
      ["Ft4WaterfallSettings.Contrast"] = "FT4 音频瀑布图显示对比度。",
      ["UdpSenderSettings.Enabled"] = "将 FT4 解码结果按 WSJT-X UDP 数据包格式发送给其他软件。",
      ["UdpSenderSettings.Port"] = "WSJT-X 兼容 UDP 数据包的目的端口。",
      ["UdpSenderSettings.Host"] = "WSJT-X 兼容 UDP 接收程序的主机名或 IP 地址。",
      ["Ft4MessagesSettings.ArchiveToFile"] = "将 FT4 解码的消息保存到本地文本文件。",
      ["Ft4MessagesSettings.FontSize"] = "FT4 解码消息窗口的字体大小。",
      ["Ft4MessagesSettings.BkColors"] = "FT4 消息不同类型文本的背景色。",
      ["Ft4BackgroundColors.Separator"] = "FT4 消息之间的分隔符颜色。",
      ["Ft4BackgroundColors.TxMessage"] = "FT4 已发送消息的显示颜色。",
      ["Ft4BackgroundColors.ToMe"] = "发送给本台的消息颜色。",
      ["Ft4BackgroundColors.FromMe"] = "由本台发出的消息颜色。",
      ["Ft4BackgroundColors.CqWord"] = "CQ 呼叫标记的文字颜色。",
      ["Ft4BackgroundColors.Ap"] = "解码器使用先验信息（Ap）标记的颜色。",
      ["Ft4BackgroundColors.Hot"] = "重要或当前操作消息的高亮颜色。",
      ["Ft4BackgroundColors.Snr"] = "用色阶表示接收信号 SNR 的配色。",
      ["QsoEntrySettings.Fields"] = "选择通联日志输入或显示的字段，包括 UTC、频段、模式、卫星、呼号、网格、信号报告等。",
      ["QsoEntrySettings.NewFileEvery"] = "选择按照日、月或年来新建 ADIF 通联日志文件。",
      ["TransverterBand.Name"] = "变频器频段名称。",
      ["TransverterBand.RfLow"] = "此变频器频段允许的最低 RF 频率（Hz，含边界）。",
      ["TransverterBand.RfHigh"] = "此变频器频段允许的最高 RF 频率（Hz，含边界）。",
      ["TransverterBand.LoOffset"] = "变频器本振偏移量（Hz）；换算公式 IF = RF − LO。",
      ["TransverterSettings.SdrOffsetEnabled"] = "将 SDR 本振变频偏移从 SDR 中心频率中扣除。",
      ["TransverterSettings.RxCatOffsetEnabled"] = "向电台发送 RX CAT 频率时扣除变频器本振偏移。",
      ["TransverterSettings.TxCatOffsetEnabled"] = "向电台发送 TX CAT 频率时扣除变频器本振偏移。",
      ["TransverterSettings.SdrBands"] = "定义 SDR 路径使用的变频器 RF 频段和本振偏移；可通过重置恢复默认。",
      ["TransverterSettings.CatBands"] = "定义收发信机 CAT 路径使用的变频器频段和本振偏移；可通过重置恢复默认。",
      ["OnvifCameraSettings.CameraName"] = "摄像头显示名称，例如 Camera 01。",
      ["OnvifCameraSettings.DeviceServiceUrl"] = "ONVIF 设备服务 URL，例如摄像头的 device_service 地址。",
      ["OnvifCameraSettings.Username"] = "ONVIF 摄像头连接用户名。",
      ["OnvifCameraSettings.Password"] = "ONVIF 摄像头连接密码；会写入受保护的配置。",
      ["OnvifCameraSettings.PreferredProfileToken"] = "首选的 ONVIF 视频媒体配置 Profile Token。",
      ["OnvifCameraSettings.PreferMedia2"] = "优先使用 ONVIF Media2 服务接口。",
      ["OnvifCameraSettings.MpvPath"] = "外部 mpv 可执行程序路径，默认 mpv.exe。",
      ["OnvifCameraSettings.AutoConnect"] = "打开摄像头窗口时自动连接设备。",
      ["OnvifCameraSettings.AutoPlay"] = "打开摄像头窗口时自动开始视频播放。",
      ["OnvifCameraSettings.AutoReconnect"] = "摄像头连接中断时尝试重新连接。",
      ["OrbitSourceSettings.Backend"] = "本地轨道计算引擎。当前推荐 Orekit，原 SGP.NET 路径已废弃。",
      ["OrbitSourceSettings.SatellitesUrl"] = "卫星列表数据库下载地址，默认 SatNOGS；留空时沿用本地缓存。",
      ["OrbitSourceSettings.TransmittersUrl"] = "卫星转发器数据库下载地址，默认 SatNOGS；留空时沿用本地缓存。",
      ["OrbitSourceSettings.CelestrakOmmCsvUrl"] = "优先级最高的自动轨道来源，CelesTrak 业余卫星 OMM/GP CSV；OMM 直接传播，不转回旧式 TLE。",
      ["OrbitSourceSettings.CustomTleSources"] = "自定义网址或本地轨道文件，多个条目以分号或换行分隔；优先级低于 CelesTrak OMM，高于内建 AutoTLE 和 SatNOGS。",
      ["OrbitSourceSettings.AutoTleUrl"] = "内建 AutoTLE 备选轨道源；留空可禁用。",
      ["OrbitSourceSettings.TleUrl"] = "最低优先级的 SatNOGS 轨道数据来源；留空不从该源更新。"
    };

    private static readonly Dictionary<string, string> Enums = new(StringComparer.Ordinal)
    {
      ["Ft4AudioSource.SDR"] = "内部 SDR 音频",
      ["Ft4AudioSource.Soundcard"] = "Windows 声卡录音",
      ["CwReceiveAudioSource.SDR"] = "内部 SDR 音频",
      ["CwReceiveAudioSource.WasapiCapture"] = "WASAPI 声卡采集",
      ["CwReceiveAudioSource.RsBa1Loopback"] = "RS-BA1 播放回环",
      ["WaterfallQuality.Auto"] = "自动（最高画质）",
      ["WaterfallQuality.High"] = "高画质",
      ["WaterfallQuality.Medium"] = "中画质",
      ["WaterfallQuality.Low"] = "低画质（较低 GPU 负载）",
      ["NewFileEvery.Day"] = "每天",
      ["NewFileEvery.Month"] = "每月",
      ["NewFileEvery.Year"] = "每年",
      ["RotatorDriverMode.Auto"] = "自动",
      ["RotatorDriverMode.Native"] = "原生设备驱动",
      ["RotatorDriverMode.LegacyRotctld"] = "外部 Hamlib rotctld",
      ["RotatorHardwareModel.Generic"] = "通用旋转器",
      ["RotatorHardwareModel.YaesuG5500"] = "Yaesu G-5500",
      ["Rc3040sMotionProfile.Direct"] = "直接运动",
      ["Rc3040sMotionProfile.PrecisionStop"] = "精确停靠",
      ["Rc3040sMotionProfile.Trapezoidal"] = "梯形速度曲线",
      ["Rc3040sMotionProfile.SCurve"] = "S 曲线速度控制",
      ["RotatorSerialParity.None"] = "无校验",
      ["RotatorSerialParity.Odd"] = "奇校验",
      ["RotatorSerialParity.Even"] = "偶校验",
      ["RotatorSerialParity.Mark"] = "标记校验",
      ["RotatorSerialParity.Space"] = "空格校验",
      ["RotatorSerialStopBits.None"] = "无停止位",
      ["RotatorSerialStopBits.One"] = "1 位",
      ["RotatorSerialStopBits.Two"] = "2 位",
      ["RotatorSerialStopBits.OnePointFive"] = "1.5 位",
      ["RotatorSerialHandshake.None"] = "无握手",
      ["RotatorSerialHandshake.XOnXOff"] = "软件 XON/XOFF",
      ["RotatorSerialHandshake.RequestToSend"] = "RTS 硬件握手",
      ["RotatorSerialHandshake.RequestToSendXOnXOff"] = "RTS 与 XON/XOFF",
      ["IcomLanScopeBand.Auto"] = "自动",
      ["IcomLanScopeBand.Main"] = "MAIN 主接收机",
      ["IcomLanScopeBand.Sub"] = "SUB 副接收机",
      ["IcomLanSpectrumSource.SkyCat"] = "SkyCAT",
      ["IcomLanSpectrumSource.RsBa1"] = "RS-BA1 被动频谱",
      ["IcomLanSpectrumSource.DirectLan"] = "已认证 Direct LAN",
      ["IcomScopeControlPath.Auto"] = "自动",
      ["IcomScopeControlPath.SkyCat"] = "SkyCAT 控制",
      ["IcomScopeControlPath.DirectLan"] = "Direct LAN 控制",
      ["IcomScopeControlPath.ReadOnly"] = "仅接收（只读）",
      ["IcomScopeWaterfallPalette.Classic"] = "经典配色",
      ["IcomScopeWaterfallPalette.Grayscale"] = "灰阶",
      ["IcomScopeWaterfallPalette.Blue"] = "蓝色",
      ["IcomScopeWaterfallPalette.Heat"] = "热力图"
    };

    internal static string? Name(string key) =>
      Names.TryGetValue(key, out string? value) ? value : null;

    internal static string? Description(string key) =>
      Descriptions.TryGetValue(key, out string? value) ? value : null;

    internal static bool HasEnum(Type type) =>
      type.IsEnum && Enum.GetNames(type).Length > 0 &&
      Enums.ContainsKey(type.Name + "." + Enum.GetNames(type)[0]);

    internal static string? EnumName(Type type, object value) =>
      Enums.TryGetValue(type.Name + "." + value, out string? translated)
        ? translated : null;

    internal static object? ParseEnum(Type type, string label)
    {
      foreach (string name in Enum.GetNames(type))
        if (Enums.TryGetValue(type.Name + "." + name, out string? translated) &&
            string.Equals(translated, label, StringComparison.Ordinal))
          return Enum.Parse(type, name);
      return null;
    }
  }
}
