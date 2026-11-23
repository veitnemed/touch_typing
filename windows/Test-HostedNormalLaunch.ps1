[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Application,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:GITHUB_ACTIONS -ne 'true' -or
    $env:TOUCH_TYPING_NORMAL_LAUNCH_ISOLATED_JOB -ne '1') {
    throw 'Only the dedicated fresh GitHub-hosted job may run this normal-profile probe.'
}
if ($env:TOUCH_TYPING_QA_DATA_ROOT) { throw 'This is not a QA-profile test.' }
$applicationPath = (Resolve-Path -LiteralPath $Application).ProviderPath
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$admin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if (-not $local -or -not (Test-Path -LiteralPath $local -PathType Container)) {
    throw 'The hosted runner has no normal Windows profile.'
}
if (Get-ChildItem -LiteralPath $local -Force -Filter 'TouchTypingStudio*') {
    throw 'An application-profile entry already exists; refusing to run or remove it.'
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$runner = Join-Path $outputPath 'TouchTyping-NormalLaunchProbe.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$log = Join-Path $outputPath 'windows-normal-launch.log'
Get-ComputerInfo -Property WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture |
    Out-String | Set-Content -LiteralPath $log
"Hosted Windows account=$($identity.Name) SID=$($identity.User.Value) tokenAdmin=$admin LocalApplicationData=$local" |
    Add-Content -LiteralPath $log
Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256 | Out-String | Add-Content -LiteralPath $log
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 "/out:$runner" `
    "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" `
    "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Xml.dll" `
    "/reference:$framework\System.Drawing.dll" "/reference:$framework\System.Windows.Forms.dll" `
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Normal launcher probe compilation failed.' }
try {
    $ErrorActionPreference = 'Continue'
    & $runner $applicationPath --hosted-normal-launch 2>&1 | Tee-Object -FilePath $log -Append
    $code = $LASTEXITCODE
} finally {
    $ErrorActionPreference = 'Stop'
}
Get-Content -LiteralPath $log
if ($code -ne 0) { throw "Hosted normal launch failed ($code)." }
