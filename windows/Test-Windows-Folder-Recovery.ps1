[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new empty evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'WindowsFolderRecoverySmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:WindowsFolderRecoveryMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'WindowsFolderRecoverySmoke.cs'),
    (Join-Path $PSScriptRoot 'TypingProofClient.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Cannot compile WPF folder-recovery observer ($LASTEXITCODE)." }
$oldRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed = $env:TOUCH_TYPING_QA_SEED
$oldPref = $ErrorActionPreference
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED = '1'
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-folder-recovery.log')
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPref
    $env:TOUCH_TYPING_QA_DATA_ROOT = $oldRoot
    $env:TOUCH_TYPING_QA_SEED = $oldSeed
}
if ($exitCode) { throw "WPF folder-recovery proof failed ($exitCode). See $out." }
