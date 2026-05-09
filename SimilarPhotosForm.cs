using System.Collections.Concurrent;

namespace PhotoViewer;

internal record SimilarResult(string Path, double Similarity);

public class SimilarPhotosForm : Form
{
    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };
    private static readonly string[] SkipDirs =
        { "windows", "program files", "program files (x86)", "$recycle.bin", "system volume information" };
    private const double SimilarityThreshold = 0.70;

    private readonly string _sourcePath;
    private readonly IReadOnlyList<string> _searchFolders;
    private ulong _sourceHash;
    private CancellationTokenSource? _searchCts;
    private readonly ConcurrentQueue<SimilarResult> _pendingResults = new();
    private int _scanned;
    private int _found;

    private SplitContainer _topSplit = null!;
    private PictureBox _sourcePictureBox = null!;
    private PictureBox _selectedPictureBox = null!;
    private Label _sourceNameLabel = null!;
    private Label _selectedNameLabel = null!;
    private ListView _listView = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Button _cancelButton = null!;
    private readonly System.Windows.Forms.Timer _flushTimer;

    private int _sortColumn = 2;
    private bool _sortAscending = false;

    public SimilarPhotosForm(string sourcePath, IReadOnlyList<string> searchFolders)
    {
        _sourcePath = sourcePath;
        _searchFolders = DeduplicateFolders(searchFolders);
        _flushTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _flushTimer.Tick += FlushResults;
        InitializeComponents();
        LoadSourceImage();
        Load += (_, _) => StartSearch();
    }

    private void InitializeComponents()
    {
        Text = $"似た写真を探す — {Path.GetFileName(_sourcePath)}";
        Size = new Size(1000, 720);
        MinimumSize = new Size(700, 500);
        Font = new Font("Segoe UI", 9f);

        // ---- 上部比較エリア ----
        _topSplit = new SplitContainer
        {
            Dock = DockStyle.Top,
            Height = 262,
            BackColor = Color.Black,
        };

        _sourceNameLabel = MakeNameLabel(Path.GetFileName(_sourcePath));
        _sourcePictureBox = MakePictureBox();
        _topSplit.Panel1.Controls.Add(_sourcePictureBox);
        _topSplit.Panel1.Controls.Add(_sourceNameLabel);

        _selectedNameLabel = MakeNameLabel("（リストから写真を選択）", Color.Gray);
        _selectedPictureBox = MakePictureBox();
        _topSplit.Panel2.Controls.Add(_selectedPictureBox);
        _topSplit.Panel2.Controls.Add(_selectedNameLabel);

        // ---- ツールバー ----
        var toolPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 4, 4, 4),
        };
        _cancelButton = new Button
        {
            Text = "キャンセル",
            Dock = DockStyle.Right,
            Width = 90,
            Enabled = false,
        };
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Right,
            Width = 160,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Visible = false,
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "準備中...",
        };
        _cancelButton.Click += (_, _) => _searchCts?.Cancel();
        toolPanel.Controls.Add(_statusLabel);
        toolPanel.Controls.Add(_progressBar);
        toolPanel.Controls.Add(_cancelButton);

        // ---- 結果リスト ----
        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            Font = new Font("Segoe UI", 9f),
        };
        _listView.Columns.Add("ファイル名", 220);
        _listView.Columns.Add("フォルダー", 400);
        _listView.Columns.Add("類似度", 70, HorizontalAlignment.Right);
        _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
        _listView.ColumnClick += ListView_ColumnClick;

        Controls.Add(_listView);
        Controls.Add(toolPanel);
        Controls.Add(_topSplit);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Width 確定後に MinSize と中央分割を設定
        _topSplit.Panel1MinSize = 150;
        _topSplit.Panel2MinSize = 150;
        int half = (_topSplit.Width - _topSplit.SplitterWidth) / 2;
        if (half >= _topSplit.Panel1MinSize)
            _topSplit.SplitterDistance = half;
    }

    private static Label MakeNameLabel(string text, Color? fg = null) => new()
    {
        Dock = DockStyle.Top,
        Height = 22,
        BackColor = Color.FromArgb(40, 40, 40),
        ForeColor = fg ?? Color.White,
        Font = new Font("Segoe UI", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(6, 0, 0, 0),
        Text = text,
        AutoEllipsis = true,
    };

    private static PictureBox MakePictureBox() => new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Black,
    };

    private void LoadSourceImage()
    {
        try
        {
            using var src = Image.FromFile(_sourcePath);
            _sourcePictureBox.Image = new Bitmap(src);
        }
        catch { }
    }

    private void StartSearch()
    {
        _searchCts = new CancellationTokenSource();
        _cancelButton.Enabled = true;
        _progressBar.Visible = true;
        _flushTimer.Start();
        _ = RunSearchAsync(_searchCts.Token);
    }

    private async Task RunSearchAsync(CancellationToken token)
    {
        try
        {
            _sourceHash = await Task.Run(() => ImageHasher.Compute(_sourcePath), token);
        }
        catch
        {
            if (!IsDisposed) _statusLabel.Text = "ハッシュ計算に失敗しました";
            return;
        }

        await Task.Run(() =>
        {
            foreach (var folder in _searchFolders)
            {
                if (token.IsCancellationRequested) break;
                if (Directory.Exists(folder))
                    SearchDirectory(folder, token);
            }
        }, CancellationToken.None);

        _flushTimer.Stop();

        if (IsDisposed) return;

        FlushResults(null, EventArgs.Empty);

        SortList();
        _progressBar.Visible = false;
        _cancelButton.Enabled = false;
        _statusLabel.Text = token.IsCancellationRequested
            ? $"キャンセル — {_found:N0} 件見つかりました（{_scanned:N0} ファイルをスキャン）"
            : $"完了 — {_found:N0} 件見つかりました（{_scanned:N0} ファイルをスキャン）";
        if (_listView.Items.Count == 0)
            _statusLabel.Text += "  ※似た写真は見つかりませんでした";
    }

    private static IReadOnlyList<string> DeduplicateFolders(IEnumerable<string> folders)
    {
        var normalized = folders
            .Where(Directory.Exists)
            .Select(f => Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 他フォルダーのサブフォルダーになっているものを除外
        return normalized
            .Where(f => !normalized.Any(other =>
                !string.Equals(f, other, StringComparison.OrdinalIgnoreCase) &&
                f.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void SearchDirectory(string dir, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        var dirName = Path.GetFileName(dir).ToLowerInvariant();
        if (SkipDirs.Contains(dirName)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (token.IsCancellationRequested) return;
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!ImageExtensions.Contains(ext)) continue;

                _scanned++;
                try
                {
                    var hash = ImageHasher.Compute(file);
                    var sim = ImageHasher.Similarity(_sourceHash, hash);
                    if (sim >= SimilarityThreshold &&
                        !string.Equals(file, _sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingResults.Enqueue(new SimilarResult(file, sim));
                        _found++;
                    }
                }
                catch { }
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
                SearchDirectory(sub, token);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void FlushResults(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        _statusLabel.Text = $"{_scanned:N0} ファイルをスキャン中 — {_found:N0} 件見つかりました";

        if (_pendingResults.IsEmpty) return;

        var items = new List<ListViewItem>();
        while (_pendingResults.TryDequeue(out var r))
        {
            var item = new ListViewItem(Path.GetFileName(r.Path)) { Tag = r };
            item.SubItems.Add(Path.GetDirectoryName(r.Path) ?? "");
            item.SubItems.Add($"{(int)(r.Similarity * 100)}%");
            items.Add(item);
        }

        _listView.BeginUpdate();
        _listView.Items.AddRange(items.ToArray());
        _listView.EndUpdate();
    }

    private void SortList()
    {
        _listView.ListViewItemSorter = new ListViewSorter(_sortColumn, _sortAscending);
        _listView.Sort();
        _listView.ListViewItemSorter = null;
    }

    private void ListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        if (_listView.SelectedItems[0].Tag is not SimilarResult result) return;

        _selectedNameLabel.Text =
            $"{Path.GetFileName(result.Path)}  ({(int)(result.Similarity * 100)}% 一致)";
        try
        {
            using var src = Image.FromFile(result.Path);
            var old = _selectedPictureBox.Image;
            _selectedPictureBox.Image = new Bitmap(src);
            old?.Dispose();
        }
        catch { }
    }

    private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_sortColumn == e.Column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = e.Column;
            _sortAscending = e.Column != 2;
        }
        SortList();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _flushTimer.Stop();
        _searchCts?.Cancel();
        _sourcePictureBox.Image?.Dispose();
        _selectedPictureBox.Image?.Dispose();
        base.OnFormClosing(e);
    }
}

internal class ListViewSorter : System.Collections.IComparer
{
    private readonly int _column;
    private readonly bool _ascending;

    public ListViewSorter(int column, bool ascending)
    {
        _column = column;
        _ascending = ascending;
    }

    public int Compare(object? x, object? y)
    {
        var a = (ListViewItem)x!;
        var b = (ListViewItem)y!;

        int result = _column == 2
            ? ParseInt(a.SubItems[2].Text).CompareTo(ParseInt(b.SubItems[2].Text))
            : string.Compare(a.SubItems[_column].Text, b.SubItems[_column].Text,
                             StringComparison.OrdinalIgnoreCase);

        return _ascending ? result : -result;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.TrimEnd('%'), out var v) ? v : 0;
}
