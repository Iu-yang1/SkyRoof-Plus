using Newtonsoft.Json;
using System.ComponentModel;
using VE3NEA;

namespace SkyRoof
{
  // Wire codes are the Icom RS-BA1 conninfo audio codec identifiers.
  public enum IcomLanAudioCodec : byte
  {
    ULaw8Mono = 0x01,
    Pcm8Mono = 0x02,
    Lpcm16Mono = 0x04
  }

  public enum IcomLanAudioSampleRate
  {
    Hz8000 = 8000,
    Hz11025 = 11025,
    Hz12000 = 12000,
    Hz16000 = 16000,
    Hz22050 = 22050,
    Hz32000 = 32000,
    Hz44100 = 44100,
    Hz48000 = 48000
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

    [DisplayName("RX prebuffer (ms)")]
    [Description("Local WASAPI playback buffering; 20–500 ms. This setting does not delay the radio's scope stream.")]
    [DefaultValue(120)]
    public int PrebufferMs { get; set; } = 120;

    [DisplayName("Speaker")]
    [Description("Windows playback endpoint for direct network receive AF. Leave empty to receive audio for the existing IF→AF pipeline without local speaker playback.")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? SpeakerDeviceId { get; set; }

    [DisplayName("Speaker volume (%)")]
    [DefaultValue(100)]
    public int SpeakerVolume { get; set; } = 100;

    public override string ToString() => Enabled ? "RX enabled" : "RX disabled";
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

    [DisplayName("TX prebuffer (ms)")]
    [Description("Icom network transmit buffer negotiation (20–500 ms).")]
    [DefaultValue(150)]
    public int PrebufferMs { get; set; } = 150;

    [DisplayName("Microphone")]
    [Description("Windows recording endpoint; virtual audio cables are supported for digital modulation. The TX stream uses this audio only when CAT reports PTT ON.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? MicrophoneDeviceId { get; set; }

    [DisplayName("Microphone gain (%)")]
    [DefaultValue(100)]
    public int MicrophoneGain { get; set; } = 100;

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
