$ErrorActionPreference = "Stop"

$ProjectDir = "E:\Develop\BlogSystemFromMindMap"
$ScanDir = "E:\yixiaozi"
$OutDir = "E:\Develop\BlogSystem"

Write-Host "========== Build & Publish Started =========="

try {
    Set-Location $ProjectDir
    Write-Host "Working directory: $ProjectDir"

    Write-Host "Running blog generation..."
    $ProjectDist = Join-Path $ProjectDir "dist"
    if ($OutDir -ne $ProjectDist) {
        # 生成到独立输出目录时，先继承 dist 里的生成记录，避免 robocopy /MIR 覆盖后历史从 0 开始
        $historyRel = Join-Path "data" "generation-history.json"
        $srcHistory = Join-Path $ProjectDist $historyRel
        $dstHistory = Join-Path $OutDir $historyRel
        if (Test-Path -LiteralPath $srcHistory) {
            New-Item -ItemType Directory -Force -Path (Split-Path -LiteralPath $dstHistory) | Out-Null
            Copy-Item -LiteralPath $srcHistory -Destination $dstHistory -Force
            Write-Host "Seeded generation history from project dist."
        }
    }

    dotnet run --project MindmapBlog --out $OutDir --scan $ScanDir
    if ($LASTEXITCODE -ne 0) {
        throw "Blog generation failed with exit code $LASTEXITCODE"
    }
    Write-Host "Blog generation completed successfully."

    if ($OutDir -ne (Join-Path $ProjectDir "dist")) {
        Write-Host "Syncing output to project dist directory..."
        $ProjectDist = Join-Path $ProjectDir "dist"
        # 禁止把输出目录里的嵌套 .git 镜像进仓库 dist（会导致嵌入式仓库 / Linux 路径分裂）
        $outGit = Join-Path $OutDir ".git"
        if (Test-Path -LiteralPath $outGit) {
            Remove-Item -LiteralPath $outGit -Recurse -Force
            Write-Host "Removed nested .git from output directory."
        }
        robocopy $OutDir $ProjectDist /MIR /NFL /NDL /NJH /NJS /NC /NS /XD .git | Out-Null
        $robocopyExit = $LASTEXITCODE
        if ($robocopyExit -gt 7) {
            throw "Robocopy failed with exit code $robocopyExit"
        }
        $distGit = Join-Path $ProjectDist ".git"
        if (Test-Path -LiteralPath $distGit) {
            Remove-Item -LiteralPath $distGit -Recurse -Force
            Write-Host "Removed nested .git from project dist."
        }
        Write-Host "Sync completed."
    }

    Write-Host "Checking for git changes..."
    . (Join-Path $ProjectDir "scripts\Repair-GitDistPathCasing.ps1")
    Repair-GitDistPathCasing -RepoRoot $ProjectDir -DistName "dist"

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
