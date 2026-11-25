[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$DisposableUserAccount
)
# Run standalone in a fresh Windows desktop session, not after other ordinary
# profile tests. Do not delete existing progress for the sake of this observer.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -and -not $DisposableUserAccount) {
    throw 'Use an ephemeral Windows desktop runner or a fresh Windows account with -DisposableUserAccount.'
}
if (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TouchTypingStudio')) {
    throw 'Ordinary TouchTyping profile already exists; refusing to open or clear it.'
}
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new evidence directory.' }
$out = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'TypingWorkNoteSmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:TypingWorkNoteMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'TypingProofClient.cs'),
    (Join-Path $PSScriptRoot 'TypingUiSmoke.cs'), (Join-Path $PSScriptRoot 'TypingWorkNoteSmoke.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Cannot build the external WPF observer ($LASTEXITCODE)." }
$oldPreference = $ErrorActionPreference
$oldDisposable = $env:TOUCH_TYPING_DISPOSABLE_USER
try {
    if ($DisposableUserAccount) { $env:TOUCH_TYPING_DISPOSABLE_USER = '1' }
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-typing-work-note.log')
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPreference
    $env:TOUCH_TYPING_DISPOSABLE_USER = $oldDisposable
}
if ($exitCode) { throw "Native Windows note probe failed ($exitCode). See $out." }
