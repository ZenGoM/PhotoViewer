Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
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
"@

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

$AE  = [System.Windows.Automation.AutomationElement]
$ECP = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
$SIP = [System.Windows.Automation.SelectionItemPattern]::Pattern
$TCh = [System.Windows.Automation.TreeScope]::Children
$TDe = [System.Windows.Automation.TreeScope]::Descendants
$TSu = [System.Windows.Automation.TreeScope]::Subtree
$desktop = $AE::RootElement

function UIFind($root,$scope,$prop,$val) {
    $c = New-Object System.Windows.Automation.PropertyCondition($prop,$val)
    return $root.FindFirst($scope,$c)
}
function UIExpandNode($node) {
    $pat=$null
    if ($node.TryGetCurrentPattern($ECP,[ref]$pat)) {
        if ($pat.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            $pat.Expand(); Start-Sleep -Milliseconds 500
        }
    }
}
function UISelectNode($node) {
    $pat=$null
    if ($node.TryGetCurrentPattern($SIP,[ref]$pat)) { $pat.Select() }
}

# ---- Kill & reset settings ----
Stop-Process -Name PhotoViewer -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

@{
    SplitterDistance=200; WindowWidth=1100; WindowHeight=700
    WindowLeft=-1; WindowTop=-1; WindowState=0
    SelectedFolder=""; SearchFolders=@()
    SimilarPhotosWidth=1000; SimilarPhotosHeight=720
    SimilarPhotosMainSplit=262; SimilarPhotosSourceWidth=0
} | ConvertTo-Json -Depth 5 | Set-Content $settingsPath
Write-Host "Settings reset to defaults."

# ---- Start app ----
$exePath = "C:\Users\masanori\source\repos\PhotoViewer\bin\Release\net9.0-windows\PhotoViewer.exe"
$proc = Start-Process $exePath -PassThru
Start-Sleep -Milliseconds 2500

$mainForm = $null
for ($i=0;$i -lt 12;$i++) {
    $mainForm = UIFind $desktop $TCh $AE::ClassNameProperty "MainForm"
    if ($mainForm) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $mainForm) { Write-Host "ERROR: MainForm not found"; exit 1 }
$mainHwnd = [IntPtr]$mainForm.Current.NativeWindowHandle
[W32]::SetForegroundWindow($mainHwnd)|Out-Null; Start-Sleep -Milliseconds 300
Write-Host "MainForm found."

