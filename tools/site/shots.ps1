param(
    [string]$Models = (Join-Path $env:APPDATA "Tapybara\models"),
    [string]$Work = (Join-Path $env:TEMP "tapybara-site")
)
# Снимки окон для сайта: assets/screens/*-dark.webp и *-light.webp.
#
# Собирает Release, копирует его в отдельную портативную папку и засевает её
# демо-данными (seed.py): живой профиль и настоящие звонки не трогаются.
# Снимаются только окна Tapybara — через PrintWindow, чужие окна в кадр не
# попадают. Там, где нужен настоящий щелчок или ввод (двойной щелчок по
# слову), окно выводится вперёд, и щелчок делается, только если под курсором
# именно оно.
#
# Пока скрипт работает, мышь и клавиатуру лучше не трогать: минута-две.
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$app = Join-Path $Work "app"
$raw = Join-Path $Work "png"
$screens = Join-Path $repo "assets\screens"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class SiteWin {
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int size);
  public struct RECT { public int L, T, R, B; }
  public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
  public static List<IntPtr> All(uint pid) { var l = new List<IntPtr>();
    EnumWindows((h, p) => { uint o; GetWindowThreadProcessId(h, out o); if (o == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static IntPtr Find(uint pid, string title) {
    foreach (var h in All(pid)) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == title) return h; }
    return IntPtr.Zero; }
  public static bool Ours(IntPtr h, uint pid) { uint o; GetWindowThreadProcessId(h, out o); return o == pid; }
  public static void Click() { mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero); }
  public static void Type(string s) { foreach (char c in s) { var d = new INPUT { type = 1 }; d.ki.wScan = c; d.ki.dwFlags = 4; var u = d; u.ki.dwFlags = 6; SendInput(2, new[] { d, u }, Marshal.SizeOf(typeof(INPUT))); System.Threading.Thread.Sleep(25); } }
}
"@
[SiteWin]::SetProcessDPIAware() | Out-Null
$root = [System.Windows.Automation.AutomationElement]::RootElement

function Element([int]$procId, [string]$name, [int]$index = 0) {
    $pc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    $nc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $found = @()
    foreach ($w in $root.FindAll("Children", $pc)) { foreach ($e in $w.FindAll("Descendants", $nc)) { $found += $e } }
    if ($found.Count -le $index) { throw "not found: $name" }
    return $found[$index]
}

# Выбрать (строку списка, пункт навигации) или нажать — без мыши.
function Press([int]$procId, [string]$name, [int]$index = 0) {
    $e = Element $procId $name $index
    foreach ($pattern in @([System.Windows.Automation.SelectionItemPattern]::Pattern, [System.Windows.Automation.InvokePattern]::Pattern)) {
        try { $e.GetCurrentPattern($pattern) | Out-Null } catch { continue }
        if ($pattern -eq [System.Windows.Automation.InvokePattern]::Pattern) { $e.GetCurrentPattern($pattern).Invoke() }
        else { $e.GetCurrentPattern($pattern).Select() }
        Start-Sleep -Milliseconds 900
        return
    }
    # Строка списка звонков — это текст внутри элемента списка.
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    for ($p = $walker.GetParent($e); $p; $p = $walker.GetParent($p)) {
        try { $p.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 900; return } catch { }
    }
    throw "cannot press: $name"
}

function Place([int]$procId, [string]$title, [int]$w, [int]$h) {
    $hw = [SiteWin]::Find([uint32]$procId, $title)
    if ($hw -eq [IntPtr]::Zero) { throw "no window: $title" }
    [SiteWin]::SetWindowPos($hw, [IntPtr]::Zero, 80, 60, $w, $h, 0x0014) | Out-Null  # NOZORDER|NOACTIVATE
    Start-Sleep -Milliseconds 900
}

