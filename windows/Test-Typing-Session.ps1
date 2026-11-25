[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$DisposableUserAccount
)
# This is intentionally a normal app launch; never run it against a learner's real profile.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -and -not $DisposableUserAccount) {
    throw 'Use a fresh throwaway Windows user and pass -DisposableUserAccount, or an ephemeral hosted runner.'
}
if (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TouchTypingStudio')) {
    throw 'Existing ordinary TouchTyping profile: refusing to modify it.'
}
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'TypingUiSmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:TypingMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'TypingUiSmoke.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Could not compile the external WPF observer ($LASTEXITCODE)." }
$oldPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-typing.log')
    $exitCode = $LASTEXITCODE
} finally { $ErrorActionPreference = $oldPreference }
if ($exitCode) { throw "Native WPF typing path failed ($exitCode). See $out." }
