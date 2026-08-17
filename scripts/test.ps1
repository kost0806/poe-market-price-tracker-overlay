<#
.SYNOPSIS
    Builds and runs the test suites.

.DESCRIPTION
    The default target is the whole solution, deliberately (S4 2.6). S4 2.4 separates "it built"
    from "it was verified": PoeOverlay.Shell.Tests is net8.0-windows and only runs on Windows, so
    on Windows there is no reason to leave it out — and leaving it out is exactly how that
    distinction gets blurred again. -Project is for shortening an edit/run cycle, not a default.

    Build and test are separate steps so that a compile failure is reported as a compile failure;
    `dotnet test` alone would bury it under a test-run summary.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER Project
    All (default), Core, or Shell.

.PARAMETER Filter
    Passed through to `dotnet test --filter`, e.g. -Filter 'FullyQualifiedName~Pricing'.

.EXAMPLE
    ./scripts/test.ps1
.EXAMPLE
    ./scripts/test.ps1 -Project Core -Filter 'FullyQualifiedName~StalenessPolicy'
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('All', 'Core', 'Shell')][string]$Project = 'All',
    [string]$Filter
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-DotnetSdk

$repoRoot = Get-RepoRoot
$target = switch ($Project) {
    'All'   { Get-SolutionPath }
    'Core'  { Join-Path $repoRoot 'tests\PoeOverlay.Core.Tests\PoeOverlay.Core.Tests.csproj' }
    'Shell' { Join-Path $repoRoot 'tests\PoeOverlay.Shell.Tests\PoeOverlay.Shell.Tests.csproj' }
}

Write-Step 'Restoring'
Invoke-Dotnet @('restore', $target)

Write-Step "Building ($Configuration)"
Invoke-Dotnet @('build', $target, '-c', $Configuration, '--no-restore', '--nologo')

Write-Step "Testing ($Project, $Configuration)"
$testArgs = @('test', $target, '-c', $Configuration, '--no-build', '--nologo')
if ($Filter) { $testArgs += @('--filter', $Filter) }
Invoke-Dotnet $testArgs

Write-Step 'Tests passed'
