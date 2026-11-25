[CmdletBinding()]
param (
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path -LiteralPath $Application).ProviderPath
# Never seed, clear or overwrite a learner profile.
if (Test-Path -LiteralPath $OutputDirectory) { throw 'A new output directory is required.' }
$fixture = Join-Path (Split-Path $PSScriptRoot) 'qa/fixtures/typing-card-boundaries.xml'
if (-not (Test-Path -LiteralPath $fixture)) { throw 'Shared QA fixture is missing.' }
$out = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$qaRoot = Join-Path $out 'profile'
$profileDirectory = [IO.Directory]::CreateDirectory((Join-Path $qaRoot 'TouchTypingStudio-QA')).FullName
Copy-Item -LiteralPath $fixture -Destination (Join-Path $profileDirectory 'progress.xml')
$previous = $env:TOUCH_TYPING_QA_DATA_ROOT
$exe = Join-Path $out 'TypingCardRecords.UiSmoke.exe'
$net = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = $qaRoot
    & (Join-Path $net 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 "/out:$exe" `
        "/reference:$net\WPF\UIAutomationClient.dll" "/reference:$net\WPF\UIAutomationTypes.dll" `
        "/reference:$net\WPF\WindowsBase.dll" "/reference:$net\System.Drawing.dll" `
        (Join-Path $PSScriptRoot 'TypingRecordUiChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Unable to compile typing record UI check.' }
    & $exe $app $out | Tee-Object -FilePath (Join-Path $out 'typing-card-records.log')
    if ($LASTEXITCODE -ne 0) { throw 'WPF typing card records did not match the learner profile.' }
} finally {
    $env:TOUCH_TYPING_QA_DATA_ROOT = $previous
}
