using VE3NEA;

namespace SkyRoof
{
  public partial class AboutBox : Form
  {
    internal const string ForkCopyright = "Fork modifications © 2026 Iu-yang1 BG5JSU";

    public AboutBox()
    {
      InitializeComponent();
      WebsiteLabel.LinkColor = Theme.Link;
      ForkWebsiteLabel.LinkColor = Theme.Link;
      Text = $"About {Utils.GetAppName()}";
      label1.Text = Utils.GetVersionString();
      label2.Text = Utils.GetCopyrightString();
      label3.Text = ForkCopyright;
    }

    private void WebsiteLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      WebsiteLabel.LinkVisited = true;
      Utils.OpenUrl("https://ve3nea.github.io/SkyRoof");
    }

    private void ForkWebsiteLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      ForkWebsiteLabel.LinkVisited = true;
      Utils.OpenUrl("https://github.com/Iu-yang1/SkyRoof-Plus");
    }
  }
}
