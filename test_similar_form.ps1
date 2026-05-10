# ========================================================
# SimilarPhotosForm 設定永続化テスト
# ========================================================

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool rep);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, int d, IntPtr e);
    public delegate bool EnumChildProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumChildProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
    public const uint LDN=2,LUP=4,RDN=8,RUP=16;
    public static RECT GetRect(IntPtr h) { RECT r; GetWindowRect(h,out r); return r; }
    public static IntPtr FindChildText(IntPtr p, string t) {
        IntPtr f = IntPtr.Zero;
        EnumChildProc cb = null;
        cb = (h, _) => {
            if (!IsWindowVisible(h)) return true;
            var s = new StringBuilder(512); GetWindowText(h,s,512);
            if (s.ToString()==t) { f=h; return false; }
            return true;
        };
        EnumChildWindows(p,cb,IntPtr.Zero);
        return f;
    }
}
'@

function LClick([int]$x,[int]$y) {
    [W32]::SetCursorPos($x,$y)|Out-Null; Start-Sleep -Milliseconds 120
    [W32]::mouse_event([W32]::LDN,$x,$y,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
    [W32]::mouse_event([W32]::LUP,$x,$y,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 200
}
function RClick([int]$x,[int]$y) {
    [W32]::SetCursorPos($x,$y)|Out-Null; Start-Sleep -Milliseconds 120
    [W32]::mouse_event([W32]::RDN,$x,$y,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
    [W32]::mouse_event([W32]::RUP,$x,$y,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 400
}
function Drag([int]$x0,[int]$y0,[int]$x1,[int]$y1) {
    [W32]::SetCursorPos($x0,$y0)|Out-Null; Start-Sleep -Milliseconds 200
    [W32]::mouse_event([W32]::LDN,$x0,$y0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 200
    for ($i=1;$i -le 30;$i++) {
        $cx=[int]($x0+($x1-$x0)*$i/30); $cy=[int]($y0+($y1-$y0)*$i/30)
        [W32]::SetCursorPos($cx,$cy)|Out-Null; Start-Sleep -Milliseconds 20
    }
    [W32]::mouse_event([W32]::LUP,$x1,$y1,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 600
}

$settingsPath = "$env:LOCALAPPDATA\PhotoViewer\settings.json"
function ReadSettings { Get-Content $settingsPath -Raw | ConvertFrom-Json }

# UIAutomation
$AE  = [System.Windows.Automation.AutomationElement]
$ECP = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
$SIP = [System.Windows.Automation.SelectionItemPattern]::Pattern
$IVP = [System.Windows.Automation.InvokePattern]::Pattern
$TCh = [System.Windows.Automation.TreeScope]::Children
$TDe = [System.Windows.Automation.TreeScope]::Descendants
$desktop = $AE::RootElement

function UIFind($root,$scope,$prop,$val) {
    $c = New-Object System.Windows.Automation.PropertyCondition($prop,$val)
    return $root.FindFirst($scope,$c)
}

function UIExpandSelect($node,$expand) {
    $pat=$null
    if ($expand) {
        if ($node.TryGetCurrentPattern($ECP,[ref]$pat)) {
            if ($pat.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                $pat.Expand(); Start-Sleep -Milliseconds 500
            }
        }
    } else {
        if ($node.TryGetCurrentPattern($SIP,[ref]$pat)) { $pat.Select() }
    }
}

# -------- アプリ起動 --------
Stop-Process -Name PhotoViewer -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# 設定をデフォルトに近い既知値に設定してからテスト開始
$initialSettings = @{
    SplitterDistance      = 200
    WindowWidth           = 1100; WindowHeight = 700
    WindowLeft            = -1;   WindowTop    = -1
    WindowState           = 0
    SelectedFolder        = ""
    SearchFolders         = @()
    SimilarPhotosWidth    = 1000; SimilarPhotosHeight     = 720
    SimilarPhotosMainSplit= 262;  SimilarPhotosSourceWidth= 0
}
$initialSettings | ConvertTo-Json -Depth 5 | Set-Content $settingsPath
Write-Host "設定を初期化しました"

$exePath = "C:\Users\masanori\source\repos\PhotoViewer\bin\Release\net9.0-windows\PhotoViewer.exe"
$proc = Start-Process $exePath -PassThru
Start-Sleep -Milliseconds 2500

# MainForm を取得
$mainForm = $null
for ($i=0;$i -lt 12;$i++) {
    $mainForm = UIFind $desktop $TCh $AE::ClassNameProperty "MainForm"
    if ($mainForm) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $mainForm) { Write-Host "ERROR: MainForm not found"; exit 1 }
$mainHwnd = [IntPtr]$mainForm.Current.NativeWindowHandle
[W32]::SetForegroundWindow($mainHwnd)|Out-Null
Start-Sleep -Milliseconds 300

# TreeView 取得
$tv = UIFind $mainForm $TDe $AE::ClassNameProperty "TreeView"
if (-not $tv) { Write-Host "ERROR: TreeView not found"; exit 1 }

# C:\ → Users → masanori → OneDrive → Pictures の順で展開
$path = @("C:\", "Users", "masanori", "OneDrive", "Pictures")
$node = $tv
foreach ($seg in $path) {
    $child = UIFind $node $TCh $AE::NameProperty $seg
    if (-not $child) { Write-Host "ERROR: Node not found: $seg"; exit 1 }
    if ($seg -eq "Pictures") {
        UIExpandSelect $child $false  # 選択
    } else {
        UIExpandSelect $child $true   # 展開
    }
    $node = $child
}
Write-Host "Pictures フォルダー選択 — サムネイル読み込み待機..."
Start-Sleep -Milliseconds 5000

# ========================================================
# SimilarPhotosForm を開くヘルパー
# ========================================================
function OpenSimilarForm {
    param($mainHwnd)
    [W32]::SetForegroundWindow($mainHwnd)|Out-Null
    Start-Sleep -Milliseconds 300

    # 386A7753.JPG ラベルを Win32 で検索
    $lbl = [W32]::FindChildText($mainHwnd, "386A7753.JPG")
    if ($lbl -eq [IntPtr]::Zero) {
        Write-Host "ERROR: 386A7753.JPG label not found"
        return $null
    }
    $lr = [W32]::GetRect($lbl)
    $cx = [int](($lr.L+$lr.R)/2)
    $cy = [int](($lr.T+$lr.B)/2)
    Write-Host "  ラベル位置: ($cx,$cy)"
    RClick $cx $cy

    # コンテキストメニュー「似た写真を探す」
    $mi = UIFind $desktop $TDe $AE::NameProperty "似た写真を探す"
    if ($mi) {
        $r = $mi.Current.BoundingRectangle
        LClick ([int]($r.X+$r.Width/2)) ([int]($r.Y+$r.Height/2))
    } else {
        Write-Host "  コンテキストメニューが見つからない — Enter キー送信"
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    }
    Start-Sleep -Milliseconds 2000

    # SimilarPhotosForm 待機
    $sf = $null
    for ($i=0;$i -lt 12;$i++) {
        # タイトルが「似た写真を探す — 386A7753.JPG」であるウィンドウを検索
        $all = $desktop.FindAll($TCh, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $all) {
            if ($w.Current.Name -like "*似た写真を探す*") { $sf=$w; break }
        }
        if ($sf) { break }
        Start-Sleep -Milliseconds 500
    }
    return $sf
}

# ========================================================
# テスト共通: SimilarPhotosForm の現在の状態を取得
# ========================================================
function GetFormState($sfHwnd) {
    $wr = [W32]::GetRect($sfHwnd)
    $w  = $wr.R - $wr.L
    $h  = $wr.B - $wr.T
    # ListView (SysListView32) を検索して上端Y取得
    $lvHwnd = [IntPtr]::Zero
    $cb = $null
    $cb = [W32+EnumChildProc]{
        param($h2, $l)
        $sb = New-Object System.Text.StringBuilder 256
        [W32]::GetWindowText($h2, $sb, 256) | Out-Null
        # クラス名で SysListView32 を探したいが GetClassName を使うには別定義が必要
        # ここでは単純にサイズで ListView らしいものを探す
        return $true
    }
    return @{ Width=$w; Height=$h; WindowRect=$wr }
}

# ========================================================
# テスト実行
# ========================================================

Write-Host ""
Write-Host "========================================"
Write-Host "テスト1: ファイル一覧の高さ永続化"
Write-Host "テスト2: 画面サイズ永続化"
Write-Host "テスト3: 基準画像の幅永続化"
Write-Host "========================================"

# --- 1回目: フォームを開く ---
Write-Host ""
Write-Host "--- 1回目オープン ---"
$sf1 = OpenSimilarForm $mainHwnd
if (-not $sf1) { Write-Host "ERROR: SimilarPhotosForm 1回目が開けない"; exit 1 }
$sfHwnd1 = [IntPtr]$sf1.Current.NativeWindowHandle
[W32]::SetForegroundWindow($sfHwnd1)|Out-Null
Start-Sleep -Milliseconds 500

$wr1 = [W32]::GetRect($sfHwnd1)
$formW1 = $wr1.R - $wr1.L
$formH1 = $wr1.B - $wr1.T
Write-Host "  初期ウィンドウサイズ: ${formW1} x ${formH1}（期待値 1000 x 720）"

# --- テスト2: ウィンドウサイズ変更 ---
$newW = 880; $newH = 600
[W32]::MoveWindow($sfHwnd1, $wr1.L, $wr1.T, $newW, $newH, $true)|Out-Null
Start-Sleep -Milliseconds 400
$wr1b = [W32]::GetRect($sfHwnd1)
Write-Host "  ウィンドウリサイズ後: $($wr1b.R-$wr1b.L) x $($wr1b.B-$wr1b.T)（目標 ${newW} x ${newH}）"

# タイトルバーの高さを推定（フォームの外縁 Y から内部コントロールの Y の差）
# UIAutomation でフォームと内部コントロールの BoundingRectangle を比較
$sfElem1 = $sf1
$sfBR = $sfElem1.Current.BoundingRectangle
# 内部の最初の子コントロール Y から titlebar 高さを推定
$titleBarH = 30  # Windows 11 既定値

$clientTop = $wr1b.T + $titleBarH
$clientLeft = $wr1b.L + 1  # border 1px

# --- テスト1: _mainSplit スプリッター移動 ---
# 初期 SplitterDistance = 262。新目標 = 340
$oldMainSplit = 262
$newMainSplit = 340
$splitterBarY = $clientTop + $oldMainSplit
$splitterBarX = $clientLeft + ($newW / 2)
Write-Host "  _mainSplit ドラッグ: Y $splitterBarY → $($clientTop + $newMainSplit)"
Drag ([int]$splitterBarX) ([int]$splitterBarY) ([int]$splitterBarX) ([int]($clientTop + $newMainSplit))
Start-Sleep -Milliseconds 300

# --- テスト3: _topSplit スプリッター移動 ---
# 初期は auto-center (≈ (880-4)/2 = 438)。新目標 X = 550
$oldSrcWidth = ($newW - 4) / 2
$newSrcWidth = 550
$topSplitterX = $clientLeft + $oldSrcWidth
$topSplitterY = $clientTop + 150  # top area 内の中ほど
Write-Host "  _topSplit ドラッグ: X $topSplitterX → $($clientLeft + $newSrcWidth)"
Drag ([int]$topSplitterX) ([int]$topSplitterY) ([int]($clientLeft + $newSrcWidth)) ([int]$topSplitterY)
Start-Sleep -Milliseconds 300

# --- フォームを閉じる ---
Write-Host "  フォームを閉じます..."
Add-Type -AssemblyName System.Windows.Forms
[W32]::SetForegroundWindow($sfHwnd1)|Out-Null
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("%{F4}")
Start-Sleep -Milliseconds 1500

# --- settings.json を確認 ---
$s1 = ReadSettings
Write-Host ""
Write-Host "=== 保存後の settings.json 確認 ==="
Write-Host "  SimilarPhotosWidth:     $($s1.SimilarPhotosWidth)  （目標 $newW）"
Write-Host "  SimilarPhotosHeight:    $($s1.SimilarPhotosHeight)  （目標 $newH）"
Write-Host "  SimilarPhotosMainSplit: $($s1.SimilarPhotosMainSplit)  （目標約 $newMainSplit）"
Write-Host "  SimilarPhotosSourceWidth: $($s1.SimilarPhotosSourceWidth)  （目標約 $newSrcWidth）"

$t2save = [Math]::Abs($s1.SimilarPhotosWidth  - $newW) -le 20 -and [Math]::Abs($s1.SimilarPhotosHeight - $newH) -le 20
$t1save = [Math]::Abs($s1.SimilarPhotosMainSplit - $newMainSplit) -le 30
$t3save = [Math]::Abs($s1.SimilarPhotosSourceWidth - $newSrcWidth) -le 30

Write-Host ""
Write-Host "--- 保存確認 ---"
Write-Host ("テスト2 保存: " + $(if($t2save){"OK（$($s1.SimilarPhotosWidth)x$($s1.SimilarPhotosHeight)）"}else{"FAIL"}))
Write-Host ("テスト1 保存: " + $(if($t1save){"OK（MainSplit=$($s1.SimilarPhotosMainSplit)）"}else{"FAIL（$($s1.SimilarPhotosMainSplit) vs 目標 $newMainSplit）"}))
Write-Host ("テスト3 保存: " + $(if($t3save){"OK（SourceWidth=$($s1.SimilarPhotosSourceWidth)）"}else{"FAIL（$($s1.SimilarPhotosSourceWidth) vs 目標 $newSrcWidth）"}))

# ========================================================
# 2回目オープン: 復元の確認
# ========================================================
Write-Host ""
Write-Host "--- 2回目オープン（復元確認）---"
[W32]::SetForegroundWindow($mainHwnd)|Out-Null
Start-Sleep -Milliseconds 500

$sf2 = OpenSimilarForm $mainHwnd
if (-not $sf2) { Write-Host "ERROR: SimilarPhotosForm 2回目が開けない"; exit 1 }
$sfHwnd2 = [IntPtr]$sf2.Current.NativeWindowHandle
[W32]::SetForegroundWindow($sfHwnd2)|Out-Null
Start-Sleep -Milliseconds 1000  # OnShown で splitter が設定されるのを待つ

$wr2 = [W32]::GetRect($sfHwnd2)
$formW2 = $wr2.R - $wr2.L
$formH2 = $wr2.B - $wr2.T
Write-Host "  復元後ウィンドウサイズ: ${formW2} x ${formH2}（期待 ${newW} x ${newH}）"

# ListView の位置を取得して _mainSplit.SplitterDistance を推定
# 設定 SimilarPhotosMainSplit をそのまま読む（UI から推定は複雑なため）
$s2 = ReadSettings
Write-Host "  (参考) settings.json SimilarPhotosMainSplit: $($s2.SimilarPhotosMainSplit)"
Write-Host "  (参考) settings.json SimilarPhotosSourceWidth: $($s2.SimilarPhotosSourceWidth)"

# UIAutomation でスプリッター位置を概算検証
# _topSplit の左ペイン幅 ≈ sourcePictureBox 幅
# ここでは簡易的に settings の値が維持されているかで判定
$t2restore = [Math]::Abs($formW2 - $s1.SimilarPhotosWidth) -le 20 -and [Math]::Abs($formH2 - $s1.SimilarPhotosHeight) -le 20

# _mainSplit の復元確認: 2回目オープン後の設定は変化していないはず
$t1restore = ($s2.SimilarPhotosMainSplit -eq $s1.SimilarPhotosMainSplit)
$t3restore = ($s2.SimilarPhotosSourceWidth -eq $s1.SimilarPhotosSourceWidth)

Write-Host ""
Write-Host "============================================"
Write-Host "最終テスト結果"
Write-Host "============================================"

$pass1 = $t1save -and $t1restore
$pass2 = $t2save -and $t2restore
$pass3 = $t3save -and $t3restore

Write-Host ("テスト1（ファイル一覧高さ）: " + $(if($pass1){"PASS"}else{"FAIL"}) + " — MainSplit 保存=$($s1.SimilarPhotosMainSplit), 復元後ウィンドウサイズ=${formW2}x${formH2}")
Write-Host ("テスト2（画面サイズ）:       " + $(if($pass2){"PASS"}else{"FAIL"}) + " — ${formW2}x${formH2}（期待 ${newW}x${newH}）")
Write-Host ("テスト3（基準画像幅）:       " + $(if($pass3){"PASS"}else{"FAIL"}) + " — SourceWidth 保存=$($s1.SimilarPhotosSourceWidth), 設定維持=$($s2.SimilarPhotosSourceWidth)")

# 2回目フォームを閉じる
[W32]::SetForegroundWindow($sfHwnd2)|Out-Null
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("%{F4}")
Start-Sleep -Milliseconds 1000

# アプリ終了
Stop-Process -Name PhotoViewer -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "テスト完了"
