[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Application,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Application).ProviderPath
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new, empty output directory for this native review check.' }
$out = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$old = $env:TOUCH_TYPING_QA_DATA_ROOT
$fixture = Join-Path $out 'TypingReview.UiTest.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
try {
    $env:TOUCH_TYPING_QA_DATA_ROOT = Join-Path $out 'profile'
    # Author only the practice material, never its results. File-backed stdin
    # avoids the BOM .NET Framework's Process can emit upon starting a pipe.
    $engine = Join-Path (Split-Path -Parent $exe) 'engine\TouchTyping.Engine.exe'
    $request = Join-Path $out 'fixture-request.jsonl'
    $response = Join-Path $out 'fixture-response.jsonl'
    $stderr = Join-Path $out 'fixture-stderr.log'
    $fixtureText = ('save notes. ' * 40).Substring(0, 391)
    $body = @{ command = 'typing-save-set'; title = 'Practice: correction'; content = $fixtureText } | ConvertTo-Json -Compress
    $noBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($request, $body + "`n", $noBom)
    $author = Start-Process -FilePath $engine -ArgumentList '--verify' -NoNewWindow -PassThru `
        -RedirectStandardInput $request -RedirectStandardOutput $response -RedirectStandardError $stderr
    try {
        # Windows PowerShell can expose a detached Process with no cached exit
        # code for a short-lived child. Pin its handle before waiting if present.
        try { $null = $author.Handle } catch { Write-Host 'Fixture exit handle unavailable; checking the wire reply and persisted set.' }
        if (-not $author.WaitForExit(12000)) {
            $author.Kill()
            throw "Timed out authoring the isolated practice set; see $stderr"
        }
        $code = $author.ExitCode
        $label = if ($null -eq $code) { 'not exposed by Windows PowerShell' } else { $code }
        $errorText = [IO.File]::ReadAllText($stderr, $noBom)
        $json = [IO.File]::ReadAllText($response, $noBom)
        Write-Host "Fixture author exited (code: $label); stdout=$((Get-Item -LiteralPath $response).Length) bytes; stderr=$((Get-Item -LiteralPath $stderr).Length) bytes."
        if (($null -ne $code -and $code -ne 0) -or -not [string]::IsNullOrWhiteSpace($errorText) -or [string]::IsNullOrWhiteSpace($json)) {
            throw "Fixture author did not exit normally with an answer; see $response and $stderr"
        }
        # An unknown exit code is not a nonzero exit. Require the exact set in
        # the successful reply; the driver independently validates on-disk XML.
        $answer = $json | ConvertFrom-Json
        $authored = @($answer.value.typing.sets | Where-Object {
            $_.title -ceq 'Practice: correction' -and $_.content -ceq $fixtureText -and $_.id -match '^set:[a-f0-9]{32}$'
        })
        if ($answer.ok -ne $true -or $answer.storageError -or $authored.Count -ne 1) {
            throw "Invalid reply while authoring the isolated practice set: $($answer.error) $($answer.storageError)"
        }
    } finally { $author.Dispose() }
    & (Join-Path $framework 'csc.exe') /nologo /platform:x64 /codepage:65001 /target:exe /main:TypingReviewWindowEntry "/out:$fixture" `
        "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" `
        "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Drawing.dll" `
        "/reference:$framework\System.Windows.Forms.dll" `
        (Join-Path $PSScriptRoot 'LiveUiSmoke.cs') (Join-Path $PSScriptRoot 'TypingReviewWindowSmoke.cs') (Join-Path $PSScriptRoot 'TypingProofClient.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Review test driver did not compile.' }
    & $fixture $exe $out | Tee-Object -FilePath (Join-Path $out 'windows-typing-review.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native WPF correction test failed; inspect the recorded UI and log.' }
} finally {
    $env:TOUCH_TYPING_QA_DATA_ROOT = $old
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}
