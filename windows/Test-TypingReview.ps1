[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Application,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new, empty output directory for this native review check.' }
$out = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$old = $env:TOUCH_TYPING_QA_DATA_ROOT
$fixture = Join-Path $out 'TypingReview.UiTest.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    & (Join-Path $framework 'csc.exe') /nologo /platform:x64 /codepage:65001 /target:exe /main:TypingReviewWindowEntry "/out:$fixture" `
        "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" `
        "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Drawing.dll" `
        "/reference:$framework\System.Web.Extensions.dll" "/reference:$framework\System.Windows.Forms.dll" `
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs') (Join-Path $PSScriptRoot 'TypingReviewWindowSmoke.cs') (Join-Path $PSScriptRoot 'TypingProofClient.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Review test driver did not compile.' }
    & $fixture $exe $out | Tee-Object -FilePath (Join-Path $out 'windows-typing-review.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native WPF correction test failed; inspect the recorded UI and log.' }
} finally {
    $env:TOUCH_TYPING_QA_DATA_ROOT = $old
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}
