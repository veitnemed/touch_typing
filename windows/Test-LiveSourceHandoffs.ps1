[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('ubuntu','windows')][string]$DesktopProfile
)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Application).ProviderPath
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh disposable output path'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out=(Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe=Join-Path $out 'LiveSourceHandoffSmoke.exe'
$oldData=$env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed=$env:TOUCH_TYPING_QA_SEED
$oldWitness=$env:TOUCH_TYPING_QA_PYTHON_EDITOR
$oldBrowser=$env:TOUCH_TYPING_QA_BROWSER_CLONE
$oldProfile=$env:TOUCH_TYPING_QA_SOURCE_PROFILE
$oldError=$ErrorActionPreference
try{
    $env:TOUCH_TYPING_QA_DATA_ROOT=Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED='1'
    $env:TOUCH_TYPING_QA_PYTHON_EDITOR='1'
    $env:TOUCH_TYPING_QA_BROWSER_CLONE=$null
    $env:TOUCH_TYPING_QA_SOURCE_PROFILE=$DesktopProfile
    $options=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:LiveSourceHandoffMain',"/out:$probe",
        "/reference:$framework\WPF\UIAutomationClient.dll","/reference:$framework\WPF\UIAutomationTypes.dll",
        "/reference:$framework\WPF\WindowsBase.dll","/reference:$framework\System.Xml.dll",
        "/reference:$framework\System.Drawing.dll","/reference:$framework\System.Windows.Forms.dll",
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'),(Join-Path $PSScriptRoot 'TypingProofClient.cs'),
        (Join-Path $PSScriptRoot 'LiveSourceHandoffSmoke.cs'))
    & (Join-Path $framework 'csc.exe') @options
    if($LASTEXITCODE){throw 'Source-handoff external observer compilation failed'}
    $ErrorActionPreference='Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-source-handoffs.log')
    $exitCode=$LASTEXITCODE
}finally{
    $ErrorActionPreference=$oldError
    $env:TOUCH_TYPING_QA_DATA_ROOT=$oldData
    $env:TOUCH_TYPING_QA_SEED=$oldSeed
    $env:TOUCH_TYPING_QA_PYTHON_EDITOR=$oldWitness
    $env:TOUCH_TYPING_QA_BROWSER_CLONE=$oldBrowser
    $env:TOUCH_TYPING_QA_SOURCE_PROFILE=$oldProfile
}
if($exitCode){throw "WPF source-handoff proof failed ($exitCode); $out"}
