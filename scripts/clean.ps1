<#
.SYNOPSIS
    Removes bin/, obj/ and artifacts/.

.DESCRIPTION
    Only ever deletes inside this repository, and only paths .gitignore already ignores. It does
    not touch the NuGet cache — a slow restore is a smaller problem than a script that reaches
    outside the tree it was run in.

    Supports -WhatIf.

.EXAMPLE
    ./scripts/clean.ps1 -WhatIf
.EXAMPLE
    ./scripts/clean.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot

$searchRoots = @('src', 'tests') |
    ForEach-Object { Join-Path $repoRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ }

$targets = @()
if ($searchRoots) {
    $targets += Get-ChildItem -LiteralPath $searchRoots -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Select-Object -ExpandProperty FullName
}

$artifacts = Join-Path $repoRoot 'artifacts'
if (Test-Path -LiteralPath $artifacts) { $targets += $artifacts }

if (-not $targets) {
    Write-Step 'Nothing to clean'
    return
}

$removed = 0
foreach ($target in $targets) {
    # A bin/ nested under an obj/ disappears with its parent; the recursive listing still names it.
    if (-not (Test-Path -LiteralPath $target)) { continue }
    if ($PSCmdlet.ShouldProcess($target, 'Remove directory')) {
        Remove-Item -LiteralPath $target -Recurse -Force
        $removed++
    }
    Write-Host "    $($target.Substring($repoRoot.Length + 1))" -ForegroundColor DarkGray
}

Write-Step "Removed $removed of $($targets.Count) director$(if ($targets.Count -eq 1) { 'y' } else { 'ies' })"