# ---- Navigate tree ----
$tv = UIFind $mainForm $TDe $AE::ClassNameProperty "TreeView"
$segments = @("C:\","Users","masanori","OneDrive","Pictures")
$node = $tv
foreach ($seg in $segments) {
    $child = UIFind $node $TCh $AE::NameProperty $seg
    if (-not $child) { Write-Host "ERROR: Node not found: $seg"; exit 1 }
    if ($seg -eq "Pictures") { UISelectNode $child }
    else { UIExpandNode $child }
    $node = $child
}
Write-Host "Pictures selected. Waiting for thumbnails..."
Start-Sleep -Milliseconds 5000

# ---- Helper: open SimilarPhotosForm ----
function OpenSimForm($mhwnd) {
    [W32]::SetForegroundWindow($mhwnd)|Out-Null; Start-Sleep -Milliseconds 400
    $lbl = [W32]::FindChildText($mhwnd,"386A7753.JPG")
    if ($lbl -eq [IntPtr]::Zero) { Write-Host "ERROR: thumbnail label not found"; return $null }
    $lr = [W32]::GetRect($lbl)
    $cx=[int](($lr.L+$lr.R)/2); $cy=[int](($lr.T+$lr.B)/2)
    Write-Host "  Label at ($cx,$cy) - right-clicking"
    RClick $cx $cy
    Start-Sleep -Milliseconds 300

    # find context menu item
    $mi = UIFind $desktop $TSu $AE::NameProperty "似た写真を探す"
    if ($mi) {
        $rb = $mi.Current.BoundingRectangle
        LClick ([int]($rb.X+$rb.Width/2)) ([int]($rb.Y+$rb.Height/2))
    } else {
        Write-Host "  Menu item not found via UIA, sending Enter"
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    }
    Start-Sleep -Milliseconds 2500

    $sf = $null
    for ($i=0;$i -lt 12;$i++) {
        $all = $desktop.FindAll($TCh,[System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $all) { if ($w.Current.Name -like "*386A7753*") { $sf=$w; break } }
        if ($sf) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $sf) { Write-Host "ERROR: SimilarPhotosForm not found"; return $null }
    Write-Host "  SimilarPhotosForm opened: '$($sf.Current.Name)'"
    return $sf
}

function CloseSimForm($hwnd) {
    [W32]::SetForegroundWindow($hwnd)|Out-Null; Start-Sleep -Milliseconds 200
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait("%{F4}")
    Start-Sleep -Milliseconds 1500
}

# ==============================================================
Write-Host ""
Write-Host "=== Open #1 ==="
$sf1 = OpenSimForm $mainHwnd
if (-not $sf1) { exit 1 }
$sfH1 = [IntPtr]$sf1.Current.NativeWindowHandle
[W32]::SetForegroundWindow($sfH1)|Out-Null; Start-Sleep -Milliseconds 600

$wr1 = [W32]::GetRect($sfH1)
$iW = $wr1.R - $wr1.L; $iH = $wr1.B - $wr1.T
Write-Host "Initial size: ${iW}x${iH} (expected 1000x720)"

# ---- Resize window (Test 2) ----
$newW=880; $newH=600
[W32]::MoveWindow($sfH1,$wr1.L,$wr1.T,$newW,$newH,$true)|Out-Null
Start-Sleep -Milliseconds 400
$wr1b = [W32]::GetRect($sfH1)
Write-Host "Resized to: $($wr1b.R-$wr1b.L)x$($wr1b.B-$wr1b.T)"

# ---- Drag _mainSplit (Test 1): splitter at clientTop+262, drag to +350 ----
$titleH = 30
$clientTop = $wr1b.T + $titleH
$cx = $wr1b.L + [int]($newW/2)
$oldMainY = $clientTop + 262
$newMainY = $clientTop + 350
Write-Host "Dragging mainSplit: Y $oldMainY -> $newMainY"
Drag $cx $oldMainY $cx $newMainY

# ---- Drag _topSplit (Test 3): splitter at clientLeft+(newW-4)/2, drag to +100 ----
$border = 1
$clientLeft = $wr1b.L + $border
$oldSrcX = $clientLeft + [int](($newW-4)/2)   # auto-center
$newSrcX  = $clientLeft + 550
$topY = $clientTop + 150
Write-Host "Dragging topSplit: X $oldSrcX -> $newSrcX"
Drag $oldSrcX $topY $newSrcX $topY

# ---- Close and read settings ----
Write-Host "Closing form #1..."
CloseSimForm $sfH1
$s1 = ReadSettings
Write-Host ""
Write-Host "--- Saved settings ---"
Write-Host "SimilarPhotosWidth:     $($s1.SimilarPhotosWidth)    (target $newW)"
Write-Host "SimilarPhotosHeight:    $($s1.SimilarPhotosHeight)   (target $newH)"
Write-Host "SimilarPhotosMainSplit: $($s1.SimilarPhotosMainSplit) (target ~350)"
Write-Host "SimilarPhotosSourceWidth: $($s1.SimilarPhotosSourceWidth) (target ~550)"

$t2save = [Math]::Abs($s1.SimilarPhotosWidth-$newW) -le 20 -and [Math]::Abs($s1.SimilarPhotosHeight-$newH) -le 20
$t1save = [Math]::Abs($s1.SimilarPhotosMainSplit - 350) -le 40
$t3save = [Math]::Abs($s1.SimilarPhotosSourceWidth - 550) -le 40

Write-Host "T1 save: $(if($t1save){'OK'}else{'FAIL'})"
Write-Host "T2 save: $(if($t2save){'OK'}else{'FAIL'})"
Write-Host "T3 save: $(if($t3save){'OK'}else{'FAIL'})"

# ==============================================================
Write-Host ""
Write-Host "=== Open #2 (restore check) ==="
[W32]::SetForegroundWindow($mainHwnd)|Out-Null; Start-Sleep -Milliseconds 500
$sf2 = OpenSimForm $mainHwnd
if (-not $sf2) { exit 1 }
$sfH2 = [IntPtr]$sf2.Current.NativeWindowHandle
[W32]::SetForegroundWindow($sfH2)|Out-Null; Start-Sleep -Milliseconds 1000

$wr2 = [W32]::GetRect($sfH2)
$rW = $wr2.R - $wr2.L; $rH = $wr2.B - $wr2.T
$s2 = ReadSettings
Write-Host "Restored size: ${rW}x${rH} (expected $($s1.SimilarPhotosWidth)x$($s1.SimilarPhotosHeight))"
Write-Host "Settings unchanged after reopen: MainSplit=$($s2.SimilarPhotosMainSplit) SrcW=$($s2.SimilarPhotosSourceWidth)"

$t2rest = [Math]::Abs($rW - $s1.SimilarPhotosWidth) -le 20 -and [Math]::Abs($rH - $s1.SimilarPhotosHeight) -le 20
$t1rest = ($s2.SimilarPhotosMainSplit -eq $s1.SimilarPhotosMainSplit)
$t3rest = ($s2.SimilarPhotosSourceWidth -eq $s1.SimilarPhotosSourceWidth)

CloseSimForm $sfH2
Stop-Process -Name PhotoViewer -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "============================="
Write-Host "RESULTS"
Write-Host "============================="
Write-Host "Test 1 (list height)  : $(if($t1save -and $t1rest){'PASS'}else{'FAIL'})  save=$(if($t1save){'OK'}else{'NG'}) restore=$(if($t1rest){'OK'}else{'NG'})  MainSplit=$($s1.SimilarPhotosMainSplit)"
Write-Host "Test 2 (window size)  : $(if($t2save -and $t2rest){'PASS'}else{'FAIL'})  save=$(if($t2save){'OK'}else{'NG'}) restore=$(if($t2rest){'OK'}else{'NG'})  ${rW}x${rH}"
Write-Host "Test 3 (source width) : $(if($t3save -and $t3rest){'PASS'}else{'FAIL'})  save=$(if($t3save){'OK'}else{'NG'}) restore=$(if($t3rest){'OK'}else{'NG'})  SourceWidth=$($s1.SimilarPhotosSourceWidth)"
