$ErrorActionPreference = "Stop"

$ProjectDir = "E:\Develop\BlogSystemFromMindMap"
$ScanDir = "E:\yixiaozi"
$OutDir = "E:\Develop\BlogSystem"

Write-Host "========== Build & Publish Started =========="

try {
    Set-Location $ProjectDir
    Write-Host "Working directory: $ProjectDir"

    Write-Host "Running blog generation..."
    dotnet run --project MindmapBlog --out $OutDir --scan $ScanDir
    if ($LASTEXITCODE -ne 0) {
        throw "Blog generation failed with exit code $LASTEXITCODE"
    }
    Write-Host "Blog generation completed successfully."

    if ($OutDir -ne (Join-Path $ProjectDir "dist")) {
        Write-Host "Syncing output to project dist directory..."
        $ProjectDist = Join-Path $ProjectDir "dist"
        robocopy $OutDir $ProjectDist /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        $robocopyExit = $LASTEXITCODE
        if ($robocopyExit -gt 7) {
            throw "Robocopy failed with exit code $robocopyExit"
        }
        Write-Host "Sync completed."
    }

    Write-Host "Checking for git changes..."
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
        Write-Host "Committed changes: $commitMsg"

        git push origin master
        if ($LASTEXITCODE -ne 0) {
            throw "Git push failed"
        }
        Write-Host "Pushed to GitHub. GitHub Actions will deploy to Pages."
    } else {
        Write-Host "No changes detected in dist/. Skipping commit and push."
    }

    Write-Host "========== Build & Publish Completed =========="
}
catch {
    Write-Host "ERROR: $_"
    Write-Host "========== Build & Publish Failed =========="
    exit 1
}
