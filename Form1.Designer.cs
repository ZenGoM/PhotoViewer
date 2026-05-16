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
        folderInfoLabel = new Label();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();

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

        // folderInfoLabel
        folderInfoLabel.Dock = DockStyle.Top;
        folderInfoLabel.Height = 25;
        folderInfoLabel.BackColor = Color.FromArgb(242, 242, 242);
        folderInfoLabel.ForeColor = Color.FromArgb(60, 60, 60);
        folderInfoLabel.Font = new Font("Segoe UI", 8.5f);
        folderInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
        folderInfoLabel.Padding = new Padding(8, 0, 0, 0);
        folderInfoLabel.Text = string.Empty;

        // thumbnailPanel
        thumbnailPanel.Dock = DockStyle.Fill;
        thumbnailPanel.AutoScroll = true;
        thumbnailPanel.BackColor = Color.WhiteSmoke;
        thumbnailPanel.Padding = new Padding(4);

        // Panel2: thumbnailPanel を先に追加し、folderInfoLabel を後に追加することで
        // WinForms のドッキング処理順（後追加が先にドック）により
        // folderInfoLabel が最上部、thumbnailPanel が残りを埋める
        splitContainer.Panel2.Controls.Add(thumbnailPanel);
        splitContainer.Panel2.Controls.Add(folderInfoLabel);

        // statusStrip
        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel });
        statusLabel.Text = "フォルダーを選択してください";
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

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
    private Label folderInfoLabel;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel statusLabel;
}
