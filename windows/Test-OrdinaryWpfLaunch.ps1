[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Use the disposable hosted Windows image, not an ordinary local profile.' }
$exe=(Resolve-Path -LiteralPath $Application).ProviderPath
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh disposable output path'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out=(Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe=Join-Path $out 'OrdinaryWpfLaunchSmoke.exe'
$oldError=$ErrorActionPreference
try{
    $options=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:OrdinaryWpfLaunchMain',"/out:$probe",
        "/reference:$framework\WPF\UIAutomationClient.dll","/reference:$framework\WPF\UIAutomationTypes.dll",
        "/reference:$framework\WPF\WindowsBase.dll","/reference:$framework\System.Xml.dll",
        "/reference:$framework\System.Drawing.dll","/reference:$framework\System.Management.dll","/reference:$framework\System.Windows.Forms.dll",
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'),(Join-Path $PSScriptRoot 'TypingProofClient.cs'),
        (Join-Path $PSScriptRoot 'OrdinaryWpfLaunchSmoke.cs'))
    & (Join-Path $framework 'csc.exe') @options
    if($LASTEXITCODE){throw 'Ordinary Windows launcher observer compilation failed'}
    $ErrorActionPreference='Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'ordinary-wpf-launch.log')
    $exitCode=$LASTEXITCODE
}finally{
    $ErrorActionPreference=$oldError
}
if($exitCode){throw "Ordinary Windows launcher proof failed ($exitCode); $out"}
