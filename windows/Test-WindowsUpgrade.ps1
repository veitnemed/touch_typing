[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OldApplication,
    [Parameter(Mandatory=$true)][string]$NewApplication,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Python = 'python',
    [Parameter(Mandatory=$true)][string]$SourceRoot
)
$ErrorActionPreference = 'Stop'
$old = (Resolve-Path -LiteralPath $OldApplication).ProviderPath
$new = (Resolve-Path -LiteralPath $NewApplication).ProviderPath
if ($old -eq $new) { throw 'Supply two different extracted builds.' }
$root = (Resolve-Path -LiteralPath $SourceRoot).ProviderPath
$folder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $folder) { throw 'Output must be a new directory, containing no real profile.' }
[void][IO.Directory]::CreateDirectory($folder)
$profiles = Join-Path $folder 'disposable-profiles'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runner = Join-Path $folder 'TouchTyping-UpgradeUiCheck.exe'
$previousQa = $env:TOUCH_TYPING_QA_DATA_ROOT
$previousPython = $env:PYTHONPATH
try {
    $env:PYTHONPATH = Join-Path $root 'linux'
    $oldEngine = Join-Path (Split-Path $old) 'engine\TouchTyping.Engine.exe'
    $newEngine = Join-Path (Split-Path $new) 'engine\TouchTyping.Engine.exe'
    Get-Item -LiteralPath $old,$new,$oldEngine,$newEngine | ForEach-Object { Get-FileHash -LiteralPath $_.FullName } | Out-String | Write-Output
    Get-ComputerInfo -Property WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 "/out:$runner" `
        "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" `
        "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Xml.dll" `
        "/reference:$framework\System.Drawing.dll" "/reference:$framework\System.Windows.Forms.dll" `
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade UI probe could not compile.' }
    $empty = Join-Path $folder 'v170-launch-only'
    [void][IO.Directory]::CreateDirectory($empty)
    $env:TOUCH_TYPING_QA_DATA_ROOT = $empty
    & $runner $old --upgrade-launch
    if ($LASTEXITCODE -ne 0) { throw 'Published UI did not open the Live chooser.' }
    & $Python (Join-Path $PSScriptRoot 'Test-UpgradeProfile.py') $oldEngine $newEngine $profiles
    if ($LASTEXITCODE -ne 0) { throw 'Bundled-engine storage migration or recovery failed.' }
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $profiles 'control'
    & $runner $new --upgrade-resume
    if ($LASTEXITCODE -ne 0) { throw 'Packaged WPF did not resume migrated progress.' }
    Write-Output 'PASS Windows upgrade QA profile; this does not imply clean-client installer acceptance.'
} finally {
    $env:TOUCH_TYPING_QA_DATA_ROOT = $previousQa
    $env:PYTHONPATH = $previousPython
}
