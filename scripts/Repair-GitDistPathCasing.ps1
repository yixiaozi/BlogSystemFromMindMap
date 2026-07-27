# 修复 Windows Git (core.ignorecase=true) 把旧路径大小写留在 index 里的问题。
# 实际逻辑在 repair_git_dist_path_casing.py（避免 PowerShell 处理 git -z 时被 \0 截断）。

function Remove-NestedDistGit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$DistName = "dist"
    )
    $nested = Join-Path $RepoRoot (Join-Path $DistName ".git")
    if (Test-Path -LiteralPath $nested) {
        Remove-Item -LiteralPath $nested -Recurse -Force
        Write-Host "Removed nested repository: $DistName/.git"
    }
}

function Repair-GitDistPathCasing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$DistName = "dist"
    )

    $py = Join-Path $RepoRoot "scripts\repair_git_dist_path_casing.py"
    if (-not (Test-Path -LiteralPath $py)) {
        throw "Missing $py"
    }

    Remove-NestedDistGit -RepoRoot $RepoRoot -DistName $DistName

    & python $py $RepoRoot $DistName
    if ($LASTEXITCODE -ne 0) {
        throw "repair_git_dist_path_casing.py failed with exit code $LASTEXITCODE"
    }
}
