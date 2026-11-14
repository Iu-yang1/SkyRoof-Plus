using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace SkyRoof
{
  /// <summary>
  /// Chinese PropertyGrid metadata and operator-facing enum labels.
  /// All keys identify presentation text or CLR members; settings JSON
  /// keys and persisted enum ordinals are never translated.
  /// </summary>
  internal static class SettingsChineseSupplement
  {
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
      ["12 kHz IF → AF / WSJT-X"] = "12 kHz 中频解调为音频 / WSJT-X",
      ["Telemetry RX Input"] = "遥测接收输入",
      ["12 kHz IF capture device"] = "12 kHz IF 录音设备",
      ["AF capture device"] = "AF 音频录音设备",
      ["AF playback loopback device"] = "AF 播放回环设备",
      ["AF tone center (Hz)"] = "AF 载波中心频率 (Hz)",
      ["Recorder AF Capture Device (USB / VAC)"] = "录音 AF 采集设备（USB / 虚拟声卡）",
      ["Recorder RS-BA1 Playback Device"] = "RS-BA1 音频播放设备（回环）",
      ["Audio Device"] = "语音播报声卡",
      ["Voice"] = "语音引擎",
      ["AOS Announcement #1"] = "AOS 升起播报 #1",
      ["AOS Announcement #2"] = "AOS 升起播报 #2",
      ["Position Announcement"] = "卫星位置播报",
      ["Minutes before AOS"] = "距离 AOS 的分钟数",
      ["Degrees Between Announcements"] = "播报角度间隔 (°)",
      ["Message"] = "播报消息模板",
      ["API Key"] = "API 密钥",
      ["Type"] = "输出流类型",
      ["Enable USB IF -> AF"] = "启用 12 kHz IF→AF 解调",
      ["IF input source"] = "原始 IF 输入来源",
      ["12 kHz IF capture (USB)"] = "USB 12 kHz IF 采集设备",
      ["IF capture channel"] = "IF 采集声道",
      ["Demodulation"] = "解调模式",
      ["AF audio route"] = "AF 输出路由",
      ["AF output latency preset"] = "AF 输出延迟预设",
      ["Installed virtual audio cable status"] = "虚拟声卡配对状态",
      ["Virtual microphone input for WSJT-X"] = "WSJT-X 虚拟录音输入",
      ["Virtual audio render device"] = "虚拟声卡播放输出设备",
      ["SSB bandwidth"] = "SSB 音频带宽",
      ["CW bandwidth"] = "CW 音频带宽",
      ["FM bandwidth"] = "FM 信道带宽",
      ["FM voice processing"] = "FM 语音处理",
      ["FM voice squelch"] = "FM 语音静噪",
      ["FM voice squelch threshold (dBFS)"] = "FM 语音静噪门限 (dBFS)",
      ["SSB voice notch"] = "SSB 语音陷波",
      ["SSB voice noise reduction"] = "SSB 语音降噪",
      ["EffectiveSsbLowHz"] = "SSB 有效下限频率 (Hz)",
      ["EffectiveSsbHighHz"] = "SSB 有效上限频率 (Hz)",
      ["EffectiveCwCenterHz"] = "CW 有效中心频率 (Hz)",
      ["EffectiveCwBandwidthHz"] = "CW 有效带宽 (Hz)",
      ["EffectiveFmIfCutoffHz"] = "FM 中频有效截止频率 (Hz)",
      ["EffectiveFmAfCutoffHz"] = "FM 音频有效截止频率 (Hz)",
      ["EffectiveFmDeviationHz"] = "FM 频偏基准 (Hz)",
      ["SSB output trim (dB)"] = "SSB 输出微调 (dB)",
      ["CW output trim (dB)"] = "CW 输出微调 (dB)",
      ["FM output gain (dB)"] = "FM 输出增益 (dB)",
      ["SSB/CW slow automatic gain control"] = "SSB/CW 慢速自动增益",
      ["SSB/CW AGC target RMS (dBFS)"] = "SSB/CW AGC 目标均方根电平 (dBFS)",
      ["SSB/CW AGC max gain (dB)"] = "SSB/CW AGC 最大提升 (dB)"
    };

    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
      ["Receive input"] = "遥测接收输入",
      ["Receive filters"] = "接收滤波器",
      ["Audio enhancements"] = "音频增强",
      ["Misc"] = "常规设置",
      ["Appearance"] = "外观",
      ["Behavior"] = "操作行为",
      ["Data"] = "数据",
      ["General"] = "常规",
      ["Security"] = "安全",
      ["Advanced"] = "高级设置"
    };

    // Missing default Description attributes are supplied as useful Chinese help
    // instead of leaving an empty help panel in the PropertyGrid.
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
      ["Settings.Audio"] = "录音来源、声卡采集和本地监听设置；与电台的 12 kHz IF→AF 解调输入分开配置。",
      ["Settings.OutputStream"] = "将 SDR 的 IQ 或音频输出到虚拟声卡或 UDP；不等于 IF→AF 接收机的独立虚拟音频输出。",
      ["Settings.Telemetry"] = "选择 SDR IQ、真实 12 kHz IF 或 AF 音频进行遥测解码，并配置文件归档、KISS 和 SatNOGS。",
      ["Settings.Announcements"] = "配置卫星升起 AOS、位置提示的语音、播放设备和播报音量。",
      ["Settings.IfAf"] = "将收发信机输出的 48 kHz 采样、12 kHz 实中频解调为 AF，可供录音、遥测及数字模式程序使用。",
      ["TelemetrySettings.InputSource"] = "遥测解码音频来源。SdrIq 沿用 SDR 复数基带；Shared12kIf 共享正在运行的 IF→AF 原始 IF；Capture12kIf 单独打开 USB IF；CaptureAf 和 LoopbackAf 读取解调后的声卡音频；IfAfDemodulatedAf 直接使用内部解调 AF。",
      ["TelemetrySettings.IfCaptureDeviceId"] = "仅 Capture12kIf 模式使用。选择收发信机输出真实 12 kHz IF 的 Windows 录音设备（通常为麦克风 USB Audio CODEC），采样率应为 48 kHz；不要选择只含 AF 的 CABLE Output。若 IF→AF 已占用此设备，建议选择 Shared12kIf。",
      ["TelemetrySettings.AfCaptureDeviceId"] = "仅 CaptureAf 模式使用。选择承载已解调 AF 的 Windows 录音设备，例如虚拟声卡 CABLE Output 或电台 USB AF；不是 12 kHz 原始 IF 输入。",
      ["TelemetrySettings.AfLoopbackDeviceId"] = "仅 LoopbackAf 模式使用。选择 Windows 播放设备而非麦克风；WASAPI 回环会采集这个播放端点上的所有混音。",
      ["TelemetrySettings.AfCenterHz"] = "仅 AF 输入模式有效，用于把实际 AFSK 音频载波搬移到复数基带零频；常见 Bell 202 的 1200/2200 Hz 取中心 1700 Hz。接收 12 kHz IF 时此项不参与处理。",
      ["TelemetrySettings.ArchiveToFile"] = "启用后将解码出的遥测帧、所属卫星和时间等信息按天写入 TelemetryDecodes 文件夹；关闭时仍可实时显示和解码。",
      ["TelemetrySettings.SstvSaveFilter"] = "仅用于 SSTV 图片完成后的自动保存图像降噪；None 不处理、Wiener 维纳滤波、Nlm 非局部均值。不会修改遥测帧 PCM 或其 FSK/GMSK 解码。",
      ["TelemetrySettings.KissServer"] = "将已解码的遥测帧通过本机 KISS-over-TCP 提供给其他应用；展开配置监听端口与开关。",
      ["TelemetrySettings.SatnogsUploader"] = "将符合条件的卫星遥测帧上传 SatNOGS DB；需要 API 密钥，地面信号不上传。",
      ["KissServerSettings.Enabled"] = "通过 KISS-over-TCP 与其他本地软件共享已解码的卫星遥测帧。",
      ["KissServerSettings.Port"] = "KISS TCP 监听端口，默认 8100；客户端应连接运行 SkyRoof 的电脑与该端口。",
      ["SatnogsUploaderSettings.Enabled"] = "启用向 SatNOGS DB (db.satnogs.org) 上传解码帧；需要正确的 API 密钥及卫星身份。",
      ["SatnogsUploaderSettings.ApiToken"] = "你的 SatNOGS DB API 密钥，可从 SatNOGS 用户资料的 Settings → API Key 获取；此项以受保护形式保存。",
      ["AudioSettings.RecorderSource"] = "Auto 优先记录活动中的 SDR 音频；否则记录正在运行的 IF→AF 解调 AF；再回退 RS-BA1 播放回环。IfAfDemodulated 可直接录制解调后的 48 kHz PCM，不必再次采集虚拟声卡。",
      ["AudioSettings.RecorderCaptureDeviceId"] = "仅 WasapiCapture 来源使用。选择 Windows 的 AF 录音端点：电台 USB AF 或虚拟声卡 CABLE Output；这里不是 IF→AF 的 12 kHz IF 采集设备。",
      ["AudioSettings.RecorderLoopbackDeviceId"] = "仅 RsBa1Loopback 来源使用。选择 RS-BA1 正在播放音频的 Windows 播放设备；录音内容包含该设备上的完整混音。",
      ["AudioSettings.SpeakerSoundcard"] = "SkyRoof 原 SDR 的本地声音播放设备；不会自动改变 IF→AF 输出到虚拟声卡的设备。",
      ["AudioSettings.Squelch"] = "原 SDR 切片器的 FM 静噪开关；接收弱信号数据时一般关闭。IF→AF 的 FM 语音静噪在 IF→AF 设置中单独控制。",
      ["OutputStreamSettings.Type"] = "选择 SDR I/Q 或 AF 音频数据输出到 Windows 虚拟声卡（VAC）还是 UDP；与独立 IF→AF 输出路由不同。",
      ["OutputStreamSettings.Gain"] = "输出数据的附加电平增益（dB），不会改变接收电台本身的 RF/AF 增益。",
      ["OutputStreamSettings.Vac"] = "输出流模式为 VAC 时的 Windows 虚拟声卡播放端点，例如 CABLE Input；不要将本项目的两条音频输出写入同一个设备。",
      ["OutputStreamSettings.UdpPort"] = "选择 UDP 输出模式时使用的目标端口，默认 7355。",
      ["AnnouncerSettings.Soundcard"] = "AOS、卫星方位和仰角等语音播报使用的 Windows 播放设备；与遥测 PCM 输入无关。",
      ["AnnouncerSettings.Voice"] = "Windows 语音合成声音；选择已安装的语音引擎后才能播放播报内容。",
      ["AnnouncerSettings.Volume"] = "语音播报音量，范围通常为 0–100；0 表示静音，不影响卫星遥测或 IF→AF 录音电平。",
      ["AnnouncerSettings.Announcement1"] = "第一次 AOS 提前播报。可设置是否启用、提前分钟数和消息模板。",
      ["AnnouncerSettings.Announcement2"] = "第二次 AOS 提示，默认在卫星升起时播报。可单独配置开关和消息。",
      ["AnnouncerSettings.PositionAnnouncement"] = "卫星位置变化的语音播报，可指定触发角度变化量及消息模板。",
      ["AosAnnouncement.Enabled"] = "是否启用这一条 AOS 语音播报规则。",
      ["AosAnnouncement.Minutes"] = "在预计 AOS 卫星升起前多少分钟播报；0 表示升起时。",
      ["AosAnnouncement.Message"] = "AOS 语音播报模板，可包含 {name}（卫星名）和 {minutes}（提前分钟数）等占位符。",
      ["PositionAnnouncement.Enabled"] = "是否启用卫星位置变化的语音播报。",
      ["PositionAnnouncement.Degrees"] = "两次播报之间卫星角位置的最小变化角度（度），默认 10 度。",
      ["PositionAnnouncement.Message"] = "卫星位置播报模板；可使用 {azimuth} 和 {elevation} 作为方位角与仰角占位符。",
      ["IfAfSettings.Enabled"] = "开启实时 12 kHz 实中频到 48 kHz 单声道 AF 的软件解调接收链。",
      ["IfAfSettings.InputSource"] = "UsbAudio 使用电台 USB 录音端点的真实 12 kHz IF；AuthenticatedLanIf 使用已认证的 Icom Direct LAN 接收 IF。两者输入都必须是 48 kHz 采样的 12 kHz IF，而不是 AF。",
      ["IfAfSettings.InputDeviceId"] = "选择电台 USB 音频录音端点（通常麦克风 USB Audio CODEC）。电台 USB AF/IF Output 应设为 IF，采样率 48 kHz；CABLE Output 不是原始 IF。",
      ["IfAfSettings.CaptureChannel"] = "用于解调的 USB 录音声道。按实际电台 USB IF 通道选择左/右，避免读取到空声道。",
      ["IfAfSettings.Mode"] = "FollowRadio 跟随电台当前模式；也可手动选择 USB、LSB、CW 或 FM 解调。此设置不发送 CAT PTT。",
      ["IfAfSettings.AudioRoute"] = "AutoSignedCable 自动识别配对的虚拟声卡播放/录音端点；ExistingVirtualCable 使用你手动选择的播放输出端。",
      ["IfAfSettings.LatencyPreset"] = "WASAPI 共享模式播放缓冲延迟预设（20/40/80/200 ms），较短延迟可能增加设备欠载概率。",
      ["IfAfSettings.DriverStatus"] = "显示虚拟声卡播放/录音端点识别状态；不是驱动文件的密码学签名校验。",
      ["IfAfSettings.VirtualCaptureDevice"] = "供 WSJT-X 等软件选择的虚拟声卡 Windows 录音端点名称。",
      ["IfAfSettings.VacRenderDeviceId"] = "仅手动选择 ExistingVirtualCable 时使用，选择虚拟声卡播放端 CABLE Input；不要选电台用于发射的 USB Audio CODEC 播放设备。",
      ["IfAfSettings.SsbBandwidth"] = "USB/LSB 解调音频带通宽度，预设 1.2–3.3 kHz；通常语音可选 2.7 kHz，数字音频按需选择。",
      ["IfAfSettings.CwBandwidth"] = "以 700 Hz 为中心的 CW 带通滤波，支持 100–1000 Hz；窄带更适合稳定、精确调谐的 CW 信号。",
      ["IfAfSettings.FmBandwidth"] = "FM 信道带宽预设：6.25 kHz（窄带）与 12.5 kHz（常规）；对应不同的 IF/AF 带宽和最大频偏。",
      ["IfAfSettings.FmVoiceProcessing"] = "关闭时保留判频器原始音频，适合数字遥测；50/75 μs 选项只用于相应模拟 FM 语音去加重。",
      ["IfAfSettings.FmVoiceSquelchEnabled"] = "仅对 FM 语音使用的可选信号功率静噪，默认关闭，防止弱信号数字数据被静噪截断。",
      ["IfAfSettings.FmVoiceSquelchDbfs"] = "根据滤波后的复数 IF 功率判断的 FM 语音静噪阈值（dBFS），并非电台 S 表或 SINAD 读数。",
      ["IfAfSettings.SsbVoiceNotch"] = "模拟 SSB 语音陷波选项，可选固定 1000/1500 Hz 或自动跟踪；数字模式和 CW 不使用此模块。",
      ["IfAfSettings.SsbNoiseReduction"] = "可选的温和型频谱降噪，用于模拟 SSB 语音；可能增加约 43 ms 延迟，数字模式、CW、FM 不使用。",
      ["IfAfSettings.EffectiveSsbLowHz"] = "当前 SSB 解调后音频通带的实际设置下限频率（Hz），只读。",
      ["IfAfSettings.EffectiveSsbHighHz"] = "当前 SSB 解调后音频通带的上限频率（Hz），由低频边界与带宽预设计算，只读。",
      ["IfAfSettings.EffectiveCwCenterHz"] = "CW 音频窄带滤波器的中心频率，当前为 700 Hz，只读。",
      ["IfAfSettings.EffectiveCwBandwidthHz"] = "当前 CW 带宽预设对应的音频滤波带宽（Hz），只读。",
      ["IfAfSettings.EffectiveFmIfCutoffHz"] = "FM 的复数中频滤波器截止频率（Hz），由 FM 带宽预设决定，只读。",
      ["IfAfSettings.EffectiveFmAfCutoffHz"] = "FM 解调后音频低通的有效截止频率（Hz），由 FM 带宽预设决定，只读。",
      ["IfAfSettings.EffectiveFmDeviationHz"] = "FM 判频器的频偏归一化参考值（Hz），由 FM 带宽预设决定，只读。",
      ["IfAfSettings.SsbGainDb"] = "SSB 慢速 AGC 之后的独立手动电平微调（dB），不改变 FM/CW 的输出电平。",
      ["IfAfSettings.CwGainDb"] = "CW 慢速 AGC 之后的独立电平微调（dB），慢释放可保留键控间歇。",
      ["IfAfSettings.FmGainDb"] = "FM 判频后专用音频增益，默认 −15 dB；与 SSB/CW 音量互不影响。",
      ["IfAfSettings.SsbCwAgcEnabled"] = "对 SSB/CW 的解调后 AF 使用有界的慢速 AGC，不会修改供 AFC 使用的原始 12 kHz IF。",
      ["IfAfSettings.SsbCwAgcTargetDbfs"] = "SSB/CW 慢速 AGC 的目标平均音频电平，默认 −24 dBFS，并非保证的信号电平。",
      ["IfAfSettings.SsbCwAgcMaxGainDb"] = "SSB/CW 慢速 AGC 允许的最大提升增益（dB），限制过度放大背景噪声。"
    };

    private static readonly Dictionary<string, string> Enums = new(StringComparer.Ordinal)
    {
      ["TelemetryReceiveSource.SdrIq"] = "SDR 复数 IQ（原有路径）",
      ["TelemetryReceiveSource.Shared12kIf"] = "共享 IF→AF 原始 12 kHz IF",
      ["TelemetryReceiveSource.Capture12kIf"] = "声卡采集 12 kHz IF",
      ["TelemetryReceiveSource.CaptureAf"] = "声卡采集 AF 音频",
      ["TelemetryReceiveSource.LoopbackAf"] = "播放设备回环 AF",
      ["TelemetryReceiveSource.IfAfDemodulatedAf"] = "IF→AF 内部解调后 AF",
      ["RecordingAudioSource.Auto"] = "自动选择",
      ["RecordingAudioSource.Sdr"] = "SDR 解调音频",
      ["RecordingAudioSource.WasapiCapture"] = "Windows AF 声卡采集",
      ["RecordingAudioSource.RsBa1Loopback"] = "RS-BA1 播放回环",
      ["RecordingAudioSource.IfAfDemodulated"] = "IF→AF 解调后 PCM",
      ["DataStreamType.IqToVac"] = "IQ 输出至虚拟声卡",
      ["DataStreamType.AudioToVac"] = "音频输出至虚拟声卡",
      ["DataStreamType.IqToUdp"] = "IQ 输出至 UDP",
      ["DataStreamType.AudioToUdp"] = "音频输出至 UDP",
      ["SstvSaveFilter.None"] = "无处理",
      ["SstvSaveFilter.Wiener"] = "维纳滤波",
      ["SstvSaveFilter.Nlm"] = "非局部均值滤波",
      ["IfAfInputSource.UsbAudio"] = "电台 USB 声卡采集",
      ["IfAfInputSource.AuthenticatedLanIf"] = "已认证 Icom LAN IF",
      ["IfAfSelection.FollowRadio"] = "跟随电台模式",
      ["IfAfSelection.Usb"] = "USB 上边带",
      ["IfAfSelection.Lsb"] = "LSB 下边带",
      ["IfAfSelection.CwUsb"] = "CW 上边带",
      ["IfAfSelection.CwLsb"] = "CW 下边带",
      ["IfAfSelection.Fm"] = "FM 调频",
      ["IfAfAudioRoute.ExistingVirtualCable"] = "手动选择虚拟声卡",
      ["IfAfAudioRoute.AutoSignedCable"] = "自动识别配对虚拟声卡",
      ["IfAfLatencyPreset.Fast20Ms"] = "快速 20 ms",
      ["IfAfLatencyPreset.Balanced40Ms"] = "均衡 40 ms",
      ["IfAfLatencyPreset.Stable80Ms"] = "稳定 80 ms",
      ["IfAfLatencyPreset.Compatible200Ms"] = "兼容 200 ms",
      ["IfAfFmVoiceProcessing.Off"] = "关闭（原始 FM 音频）",
      ["IfAfFmVoiceProcessing.Voice50us"] = "模拟语音去加重 50 μs",
      ["IfAfFmVoiceProcessing.Voice75us"] = "模拟语音去加重 75 μs",
      ["IfAfSsbVoiceNotch.Off"] = "关闭",
      ["IfAfSsbVoiceNotch.Tone1000Hz"] = "1000 Hz 陷波",
      ["IfAfSsbVoiceNotch.Tone1500Hz"] = "1500 Hz 陷波",
      ["IfAfSsbVoiceNotch.AutoTracking"] = "自动跟踪陷波",
      ["IfAfSsbNoiseReduction.Off"] = "关闭",
      ["IfAfSsbNoiseReduction.Mild"] = "温和降噪"
    };

    internal static string? Name(string label) =>
      Names.TryGetValue(label, out string? translation) ? translation
        : SettingsChineseWide.Name(label);

    internal static string? Category(string source) =>
      Categories.TryGetValue(source, out string? translation) ? translation : null;

    internal static string? Description(string key) =>
      Descriptions.TryGetValue(key, out string? translation) ? translation
        : SettingsChineseWide.Description(key);

    internal static bool HasEnum(Type type) =>
      type.IsEnum && Enum.GetNames(type).Length > 0 &&
      (Enums.ContainsKey(type.Name + "." + Enum.GetNames(type)[0]) ||
       SettingsChineseWide.HasEnum(type));

    internal static string? EnumName(Type type, object value) =>
      Enums.TryGetValue(type.Name + "." + value, out string? translation)
        ? translation : SettingsChineseWide.EnumName(type, value);

    internal static object? ParseEnum(Type type, string label)
    {
      foreach (string name in Enum.GetNames(type))
        if (Enums.TryGetValue(type.Name + "." + name, out string? zh) &&
            string.Equals(zh, label, StringComparison.Ordinal))
          return Enum.Parse(type, name);
      return SettingsChineseWide.ParseEnum(type, label);
    }

    internal static bool IsNestedSettingsModel(Type type) =>
      type.Name is nameof(AosAnnouncement) or nameof(PositionAnnouncement)
        or "Ft4BackgroundColors" or "TransverterBand" or "RotatorParkWaypoint";
  }

  /// <summary>
  /// PropertyGrid-only localized enum display. Delegates unknown and English
  /// strings to each original enum converter so existing labels remain usable.
  /// </summary>
  internal sealed class SettingsEnumValueConverter : TypeConverter
  {
    private readonly Type enumType;
    private readonly TypeConverter original;

    internal SettingsEnumValueConverter(Type enumType, TypeConverter original)
    {
      this.enumType = enumType;
      this.original = original;
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
      destinationType == typeof(string) || original.CanConvertTo(context, destinationType);

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
      sourceType == typeof(string) || original.CanConvertFrom(context, sourceType);

    public override object? ConvertTo(ITypeDescriptorContext? context,
      CultureInfo? culture, object? value, Type destinationType)
    {
      if (destinationType == typeof(string) && value != null &&
          SettingsLanguage.Current == UiLanguage.SimplifiedChinese &&
          SettingsChineseSupplement.EnumName(enumType, value) is string zh)
        return zh;
      return original.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context,
      CultureInfo? culture, object value)
    {
      if (value is string label &&
          SettingsChineseSupplement.ParseEnum(enumType, label) is object parsed)
        return parsed;
      return original.ConvertFrom(context, culture, value);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) =>
      original.GetStandardValuesSupported(context);

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) =>
      original.GetStandardValuesExclusive(context);

    public override StandardValuesCollection? GetStandardValues(ITypeDescriptorContext? context) =>
      original.GetStandardValues(context);

    public override bool IsValid(ITypeDescriptorContext? context, object? value) =>
      original.IsValid(context, value);
  }
}
