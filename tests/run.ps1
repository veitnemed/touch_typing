$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.IO.Compression.dll','Microsoft.CSharp.dll','WPF\WindowsBase.dll',
    'WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','System.Xaml.dll','System.Web.Extensions.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = Get-ChildItem -LiteralPath $root -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$tests = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('TypingStudioTests-' + [Guid]::NewGuid().ToString('n') + '.exe')
try {
    & $compiler /nologo /target:exe /codepage:65001 /main:TouchTypingStudio.TrainingTests "/out:$tmp" @refs @sources @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $tmp
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp }
}
