# Optional integration test with an installed VS Code. Uses a separate profile and workspace.
[CmdletBinding()]
param([string]$Code = (Get-Command code.cmd -ErrorAction Stop).Source, [switch]$PreparedNote)
$ErrorActionPreference = 'Stop'
$temp = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\')
$name = 'WorkdayEditorTest-' + [Guid]::NewGuid().ToString('N')
$root = Join-Path $temp $name
$projectName = if ($PreparedNote) { 'editor-test-001-aabbccdd' } else { 'test-001-aabbccdd' }
$project = Join-Path $root $projectName
$previous = $env:TOUCH_TYPING_WORKDAY_ROOT
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $project 'Copies'))
    $sample = "QA / Windows`nStatus: ready"
    [IO.File]::WriteAllText((Join-Path $project 'review-test.txt'), $sample)
    [IO.File]::WriteAllText((Join-Path $project 'Copies/review-test.txt'), $sample)
    [IO.File]::WriteAllText((Join-Path $project 'summary.py'), "count = 3`nprint(count)")
    $env:TOUCH_TYPING_WORKDAY_ROOT = $root
    $extension = (Resolve-Path -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'code-lesson')).Path
    & $Code --new-window --wait --skip-welcome --disable-workspace-trust `
        --user-data-dir (Join-Path $root 'profile') --extensions-dir (Join-Path $root 'extensions') `
        "--extensionDevelopmentPath=$extension" "--extensionTestsPath=$PSScriptRoot\code-editor.js" $project
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $root 'editor-test-passed'))) {
        $report = Join-Path $root 'editor-test-failure'
        if (Test-Path -LiteralPath $report) { throw (Get-Content -Raw -LiteralPath $report) }
        throw "VS Code extension host did not complete its assertions ($LASTEXITCODE)."
    }
    Write-Output 'PASS: VS Code file lookup, line selection, same-name copy, multi-cursor Python edit and saved contents.'
} finally {
    $env:TOUCH_TYPING_WORKDAY_ROOT = $previous
    $actual = Get-Item -LiteralPath $root -ErrorAction SilentlyContinue
    if ($actual -and $actual.Parent.FullName -eq $temp -and $actual.Name -eq $name -and
        -not ($actual.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $actual.FullName -Force -Recurse
    }
}
