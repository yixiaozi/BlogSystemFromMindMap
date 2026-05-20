$ErrorActionPreference = "Stop"

$ProjectDir = "d:\Dropbox\Code\C#\Project\BlogSystem"
$ScanDir = "D:\Dropbox\yixiaozi"
$OutDir = "D:\Dropbox\Code\C#\Project\BlogSystem\dist"
$LogFile = Join-Path $ProjectDir "build-publish.log"

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

Write-Log "========== Build & Publish Started =========="

try {
    Set-Location $ProjectDir
    Write-Log "Working directory: $ProjectDir"

    Write-Log "Running blog generation..."
    dotnet run --project MindmapBlog --out $OutDir --scan $ScanDir
    if ($LASTEXITCODE -ne 0) {
        throw "Blog generation failed with exit code $LASTEXITCODE"
    }
    Write-Log "Blog generation completed successfully."

    if ($OutDir -ne (Join-Path $ProjectDir "dist")) {
        Write-Log "Syncing output to project dist directory..."
        $ProjectDist = Join-Path $ProjectDir "dist"
        robocopy $OutDir $ProjectDist /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        $robocopyExit = $LASTEXITCODE
        if ($robocopyExit -gt 7) {
            throw "Robocopy failed with exit code $robocopyExit"
        }
        Write-Log "Sync completed."
    }

    Write-Log "Checking for git changes..."
    git add dist/
    $hasChanges = $false
    $statusOutput = git status --porcelain dist/
    if ($statusOutput) {
        $hasChanges = $true
    }

    if ($hasChanges) {
        $commitMsg = "auto: rebuild blog $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
        git commit -m $commitMsg
        if ($LASTEXITCODE -ne 0) {
            throw "Git commit failed"
        }
        Write-Log "Committed changes: $commitMsg"

        git push origin master
        if ($LASTEXITCODE -ne 0) {
            throw "Git push failed"
        }
        Write-Log "Pushed to GitHub. GitHub Actions will deploy to Pages."
    } else {
        Write-Log "No changes detected in dist/. Skipping commit and push."
    }

    Write-Log "========== Build & Publish Completed =========="
}
catch {
    Write-Log "ERROR: $_"
    Write-Log "========== Build & Publish Failed =========="
    exit 1
}
