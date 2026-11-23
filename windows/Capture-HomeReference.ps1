[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Application,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(640,3840)][int]$ClientWidth = 1006,
    [ValidateRange(480,2160)][int]$ClientHeight = 863,
    [switch]$AdjustHostedDisplay
)
$ErrorActionPreference = 'Stop'
$Application = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new private output directory.' }
$OutputDirectory = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReferenceUser32 {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct DisplayMode {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string FormName;
        public short LogPixels;
        public int BitsPerPixel, Width, Height, DisplayFlags, Frequency, ICMMethod, ICMIntent,
            MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", EntryPoint="EnumDisplaySettingsW", CharSet=CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int index, ref DisplayMode mode);
    [DllImport("user32.dll", EntryPoint="ChangeDisplaySettingsW", CharSet=CharSet.Unicode)]
    public static extern int ChangeDisplaySettings(ref DisplayMode mode, int flags);
    public static DisplayMode ReadMode(int index) {
        var result = new DisplayMode();
        result.Size = (short)Marshal.SizeOf(typeof(DisplayMode));
        return EnumDisplaySettings(null, index, ref result) ? result : default(DisplayMode);
    }
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int w, int h, uint flags);
}
'@
$oldDpi = [ReferenceUser32]::SetThreadDpiAwarenessContext([IntPtr](-4))
$oldRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed = $env:TOUCH_TYPING_QA_SEED
$process = $null
$displayChanged = $false
$pointerParked = $false
try {
    if ($oldDpi -eq [IntPtr]::Zero) { throw 'Per-monitor-aware capture could not be enabled.' }
    if ($AdjustHostedDisplay -and ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted')) {
        throw 'Display adjustment is restricted to a verified disposable GitHub-hosted runner.'
    }
    if ($AdjustHostedDisplay) {
        $current = [ReferenceUser32]::ReadMode(-1)
        if ($current.Size -eq 0) { throw 'Cannot record current display mode safely.' }
        if ($current.Width -lt ($ClientWidth+40) -or $current.Height -lt ($ClientHeight+80)) {
            # Use only a reported mode at the same color depth; CDS_TEST first.
            $available = for ($i = 0; $i -lt 4096; $i++) {
                $mode = [ReferenceUser32]::ReadMode($i)
                if ($mode.Size -eq 0) { break }
                if ($mode.Width -ge ($ClientWidth+40) -and $mode.Height -ge ($ClientHeight+80) -and $mode.BitsPerPixel -eq $current.BitsPerPixel) { $mode }
            }
            $sorted = $available | Sort-Object -Property @{Expression={$_.Width*$_.Height}},@{Expression={-[int]$_.Frequency}}
            foreach ($mode in $sorted) {
                $desired = $mode
                if ([ReferenceUser32]::ChangeDisplaySettings([ref]$desired,2) -ne 0) { continue }
                if ([ReferenceUser32]::ChangeDisplaySettings([ref]$desired,0) -eq 0) {
                    $displayChanged = $true
                    Start-Sleep -Seconds 2
                    break
                }
            }
            if (-not $displayChanged) { throw 'Hosted display does not offer a safe mode large enough for physical-pixel capture.' }
        }
    }
    $env:TOUCH_TYPING_QA_SEED = '7'
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $OutputDirectory 'profile'
    $process = Start-Process -FilePath $Application -ArgumentList '--qa','--live','--qa-home' -WorkingDirectory (Split-Path -Parent $Application) -PassThru
    for ($i = 0; $i -lt 100; $i++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before opening its window.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    $hwnd = $process.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { throw 'No application window.' }
    $app = [Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if ($app.Current.ProcessId -ne $process.Id) { throw 'Window is not owned by the disposable app.' }
    $homeCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'HomeDashboard')
    for ($i = 0; $i -lt 40; $i++) {
        $dashboard = $app.FindFirst([Windows.Automation.TreeScope]::Descendants,$homeCondition)
        if ($dashboard -ne $null) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($dashboard -eq $null) { throw 'Home dashboard was not visible in launched app.' }
    [void][ReferenceUser32]::ShowWindow($hwnd,9)
    # Match by physical client pixels, excluding OS chrome. Never stretch the capture.
    $client = New-Object 'ReferenceUser32+Rect'
    $outer = New-Object 'ReferenceUser32+Rect'
    $origin = New-Object 'ReferenceUser32+Point'
    $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    for ($i = 0; $i -lt 4; $i++) {
        if (-not [ReferenceUser32]::GetClientRect($hwnd,[ref]$client) -or -not [ReferenceUser32]::GetWindowRect($hwnd,[ref]$outer)) { throw 'Cannot read geometry.' }
        $width = $outer.Right - $outer.Left + $ClientWidth - ($client.Right - $client.Left)
        $height = $outer.Bottom - $outer.Top + $ClientHeight - ($client.Bottom - $client.Top)
        if (-not [ReferenceUser32]::SetWindowPos($hwnd,[IntPtr]::Zero,$area.Left,$area.Top,$width,$height,0x0010)) {
            throw 'Cannot set window dimensions.'
        }
        Start-Sleep -Milliseconds 200
    }
    [void][ReferenceUser32]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 400
    if ([ReferenceUser32]::GetForegroundWindow() -ne $hwnd) { throw 'App was not the foreground window; refusing desktop capture.' }
    if (-not [ReferenceUser32]::GetClientRect($hwnd,[ref]$client) -or -not [ReferenceUser32]::ClientToScreen($hwnd,[ref]$origin)) {
        throw 'Cannot read client rectangle on the display.'
    }
    if (($client.Right - $client.Left) -ne $ClientWidth -or ($client.Bottom - $client.Top) -ne $ClientHeight) {
        throw "Wrong client dimensions $($client.Right - $client.Left)x$($client.Bottom - $client.Top); refusing to rescale."
    }
    $screen = [Windows.Forms.Screen]::FromHandle($hwnd).Bounds
    if ($origin.X -lt $screen.Left -or $origin.Y -lt $screen.Top -or $origin.X+$ClientWidth -gt $screen.Right -or $origin.Y+$ClientHeight -gt $screen.Bottom) {
        throw 'Client rectangle not fully visible; need a larger desktop or a smaller requested client.'
    }
    $previousPointer = New-Object 'ReferenceUser32+Point'
    $parking = New-Object 'ReferenceUser32+Point'
    $parking.X = $origin.X + 8
    $parking.Y = $origin.Y + [int]($ClientHeight / 2)
    if ([ReferenceUser32]::GetForegroundWindow() -ne $hwnd -or
        [ReferenceUser32]::WindowFromPoint($parking) -ne $hwnd -or
        -not [ReferenceUser32]::GetCursorPos([ref]$previousPointer) -or
        -not [ReferenceUser32]::SetCursorPos($parking.X,$parking.Y)) {
        throw 'Cannot safely park the pointer on the owned Home window edge.'
    }
    $pointerParked = $true
    # Let the tooltip teardown and hover style update reach the compositor.
    Start-Sleep -Milliseconds 750
    if ([ReferenceUser32]::GetForegroundWindow() -ne $hwnd) { throw 'App lost foreground before capture.' }
    $bitmap = [Drawing.Bitmap]::new($ClientWidth,$ClientHeight)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($origin.X,$origin.Y,0,0,$bitmap.Size) } finally { $graphics.Dispose() }
        if ([ReferenceUser32]::GetForegroundWindow() -ne $hwnd) { throw 'App lost foreground during capture.' }
        $bitmap.Save((Join-Path $OutputDirectory 'real-home-wpf.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    [ordered]@{
        screen='home'; platform='WPF'; native_window=$true
        capture='physical desktop pixels of owned foreground application client'
        pixels=@($ClientWidth,$ClientHeight); dpi=[ReferenceUser32]::GetDpiForWindow($hwnd)
        windows=[Environment]::OSVersion.VersionString; hosted_display_adjusted=$displayChanged; pointer_parked=$pointerParked
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'home-wpf.json') -Encoding UTF8
} finally {
    if ($pointerParked) {
        $pointerNow = New-Object 'ReferenceUser32+Point'
        if ([ReferenceUser32]::GetCursorPos([ref]$pointerNow) -and
            $pointerNow.X -eq $parking.X -and $pointerNow.Y -eq $parking.Y) {
            [void][ReferenceUser32]::SetCursorPos($previousPointer.X,$previousPointer.Y)
        }
    }
    if ($process -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
    }
    $env:TOUCH_TYPING_QA_SEED = $oldSeed
    $env:TOUCH_TYPING_QA_DATA_ROOT = $oldRoot
    if ($oldDpi -ne [IntPtr]::Zero) { [void][ReferenceUser32]::SetThreadDpiAwarenessContext($oldDpi) }
}