# Окно и все его всплывающие окна — одной картинкой, по их местам на экране.
function Shot([int]$procId, [string]$title, [string]$out, [switch]$AroundPopup) {
    $hw = [SiteWin]::Find([uint32]$procId, $title)
    $main = New-Object SiteWin+RECT; [SiteWin]::GetWindowRect($hw, [ref]$main) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($main.R - $main.L), ($main.B - $main.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    foreach ($h in @($hw) + @([SiteWin]::All([uint32]$procId) | Where-Object { $_ -ne $hw })) {
        $r = New-Object SiteWin+RECT; [SiteWin]::GetWindowRect($h, [ref]$r) | Out-Null
        # Поверх снимаемого окна — только его всплывающие: они целиком внутри.
        # Главное окно за диалогом или окном настроек в кадр не нужно.
        if ($h -ne $hw -and ($r.L -lt $main.L -or $r.T -lt $main.T -or $r.R -gt $main.R -or $r.B -gt $main.B)) { continue }
        $b = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
        $gg = [System.Drawing.Graphics]::FromImage($b); $dc = $gg.GetHdc(); [SiteWin]::PrintWindow($h, $dc, 2) | Out-Null; $gg.ReleaseHdc($dc)
        $g.DrawImage($b, $r.L - $main.L, $r.T - $main.T)
    }
    if ($AroundPopup) {
        # Карточка и слово, к которому она относится, — строка над ней. Прозрачные
        # края и тень всплывающего окна PrintWindow отдаёт чёрным, поэтому этот
        # кусок снимается с экрана — но только если на всей его площади окна
        # Tapybara: чужое окно в кадр не попадёт.
        $popup = [SiteWin]::All([uint32]$procId) | Where-Object { $_ -ne $hw } | Select-Object -First 1
        $r = New-Object SiteWin+RECT; [SiteWin]::GetWindowRect($popup, [ref]$r) | Out-Null
        $left = [Math]::Max($main.L, $r.L - 60); $top = [Math]::Max($main.T, $r.T - 110)
        $right = [Math]::Min($main.R, $r.R + 60); $bottom = [Math]::Min($main.B, $r.B + 20)
        for ($x = $left; $x -lt $right; $x += 24) {
            for ($y = $top; $y -lt $bottom; $y += 24) {
                $pt = New-Object SiteWin+POINT; $pt.X = $x; $pt.Y = $y
                if (-not [SiteWin]::Ours([SiteWin]::WindowFromPoint($pt), [uint32]$procId)) { throw "another window over the card" }
            }
        }
        $bmp = New-Object System.Drawing.Bitmap ($right - $left), ($bottom - $top)
        [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($left, $top, 0, 0, $bmp.Size)
    }
    $bmp.Save($out)
}

# Двойной щелчок по слову в реплике — только если впереди и под курсором Tapybara.
function DoubleClickWord([int]$procId, [string]$word) {
    $pc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    $w = $root.FindAll("Children", $pc) | Where-Object { $_.Current.Name -eq "Tapybara" } | Select-Object -First 1
    try { $w.SetFocus() } catch { }
    Start-Sleep -Milliseconds 600
    if (-not [SiteWin]::Ours([SiteWin]::GetForegroundWindow(), [uint32]$procId)) { throw "Tapybara is not in front" }
    $ec = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    foreach ($e in $w.FindAll("Descendants", $ec)) {
        try { $range = $e.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.FindText($word, $false, $false) } catch { continue }
        if (-not $range) { continue }
        $rect = $range.GetBoundingRectangles()[0]
        $pt = New-Object SiteWin+POINT; $pt.X = [int]($rect.X + $rect.Width / 2); $pt.Y = [int]($rect.Y + $rect.Height / 2)
        if (-not [SiteWin]::Ours([SiteWin]::WindowFromPoint($pt), [uint32]$procId)) { throw "another window is over the word" }
        $old = New-Object SiteWin+POINT; [SiteWin]::GetCursorPos([ref]$old) | Out-Null
        [SiteWin]::SetCursorPos($pt.X, $pt.Y) | Out-Null
        # Первый щелчок может только активировать окно — поэтому до двух попыток.
        for ($i = 0; $i -lt 2 -and ([SiteWin]::All([uint32]$procId)).Count -lt 2; $i++) {
            [SiteWin]::Click(); Start-Sleep -Milliseconds 60; [SiteWin]::Click(); Start-Sleep -Milliseconds 900
        }
        [SiteWin]::SetCursorPos($old.X, $old.Y) | Out-Null
        return
    }
    throw "word not found: $word"
}

# Щёлкнуть элемент мышью — там, где UIA оставил бы рамку фокуса клавиатуры
# (пункты навигации). Только если впереди и под курсором Tapybara.
function ClickElement([int]$procId, [string]$name) {
    $e = Element $procId $name
    $r = $e.Current.BoundingRectangle
    $pt = New-Object SiteWin+POINT; $pt.X = [int]($r.X + $r.Width / 2); $pt.Y = [int]($r.Y + $r.Height / 2)
    if (-not [SiteWin]::Ours([SiteWin]::GetForegroundWindow(), [uint32]$procId)) { throw "Tapybara is not in front" }
    if (-not [SiteWin]::Ours([SiteWin]::WindowFromPoint($pt), [uint32]$procId)) { throw "another window is over $name" }
    $old = New-Object SiteWin+POINT; [SiteWin]::GetCursorPos([ref]$old) | Out-Null
    [SiteWin]::SetCursorPos($pt.X, $pt.Y) | Out-Null
    [SiteWin]::Click()
    [SiteWin]::SetCursorPos($old.X, $old.Y) | Out-Null
    Start-Sleep -Milliseconds 900
}

function Webp([string]$png, [string]$name) {
    python -c "from PIL import Image; Image.open(r'$png').convert('RGB').save(r'$(Join-Path $screens $name)', 'WEBP', quality=86, method=6)"
}

# --- сборка и засев -------------------------------------------------------
dotnet build (Join-Path $repo "src\Tapybara.App") -c Release -v q -nologo | Out-Null
$build = Join-Path $repo "src\Tapybara.App\bin\Release\net10.0-windows\win-x64"
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $app, $raw | Out-Null
Copy-Item "$build\*" $app -Recurse
$exe = Join-Path $app "Tapybara.exe"

foreach ($theme in "Dark", "Light") {
    $t = $theme.ToLower()

    # Заново для каждой темы: правка слова в прошлом проходе изменила бы
    # данные, и тёмный и светлый снимки разошлись бы.
    python (Join-Path $PSScriptRoot "seed.py") $app $Models | Out-Null
    python (Join-Path $PSScriptRoot "seed.py") $app --theme $theme

    $p = Start-Process $exe -ArgumentList "--calls" -PassThru
    Start-Sleep -Seconds 6
    Place $p.Id "Tapybara" 1920 1200

    # Звонок из демонстрации: Кирилл собран из двух голосов, Дана ждёт имени.
    Press $p.Id "Marina, Kirill, Dana"
    Shot $p.Id "Tapybara" "$raw\calls-$t.png"

    # Копия без имён — «Все — «Участник N»», чтобы было видно, во что превратятся имена.
    Press $p.Id "More"
    Press $p.Id "Copy without names…"
    Start-Sleep -Milliseconds 800
    Press $p.Id 'Everyone — “Participant N”'
    Shot $p.Id "Copy without names" "$raw\anonymize-$t.png"
    Press $p.Id "Cancel"

    # Карточка правки слова на звонке, где слово искажено по-разному.
    Press $p.Id "Weekly sync"
    DoubleClickWord $p.Id "Kubernetis"
    [SiteWin]::Type("Kubernetes")
    Start-Sleep -Milliseconds 500
    Shot $p.Id "Tapybara" "$raw\fix-word-$t.png" -AroundPopup
    Press $p.Id "Only here"   # закрыть карточку; копия всё равно выбрасывается

    ClickElement $p.Id "Dictations"
    Shot $p.Id "Tapybara" "$raw\dictations-$t.png"
    ClickElement $p.Id "Dictionary"
    Shot $p.Id "Tapybara" "$raw\dictionary-$t.png"

    Press $p.Id "Settings"
    Start-Sleep -Milliseconds 800
    Place $p.Id "Tapybara settings" 1770 1106
    ClickElement $p.Id "Models"
    Shot $p.Id "Tapybara settings" "$raw\settings-$t.png"
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Seconds 1

    $p = Start-Process $exe -ArgumentList "--welcome" -PassThru
    Start-Sleep -Seconds 5
    Shot $p.Id "Welcome to Tapybara" "$raw\welcome-$t.png"
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Seconds 1
}

Get-ChildItem $raw -Filter *.png | ForEach-Object { Webp $_.FullName ($_.BaseName + ".webp") }
Write-Output "screens -> $screens (raw png in $raw)"
