#Requires -Version 5.1
<#
.SYNOPSIS
  Generate site, git commit, git push, write logs.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Develop\BlogSystemFromMindMap\scripts\publish-blog.ps1"
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "E:\Develop\BlogSystemFromMindMap",
    [string]$ScanDir = "E:\yixiaozi",
    [string]$OutDir = "E:\Develop\BlogSystemFromMindMap\dist",
    [string]$BaseUrl = "https://yixiaozi.github.io/BlogSystemFromMindMap",
    [string]$GitRemote = "origin",
    [string]$CommitMessage = "",
    [switch]$SkipPush,
    [switch]$SkipCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$ScanDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScanDir)
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)

$LogsDir = Join-Path $RepoRoot "logs"
$IndexLog = Join-Path $RepoRoot "log.txt"
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$RunLog = Join-Path $LogsDir "publish-$Stamp.log"

New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

function Write-RunLog {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR", "OK")]
        [string]$Level = "INFO"
    )
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Add-Content -LiteralPath $RunLog -Value $line -Encoding UTF8
    switch ($Level) {
        "ERROR" { Write-Host $line -ForegroundColor Red }
        "WARN"  { Write-Host $line -ForegroundColor Yellow }
        "OK"    { Write-Host $line -ForegroundColor Green }
        default { Write-Host $line }
    }
}

function Append-IndexLog {
    param(
        [string]$Status,
        [string]$Detail
    )
    $indexLine = "[{0}] {1} | {2} | log: {3}" -f (
        (Get-Date -Format "yyyy-MM-dd HH:mm:ss"),
        $Status,
        $Detail,
        $RunLog
    )
    Add-Content -LiteralPath $IndexLog -Value $indexLine -Encoding UTF8
}

function Get-OutputText {
    param([object]$Item)
    if ($Item -is [System.Management.Automation.ErrorRecord]) {
        return $Item.ToString()
    }
    return $Item.ToString()
}

function Invoke-LoggedCommand {
    param(
        [string]$Name,
        [scriptblock]$Command
    )
    Write-RunLog ">>> $Name"

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $Command 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prevEap
    }

    foreach ($item in @($output)) {
        $text = Get-OutputText $item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        Add-Content -LiteralPath $RunLog -Value $text -Encoding UTF8
        if ($text -match '^(warning|error):' -and $Name -like "git*") {
            Write-Host $text -ForegroundColor Yellow
        }
        else {
            Write-Host $text
        }
    }

    if ($null -ne $exitCode -and $exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode"
    }
}

function Test-GitHasStagedChanges {
    param([string]$Root)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & git -C $Root diff --cached --quiet 2>$null | Out-Null
        return ($LASTEXITCODE -ne 0)
    }
    finally {
        $ErrorActionPreference = $prevEap
    }
}

$exitCode = 0
$summary = ""

try {
    Write-RunLog "publish started"
    Write-RunLog "repo: $RepoRoot"
    Write-RunLog "scan: $ScanDir"
    Write-RunLog "out:  $OutDir"
    Write-RunLog "base-url: $BaseUrl"
    Write-RunLog "run log: $RunLog"

    if (-not (Test-Path -LiteralPath $ScanDir -PathType Container)) {
        throw "scan directory not found: $ScanDir"
    }

    Set-Location -LiteralPath $RepoRoot

    $dotnetArgs = @(
        "run",
        "--project", (Join-Path $RepoRoot "MindmapBlog\MindmapBlog.csproj"),
        "--",
        "--out", $OutDir,
        "--scan", $ScanDir,
        "--base-url", $BaseUrl
    )
    $dotnetCmd = "dotnet " + ($dotnetArgs -join " ")
    Write-RunLog "command: $dotnetCmd"

    Invoke-LoggedCommand "dotnet generate" {
        & dotnet @dotnetArgs
    }

    if (-not (Test-Path -LiteralPath (Join-Path $OutDir "index.html") -PathType Leaf)) {
        throw "index.html not found under $OutDir"
    }

    Write-RunLog "site generated" "OK"

    if (-not $SkipCommit) {
        Invoke-LoggedCommand "git status" {
            & git -C $RepoRoot status -sb
        }

        Invoke-LoggedCommand "git add" {
            . (Join-Path $RepoRoot "scripts\Repair-GitDistPathCasing.ps1")
            Repair-GitDistPathCasing -RepoRoot $RepoRoot -DistName "dist"
            & git -C $RepoRoot -c advice.addIgnoredFile=false add -A
        }

        $hasStaged = Test-GitHasStagedChanges -Root $RepoRoot

        if (-not $hasStaged) {
            Write-RunLog "no staged changes, skip git commit" "WARN"
        }
        else {
            if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
                $CommitMessage = "publish: site snapshot $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            }

            Invoke-LoggedCommand "git commit" {
                & git -C $RepoRoot commit -m $CommitMessage
            }

            $commitHash = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
            Write-RunLog "committed $commitHash | $CommitMessage" "OK"
            $summary = "commit $commitHash"
        }
    }
    else {
        Write-RunLog "skip git commit (-SkipCommit)" "WARN"
        $summary = "skip commit"
    }

    if (-not $SkipPush) {
        $branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()
        Write-RunLog "push to $GitRemote/$branch"

        Invoke-LoggedCommand "git push" {
            & git -C $RepoRoot push $GitRemote $branch
        }

        Write-RunLog "pushed to $GitRemote/$branch" "OK"
        if ($summary) { $summary += " | " }
        $summary += "pushed $GitRemote/$branch"
    }
    else {
        Write-RunLog "skip git push (-SkipPush)" "WARN"
        if ($summary) { $summary += " | " }
        $summary += "skip push"
    }

    if (-not $summary) { $summary = "no changes" }
    Write-RunLog "publish done: $summary" "OK"
    Append-IndexLog -Status "SUCCESS" -Detail $summary
}
catch {
    $exitCode = 1
    $msg = $_.Exception.Message
    Write-RunLog $msg "ERROR"
    if ($_.ScriptStackTrace) {
        Add-Content -LiteralPath $RunLog -Value $_.ScriptStackTrace -Encoding UTF8
    }
    Append-IndexLog -Status "FAILED" -Detail $msg
}
finally {
    Write-RunLog "exit code $exitCode"
}

exit $exitCode
