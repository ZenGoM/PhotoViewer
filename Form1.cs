using System.Drawing.Imaging;

namespace PhotoViewer;

public partial class MainForm : Form
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };
    private const int ThumbnailSize = 150;
    private const int ThumbnailPadding = 8;

    private readonly List<ThumbnailPanel> _thumbnailPanels = new();
    private ThumbnailPanel? _selectedPanel;
    private CancellationTokenSource? _loadCts;
    private readonly AppSettings _settings;
    private ContextMenuStrip? _sharedContextMenu;

    public MainForm()
    {
        _settings = AppSettings.Load();
        InitializeComponent();
        ApplyWindowSettings();
        InitializeTree();
    }

    private void ApplyWindowSettings()
    {
        var state = (FormWindowState)_settings.WindowState;
        var bounds = new Rectangle(_settings.WindowLeft, _settings.WindowTop,
                                   _settings.WindowWidth, _settings.WindowHeight);

        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0
            && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_settings.WindowLeft, _settings.WindowTop);
        }

        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        splitContainer.SplitterDistance = Math.Max(100, _settings.SplitterDistance);

        if (state == FormWindowState.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreFolderSelection(_settings.SelectedFolder);
    }

    private void RestoreFolderSelection(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        var segments = GetPathSegments(folderPath);
        TreeNodeCollection nodes = treeView.Nodes;
        TreeNode? node = null;

        for (int i = 0; i < segments.Count; i++)
        {
            node = FindNodeByTag(nodes, segments[i]);
            if (node == null) return;

            if (i < segments.Count - 1)
            {
                if (!node.IsExpanded)
                    node.Expand();
                nodes = node.Nodes;
            }
        }

        if (node != null)
        {
            treeView.SelectedNode = node;
            node.EnsureVisible();
        }
    }

    private static TreeNode? FindNodeByTag(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode node in nodes)
        {
            if (string.Equals(node.Tag as string, path, StringComparison.OrdinalIgnoreCase))
                return node;
        }
        return null;
    }

    private static List<string> GetPathSegments(string path)
    {
        var segments = new List<string>();
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            segments.Insert(0, current);
            var parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current) break;
            current = parent;
        }
        return segments;
    }

    private void InitializeTree()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var node = new TreeNode(drive.RootDirectory.FullName)
            {
                Tag = drive.RootDirectory.FullName,
                ImageIndex = 0,
                SelectedImageIndex = 0
            };
            node.Nodes.Add(new TreeNode("loading..."));
            treeView.Nodes.Add(node);
        }
    }

    private void treeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
        if (e.Node is not { Tag: string path }) return;
        if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "loading...")
        {
            e.Node.Nodes.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    var node = new TreeNode(Path.GetFileName(dir) is { Length: > 0 } name ? name : dir)
                    {
                        Tag = dir,
                        ImageIndex = 1,
                        SelectedImageIndex = 1
                    };
                    node.Nodes.Add(new TreeNode("loading..."));
                    e.Node.Nodes.Add(node);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private async void treeView_AfterSelect(object sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is not string path) return;
        try
        {
            await LoadThumbnailsAsync(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            statusLabel.Text = $"エラー: {ex.Message}";
        }
    }

    private async Task LoadThumbnailsAsync(string folderPath)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        ClearThumbnails();
        statusLabel.Text = $"読み込み中: {folderPath}";

        string[] files;
        try
        {
            files = await Task.Run(() =>
                Directory.GetFiles(folderPath)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToArray(), token);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException)
        {
            statusLabel.Text = "アクセスが拒否されました";
            return;
        }
        catch (IOException ex)
        {
            statusLabel.Text = $"エラー: {ex.Message}";
            return;
        }

        if (files.Length == 0)
        {
            statusLabel.Text = "画像ファイルがありません";
            return;
        }

        statusLabel.Text = $"{files.Length} 件の画像";

        _sharedContextMenu = BuildSharedContextMenu();

        // パネルをバッチ作成しながら yield して UI を解放する
        const int BatchSize = 50;
        for (int i = 0; i < files.Length; i += BatchSize)
        {
            if (token.IsCancellationRequested) return;
            int end = Math.Min(i + BatchSize, files.Length);
            var batch = new ThumbnailPanel[end - i];
            for (int j = i; j < end; j++)
            {
                batch[j - i] = CreateThumbnailPanel(files[j], _sharedContextMenu);
                _thumbnailPanels.Add(batch[j - i]);
            }
            thumbnailPanel.SuspendLayout();
            thumbnailPanel.Controls.AddRange(batch);
            thumbnailPanel.ResumeLayout(false);
            await Task.Yield();
        }
        thumbnailPanel.PerformLayout();

        if (token.IsCancellationRequested) return;

        // サムネイルを並列読み込み
        int completed = 0;
        var snapshot = _thumbnailPanels.ToList();
        int parallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);

        await Parallel.ForEachAsync(
            snapshot,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = token },
            async (panel, ct) =>
            {
                if (panel.IsDisposed || panel.Tag is not string filePath) return;

                Image? thumb = null;
                try { thumb = await Task.Run(() => LoadThumbnail(filePath), ct); }
                catch { return; }

                if (ct.IsCancellationRequested || panel.IsDisposed)
                {
                    thumb?.Dispose();
                    return;
                }
                try
                {
                    panel.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested && !panel.IsDisposed)
                            panel.SetThumbnail(thumb);
                        else
                            thumb?.Dispose();
                    });
                }
                catch (ObjectDisposedException) { thumb?.Dispose(); }

                int count = Interlocked.Increment(ref completed);
                if (count % 20 == 0 || count == snapshot.Count)
                    BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            statusLabel.Text = $"{count} / {snapshot.Count} 件読み込み済み";
                    });
            });

        if (!token.IsCancellationRequested)
            statusLabel.Text = $"{files.Length} 件の画像";
    }

    private static Image LoadThumbnail(string filePath)
    {
        using var img = Image.FromFile(filePath);
        var (w, h) = FitSize(img.Width, img.Height, ThumbnailSize, ThumbnailSize);
        // JPEG 埋め込みサムネイルがあれば GDI+ が自動で利用するため高速
        return img.GetThumbnailImage(w, h, null, IntPtr.Zero);
    }

    private ContextMenuStrip BuildSharedContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("似た写真を探す", null, (_, _) =>
        {
            var src = menu.SourceControl;
            var panel = src as ThumbnailPanel ?? src?.Parent as ThumbnailPanel;
            if (panel?.Tag is not string filePath) return;

            using var folderDlg = new SearchFolderDialog(_settings.SearchFolders);
            if (folderDlg.ShowDialog(this) != DialogResult.OK) return;
            if (folderDlg.SelectedFolders.Count == 0)
            {
                MessageBox.Show("検索フォルダーが指定されていません。", "フォト ビューアー",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.SearchFolders = folderDlg.SelectedFolders.ToList();
            _settings.Save();
            var form = new SimilarPhotosForm(filePath, folderDlg.SelectedFolders, _settings);
            form.Show(this);
        }));
        return menu;
    }

    private ThumbnailPanel CreateThumbnailPanel(string filePath, ContextMenuStrip menu)
    {
        var panel = new ThumbnailPanel(filePath, ThumbnailSize, ThumbnailPadding);
        panel.Click += ThumbnailPanel_Click;
        panel.DoubleClick += ThumbnailPanel_DoubleClick;
        panel.ApplyContextMenu(menu);
        return panel;
    }

    private void ThumbnailPanel_Click(object? sender, EventArgs e)
    {
        if (sender is not ThumbnailPanel panel) return;
        _selectedPanel?.SetSelected(false);
        _selectedPanel = panel;
        panel.SetSelected(true);
        if (panel.Tag is string path)
            statusLabel.Text = Path.GetFileName(path);
    }

    private void ThumbnailPanel_DoubleClick(object? sender, EventArgs e)
    {
        if (sender is ThumbnailPanel { Tag: string path })
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { }
        }
    }

    private static (int w, int h) FitSize(int srcW, int srcH, int maxW, int maxH)
    {
        if (srcW <= maxW && srcH <= maxH) return (srcW, srcH);
        var ratio = Math.Min((double)maxW / srcW, (double)maxH / srcH);
        return ((int)(srcW * ratio), (int)(srcH * ratio));
    }

    private void ClearThumbnails()
    {
        _selectedPanel = null;

        foreach (var p in _thumbnailPanels)
        {
            p.Click -= ThumbnailPanel_Click;
            p.DoubleClick -= ThumbnailPanel_DoubleClick;
            p.ContextMenuStrip = null;
        }
        _thumbnailPanels.Clear();

        _sharedContextMenu?.Dispose();
        _sharedContextMenu = null;

        // 旧コンテナを新しい空のコンテナに差し替える。
        // Controls.Clear() は子パネル1枚ごとに SetParent(NULL) を呼ぶため
        // 1254 枚では約 2.5 秒かかる。差し替えなら Win32 呼び出しは 1 回だけ。
        var oldPanel = thumbnailPanel;
        oldPanel.Visible = false; // 差し替え中にデスクトップに浮かないよう非表示
        thumbnailPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(4)
        };
        splitContainer.Panel2.SuspendLayout();
        splitContainer.Panel2.Controls.Remove(oldPanel);
        splitContainer.Panel2.Controls.Add(thumbnailPanel);
        splitContainer.Panel2.ResumeLayout(false);

        // 旧コンテナは次の UI メッセージループで破棄（非同期・UI スレッド上）
        // DestroyWindow が子 HWND を一括破棄するため個別 SetParent より大幅に高速
        BeginInvoke(oldPanel.Dispose);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _loadCts?.Cancel();
        SaveSettings();
        // 設定保存後は Environment.Exit で即終了する。
        // base 経由の DestroyWindow → WM_DESTROY チェーンは
        // 3000 個超の HWND を UI スレッドで同期破棄するため応答なしになる。
        // OS がプロセス終了時にすべてのリソースを回収するため安全。
        Environment.Exit(0);
    }

    private void SaveSettings()
    {
        var state = WindowState;
        _settings.WindowState = (int)state;

        var bounds = state == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;

        if (state != FormWindowState.Maximized)
            _settings.SplitterDistance = splitContainer.SplitterDistance;

        if (treeView.SelectedNode?.Tag is string folder)
            _settings.SelectedFolder = folder;

        _settings.Save();
    }
}

