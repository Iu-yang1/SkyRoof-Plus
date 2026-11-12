namespace SkyRoof
{
  partial class AboutBox
  {
    private System.ComponentModel.IContainer components = null;
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
        components.Dispose();
      base.Dispose(disposing);
    }
    #region Windows Form Designer generated code
    private void InitializeComponent()
    {
      CloseBtn = new Button();
      label1 = new Label();
      label2 = new Label();
      label3 = new Label();
      WebsiteLabel = new LinkLabel();
      ForkWebsiteLabel = new LinkLabel();
      SuspendLayout();
      // CloseBtn
      CloseBtn.Anchor = AnchorStyles.Bottom;
      CloseBtn.Location = new Point(226, 178);
      CloseBtn.Name = "CloseBtn";
      CloseBtn.Size = new Size(88, 25);
      CloseBtn.TabIndex = 0;
      CloseBtn.Text = "Close";
      CloseBtn.UseVisualStyleBackColor = true;
      CloseBtn.DialogResult = DialogResult.OK;
      // label1
      label1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      label1.Font = new Font("Segoe UI", 20.25F, FontStyle.Bold);
      label1.ForeColor = Color.Teal;
      label1.Location = new Point(8, 9);
      label1.Name = "label1";
      label1.Size = new Size(524, 37);
      label1.TabIndex = 1;
      label1.Text = "Product";
      label1.TextAlign = ContentAlignment.MiddleCenter;
      // label2 (original author)
      label2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      label2.Location = new Point(12, 55);
      label2.Name = "label2";
      label2.Size = new Size(516, 20);
      label2.TabIndex = 2;
      label2.Text = "Original author copyright";
      label2.TextAlign = ContentAlignment.MiddleCenter;
      // label3 (fork modifications)
      label3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      label3.Location = new Point(12, 82);
      label3.Name = "label3";
      label3.Size = new Size(516, 20);
      label3.TabIndex = 3;
      label3.Text = "Fork modifications";
      label3.TextAlign = ContentAlignment.MiddleCenter;
      // WebsiteLabel
      WebsiteLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      WebsiteLabel.Location = new Point(12, 111);
      WebsiteLabel.Name = "WebsiteLabel";
      WebsiteLabel.Size = new Size(516, 20);
      WebsiteLabel.TabIndex = 4;
      WebsiteLabel.TabStop = true;
      WebsiteLabel.Text = "Original project / 原作者网站 (VE3NEA)";
      WebsiteLabel.TextAlign = ContentAlignment.MiddleCenter;
      WebsiteLabel.LinkClicked += WebsiteLabel_LinkClicked;
      // ForkWebsiteLabel
      ForkWebsiteLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      ForkWebsiteLabel.Location = new Point(12, 137);
      ForkWebsiteLabel.Name = "ForkWebsiteLabel";
      ForkWebsiteLabel.Size = new Size(516, 20);
      ForkWebsiteLabel.TabIndex = 5;
      ForkWebsiteLabel.TabStop = true;
      ForkWebsiteLabel.Text = "SkyRoof-Plus / Fork 仓库 (Iu-yang1)";
      ForkWebsiteLabel.TextAlign = ContentAlignment.MiddleCenter;
      ForkWebsiteLabel.LinkClicked += ForkWebsiteLabel_LinkClicked;
      // AboutBox
      AcceptButton = CloseBtn;
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      CancelButton = CloseBtn;
      ClientSize = new Size(540, 217);
      Controls.Add(ForkWebsiteLabel);
      Controls.Add(WebsiteLabel);
      Controls.Add(label3);
      Controls.Add(label2);
      Controls.Add(label1);
      Controls.Add(CloseBtn);
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;
      Name = "AboutBox";
      ShowInTaskbar = false;
      StartPosition = FormStartPosition.CenterParent;
      Text = "About SkyRoof";
      ResumeLayout(false);
    }
    #endregion
    private Button CloseBtn;
    private Label label1;
    private Label label2;
    private Label label3;
    private LinkLabel WebsiteLabel;
    private LinkLabel ForkWebsiteLabel;
  }
}
