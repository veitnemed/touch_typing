[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [int]$Seed = 1
)
# Run only on an isolated interactive Windows desktop. Actual SendInput is
# guarded by the new owning process and viewport. Never touches a learner profile.
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new empty evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'EditorGitReleaseSmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:EditorGitReleaseMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll", "/reference:$framework\System.Web.Extensions.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'EditorGitReleaseSmoke.cs'),
    (Join-Path $PSScriptRoot 'TypingProofClient.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Cannot compile WPF Editor Git Release observer ($LASTEXITCODE)." }
$oldRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed = $env:TOUCH_TYPING_QA_SEED
$oldPref = $ErrorActionPreference
$oldWitness = $env:TOUCH_TYPING_QA_PYTHON_EDITOR
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:TOUCH_TYPING_QA_PYTHON_EDITOR = '1'
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-editor-git-release.log')
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPref
    $env:TOUCH_TYPING_QA_DATA_ROOT = $oldRoot
    $env:TOUCH_TYPING_QA_SEED = $oldSeed
    $env:TOUCH_TYPING_QA_PYTHON_EDITOR = $oldWitness
}
if ($exitCode) { throw "Editor Git Release WPF gate failed ($exitCode). See $out." }
