param([switch]$DesktopShortcut, [switch]$Stage, [string]$StageDirectory)
$ErrorActionPreference = 'Stop'
if ($StageDirectory -and (-not $Stage -or $DesktopShortcut)) {
    throw 'StageDirectory requires -Stage and cannot be combined with -DesktopShortcut.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$references = @(
    'System.dll', 'System.Core.dll', 'System.Xml.dll', 'Microsoft.CSharp.dll',
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
$compilerOutput = & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    "/win32manifest:$PSScriptRoot\app.manifest" "/out:$appPath" @references @sources
if ($LASTEXITCODE -ne 0) {
    throw ('TouchTyping compilation failed.' + [Environment]::NewLine + ($compilerOutput -join [Environment]::NewLine))
}
foreach ($diagnostic in $compilerOutput) { Write-Warning $diagnostic }
$config = Join-Path $PSScriptRoot 'TouchTyping.exe.config'
if ($appPath + '.config' -ne $config) { Copy-Item -LiteralPath $config -Destination ($appPath + '.config') }
if ($Stage) {
    Write-Output $appPath
    return
}

$shell = New-Object -ComObject WScript.Shell
$entries = @(
    @{ Name = 'Код вслепую'; Mode = '--typing' },
    @{ Name = 'Windows без мыши'; Mode = '--windows' }
)
foreach ($entry in $entries) {
    $shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot ($entry.Name + '.lnk')))
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Arguments = $entry.Mode
    $shortcut.Save()
}
if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $shortcut = $shell.CreateShortcut((Join-Path $desktop 'Код вслепую.lnk'))
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Arguments = '--typing'
    $shortcut.Save()
}
Write-Output $appPath
