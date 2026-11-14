using System.ComponentModel;
using NAudio.CoreAudioApi;
using VE3NEA;

namespace SkyRoof
{
  // the denoising filter applied to an SSTV image before it is auto-saved
  public enum SstvSaveFilter
  {
    [Description("None")]
    None,
    [Description("Wiener")]
    Wiener,
    [Description("Non-local means")]
    Nlm,
  }

  public enum TelemetryReceiveSource
  {
    SdrIq = 0,
    Shared12kIf = 1,
    Capture12kIf = 2,
    CaptureAf = 3,
    LoopbackAf = 4,
    IfAfDemodulatedAf = 5
  }

  public class TelemetrySettings
  {
    [Category("Receive input")]
    [DisplayName("Telemetry RX Input")]
    [Description("SDR IQ (existing), shared raw 12-kHz IF, independent 12-kHz IF capture, AF capture, AF playback loopback, or IF->AF directly demodulated PCM.")]
    [DefaultValue(TelemetryReceiveSource.SdrIq)]
    public TelemetryReceiveSource InputSource { get; set; } =
      TelemetryReceiveSource.SdrIq;

    [Category("Receive input")]
    [DisplayName("12 kHz IF capture device")]
    [Description("Real 12-kHz IF at 48 kHz. Use the radio's USB Audio CODEC recording endpoint, NOT CABLE Output carrying AF.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? IfCaptureDeviceId { get; set; } =
      Soundcard.GetPreferredSoundcardId(DataFlow.Capture, "USB Audio CODEC", "ICOM");

    [Category("Receive input")]
    [DisplayName("AF capture device")]
    [Description("Windows recording endpoint carrying decoded AF, e.g. CABLE Output or USB radio AF; 48-kHz mono PCM after OS resampling.")]
    [TypeConverter(typeof(InputSoundcardNameConverter))]
    public string? AfCaptureDeviceId { get; set; }

    [Category("Receive input")]
    [DisplayName("AF playback loopback device")]
    [Description("Windows playback/render endpoint, not a microphone capture endpoint. Captures that endpoint's full mix.")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? AfLoopbackDeviceId { get; set; }

    [Category("Receive input")]
    [DisplayName("AF tone center (Hz)")]
    [Description("AFSK carrier midpoint used to translate real AF into complex baseband for the IQ telemetry pipeline. Typical Bell 202 AFSK: 1700 Hz; choose according to the satellite's modem tones.")]
    [DefaultValue(1700)]
    public int AfCenterHz { get; set; } = 1700;

    [DisplayName("Save to File")]
    [Description("Save decoded frames to a file")]
    [DefaultValue(false)]
    public bool ArchiveToFile { get; set; }

    [DisplayName("SSTV Auto-Save Filter")]
    [Description("Denoising filter applied to SSTV images when they are saved automatically")]
    [DefaultValue(SstvSaveFilter.Wiener)]
    [TypeConverter(typeof(EnumDescriptionConverter))]
    public SstvSaveFilter SstvSaveFilter { get; set; } = SstvSaveFilter.Wiener;

    [DisplayName("KISS Server")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public KissServerSettings KissServer { get; set; } = new();

    [DisplayName("SatNOGS Upload")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public SatnogsUploaderSettings SatnogsUploader { get; set; } = new();

    [Browsable(false)]
    [DefaultValue(247)]
    public int SplitterDistance { get; set; } = 247;

    // height of the text sub-panel below the image. This is the fixed panel of ImageSplitContainer, so it,
    // and not the splitter distance, is the quantity that survives a resize of the panel
    [Browsable(false)]
    [DefaultValue(106)]
    public int ImageTextHeight { get; set; } = 106;


    public override string ToString() { return string.Empty; }
  }
}
