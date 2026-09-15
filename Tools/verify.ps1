[CmdletBinding()]
param(
    [string]$Godot = $env:GODOT4,
    [switch]$Capture
)
# Explicit local validation. No downloads, repository writes, or background jobs.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'Artifacts'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$report = [ordered]@{ generatedAtUtc=[DateTime]::UtcNow.ToString('o'); status='running'; steps=@(); nativeVisualComparison='not_run' }

function Invoke-Checked([string]$Name, [string]$Exe, [string[]]$Arguments) {
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    $log = Join-Path $out ($Name + '.txt')
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Exe @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    $report.steps += [ordered]@{name=$Name;exitCode=$code;log=$log}
    if ($code -ne 0) { throw "$Name failed (exit $code). See $log" }
}

Push-Location $root
try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    Invoke-Checked 'dotnet-sdk' $dotnet @('--info')
    Invoke-Checked 'core-tests' $dotnet @('run','--project',(Join-Path $root 'Tests/DiceGame.Tests.csproj'),'--configuration','Release')
    Invoke-Checked 'campaign-tests' $dotnet @('run','--project',(Join-Path $root 'Tests/Campaign/Campaign.Tests.csproj'),'--configuration','Release')
    Invoke-Checked 'skills-tests' $dotnet @('run','--project',(Join-Path $root 'Tests/Skills/Skills.Tests.csproj'),'--configuration','Release')
    Invoke-Checked 'native-build' $dotnet @('build',(Join-Path $root 'DiceGame.csproj'),'--configuration','Debug')
    if (-not $Godot) {
        foreach ($name in @('godot','godot4')) {
            $found = Get-Command $name -ErrorAction SilentlyContinue
            if ($found) { $Godot = $found.Source; break }
        }
    }
    if (-not $Godot) { $Godot = Read-Host 'Enter the full path to Godot 4.6 stable Mono executable' }
    $Godot = $Godot.Trim('"')
    if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) { throw 'Godot executable not found. Pass -Godot with its full path.' }
    Invoke-Checked 'godot-version' $Godot @('--version')
    $version = Get-Content (Join-Path $out 'godot-version.txt') -Raw
    if ($version -notmatch '(?m)^4\.6\.stable\.mono[.]') { throw 'Expected exactly Godot 4.6 stable Mono/.NET, not standard Godot or another release.' }
    Invoke-Checked 'godot-import' $Godot @('--headless','--path',$root,'--editor','--import','--quit','--log-file',(Join-Path $out 'godot-import-engine.log'))
    $smokeLog = Join-Path $out 'godot-smoke-engine.log'
    Invoke-Checked 'godot-smoke' $Godot @('--headless','--path',$root,'--quit-after','3','--log-file',$smokeLog)
    $text = Get-Content $smokeLog -Raw
    if ($text -notmatch 'CAMPAIGN READY' -or $text -match '(?m)(SCRIPT ERROR:|ERROR:|startup failed)') {
        throw 'Godot smoke check reported a startup/script error. Inspect godot-smoke-engine.log.'
    }
    if ($Capture) {
        Invoke-Checked 'campaign-native-input-and-capture' $Godot @('--path',$root,'--log-file',(Join-Path $out 'campaign-capture-engine.log'),'--','--capture-campaign')
        $inputChecks = Get-Content (Join-Path $out 'CampaignScreenshots/input-checks.json') -Raw | ConvertFrom-Json
        $campaignCapture = Get-Content (Join-Path $out 'CampaignScreenshots/capture_result.json') -Raw | ConvertFrom-Json
        if ($inputChecks.failed -ne 0 -or $inputChecks.passed -lt 23 -or -not $campaignCapture.completed) { throw 'Campaign native interaction/capture checks failed.' }
        $native = Join-Path $out 'Native'
        New-Item -ItemType Directory -Force -Path $native | Out-Null
        $captureResult = Join-Path $native 'capture_result.json'
        if (Test-Path $captureResult) { Remove-Item $captureResult }
        # Deliberately NOT --headless: headless disables actual rendering.
        Invoke-Checked 'native-capture' $Godot @('--path',$root,'--log-file',(Join-Path $out 'native-capture-engine.log'),'--','--capture-reference','--capture-dir',$native)
        if (-not (Test-Path $captureResult)) { throw 'Native capture did not produce its completion marker.' }
        $captured = Get-Content $captureResult -Raw | ConvertFrom-Json
        if ($captured.status -ne 'completed' -or $captured.screenshots -ne 9) { throw 'Native capture was incomplete.' }
        $report.nativeVisualComparison = 'captured_not_compared'
    }
    $report.status='passed_executed_steps_only'
    Write-Host "`nExecuted checks passed. Visual/audio listening parity is NOT implied by a successful build." -ForegroundColor Green
    if ($Capture) { Write-Host 'Next: python Tools/compare_screenshots.py  (from the project folder)' }
} catch {
    $report.status='failed';$report.error=$_.Exception.Message
    Write-Host $report.error -ForegroundColor Red
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $out 'local-verification.json')
    Pop-Location
}
if ($report.status -eq 'failed') { exit 1 }
