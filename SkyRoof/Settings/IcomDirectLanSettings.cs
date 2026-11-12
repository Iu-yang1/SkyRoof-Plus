using Newtonsoft.Json;
using System.ComponentModel;
using VE3NEA;

namespace SkyRoof
{
  // Wire codes are the Icom RS-BA1 conninfo audio codec identifiers.
  [TypeConverter(typeof(EnumDescriptionConverter))]
  public enum IcomLanAudioCodec : byte
  {
    [Description("u-Law 1ch 8bit")] ULaw8Mono = 0x01,
    [Description("PCM 1ch 8bit")] Pcm8Mono = 0x02,
    [Description("LPCM 1ch 16bit")] Lpcm16Mono = 0x04
  }

  [TypeConverter(typeof(EnumDescriptionConverter))]
  public enum IcomLanAudioSampleRate
  {
    [Description("8 kHz")] Hz8000 = 8000,
    [Description("11.025 kHz")] Hz11025 = 11025,
    [Description("12 kHz")] Hz12000 = 12000,
    [Description("16 kHz")] Hz16000 = 16000,
    [Description("22.05 kHz")] Hz22050 = 22050,
    [Description("32 kHz")] Hz32000 = 32000,
    [Description("44.1 kHz")] Hz44100 = 44100,
    [Description("48 kHz")] Hz48000 = 48000
  }

