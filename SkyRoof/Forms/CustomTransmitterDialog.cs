using System.Globalization;
using VE3NEA;

namespace SkyRoof
{
  internal sealed class CustomTransmitterDialog : Form
  {
    private readonly TextBox NameBox = new();
    private readonly TextBox DownlinkBox = new();
    private readonly TextBox UplinkBox = new();
    private readonly ComboBox ModeBox = new();
    private readonly Button SaveButton = new();
    private readonly Button CancelBtn = new();

    internal CustomTransmitterDefinition? Definition { get; private set; }
    private readonly string? ExistingUuid;

    private static string FormatMHz(long? hertz) =>
      hertz.HasValue
        ? (hertz.Value / 1_000_000M).ToString("0.000000", CultureInfo.InvariantCulture)
        : "";

    internal CustomTransmitterDialog(
      SatnogsDbSatellite satellite,
      SatnogsDbTransmitter? existing = null)
    {
      Text = UiLocalizationService.Format(
        existing == null ? "Transmitter.NewTitle" : "Transmitter.EditTitle",
        satellite.name);
      FormBorderStyle = FormBorderStyle.FixedToolWindow;
      StartPosition = FormStartPosition.Manual;
      ShowInTaskbar = false;
      MinimizeBox = false;
      MaximizeBox = false;
      ClientSize = new Size(356, 222);
      AutoScaleMode = AutoScaleMode.Font;

      var info = new Label
      {
        AutoSize = false,
        Location = new Point(12, 10),
        Size = new Size(332, 32),
        Text = UiLocalizationService.Get(existing == null
          ? "Transmitter.NewHint" : "Transmitter.EditHint"),
        TextAlign = ContentAlignment.MiddleLeft
      };
      Controls.Add(info);

      AddLabel(UiLocalizationService.Get("Transmitter.Name"), 12, 52, 96);
      NameBox.Location = new Point(116, 50);
      NameBox.Size = new Size(228, 23);
      NameBox.PlaceholderText = UiLocalizationService.Get("Transmitter.Placeholder");
      Controls.Add(NameBox);

      AddLabel(UiLocalizationService.Get("Transmitter.Downlink"), 12, 84, 96);
      DownlinkBox.Location = new Point(116, 82);
      DownlinkBox.Size = new Size(110, 23);
      DownlinkBox.PlaceholderText = UiLocalizationService.Get("Transmitter.Optional");
      Controls.Add(DownlinkBox);

      AddLabel(UiLocalizationService.Get("Transmitter.Uplink"), 12, 116, 96);
      UplinkBox.Location = new Point(116, 114);
      UplinkBox.Size = new Size(110, 23);
      UplinkBox.PlaceholderText = UiLocalizationService.Get("Transmitter.Optional");
      Controls.Add(UplinkBox);

      AddLabel(UiLocalizationService.Get("Transmitter.Mode"), 12, 148, 96);
      ModeBox.Location = new Point(116, 146);
      ModeBox.Size = new Size(110, 23);
      ModeBox.DropDownStyle = ComboBoxStyle.DropDown;
      ModeBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
      ModeBox.AutoCompleteSource = AutoCompleteSource.ListItems;
      ModeBox.Items.AddRange(
        new object[]
        {
          "FM",
          "FMN",
          "FM_D",
          "USB",
          "LSB",
          "CW",
          "AFSK",
          "FSK",
          "GFSK",
          "GMSK",
          "BPSK",
          "QPSK",
          "FT4",
          "FT8",
          "SSTV"
        });
      ModeBox.Text = "FM";
      if (existing != null)
      {
        if (!existing.local_custom)
          throw new ArgumentException("Only locally created transmitters can be edited.", nameof(existing));
        NameBox.Text = existing.description;
        DownlinkBox.Text = FormatMHz(existing.downlink_low);
        UplinkBox.Text = FormatMHz(existing.uplink_low);
        ModeBox.Text = string.IsNullOrWhiteSpace(existing.DownlinkMode)
          ? existing.mode
          : existing.DownlinkMode;
      }
      Controls.Add(ModeBox);

      ExistingUuid = existing?.uuid;

      var unitsHint = new Label
      {
        AutoSize = false,
        ForeColor = SystemColors.GrayText,
        Location = new Point(236, 82),
        Size = new Size(108, 55),
        Text = UiLocalizationService.Get("Transmitter.Examples"),
        TextAlign = ContentAlignment.TopLeft
      };
      Controls.Add(unitsHint);

      SaveButton.Location = new Point(185, 184);
      SaveButton.Size = new Size(75, 28);
      SaveButton.Text = UiLocalizationService.Get("Common.Save");
      SaveButton.Click += SaveButton_Click;
      Controls.Add(SaveButton);

      CancelBtn.Location = new Point(269, 184);
      CancelBtn.Size = new Size(75, 28);
      CancelBtn.Text = UiLocalizationService.Get("Common.Cancel");
      CancelBtn.DialogResult = DialogResult.Cancel;
      Controls.Add(CancelBtn);

      AcceptButton = SaveButton;
      CancelButton = CancelBtn;

      Location = Cursor.Position;
      Shown += (_, _) =>
      {
        Utils.EnsureFormVisible(this);
        NameBox.Focus();
      };
    }

