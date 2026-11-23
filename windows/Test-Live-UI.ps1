[CmdletBinding()]
param(
    [string]$Application,
    [switch]$FullSession,
    [switch]$WorkdaySourceNote,
    [switch]$ReferenceEvidence,
    [switch]$ReferenceBrowser,
    [switch]$SoftFinish,
    [switch]$NativeLayout,
    [switch]$DailyUse,
    [int]$Seed = 20261122,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('ui-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$modes = @($FullSession,$WorkdaySourceNote,$ReferenceBrowser,$SoftFinish,$NativeLayout,$DailyUse) | Where-Object { [bool]$_ }
if (@($modes).Count -gt 1) { throw 'Choose only one of FullSession, WorkdaySourceNote, ReferenceBrowser, SoftFinish or NativeLayout.' }
if ($ReferenceBrowser) { $Seed = 0 }
if (-not $Application) {
    $Application = Join-Path $PSScriptRoot 'TouchTyping.exe'
    if (-not (Test-Path -LiteralPath $Application)) {
        $Application = Join-Path (Split-Path $PSScriptRoot) 'TouchTyping.exe'
    }
}
$Application = (Resolve-Path -LiteralPath $Application).ProviderPath
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$source = Join-Path $PSScriptRoot 'LiveUiSmoke.cs'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$test = Join-Path ([IO.Path]::GetTempPath()) ('TouchTyping-UI-' + [Guid]::NewGuid().ToString('n') + '.exe')
$log = Join-Path $OutputDirectory 'windows-ui.log'
$reportFile = Join-Path $OutputDirectory 'report.json'
$previousSeed = $env:TOUCH_TYPING_QA_SEED
$previousRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$report = [ordered]@{
    status = 'running'
    started = [DateTime]::UtcNow.ToString('o')
    seed = $Seed
    application = $Application
    fullSession = [bool]$FullSession
    workdaySourceNote = [bool]$WorkdaySourceNote
    referenceEvidence = [bool]($ReferenceEvidence -or $ReferenceBrowser)
    referenceBrowser = [bool]$ReferenceBrowser
    softFinish = [bool]$SoftFinish
    nativeLayout = [bool]$NativeLayout
    profile = 'TouchTypingStudio-QA'
    log = 'windows-ui.log'
}
try {
    $env:TOUCH_TYPING_QA_SEED = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $OutputDirectory 'profile'
    if (($WorkdaySourceNote -or $ReferenceBrowser -or $SoftFinish -or $NativeLayout -or $DailyUse) -and (Test-Path -LiteralPath $env:TOUCH_TYPING_QA_DATA_ROOT)) {
        throw 'Use a new output directory for the standalone Workday, Chrome, soft-finish or native-layout check.'
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath $reportFile -Encoding UTF8
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 "/out:$test" `
        "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" `
        "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Xml.dll" `
        "/reference:$framework\System.Drawing.dll" "/reference:$framework\System.Windows.Forms.dll" $source 2>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw 'UI smoke runner compilation failed.' }
    $arguments = @($Application)
    if ($FullSession) { $arguments += '--full-session' }
    if ($WorkdaySourceNote) { $arguments += '--workday-source-note' }
    if ($ReferenceEvidence) { $arguments += '--reference-evidence' }
    if ($ReferenceBrowser) { $arguments += '--reference-browser' }
    if ($SoftFinish) { $arguments += '--soft-finish' }
    if ($NativeLayout) { $arguments += '--native-layout' }
    if ($DailyUse) { $arguments += '--daily-use' }
    # Windows PowerShell 5 wraps native stderr as errors. Do not abort the
    # pipeline on the first diagnostic line; the native exit code is the gate.
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $test @arguments 2>&1 | Tee-Object -FilePath $log -Append
        $report.exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldPreference
    }
    if ($report.exitCode -ne 0) { throw 'Native UI smoke failed. See the last reported assertion.' }
    $report.status = 'passed'
} catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
    throw
} finally {
    $report.finished = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json | Set-Content -LiteralPath $reportFile -Encoding UTF8
    $env:TOUCH_TYPING_QA_SEED = $previousSeed
    $env:TOUCH_TYPING_QA_DATA_ROOT = $previousRoot
    if (Test-Path -LiteralPath $test) { Remove-Item -LiteralPath $test }
    Write-Output "UI report saved to $reportFile"
}
