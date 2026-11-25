[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$OutputDirectory)
# Test with a new owned EXE/process on an isolated Windows desktop.
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Application).ProviderPath
if(Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new empty evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out=(Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$probe=Join-Path $out 'ChromeHistoryViewSmoke.exe'
$options=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:ChromeHistoryViewMain',"/out:$probe",
    "/reference:$framework/WPF/UIAutomationClient.dll", "/reference:$framework/WPF/UIAutomationTypes.dll",
    "/reference:$framework/WPF/WindowsBase.dll", "/reference:$framework/System.Xml.dll",
    "/reference:$framework/System.Drawing.dll", "/reference:$framework/System.Windows.Forms.dll",
    "/reference:$framework/System.Web.Extensions.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'ChromeHistoryViewSmoke.cs'),
    (Join-Path $PSScriptRoot 'TypingProofClient.cs'))
& (Join-Path $framework 'csc.exe') @options
if($LASTEXITCODE) { throw "Cannot compile WPF Chrome observer ($LASTEXITCODE)." }
$oldRoot=$env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed=$env:TOUCH_TYPING_QA_SEED
$oldWitness=$env:TOUCH_TYPING_QA_BROWSER_CLONE
$oldPreference=$ErrorActionPreference
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT=Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED='1'
    $env:TOUCH_TYPING_QA_BROWSER_CLONE='1'
    $ErrorActionPreference='Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-chrome-history.log')
    $exitCode=$LASTEXITCODE
} finally {
    $ErrorActionPreference=$oldPreference
    $env:TOUCH_TYPING_QA_DATA_ROOT=$oldRoot
    $env:TOUCH_TYPING_QA_SEED=$oldSeed
    $env:TOUCH_TYPING_QA_BROWSER_CLONE=$oldWitness
}
if($exitCode) { throw "Chrome History WPF gate failed ($exitCode). See $out." }
