<#
.SYNOPSIS
    Publishes the shell to artifacts/publish/<Configuration>/.

.DESCRIPTION
    Framework-dependent, no RID, not self-contained, not single-file. The absent switches are the
    decision, not an omission (HLD 9): trimming and AOT are off the table because WPF does not
    support them and this is a local build for one machine (G1), and the dictionaries
    (Localization/*.json, FR-07-3) and item icons (Icons/, D23) have to stay as files beside the
    exe — being replaceable without a rebuild is the whole reason they are there.

    The output directory is emptied first. `dotnet publish` overwrites but never deletes, so a
    league turnover that drops an icon would otherwise leave the old file sitting there looking
    current.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER SkipTests
    Publish without running the suite first. Off by default: publishing is the point at which
    something gets run for real.

.EXAMPLE
    ./scripts/publish.ps1
.EXAMPLE
    ./scripts/publish.ps1 -Configuration Debug -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-DotnetSdk

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'test.ps1') -Configuration $Configuration
}

$project = Get-ShellProjectPath
$outputDir = Join-Path (Get-RepoRoot) "artifacts\publish\$Configuration"

if (Test-Path -LiteralPath $outputDir) {
    Write-Step "Clearing $outputDir"
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

Write-Step "Publishing ($Configuration)"
Invoke-Dotnet @('publish', $project, '-c', $Configuration, '-o', $outputDir, '--nologo')

$exe = Join-Path $outputDir 'PoeOverlay.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "publish reported success but $exe is not there."
}

Write-Step "Published to $outputDir"