  [JsonObject]
  [TypeConverter(typeof(ExpandableObjectConverter))]
  public sealed class IcomDirectLanReceiveSettings
  {
    [DisplayName("AF Transmission (RX)")]
    [Description("Request the radio-to-PC receive audio stream. Select AF for listening or IF when using the separate 12-kHz IF→AF pipeline.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [DisplayName("RX sample rate")]
    [DefaultValue(IcomLanAudioSampleRate.Hz48000)]
    public IcomLanAudioSampleRate SampleRate { get; set; } = IcomLanAudioSampleRate.Hz48000;

    [DisplayName("RX codec")]
    [DefaultValue(IcomLanAudioCodec.Lpcm16Mono)]
    public IcomLanAudioCodec Codec { get; set; } = IcomLanAudioCodec.Lpcm16Mono;

    [DisplayName("RX speaker playback buffer (ms)")]
    [Description("Local Windows playback/output latency 20–500 ms; separate from network packet reordering and does not change radio RX latency.")]
    [DefaultValue(120)]
    public int PrebufferMs { get; set; } = 120;

    [DisplayName("Speaker")]
    [Description("Windows playback endpoint for direct network receive AF. Leave empty to receive audio for the existing IF→AF pipeline without local speaker playback.")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? SpeakerDeviceId { get; set; }

    [DisplayName("Speaker volume (%)")]
    [Description("Local Windows playback level 0–100%; does not affect the IC-9700 received audio packets or IF demodulator.")]
    [DefaultValue(100)]
    public int SpeakerVolume { get; set; } = 100;

    public override string ToString() => Enabled ? "RX enabled" : "RX disabled";
  }

  /// <summary>Software voice leveling applied after fixed microphone gain,
  /// before IC-9700 LAN PCM encoding. Disabled for digital modulation by default.</summary>
  [JsonObject]
  [TypeConverter(typeof(ExpandableObjectConverter))]
  public sealed class IcomLanTxAgcSettings
  {
    [DisplayName("Enable voice AGC")]
    [Description("Optional software speech leveler. Keep OFF for FT4/FT8/Q65 and calibrated digital waveforms.")]
    [DefaultValue(false)]
    public bool Enabled { get; set; }

    [DisplayName("Target RMS (dBFS)")]
    [Description("Desired voice RMS before Icom LAN encoding, typically -21 to -15 dBFS. Not a radio ALC or RF power setting.")]
    [DefaultValue(-18.0)]
    public double TargetRmsDbfs { get; set; } = -18.0;

    [DisplayName("Maximum gain (dB)")]
    [Description("Limits amplification of quiet speech; noise below the threshold is not boosted.")]
    [DefaultValue(12.0)]
    public double MaximumGainDb { get; set; } = 12.0;

    [DisplayName("Noise floor (dBFS)")]
    [Description("Below this RMS level the AGC stops adding gain, to avoid pumping microphone noise.")]
    [DefaultValue(-48.0)]
    public double NoiseFloorDbfs { get; set; } = -48.0;

    [DisplayName("Attack (ms)")]
    [Description("Reduction time when voice suddenly becomes louder. A short attack limits overshoot.")]
    [DefaultValue(40)]
    public int AttackMs { get; set; } = 40;

    [DisplayName("Release (ms)")]
    [Description("Recovery time when normal speech becomes quieter; slower release reduces pumping.")]
    [DefaultValue(450)]
    public int ReleaseMs { get; set; } = 450;

    [DisplayName("Peak ceiling (dBFS)")]
    [Description("Software PCM peak ceiling when AGC is enabled; does not replace IC-9700 ALC.")]
    [DefaultValue(-3.0)]
    public double PeakCeilingDbfs { get; set; } = -3.0;

    public override string ToString() => Enabled ? "Voice AGC enabled" : "Voice AGC off";
  }

  [JsonObject]
  [TypeConverter(typeof(ExpandableObjectConverter))]
  public sealed class IcomDirectLanTransmitSettings
  {
    [DisplayName("MOD Transmission (TX)")]
    [Description("Negotiate PC-to-radio LAN modulation audio. The actual audio is transmitted only with verified CAT PTT ON; set the radio's MOD input source to LAN.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [DisplayName("TX sample rate")]
    [DefaultValue(IcomLanAudioSampleRate.Hz48000)]
    public IcomLanAudioSampleRate SampleRate { get; set; } = IcomLanAudioSampleRate.Hz48000;

    [DisplayName("TX codec")]
    [DefaultValue(IcomLanAudioCodec.Lpcm16Mono)]
    public IcomLanAudioCodec Codec { get; set; } = IcomLanAudioCodec.Lpcm16Mono;

    [DisplayName("TX network prebuffer (ms)")]
    [Description("IC-9700 RS-BA1 connection-info TX buffer field; 20–500 ms. Independent from local microphone capture and software jitter buffers.")]
    [DefaultValue(150)]
    public int PrebufferMs { get; set; } = 150;

    [DisplayName("TX microphone capture buffer (ms)")]
    [Description("Local Windows WASAPI microphone capture buffer, 20–500 ms; does not change the Icom radio's network TX buffer.")]
    [DefaultValue(150)]
    public int CaptureBufferMs { get; set; } = 150;

    [DisplayName("TX software jitter prefill (ms)")]
    [Description("Additional local audio queued before the 20-ms TX clock starts sending speech, 0–250 ms. Does not alter the network TX buffer.")]
    [DefaultValue(60)]
    public int JitterPrefillMs { get; set; } = 60;

    [DisplayName("TX software jitter maximum (ms)")]
    [Description("Maximum local queued audio before oldest PCM is dropped, 60–500 ms. Must be at least the jitter prefill.")]
    [DefaultValue(250)]
    public int JitterMaximumMs { get; set; } = 250;

    [DisplayName("Microphone")]
    [Description("Windows recording endpoint; virtual audio cables are supported for digital modulation. The TX stream uses this audio only when CAT reports PTT ON.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? MicrophoneDeviceId { get; set; }

    [DisplayName("Microphone gain (%)")]
    [Description("Static PCM multiplier: 0–200%, applied before optional voice AGC. 100% is unity gain; 200% is +6.02 dB.")]
    [DefaultValue(100)]
    public int MicrophoneGain { get; set; } = 100;

    [DisplayName("Voice AGC")]
    [Description("Optional local speech leveling, noise-floor control and peak ceiling. Leave disabled for digital modulation.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public IcomLanTxAgcSettings Agc { get; set; } = new();

    public override string ToString() => Enabled ? "TX enabled" : "TX disabled";
  }

  [JsonObject]
  [TypeConverter(typeof(ExpandableObjectConverter))]
  public sealed class IcomDirectLanMeterSettings
  {
    [DisplayName("Enable meters")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [DisplayName("S-meter")]
    [DefaultValue(true)]
    public bool S { get; set; } = true;

    [DisplayName("Power / PWR")]
    [DefaultValue(true)]
    public bool Power { get; set; } = true;

    [DisplayName("ALC")]
    [DefaultValue(true)]
    public bool Alc { get; set; } = true;

    [DisplayName("SWR")]
    [DefaultValue(true)]
    public bool Swr { get; set; } = true;

    [DisplayName("Meter poll interval (ms)")]
    [Description("Round-robin CI-V 15 xx query spacing, not a parallel command burst. Valid 100–2000 ms.")]
    [DefaultValue(250)]
    public int PollIntervalMs { get; set; } = 250;

    public override string ToString() => Enabled ? "CI-V meters enabled" : "CI-V meters disabled";
  }

  [JsonObject]
  public sealed class IcomDirectLanSettings
  {
    [DisplayName("Receive audio")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public IcomDirectLanReceiveSettings Receive { get; set; } = new();

    [DisplayName("Transmit audio")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public IcomDirectLanTransmitSettings Transmit { get; set; } = new();

    [DisplayName("Meter readback")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public IcomDirectLanMeterSettings Meters { get; set; } = new();

    [DisplayName("Retransmission Audio Data")]
    [Description("Respond to correlated UDP retransmit requests for locally transmitted audio. Audio retransmissions never assert PTT.")]
    [DefaultValue(true)]
    public bool RetransmitAudio { get; set; } = true;

    public override string ToString() => "RX / TX audio and CI-V meters";
  }
}