    private void AddLabel(
      string text,
      int x,
      int y,
      int width)
    {
      Controls.Add(
        new Label
        {
          AutoSize = false,
          Location = new Point(x, y),
          Size = new Size(width, 23),
          Text = text,
          TextAlign = ContentAlignment.MiddleLeft
        });
    }

    private void SaveButton_Click(
      object? sender,
      EventArgs e)
    {
      string name =
        NameBox.Text.Trim();
      if (name.Length == 0)
      {
        MessageBox.Show(
          this,
          UiLocalizationService.Get("Transmitter.EnterName"),
          UiLocalizationService.Get("Transmitter.New"),
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        NameBox.Focus();
        return;
      }

      if (!TryParseFrequency(
            DownlinkBox.Text,
            out long? downlink) ||
          !TryParseFrequency(
            UplinkBox.Text,
            out long? uplink))
      {
        MessageBox.Show(
          this,
          UiLocalizationService.Get("Transmitter.InvalidFrequency"),
          UiLocalizationService.Get("Transmitter.New"),
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        return;
      }

      if (downlink == null &&
          uplink == null)
      {
        MessageBox.Show(
          this,
          UiLocalizationService.Get("Transmitter.MissingFrequency"),
          UiLocalizationService.Get("Transmitter.New"),
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        return;
      }

      string mode =
        ModeBox.Text.Trim();
      if (mode.Length == 0)
        mode = "FM";

      Definition =
        new CustomTransmitterDefinition
        {
          uuid = ExistingUuid ?? $"local-{Guid.NewGuid():N}",
          description = name,
          downlink_hz = downlink,
          uplink_hz = uplink,
          mode = mode
        };

      DialogResult = DialogResult.OK;
      Close();
    }

    internal static bool TryParseFrequency(
      string? text,
      out long? frequencyHz)
    {
      frequencyHz = null;
      if (string.IsNullOrWhiteSpace(text))
        return true;

      string value = text.Trim();
      bool parsed =
        decimal.TryParse(
          value,
          NumberStyles.Float,
          CultureInfo.CurrentCulture,
          out decimal mhz) ||
        decimal.TryParse(
          value,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out mhz);

      if (!parsed ||
          mhz <= 0 ||
          mhz > 100000)
        return false;

      decimal hz =
        decimal.Round(
          mhz * 1_000_000M,
          0,
          MidpointRounding.AwayFromZero);

      if (hz > long.MaxValue)
        return false;

      frequencyHz = (long)hz;
      return true;
    }
  }
}
