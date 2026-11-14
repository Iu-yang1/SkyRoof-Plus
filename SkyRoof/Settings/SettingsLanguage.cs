using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace SkyRoof
{
  // The persisted language is an enum value, never a translated string.
  public enum UiLanguage { English = 0, SimplifiedChinese = 1 }

  /// <summary>
  /// Localization of PropertyGrid presentation metadata. Property names,
  /// HelpKeyword, converters and JSON serialization are left untouched.
  /// This is intentionally separate from the model objects.
  /// </summary>
  internal static class SettingsLanguage
  {
    internal static UiLanguage Current { get; private set; } = UiLanguage.SimplifiedChinese;
    private static bool registered;
    private static Type[] types = Array.Empty<Type>();
    private static readonly Dictionary<string, string> Chinese = new(StringComparer.OrdinalIgnoreCase)
    {
      // Settings sections.
      ["Orbit / Ephemeris Sources"] = "轨道与星历来源",
      ["User"] = "用户信息", ["Audio"] = "音频",
      ["OutputStream"] = "音频输出流", ["Telemetry"] = "卫星遥测",
      ["Announcements"] = "语音播报", ["Waterfall"] = "瀑布图",
      ["CAT Control"] = "CAT 电台控制", ["Icom LAN Spectrum"] = "ICOM LAN 频谱",
       ["Icom Direct LAN"] = "ICOM Direct LAN 音频与仪表",
      ["ONVIF Camera"] = "ONVIF 摄像头", ["Rotator Control"] = "天线旋转器控制",
      ["SoapyRemote"] = "远程 SDR", ["Amsat Satellite Status"] = "AMSAT 卫星状态",
      ["QSO Entry"] = "通联日志", ["FT4 Console"] = "FT4 控制台",
      ["CW Console"] = "CW 控制台", ["FM / CW / SSB AFC"] = "FM / CW / SSB 自动频率控制",
      ["Transverter"] = "变频器", ["OrbitSources"] = "轨道来源",
      ["Satellites"] = "卫星", ["Sdr"] = "SDR", ["Amsat"] = "AMSAT",
      ["QsoEntry"] = "QSO 日志", ["Ft4Console"] = "FT4 控制台",
      ["CwConsole"] = "CW 控制台", ["Afc"] = "自动频率控制 AFC",
      ["Rotator"] = "天线旋转器", ["Cat"] = "CAT 电台控制",
      ["IcomLanSpectrum"] = "ICOM LAN 频谱",
       ["IcomDirectLan"] = "ICOM Direct LAN",
      // Common model labels used throughout the settings PropertyGrid.
      ["Enabled"] = "启用", ["Auto start"] = "自动启动", ["AutoStart"] = "自动启动",
      ["Host"] = "主机地址", ["Port"] = "端口",
      ["Name"] = "名称", ["Device"] = "设备", ["Mode"] = "模式",
      ["Volume"] = "音量", ["Gain"] = "增益",
      ["Sample rate"] = "采样率", ["Bandwidth"] = "带宽",
      ["Frequency"] = "频率", ["Timeout"] = "超时",
      ["Poll interval"] = "轮询间隔",
      ["Delay"] = "延迟", ["Theme"] = "主题",
      ["Latitude"] = "纬度", ["Longitude"] = "经度",
      ["Altitude"] = "海拔高度", ["Call"] = "呼号",
      ["Square"] = "网格定位", ["Callsign"] = "呼号",
      ["Receive audio"] = "接收音频", ["Transmit audio"] = "发射音频",
       ["Meter readback"] = "仪表读回", ["AF Transmission (RX)"] = "接收 AF 传输",
       ["MOD Transmission (TX)"] = "发射 MOD 传输",
       ["RX sample rate"] = "接收采样率", ["TX sample rate"] = "发射采样率",
       ["RX codec"] = "接收编码", ["TX codec"] = "发射编码",
       ["RX prebuffer (ms)"] = "接收预缓冲 (ms)",
       ["TX prebuffer (ms)"] = "发射预缓冲 (ms)",
       ["Speaker"] = "扬声器", ["Microphone"] = "麦克风",
       ["Speaker volume (%)"] = "扬声器音量 (%)",
       ["Microphone gain (%)"] = "麦克风增益 (%)",
       ["Retransmission Audio Data"] = "音频数据重传",
       ["Enable meters"] = "启用仪表", ["S-meter"] = "S 表",
       ["Power / PWR"] = "发射功率 PWR", ["ALC"] = "ALC",
       ["SWR"] = "驻波比 SWR", ["Meter poll interval (ms)"] = "仪表轮询间隔 (ms)",
       ["Radio IPv4 address"] = "电台 IPv4 地址",
      ["CI-V LAN port"] = "CI-V LAN 端口",
      ["Scope band"] = "频谱接收机", ["Scope source"] = "频谱数据来源",
      ["Scope control path"] = "频谱控制路径",
      ["Scope edge number"] = "固定频谱边界编号",
      ["Scope reference level"] = "频谱参考电平",
      ["Scope sweep speed"] = "频谱扫描速度",
      ["Scope during TX"] = "发射时显示频谱",
      ["CENTER type display"] = "CENTER 显示基准",
      ["Scope VBW"] = "频谱视频带宽 VBW",
      ["Fixed/scroll marker position"] = "固定/滚动模式标记位置",
      ["SkyCAT scope TCP port"] = "SkyCAT 频谱 TCP 端口",
      ["Direct LAN control port"] = "直连 LAN 控制端口",
      ["Direct LAN username"] = "直连 LAN 用户名",
      ["Direct LAN password"] = "直连 LAN 密码",
      ["Direct LAN client name"] = "直连 LAN 客户端名",
      ["Spectrum averaging"] = "频谱平均次数",
      ["Spectrum smoothing"] = "频谱平滑点数",
      ["Spectrum height"] = "频谱显示高度",
      ["Waterfall history"] = "瀑布图历史行数",
      ["Show waterfall"] = "显示瀑布图",
      ["Waterfall brightness"] = "瀑布图亮度",
      ["Waterfall contrast"] = "瀑布图对比度",
      ["Waterfall palette"] = "瀑布图配色",
      ["Estimated RX SSB filter width (Hz)"] = "SSB 接收滤波带宽估计 (Hz)",
      ["Estimated RX SSB-D filter width (Hz)"] = "SSB-D 接收滤波带宽估计 (Hz)",
      ["Estimated RX CW filter width (Hz)"] = "CW 接收滤波带宽估计 (Hz)",
      ["Estimated RX FM/FM-D filter width (Hz)"] = "FM 接收滤波带宽估计 (Hz)",
      ["IF reference frequency (Hz)"] = "IF 参考频率 (Hz)",
      // Localized labels for the main settings sections and device subpanels.
      ["Grid Square"] = "网格定位",
      ["Delay (ms)"] = "延迟 (ms)",
      ["Log Traffic"] = "记录通信数据",
      ["Send Timeout"] = "发送超时",
      ["Receive Timeout"] = "接收超时",
      ["Reconnect Delay"] = "重连延迟",
      ["Ignore Dial Knob"] = "忽略电台调谐旋钮",
      ["Tuning Step (Hz)"] = "调谐步进 (Hz)",
      ["Physical PTT Key"] = "物理 PTT 按键",
      ["Suppress PTT Key"] = "禁用 PTT 按键",
      ["Embedded SkyCAT"] = "内嵌 SkyCAT",
      ["RX CAT"] = "接收 CAT",
      ["TX CAT"] = "发射 CAT",
      ["TCP Port"] = "TCP 端口",
      ["Use Embedded SkyCAT"] = "使用内嵌 SkyCAT",
      ["Show Corrected Frequency"] = "显示校正频率",
      ["Controller Driver"] = "控制器驱动",
      ["Native Protocol"] = "原生控制协议",
      ["Native Transport"] = "原生通信传输",
      ["RC-3040S Motion Profile"] = "RC-3040S 运动曲线",
      ["RC-3040S Motion Planner Enabled"] = "启用 RC-3040S 轨迹规划",
      ["RC-3040S AZ Planned Speed (deg/s)"] = "RC-3040S 方位角计划速度 (°/s)",
      ["RC-3040S EL Planned Speed (deg/s)"] = "RC-3040S 仰角计划速度 (°/s)",
      ["RC-3040S Acceleration (deg/s²)"] = "RC-3040S 加速度 (°/s²)",
      ["RC-3040S Jerk (deg/s³)"] = "RC-3040S 加加速度 (°/s³)",
      ["RC-3040S Motion Hardware Validated"] = "RC-3040S 运动硬件已验证",
      ["RC-3040S Independent Angle Verification"] = "RC-3040S 独立角度核验",
      ["RC-3040S AT Readback"] = "RC-3040S AT 配置读回",
      ["Serial Port"] = "串行端口",
      ["Baud Rate"] = "波特率",
      ["Serial Data Bits"] = "串口数据位",
      ["Serial Parity"] = "串口校验位",
      ["Serial Stop Bits"] = "串口停止位",
      ["Serial Handshake"] = "串口握手",
      ["Serial DTR Enabled"] = "启用串口 DTR",
      ["Serial RTS Enabled"] = "启用串口 RTS",
      ["Native Poll Interval (ms)"] = "原生轮询间隔 (ms)",
      ["Native Response Timeout (ms)"] = "原生响应超时 (ms)",
      ["Native Feedback Reconnect Grace (ms)"] = "反馈断线重连宽限 (ms)",
      ["Allow Open Loop Motion"] = "允许开环旋转",
      ["Custom Point Command"] = "自定义角度指令",
      ["Custom STOP Command"] = "自定义停止指令",
      ["Custom Query Command"] = "自定义查询指令",
      ["Custom Response Format"] = "自定义响应格式",
      ["Rotator Network Host"] = "旋转器网络主机",
      ["Rotator Network Port"] = "旋转器网络端口",
      ["Minimum Azimuth"] = "最小方位角",
      ["Maximum Azimuth"] = "最大方位角",
      ["Azimuth Offset"] = "方位角偏置",
      ["Minimum Elevation"] = "最小仰角",
      ["Maximum Elevation"] = "最大仰角",
      ["Elevation Offset"] = "仰角偏置",
      ["Tracking Step Size"] = "追踪步进",
      ["Position Command Resolution (deg)"] = "位置指令分辨率 (°)",
      ["Position Jump Limit"] = "位置突变限制",
      ["Parking Azimuth"] = "停靠方位角",
      ["Parking Elevation"] = "停靠仰角",
      ["Enable Receive"] = "启用接收",
      ["Audio Source"] = "音频来源",
      ["Radio / USB Capture Device (RX)"] = "电台 / USB 接收设备",
      ["RS-BA1 Playback Endpoint (loopback)"] = "RS-BA1 回环播放端点",
      ["Spectrum Cleanup"] = "频谱清理",
      ["SkyCAT CW Keyer Port"] = "SkyCAT CW 键控端口",
      ["CW Message Macros"] = "CW 宏消息",
      ["WSJT-X UDP Packets"] = "WSJT-X UDP 数据包",
      ["Decoded Messages"] = "解码消息",
      ["Soundcard"] = "声卡",
      ["Watchdog"] = "看门狗",
      ["Use XIT"] = "使用 XIT",
      ["UDP Port"] = "UDP 端口",
      ["Save to File"] = "保存到文件",
      ["Font Size"] = "字体大小",
      ["Background Colors"] = "背景颜色",
      ["Separator"] = "分隔符",
      ["Transmitted Message"] = "发射消息",
      ["Message To Me"] = "发给我的消息",
      ["Message From Me"] = "我发出的消息",
      ["CQ Word"] = "CQ 字样",
      ["Hot Item"] = "高亮项目",
      ["Quality"] = "画面质量",
      ["Show performance counters"] = "显示性能计数器",
      ["Recorder Audio Source"] = "录音音频来源",
      ["Recorder USB AF Capture Device"] = "USB AF 录音设备",
      ["Recorder RS-BA1 Playback Device"] = "RS-BA1 播放设备",
      ["Speaker Audio Device"] = "扬声器音频设备",
      ["FM Squelch"] = "FM 静噪",
      ["SSTV Auto-Save Filter"] = "SSTV 自动保存滤波器",
      ["KISS Server"] = "KISS 服务器",
      ["SatNOGS Upload"] = "SatNOGS 上传",
      ["Satellite List URL"] = "卫星列表网址",
      ["Transmitter List URL"] = "转发器列表网址",
      ["CelesTrak OMM CSV URL"] = "CelesTrak OMM CSV 网址",
      ["Manual Orbit Source URLs"] = "手动轨道来源网址",
      ["AutoTLE URL"] = "AutoTLE 网址",
      ["SatNOGS Orbit URL"] = "SatNOGS 轨道网址",
      ["Auto-download Orekit Data"] = "自动下载 Orekit 数据",
      ["Show Solar-System Targets"] = "显示太阳系目标",
      ["Orekit Data Directory"] = "Orekit 数据目录",
      ["IERS EOP Download Sources"] = "IERS 地球定向参数下载来源",
      ["JPL DE430 Download URL"] = "JPL DE430 星历下载网址",
      ["Orekit Data Status"] = "Orekit 数据状态",
      ["Active Orbit Backend"] = "当前轨道计算引擎",
      ["Orbit Data Quality"] = "轨道数据质量",
      ["Gain, dB"] = "增益 (dB)",
      ["VAC Device"] = "虚拟音频线设备",
      ["Enable Embedded SkyCAT"] = "启用内嵌 SkyCAT",
      ["Radio Model"] = "电台型号",
      ["CI-V Serial Port"] = "CI-V 串口",
      ["Serial Baud Rate"] = "串口波特率",
      ["Main CAT TCP Port"] = "主 CAT TCP 端口",
      ["WSJT-X Proxy Enabled"] = "启用 WSJT-X 代理",
      ["WSJT-X TCP Port"] = "WSJT-X TCP 端口",
      ["Spectrum TCP Port"] = "频谱 TCP 端口",
      ["Switch Enabled"] = "启用远程切换",
      ["Switch TCP Port"] = "切换服务 TCP 端口",
      ["CW Keyer Enabled"] = "启用 CW 键控",
      ["CW Keyer TCP Port"] = "CW 键控 TCP 端口",
      // AFC property grid. The source class intentionally carries Chinese
      // metadata, which is translated in the English branch below.
      ["Enabled AFC"] = "启用 AFC",
      // IC-9700 native LAN audio, meter and scope settings added in 2026-11.
      ["RX speaker playback buffer (ms)"] = "接收扬声器播放缓冲 (ms)",
      ["Auto select LAN MOD input"] = "自动选择电台 LAN 调制输入",
      ["TX network prebuffer (ms)"] = "发射网络预缓冲 (ms)",
      ["TX microphone capture buffer (ms)"] = "发射麦克风采集缓冲 (ms)",
      ["TX software jitter prefill (ms)"] = "发射软件抖动预填充 (ms)",
      ["TX software jitter maximum (ms)"] = "发射软件抖动缓冲上限 (ms)",
      ["Voice AGC"] = "发射语音 AGC",
      ["Enable voice AGC"] = "启用语音 AGC",
      ["Target RMS (dBFS)"] = "目标均方根电平 (dBFS)",
      ["Maximum gain (dB)"] = "最大补偿增益 (dB)",
      ["Noise floor (dBFS)"] = "噪声门限 (dBFS)",
      ["Attack (ms)"] = "AGC 启动时间 (ms)",
      ["Release (ms)"] = "AGC 恢复时间 (ms)",
      ["Peak ceiling (dBFS)"] = "峰值上限 (dBFS)",
      ["SkyCAT CAT control TCP port"] = "SkyCAT 频谱控制 CAT TCP 端口",
      ["Scope radio model"] = "频谱电台型号",
      ["Native LAN FAST sweep"] = "原生 LAN 快速频谱扫描"
    };
    // PropertyGrid descriptions belong to the *owning type + property*, so
    // similarly named controls elsewhere never pick up LAN-specific help.
    private static readonly Dictionary<string, string> ChineseDescriptions = new(StringComparer.Ordinal)
    {
      ["IcomDirectLanSettings.RetransmitAudio"] =
        "响应已经关联到本次会话的 UDP 发射音频重传请求；重传绝不会自行触发 PTT。",
      ["IcomDirectLanReceiveSettings.Enabled"] =
        "启用电台到计算机的 LAN 接收音频。监听请选择 AF；使用 12 kHz IF→AF 解调链路时请选择 IF。",
      ["IcomDirectLanReceiveSettings.PrebufferMs"] =
        "Windows 本地扬声器播放缓冲，20–500 ms，默认 120 ms；不改变电台或网络 UDP 的接收缓冲。",
      ["IcomDirectLanReceiveSettings.SpeakerDeviceId"] =
        "选择 LAN 接收音频的 Windows 播放设备；留空可只向 IF→AF 处理链路送入音频。",
      ["IcomDirectLanReceiveSettings.SpeakerVolume"] =
        "本地扬声器音量，0–100%；不会调整电台 AF/RF 增益或原始 LAN 音频报文。",
      ["IcomDirectLanTransmitSettings.Enabled"] =
        "协商 PC→IC-9700 的 LAN 调制音频；发射需 CAT PTT 为 ON，并确保电台选择 LAN 作为 MOD 输入。",
      ["IcomDirectLanTransmitSettings.AutoSelectLanModInput"] =
        "开启后读取电台 DATA OFF MOD、DATA MOD；仅在实际 CAT PTT 发射且最近有 PC TX PCM 时，将已确认的非 LAN 输入切换为 LAN 并回读核对。断开软件后电台可能仍保持 LAN；关闭此选项可手动选择。",
      ["IcomDirectLanTransmitSettings.PrebufferMs"] =
        "与 IC-9700 协商的网络 TX 音频预缓冲，20–500 ms，默认 150 ms；不控制 Windows 采集缓冲。",
      ["IcomDirectLanTransmitSettings.CaptureBufferMs"] =
        "Windows WASAPI 麦克风采集缓冲，20–500 ms，默认 150 ms；与电台网络预缓冲独立。",
      ["IcomDirectLanTransmitSettings.JitterPrefillMs"] =
        "20 ms PCM 定时发送器启动时的预填充量，0–250 ms，默认 60 ms；数值越大通常越能吸收采集抖动，但延迟也增加。",
      ["IcomDirectLanTransmitSettings.JitterMaximumMs"] =
        "本地待发 PCM 的最大积压量，60–500 ms，默认 250 ms；超出时丢弃旧采样，不能小于启动预填充量。",
      ["IcomDirectLanTransmitSettings.MicrophoneDeviceId"] =
        "Windows 麦克风录音设备；数字模式也可选择虚拟声卡。只有 CAT PTT 发射期间才向电台发送该音频。",
      ["IcomDirectLanTransmitSettings.MicrophoneGain"] =
        "AGC 前的固定 PCM 增益，0–200%；100% 为 0 dB，200% 约为 +6.02 dB，不等于电台发射功率。",
      ["IcomDirectLanTransmitSettings.Agc"] =
        "可选的本地语音自动增益与峰值限制；SSB/FM 语音可启用，FT4/FT8/Q65 等数字模式通常应保持关闭。",
      ["IcomLanTxAgcSettings.Enabled"] =
        "启用发射语音自动增益控制。默认关闭；对数字调制音频应保持关闭，以免改变校准电平。",
      ["IcomLanTxAgcSettings.TargetRmsDbfs"] =
        "语音目标均方根电平，默认 −18 dBFS；这是软件 PCM 电平，不是 IC-9700 ALC 或射频功率读数。",
      ["IcomLanTxAgcSettings.MaximumGainDb"] =
        "轻声语音的最大增益提升，默认 +12 dB；低于噪声门限的音频不会被提升到语音目标。",
      ["IcomLanTxAgcSettings.NoiseFloorDbfs"] =
        "AGC 噪声门限，默认 −48 dBFS；低于此电平时停止额外提升，减少背景噪声泵吸。",
      ["IcomLanTxAgcSettings.AttackMs"] =
        "声音突然增大时降低增益的时间常数，默认 40 ms；数值越小响应越快。",
      ["IcomLanTxAgcSettings.ReleaseMs"] =
        "声音变小时恢复增益的时间常数，默认 450 ms；较长的恢复时间可减轻音量泵吸。",
      ["IcomLanTxAgcSettings.PeakCeilingDbfs"] =
        "AGC 开启时的 PCM 峰值上限，默认 −3 dBFS；不取代电台的 ALC。",
      ["IcomDirectLanMeterSettings.PollIntervalMs"] =
        "CI-V 15 xx 仪表轮询间隔，100–2000 ms；按顺序读取，不会并行突发查询。",
      ["IcomLanSpectrumSettings.RadioModel"] =
        "指定解析 CI-V 27 00 频谱的电台型号；IC-705/IC-905 为单频谱接收机，不修改 CAT/PTT 设置。",
      ["IcomLanSpectrumSettings.DirectLanFastSweep"] =
        "Direct LAN 会话启动时发送一次 CI-V 27 1A FAST；会改变电台侧扫描速度，取消勾选可保留电台原有设置。",
      ["IcomLanSpectrumSettings.ScopeBand"] =
        "自动选择当前收到的频谱；MAIN 或 SUB 仅显示指定接收机的 CI-V 27 00 数据。",
      ["IcomLanSpectrumSettings.Source"] =
        "选择 SkyCAT 频谱流、RS-BA1 被动抓包或独立鉴权 Direct LAN；Direct LAN 需要手动启动。",
      ["IcomLanSpectrumSettings.ControlPath"] =
        "频谱数据和控制链路相互独立。自动模式跟随来源；RS-BA1 被动模式默认只读，可明确选择 SkyCAT 控制。",
      ["IcomLanSpectrumSettings.SkyCatScopePort"] =
        "SkyCAT 输出原生频谱帧的本机 TCP 端口，通常为 4535；仅用于接收波形。",
      ["IcomLanSpectrumSettings.SkyCatControlPort"] =
        "独立的频谱 MODE/SPAN/REF 控制专用 SkyCAT CAT TCP 端口，通常为 4532；内嵌 SkyCAT 使用已配置的主 CAT 端口，不会接管 PTT 或多普勒。"
    };
    private static readonly Dictionary<string, string> AfcEnglish = new(StringComparer.Ordinal)
    {
      ["InputDeviceId"] = "AFC audio input (12 kHz IF)",
      ["Control"] = "AFC control level",
      ["InputSource"] = "IF audio source",
      ["LoopbackDeviceId"] = "Remote IF playback loopback device",
      ["IfReferenceHz"] = "IF reference frequency (Hz)",
      ["RfIfSign"] = "RF/IF frequency polarity (0=Auto, ±1=Manual)",
      ["TargetOffsetHz"] = "Target IF search offset (Hz)",
      ["DesiredOffsetHz"] = "Desired centered IF offset (Hz)",
      ["SsbPilotEnabled"] = "SSB pilot reference available",
      ["SpectrumRefLevelDb"] = "IF spectrum REF LEVEL (dB)",
      ["MaxCorrectionHz"] = "Maximum AFC correction (Hz)",
      ["FmDeadbandHz"] = "FM tuning deadband (Hz)",
      ["ToneDeadbandHz"] = "CW/SSB tuning deadband (Hz)",
      ["Enabled"] = "Enable AFC"
    };
    private static readonly Dictionary<string, string> AfcEnglishHelp = new(StringComparer.Ordinal)
    {
      ["InputDeviceId"] = "Windows recording device carrying the radio's 12 kHz USB IF. Use a separate input if another application needs AF audio.",
      ["LoopbackDeviceId"] = "Playback or virtual speaker endpoint carrying remote 12 kHz IF, not a demodulated AF-only output.",
      ["RfIfSign"] = "Zero learns polarity from stable signals and CAT readback. Only automatic CAT correction requires a known polarity.",
      ["TargetOffsetHz"] = "Search center for a CW/pilot reference. You can also click the live IF spectrum to select a target.",
      ["DesiredOffsetHz"] = "The offset to which Center & Lock moves the selected signal; independent of the target search frequency.",
      ["SsbPilotEnabled"] = "Enable only for a stable recognizable CW/pilot. Suppressed-carrier speech SSB cannot establish absolute AFC reference.",
      ["SpectrumRefLevelDb"] = "Display reference for the 12 kHz IF spectrum/waterfall only. Does not change IF audio, AFC estimates or CAT tuning."
    };

    internal static string Text(string chinese, string english) =>
      Current == UiLanguage.SimplifiedChinese ? chinese : english;

    internal static void Set(UiLanguage language)
    {
      if (!Enum.IsDefined(typeof(UiLanguage), language))
        language = UiLanguage.SimplifiedChinese;
      EnsureRegistered();
      if (Current == language) return;
      Current = language;
      foreach (Type type in types) TypeDescriptor.Refresh(type);
    }

    internal static void EnsureRegistered()
    {
      if (registered) return;
      registered = true;
      types = typeof(Settings).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract &&
          t.Namespace != null && t.Namespace.StartsWith("SkyRoof", StringComparison.Ordinal) &&
          (t == typeof(Settings) || t == typeof(AfcSettings) ||
           t.Name.EndsWith("Settings", StringComparison.Ordinal) ||
           SettingsChineseSupplement.IsNestedSettingsModel(t)))
        .ToArray();
      foreach (Type type in types)
        TypeDescriptor.AddProvider(
          new LocalizedProvider(TypeDescriptor.GetProvider(type)), type);
    }

    private static string DisplayFor(PropertyDescriptor item)
    {
      if (item.ComponentType == typeof(AfcSettings) &&
          Current == UiLanguage.English &&
          AfcEnglish.TryGetValue(item.Name, out string? en)) return en;
      if (Current == UiLanguage.English) return
        SettingsEnglishSupplement.Name(item.ComponentType.Name + "." + item.Name) ?? item.DisplayName;
      if (Chinese.TryGetValue(item.DisplayName, out string? zh)) return zh;
      if (Chinese.TryGetValue(item.Name, out zh)) return zh;
      return SettingsChineseSupplement.Name(item.DisplayName) ?? item.DisplayName;
    }

    private static string DescriptionFor(PropertyDescriptor item)
    {
      if (item.ComponentType == typeof(AfcSettings) &&
          Current == UiLanguage.English &&
          AfcEnglishHelp.TryGetValue(item.Name, out string? en)) return en;
      if (Current == UiLanguage.English) return
        SettingsEnglishSupplement.Description(item.ComponentType.Name + "." + item.Name) ?? item.Description;
      if (Current == UiLanguage.SimplifiedChinese)
      {
        string key = item.ComponentType.Name + "." + item.Name;
        if (OrbitSettingsLocalization.Description(key) is string orbitHelp)
          return orbitHelp;
        if (ChineseDescriptions.TryGetValue(key, out string? zh))
          return zh;
        if (SettingsChineseSupplement.Description(key) is string extra) return extra;
      }
      return item.Description;
    }

    private sealed class LocalizedProvider : TypeDescriptionProvider
    {
      private readonly TypeDescriptionProvider parent;
      internal LocalizedProvider(TypeDescriptionProvider parent) => this.parent = parent;

      public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object? instance) =>
        new LocalizedDescriptor(parent.GetTypeDescriptor(objectType, instance));
    }

    private sealed class LocalizedDescriptor : CustomTypeDescriptor
    {
      internal LocalizedDescriptor(ICustomTypeDescriptor? parent) : base(parent) { }

      public override PropertyDescriptorCollection GetProperties() =>
        Wrap(base.GetProperties());

      public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) =>
        Wrap(base.GetProperties(attributes));

      private static PropertyDescriptorCollection Wrap(PropertyDescriptorCollection items) =>
        new(items.Cast<PropertyDescriptor>().Select(p =>
          (PropertyDescriptor)new LocalizedProperty(p)).ToArray(), true);
    }

    private sealed class LocalizedProperty : PropertyDescriptor
    {
      private readonly PropertyDescriptor source;
      internal LocalizedProperty(PropertyDescriptor source)
        : base(source, source.Attributes.Cast<Attribute>().ToArray()) =>
        this.source = source;

      public override string DisplayName => DisplayFor(source);
      public override string Category => Current == UiLanguage.SimplifiedChinese
        ? SettingsChineseSupplement.Category(source.Category) ??
          OrbitSettingsLocalization.Category(source.Category, Current)
        : source.Category;
      public override string Description => DescriptionFor(source);
      public override bool IsReadOnly => source.IsReadOnly;
      public override Type ComponentType => source.ComponentType;
      public override Type PropertyType => source.PropertyType;
      public override TypeConverter Converter => SettingsChineseSupplement.HasEnum(source.PropertyType)
        ? new SettingsEnumValueConverter(source.PropertyType, source.Converter) : source.Converter;
      public override object? GetEditor(Type editorBaseType) => source.GetEditor(editorBaseType);
      public override bool CanResetValue(object component) => source.CanResetValue(component);
      public override object? GetValue(object? component) => source.GetValue(component);
      public override void ResetValue(object component) => source.ResetValue(component);
      public override void SetValue(object? component, object? value) => source.SetValue(component, value);
      public override bool ShouldSerializeValue(object component) => source.ShouldSerializeValue(component);
      public override void AddValueChanged(object component, EventHandler handler) =>
        source.AddValueChanged(component, handler);
      public override void RemoveValueChanged(object component, EventHandler handler) =>
        source.RemoveValueChanged(component, handler);
    }
  }
}
