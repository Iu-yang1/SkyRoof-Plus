using System.ComponentModel;
using NAudio.CoreAudioApi;
using VE3NEA;

namespace SkyRoof
{
  public enum RecordingAudioSource
  {
    Auto,
    Sdr,
    WasapiCapture,
    RsBa1Loopback,
    IfAfDemodulated,
    IfRaw
  }

  public enum RecordingIqSource
  {
    Sdr = 0,
    If12k = 1
  }

  public class AudioSettings
  {
    // non-browsable
    public int SoundcardVolume = -25;
    // Optional explicit Windows mixer session for RS-BA1 Remote Utility.
    // Never change the global endpoint volume or an unrelated application.
    public string? RsBa1PlaybackProcessName;
    public string? RsBa1PlaybackDeviceId;
    public bool SpeakerEnabled = true;

    [DisplayName("Recorder Audio Source")]
    [Description("Auto uses SDR when active, otherwise IF->AF demodulated PCM if running, then RS-BA1 playback. IF-AF Demodulated records direct 48-kHz post-DSP AF without reopening a soundcard.")]
    [DefaultValue(RecordingAudioSource.Auto)]
    public RecordingAudioSource RecorderSource { get; set; } = RecordingAudioSource.Auto;

    [DisplayName("Recorder I/Q Source")]
    [Description("SDR complex IQ, or 12-kHz real IF converted to 48-kHz complex baseband for two-channel float WAV.")]
    [DefaultValue(RecordingIqSource.Sdr)]
    [TypeConverter(typeof(EnumDescriptionConverter))]
    public RecordingIqSource RecorderIqSource { get; set; } = RecordingIqSource.Sdr;

    [DisplayName("Recorder 12-kHz IF Capture Device")]
    [Description("USB raw IF capture endpoint, used only when the shared IF->AF receiver is not running.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? RecorderIfCaptureDeviceId { get; set; } =
      Soundcard.GetPreferredSoundcardId(DataFlow.Capture, "USB Audio CODEC", "ICOM");

    [DisplayName("Recorder AF Capture Device (USB / VAC)")]
    [Description("Windows capture endpoint for decoded AF: radio USB AF (Microphone / USB Audio CODEC) or virtual cable recording end (CABLE Output). This is not the 12-kHz IF input of the IF->AF demodulator.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? RecorderCaptureDeviceId { get; set; } = Soundcard.GetPreferredSoundcardId(DataFlow.Capture, "USB Audio CODEC", "ICOM");

    [DisplayName("Recorder RS-BA1 Playback Device")]
    [Description("Windows render/playback endpoint used by RS-BA1 Remote Utility, not the radio USB microphone input. WASAPI loopback captures the entire endpoint mix.")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? RecorderLoopbackDeviceId { get; set; }


    [DisplayName("Speaker Audio Device")]
    [Description("Soundcard for audio output")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? SpeakerSoundcard { get; set; } = Soundcard.GetDefaultSoundcardId(DataFlow.Render);

    [DisplayName("FM Squelch")]
    [Description("Enable Squelch in the FM mode")]
    [DefaultValue(true)]
    public bool Squelch { get; set; } = true;

    public override string ToString() { return string.Empty; }
  }
}
