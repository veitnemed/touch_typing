[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$OutputDirectory)
# No ordinary profile is opened; the trainer must keep the foreground and is
# launched with --qa --qa-home. Run only on a dedicated interactive Windows desktop.
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new empty evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'TypingMixedSmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:MixedMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'TypingMixedSmoke.cs'),
    (Join-Path $PSScriptRoot 'TypingProofClient.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Cannot compile WPF keyboard observer ($LASTEXITCODE)." }
$oldRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$oldPref = $ErrorActionPreference
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-mixed.log')
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPref
    $env:TOUCH_TYPING_QA_DATA_ROOT = $oldRoot
}
if ($exitCode) { throw "Mixed typing WPF gate failed ($exitCode). See $out." }
