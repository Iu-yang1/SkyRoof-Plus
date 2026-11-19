using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class IcomScopePaintOwnershipTests
{
  [Fact]
  public void RepeatedParentChromePaintCannotOverwriteNativePlotOrWaterfall()
  {
    var client = new Rectangle(0, 0, 320, 240);
    var origin = new Point(0, 25);
    var plot = new Rectangle(0, 0, 320, 70);
    var waterfall = new Rectangle(0, 96, 320, 119);
    using var bitmap = new Bitmap(client.Width, client.Height);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Lime);
    using var chrome = IcomScopePaintOwnership.CreateChromeRegion(client, origin, plot, waterfall);
    for (int i = 0; i < 150; i++)
    {
      GraphicsState saved = graphics.Save();
      graphics.SetClip(chrome, CombineMode.Intersect);
      graphics.FillRectangle(Brushes.Black, client);
      graphics.Restore(saved);
    }
    Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(150, 45).ToArgb());
    Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(150, 150).ToArgb());
    Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(150, 10).ToArgb());
    Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(150, 105).ToArgb());
  }

  [Fact]
  public void NativeChildAndBufferedParentHaveComplementaryWindowClipping()
  {
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        using var parent = new IcomLanSpectrumView();
        using var child = new IcomScopeGpuSurface((_, _) => { });
        var parentParams = GetParams(parent);
        var childParams = GetParams(child);
        Assert.NotEqual(0, parentParams.Style & IcomScopePaintOwnership.ClipChildren);
        Assert.NotEqual(0, childParams.Style & IcomScopePaintOwnership.ClipSiblings);
        Assert.NotEqual(0, childParams.ClassStyle & 0x20);
      }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
  }

  private static System.Windows.Forms.CreateParams GetParams(System.Windows.Forms.Control control)
  {
    var property = control.GetType().GetProperty("CreateParams",
      BindingFlags.Instance | BindingFlags.NonPublic);
    return (System.Windows.Forms.CreateParams)(property?.GetValue(control)
      ?? throw new InvalidOperationException("CreateParams unavailable"));
  }

  [Fact]
  public void HiddenWaterfallAndResizedChildRetainParentChrome()
  {
    using var chrome = IcomScopePaintOwnership.CreateChromeRegion(
      new Rectangle(0, 0, 320, 240), new Point(0, 25),
      new Rectangle(0, 0, 320, 195), Rectangle.Empty);
    Assert.True(chrome.IsVisible(20, 10));
    Assert.False(chrome.IsVisible(20, 120));
    Assert.True(chrome.IsVisible(20, 230));
  }
}
