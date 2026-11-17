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

  // AF recordings of a digital transmitter can be either SSB audio
  // containing the carrier tones or a receiver's already-discriminated
  // FM baseband. Both are valid and require different IQ front ends.
  public enum TelemetryFskAfInput
  {
    ToneAudio = 0,
    FmDiscriminator = 1
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
    [DisplayName("12 kHz IF capture channel")]
    [Description("Select IF L/R channel; avoid mixing inverted stereo IF samples.")]
    [DefaultValue(AudioChannelChoice.Left)]
    public AudioChannelChoice IfCaptureChannel { get; set; } = AudioChannelChoice.Left;

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
    [Description("Center frequency for analytic conversion of non-AFSK AF signals. For AFSK, preserve the already demodulated mark/space audio; the selected transmitter's AF carrier/deviation control the Bell-202 demodulator.")]
    [DefaultValue(1700)]
    public int AfCenterHz { get; set; } = 1700;

    [Category("Receive input")]
    [DisplayName("FSK/GMSK AF waveform")]
    [Description("Tone audio (SSB/USB recording) or FM discriminator output from a receiver already tuned to FM. Only affects FSK, GFSK and GMSK AF capture/loopback; 12-kHz IF and SDR IQ stay unchanged.")]
    [DefaultValue(TelemetryFskAfInput.ToneAudio)]
    public TelemetryFskAfInput FskAfInput { get; set; } = TelemetryFskAfInput.ToneAudio;

    [Category("Receive input")]
    [DisplayName("FM discriminator deviation (Hz)")]
    [Description("Deviation used to reconstruct IQ from FM-discriminated FSK/GMSK audio. 0 uses transmitter metadata or the 0.5 modulation-index default (baud/4); nonzero values allow manual calibration.")]
    [DefaultValue(0.0)]
    public double FskAfDeviationHz { get; set; }

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
