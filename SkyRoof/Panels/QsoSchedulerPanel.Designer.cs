namespace SkyRoof
{
  partial class QsoSchedulerPanel
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      dockPanel1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
      label1 = new Label();
      label2 = new Label();
      SatelliteComboBox = new ComboBox();
      DxSquareEdit = new VE3NEA.TextBoxEx();
      startUtcLabel = new Label();
      endUtcLabel = new Label();
      StartUtcPicker = new DateTimePicker();
      EndUtcPicker = new DateTimePicker();
      CalculateButton = new Button();
      CalculationProgress = new ProgressBar();
      StatusLabel = new Label();
      PredictionList = new VE3NEA.ListViewEx();
      columnHeader1 = new ColumnHeader();
      SuspendLayout();
      // 
      // dockPanel1
      // 
      dockPanel1.Dock = DockStyle.Top;
      dockPanel1.Location = new Point(0, 0);
      dockPanel1.Name = "dockPanel1";
      dockPanel1.Size = new Size(600, 100);
      dockPanel1.TabIndex = 0;
      // 
      // label1
      // 
      label1.AutoSize = true;
      label1.Location = new Point(8, 8);
      label1.Name = "label1";
      label1.Size = new Size(48, 15);
      label1.TabIndex = 1;
      label1.Text = "Satellite";
      // 
      // label2
      // 
      label2.AutoSize = true;
      label2.Location = new Point(208, 9);
      label2.Name = "label2";
      label2.Size = new Size(86, 15);
      label2.TabIndex = 2;
      label2.Text = "DX Grid Square";
      // 
      // SatelliteComboBox
      // 
      SatelliteComboBox.DisplayMember = "name";
      SatelliteComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
      SatelliteComboBox.FormattingEnabled = true;
      SatelliteComboBox.Location = new Point(62, 5);
      SatelliteComboBox.Name = "SatelliteComboBox";
      SatelliteComboBox.Size = new Size(134, 23);
      SatelliteComboBox.TabIndex = 12;
      SatelliteComboBox.SelectedIndexChanged += SatelliteComboBox_SelectedIndexChanged;
      // 
      // DxSquareEdit
      // 
      DxSquareEdit.CharacterCasing = CharacterCasing.Upper;
      DxSquareEdit.Location = new Point(338, 6);
      DxSquareEdit.Name = "DxSquareEdit";
      DxSquareEdit.Size = new Size(82, 23);
      DxSquareEdit.TabIndex = 13;
      DxSquareEdit.TextChanged += DxSquareEdit_TextChanged;
      // 
      // startUtcLabel
      // 
      startUtcLabel.AutoSize = false;
      startUtcLabel.Location = new Point(8, 43);
      startUtcLabel.Name = "startUtcLabel";
      startUtcLabel.Size = new Size(72, 23);
      startUtcLabel.TextAlign = ContentAlignment.MiddleLeft;
      startUtcLabel.Text = "Start (UTC)";
      // 
      // endUtcLabel
      // 
      endUtcLabel.AutoSize = false;
      endUtcLabel.Location = new Point(275, 43);
      endUtcLabel.Name = "endUtcLabel";
      endUtcLabel.Size = new Size(72, 23);
      endUtcLabel.TextAlign = ContentAlignment.MiddleLeft;
      endUtcLabel.Text = "End (UTC)";
      // 
      // StartUtcPicker
      // 
      StartUtcPicker.CustomFormat = "yyyy-MM-dd HH:mm";
      StartUtcPicker.Format = DateTimePickerFormat.Custom;
      StartUtcPicker.ShowUpDown = true;
      StartUtcPicker.Location = new Point(82, 43);
      StartUtcPicker.Name = "StartUtcPicker";
      StartUtcPicker.Size = new Size(184, 23);
      StartUtcPicker.TabIndex = 14;
      StartUtcPicker.ValueChanged += RangePicker_ValueChanged;
      // 
      // EndUtcPicker
      // 
      EndUtcPicker.CustomFormat = "yyyy-MM-dd HH:mm";
      EndUtcPicker.Format = DateTimePickerFormat.Custom;
      EndUtcPicker.ShowUpDown = true;
      EndUtcPicker.Location = new Point(349, 43);
      EndUtcPicker.Name = "EndUtcPicker";
      EndUtcPicker.Size = new Size(241, 23);
      EndUtcPicker.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      EndUtcPicker.TabIndex = 15;
      EndUtcPicker.ValueChanged += RangePicker_ValueChanged;
      // 
      // CalculateButton
      // 
      CalculateButton.Location = new Point(439, 5);
      CalculateButton.Name = "CalculateButton";
      CalculateButton.Size = new Size(151, 24);
      CalculateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      CalculateButton.TabIndex = 16;
      CalculateButton.Text = "Start calculation";
      CalculateButton.UseVisualStyleBackColor = true;
      CalculateButton.Click += CalculateButton_Click;
      // 
      // CalculationProgress
      // 
      CalculationProgress.Location = new Point(8, 79);
      CalculationProgress.Name = "CalculationProgress";
      CalculationProgress.Size = new Size(405, 13);
      CalculationProgress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      CalculationProgress.TabIndex = 17;
      // 
      // StatusLabel
      // 
      StatusLabel.AutoEllipsis = true;
      StatusLabel.Location = new Point(420, 74);
      StatusLabel.Name = "StatusLabel";
      StatusLabel.Size = new Size(170, 24);
      StatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      StatusLabel.TextAlign = ContentAlignment.MiddleRight;
      StatusLabel.Text = "Ready";
      // 
      // PredictionList
      // 
      PredictionList.Columns.AddRange(new ColumnHeader[] { columnHeader1 });
      PredictionList.Dock = DockStyle.Fill;
      PredictionList.FullRowSelect = true;
      PredictionList.HeaderStyle = ColumnHeaderStyle.None;
      PredictionList.Location = new Point(0, 100);
      PredictionList.MultiSelect = false;
      PredictionList.Name = "PredictionList";
      PredictionList.OwnerDraw = true;
      PredictionList.ShowItemToolTips = true;
      PredictionList.Size = new Size(600, 300);
      PredictionList.TabIndex = 14;
      PredictionList.UseCompatibleStateImageBehavior = false;
      PredictionList.View = View.Details;
      PredictionList.VirtualMode = true;
      PredictionList.DrawSubItem += PredictionList_DrawSubItem;
      PredictionList.RetrieveVirtualItem += PredictionList_RetrieveVirtualItem;
      PredictionList.Resize += PredictionList_Resize;
      PredictionList.MouseDown += PredictionList_MouseDown;
      // 
      // QsoSchedulerPanel
      // 
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(600, 400);
      Controls.Add(PredictionList);
      Controls.Add(StatusLabel);
      Controls.Add(CalculationProgress);
      Controls.Add(CalculateButton);
      Controls.Add(EndUtcPicker);
      Controls.Add(StartUtcPicker);
      Controls.Add(endUtcLabel);
      Controls.Add(startUtcLabel);
      Controls.Add(DxSquareEdit);
      Controls.Add(SatelliteComboBox);
      Controls.Add(label2);
      Controls.Add(label1);
      Controls.Add(dockPanel1);
      Name = "QsoSchedulerPanel";
      Text = "QSO Scheduler";
      FormClosing += QsoScheduler_FormClosing;
      ResumeLayout(false);
      PerformLayout();
    }

    #endregion

    private WeifenLuo.WinFormsUI.Docking.DockPanel dockPanel1;
    private Label label1;
    private Label label2;
    private ComboBox SatelliteComboBox;
    private VE3NEA.TextBoxEx DxSquareEdit;
    private Label startUtcLabel;
    private Label endUtcLabel;
    private DateTimePicker StartUtcPicker;
    private DateTimePicker EndUtcPicker;
    private Button CalculateButton;
    private ProgressBar CalculationProgress;
    private Label StatusLabel;
    private VE3NEA.ListViewEx PredictionList;
    private ColumnHeader columnHeader1;
  }
}
