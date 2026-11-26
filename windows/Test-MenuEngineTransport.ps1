[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This is a disposable Windows runner proof.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh disposable output path' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$transport = Join-Path $out 'MenuEngineTransportSmoke.exe'
$transportOptions = @('/nologo','/target:exe','/platform:x64','/codepage:65001',"/out:$transport",
    "/reference:$framework\System.Web.Extensions.dll",
    (Join-Path $PSScriptRoot 'MenuEngineTransportSmoke.cs'),
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'PracticeEngineClient.cs'))
& (Join-Path $framework 'csc.exe') @transportOptions
if ($LASTEXITCODE) { throw "Windows transport proof compilation failed; $out" }
& $transport $out | Tee-Object -FilePath (Join-Path $out 'menu-transport.log')
if ($LASTEXITCODE) { throw "Windows transport proof failed; $out" }
