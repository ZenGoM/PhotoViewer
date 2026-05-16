namespace PhotoViewer;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        splitContainer = new SplitContainer();
        treeView = new TreeView();
        thumbnailPanel = new FlowLayoutPanel();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        folderInfoStatusLabel = new ToolStripStatusLabel();

        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // splitContainer
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.Location = new Point(0, 0);
        splitContainer.SplitterDistance = 200;
        splitContainer.TabIndex = 0;

        // treeView
        treeView.Dock = DockStyle.Fill;
        treeView.HideSelection = false;
        treeView.Font = new Font("Segoe UI", 9.5f);
        treeView.BeforeExpand += treeView_BeforeExpand;
        treeView.AfterSelect += treeView_AfterSelect;
        splitContainer.Panel1.Controls.Add(treeView);

        // thumbnailPanel
        thumbnailPanel.Dock = DockStyle.Fill;
        thumbnailPanel.AutoScroll = true;
        thumbnailPanel.BackColor = Color.WhiteSmoke;
        thumbnailPanel.Padding = new Padding(4);
        splitContainer.Panel2.Controls.Add(thumbnailPanel);

        // statusStrip
        statusLabel.Text = "フォルダーを選択してください";
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        folderInfoStatusLabel.Text = string.Empty;
        folderInfoStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        folderInfoStatusLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        folderInfoStatusLabel.BorderStyle = Border3DStyle.Etched;

        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel, folderInfoStatusLabel });

        // MainForm
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 700);
        Controls.Add(splitContainer);
        Controls.Add(statusStrip);
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(700, 500);
        Text = "フォト ビューアー";

        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        splitContainer.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private SplitContainer splitContainer;
    private TreeView treeView;
    private FlowLayoutPanel thumbnailPanel;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel statusLabel;
    private ToolStripStatusLabel folderInfoStatusLabel;
}