internal class ThumbnailPanel : Panel
{
    private static readonly Color SelectedColor = Color.FromArgb(0, 120, 215);
    private static readonly Color HoverColor = Color.FromArgb(220, 235, 252);
    private static readonly Color NormalColor = Color.White;

    private readonly PictureBox _pictureBox;
    private readonly Label _label;
    private bool _selected;

    public ThumbnailPanel(string filePath, int size, int padding)
    {
        Tag = filePath;

        Width = size + padding * 2;
        Height = size + padding * 2 + 20;
        BackColor = NormalColor;
        Cursor = Cursors.Hand;
        Margin = new Padding(4);

        _pictureBox = new PictureBox
        {
            Width = size,
            Height = size,
            Left = padding,
            Top = padding,
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = Color.LightGray
        };

        _label = new Label
        {
            Text = Path.GetFileName(filePath),
            Left = 2,
            Top = size + padding + 2,
            Width = size + padding * 2 - 4,
            Height = 18,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Black,
            AutoEllipsis = true
        };

        Controls.Add(_pictureBox);
        Controls.Add(_label);

        _pictureBox.Click += (s, e) => OnClick(e);
        _label.Click += (s, e) => OnClick(e);
        _pictureBox.DoubleClick += (s, e) => OnDoubleClick(e);
        _label.DoubleClick += (s, e) => OnDoubleClick(e);

        MouseEnter += (s, e) => { if (!_selected) BackColor = HoverColor; };
        MouseLeave += (s, e) => { if (!_selected) BackColor = NormalColor; };
        _pictureBox.MouseEnter += (s, e) => { if (!_selected) BackColor = HoverColor; };
        _pictureBox.MouseLeave += (s, e) => { if (!_selected) BackColor = NormalColor; };
    }

    public void ApplyContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        _pictureBox.ContextMenuStrip = menu;
        _label.ContextMenuStrip = menu;
    }

    public void SetThumbnail(Image thumbnail)
    {
        var old = _pictureBox.Image;
        _pictureBox.Image = thumbnail;
        _pictureBox.BackColor = Color.Transparent;
        old?.Dispose();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        BackColor = selected ? SelectedColor : NormalColor;
        _label.ForeColor = selected ? Color.White : Color.Black;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pictureBox.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
