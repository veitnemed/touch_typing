param([switch]$DesktopShortcut, [switch]$Stage, [string]$StageDirectory, [switch]$LegacyOnly)
$ErrorActionPreference = 'Stop'
if ($StageDirectory -and (-not $Stage -or $DesktopShortcut)) {
    throw 'StageDirectory requires -Stage and cannot be combined with -DesktopShortcut.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$references = @(
    'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.IO.Compression.dll', 'Microsoft.CSharp.dll',
    'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll',
    'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'System.Xaml.dll'
) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$appPath = Join-Path $PSScriptRoot 'TouchTyping.exe'
if ($StageDirectory) {
    $stageFolder = Get-Item -LiteralPath $StageDirectory
    if (-not $stageFolder.PSIsContainer) { throw 'StageDirectory must be a directory.' }
    $appPath = Join-Path $stageFolder.FullName 'TouchTyping.exe'
    if ((Test-Path -LiteralPath $appPath) -or (Test-Path -LiteralPath ($appPath + '.config'))) {
        throw 'The staged executable or configuration already exists.'
    }
} elseif ($Stage -or (Get-Process -Name TouchTyping -ErrorAction SilentlyContinue | Where-Object Path -eq $appPath)) {
    # The running image is locked. Update the shortcuts to a sibling build for the next launch.
    $appPath = Join-Path $PSScriptRoot ('TouchTyping-' + (Get-Date -Format 'yyyyMMddHHmmssfff') + '.exe')
}
$compilerOutput = & $compiler /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 `
    "/win32manifest:$PSScriptRoot\app.manifest" "/out:$appPath" "/resource:$PSScriptRoot\linux\hands.png,TouchTyping.Hands.png" `
    "/resource:$PSScriptRoot\linux\result-time.png,TouchTyping.Result.time.png" `
    "/resource:$PSScriptRoot\linux\result-count.png,TouchTyping.Result.count.png" `
    "/resource:$PSScriptRoot\linux\result-speed.png,TouchTyping.Result.speed.png" `
    "/resource:$PSScriptRoot\linux\result-accuracy.png,TouchTyping.Result.accuracy.png" @references @sources
if ($LASTEXITCODE -ne 0) {
    throw ('TouchTyping compilation failed.' + [Environment]::NewLine + ($compilerOutput -join [Environment]::NewLine))
}
foreach ($diagnostic in $compilerOutput) { Write-Warning $diagnostic }
$config = Join-Path $PSScriptRoot 'TouchTyping.exe.config'
if ($appPath + '.config' -ne $config) { Copy-Item -LiteralPath $config -Destination ($appPath + '.config') }
if (-not $LegacyOnly) {
    $fontSource = Join-Path $PSScriptRoot 'assets\fonts'
    $fontDirectory = Join-Path (Split-Path -Parent $appPath) 'fonts'
    [void][IO.Directory]::CreateDirectory($fontDirectory)
    foreach ($fontFile in @('Roboto-Regular.ttf', 'Roboto-Medium.ttf', 'Roboto-Bold.ttf', 'OFL.txt', 'ROBOTO-SOURCE.txt')) {
        Copy-Item -LiteralPath (Join-Path $fontSource $fontFile) -Destination $fontDirectory
    }
    $project = Join-Path $PSScriptRoot 'linux\engine\TouchTyping.Engine.csproj'
    $engineDirectory = Join-Path (Split-Path -Parent $appPath) 'engine'
    $sdk = (Get-Command dotnet -ErrorAction Stop).Source
    $engineOutput = & $sdk publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $engineDirectory
    if ($LASTEXITCODE -ne 0) {
        throw ('TouchTyping engine publish failed.' + [Environment]::NewLine + ($engineOutput -join [Environment]::NewLine))
    }
    $deps = Get-Content -Raw -LiteralPath (Join-Path $engineDirectory 'TouchTyping.Engine.deps.json') | ConvertFrom-Json
    $runtimeKey = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' } | Select-Object -First 1
    if (-not $runtimeKey) { throw 'Cannot determine bundled .NET runtime version.' }
    $runtimeVersion = ($runtimeKey -split '/')[1]
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $runtimePackage = Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
    foreach ($license in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $runtimePackage $license) -Destination $engineDirectory
    }
}
if ($Stage) {
    Write-Output $appPath
    return
}

$shell = New-Object -ComObject WScript.Shell
$entries = @(
    @{ Name = 'Код вслепую'; Mode = '--typing' },
    @{ Name = 'Windows без мыши'; Mode = '--windows' }
)
if (-not $LegacyOnly) {
    $entries = @(@{ Name = 'Код вслепую - Live'; Mode = '--live' }) + $entries
}
foreach ($entry in $entries) {
    $shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot ($entry.Name + '.lnk')))
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Arguments = $entry.Mode
    $shortcut.Save()
    if ($DesktopShortcut) {
        $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
        $shortcut = $shell.CreateShortcut((Join-Path $desktop ($entry.Name + '.lnk')))
        $shortcut.TargetPath = $appPath
        $shortcut.WorkingDirectory = $PSScriptRoot
        $shortcut.Arguments = $entry.Mode
        $shortcut.Save()
    }
}
Write-Output $appPath
