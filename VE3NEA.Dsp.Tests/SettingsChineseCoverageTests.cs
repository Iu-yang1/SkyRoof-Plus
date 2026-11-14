using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>Audits the user-visible PropertyGrid metadata of the settings tree.</summary>
public sealed class SettingsChineseCoverageTests
{
  [Fact]
  public void MainSettingsPanelsHaveChineseDescriptionsForBrowsableProperties()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      var settings = new Settings();
      object[] models =
      {
        settings, settings.User, settings.Audio, settings.OutputStream,
        settings.Telemetry, settings.Telemetry.KissServer,
        settings.Telemetry.SatnogsUploader,
        settings.Announcements, settings.Announcements.Announcement1,
        settings.Announcements.Announcement2,
        settings.Announcements.PositionAnnouncement,
        settings.IfAf, settings.Afc, settings.Waterfall,
        settings.Cat, settings.Cat.RxCat, settings.Cat.TxCat,
        settings.Cat.Embedded,
        settings.Rotator, settings.IcomLanSpectrum,
        settings.IcomDirectLan, settings.IcomDirectLan.Receive,
        settings.IcomDirectLan.Transmit, settings.IcomDirectLan.Meters,
        settings.IcomDirectLan.Transmit.Agc,
        settings.SoapyRemote, settings.Amsat, settings.QsoEntry,
        settings.Ft4Console, settings.Ft4Console.Receive,
        settings.Ft4Console.Transmit, settings.Ft4Console.Waterfall,
        settings.Ft4Console.UdpSender, settings.Ft4Console.Messages,
        settings.Ft4Console.Messages.BkColors,
        settings.CwConsole, settings.CwConsole.Macros,
        settings.Transverter, new TransverterBand(),
        settings.OnvifCamera, settings.OrbitSources
      };

      var missing = new List<string>();
      var untranslatedLabels = new List<string>();
      var untranslatedCategories = new List<string>();
      string[] technicalLabels = { "ALC", "SWR", "SNR", "F1", "F2", "F3", "F4",
        "F5", "F6", "F7", "F8" };
      foreach (object model in models)
      {
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(model))
        {
          if (!property.IsBrowsable) continue;
          string description = property.Description;
          string key = model.GetType().Name + "." + property.Name;
          if (string.IsNullOrWhiteSpace(description) ||
              !description.Any(c => c >= 0x3400 && c <= 0x9fff))
            missing.Add(key);
          if (!property.DisplayName.Any(c => c >= 0x3400 && c <= 0x9fff) &&
              !technicalLabels.Contains(property.DisplayName))
            untranslatedLabels.Add(key + "=" + property.DisplayName);
          if (!property.Category.Any(c => c >= 0x3400 && c <= 0x9fff))
            untranslatedCategories.Add(key + "=" + property.Category);
        }
      }
      Assert.True(missing.Count == 0,
        "Untranslated visible settings help: " + string.Join(", ", missing));
      Assert.True(untranslatedLabels.Count == 0,
        "Untranslated settings labels: " + string.Join(", ", untranslatedLabels));
      Assert.True(untranslatedCategories.Count == 0,
        "Untranslated settings categories: " + string.Join(", ", untranslatedCategories));
    }
    finally { SettingsLanguage.Set(previous); }
  }
}
