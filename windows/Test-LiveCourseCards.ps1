[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Application).ProviderPath
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh disposable output path'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out=(Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe=Join-Path $out 'LiveCourseEntrySmoke.exe'
$oldData=$env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed=$env:TOUCH_TYPING_QA_SEED
$oldError=$ErrorActionPreference
try{
    $env:TOUCH_TYPING_QA_DATA_ROOT=Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED='0'
    $options=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:LiveCourseEntryMain',"/out:$probe",
        "/reference:$framework\WPF\UIAutomationClient.dll","/reference:$framework\WPF\UIAutomationTypes.dll",
        "/reference:$framework\WPF\WindowsBase.dll","/reference:$framework\System.Xml.dll",
        "/reference:$framework\System.Drawing.dll","/reference:$framework\System.Windows.Forms.dll",
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'),(Join-Path $PSScriptRoot 'TypingProofClient.cs'),
        (Join-Path $PSScriptRoot 'LiveCourseEntrySmoke.cs'))
    & (Join-Path $framework 'csc.exe') @options
    if($LASTEXITCODE){throw 'Course card external observer compilation failed'}
    $ErrorActionPreference='Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-live-cards.log')
    $exitCode=$LASTEXITCODE
}finally{
    $ErrorActionPreference=$oldError
    $env:TOUCH_TYPING_QA_DATA_ROOT=$oldData
    $env:TOUCH_TYPING_QA_SEED=$oldSeed
}
if($exitCode){throw "WPF native card proof failed ($exitCode); $out"}
