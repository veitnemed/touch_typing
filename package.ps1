[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'release')
)

$ErrorActionPreference = 'Stop'
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$name = "TouchTyping-$Version-win.zip"
$archive = Join-Path $destination $name
$checksum = "$archive.sha256"
if ((Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $checksum)) {
    throw "Release already exists: $archive"
}

$tempParent = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).ProviderPath.TrimEnd('\', '/')
$tempName = 'TouchTyping-package-' + [Guid]::NewGuid().ToString('N')
$temp = Join-Path $tempParent $tempName
$payload = Join-Path $temp 'payload'
$tempArchive = Join-Path $temp $name
$tempChecksum = "$tempArchive.sha256"

[void][IO.Directory]::CreateDirectory($payload)
try {
    [void](& (Join-Path $PSScriptRoot 'build.ps1') -Stage -StageDirectory $payload)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'START-RU.txt') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows\Start-Live.cmd') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows\Start-Workday.cmd') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows\Test-Live-UI.ps1') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows\LiveUiSmoke.cs') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows\README.md') -Destination (Join-Path $payload 'README.md')
    $codeLesson = Join-Path $payload 'code-lesson'
    [void][IO.Directory]::CreateDirectory($codeLesson)
    foreach ($file in @('lesson.js', 'package.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "code-lesson\$file") -Destination $codeLesson
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $tempArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $tempArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $tempChecksum -Value "$hash  $name" -Encoding Ascii

    [void][IO.Directory]::CreateDirectory($destination)
    Move-Item -LiteralPath $tempArchive -Destination $archive
    Move-Item -LiteralPath $tempChecksum -Destination $checksum
    Write-Output $archive
    Write-Output $checksum
} finally {
    $resolvedTemp = (Resolve-Path -LiteralPath $temp).ProviderPath
    $isRedirected = (Get-Item -LiteralPath $resolvedTemp).Attributes -band [IO.FileAttributes]::ReparsePoint
    if (-not $isRedirected -and (Split-Path -Parent $resolvedTemp) -eq $tempParent -and
        (Split-Path -Leaf $resolvedTemp) -eq $tempName) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
