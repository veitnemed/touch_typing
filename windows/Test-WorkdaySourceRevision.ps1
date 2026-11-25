[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('windows','ubuntu')][string]$Simulation = 'windows',
    [int]$Seed = 1,
    [switch]$OrdinaryRotation
)
# Isolated interactive Windows host only. The observer targets the owning
# WPF process and never sends keys to a background or unrelated window.
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (-not [Environment]::UserInteractive) { throw 'Workday requires an interactive Windows test desktop.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new empty evidence directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe = Join-Path $out 'WorkdaySourceRevisionSmoke.exe'
$options = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:WorkdaySourceRevisionMain',"/out:$probe",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\System.Xml.dll",
    "/reference:$framework\System.Drawing.dll", "/reference:$framework\System.Windows.Forms.dll", "/reference:$framework\System.Web.Extensions.dll",
    (Join-Path $PSScriptRoot 'LiveUiSmoke.cs'), (Join-Path $PSScriptRoot 'WorkdaySourceRevisionSmoke.cs'),
    (Join-Path $PSScriptRoot 'LiveSourceHandoffSmoke.cs'), (Join-Path $PSScriptRoot 'ChromeHistorySmoke.cs'),
    (Join-Path $PSScriptRoot 'TypingProofClient.cs'))
& (Join-Path $framework 'csc.exe') @options
if ($LASTEXITCODE) { throw "Cannot compile WPF Workday observer ($LASTEXITCODE)." }
$oldRoot = $env:TOUCH_TYPING_QA_DATA_ROOT
$oldSeed = $env:TOUCH_TYPING_QA_SEED
$oldPref = $ErrorActionPreference
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    $env:TOUCH_TYPING_QA_SEED = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    $route = if ($OrdinaryRotation) { 'rotation' } else { 'catalog' }
    $os = Get-CimInstance Win32_OperatingSystem
    @(
        "Windows: $($os.Caption), build $($os.BuildNumber), $($os.OSArchitecture)"
        "Application SHA256: $((Get-FileHash $exe -Algorithm SHA256).Hash)"
        "Simulation: $Simulation; mode: $route; private profile; seed: $Seed"
        'Input: Windows SendInput scan codes; not physical hardware.'
    ) | Set-Content -LiteralPath (Join-Path $out 'native-workday-environment.txt') -Encoding UTF8
    $ErrorActionPreference = 'Continue'
    & $probe $exe $out $Simulation $route 2>&1 | Tee-Object -FilePath (Join-Path $out 'native-workday-source-revision.log')
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPref
    $env:TOUCH_TYPING_QA_DATA_ROOT = $oldRoot
    $env:TOUCH_TYPING_QA_SEED = $oldSeed
}
if ($exitCode) { throw "Workday WPF gate failed ($exitCode). See $out." }
