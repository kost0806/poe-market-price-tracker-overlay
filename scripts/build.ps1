<#
.SYNOPSIS
    Restores and builds PoeOverlay.sln.

.DESCRIPTION
    Nothing here is more than `dotnet restore` + `dotnet build` over the solution. It exists so
    that the exit code is checked (see common.ps1) and so that "how this repository is built" has
    one answer rather than one per shell history.

    Warnings are promoted to errors by Directory.Build.props (Nullable, CA2007, CA1031), not by a
    switch here — a build run any other way has to fail the same way.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER NoRestore
    Skip the restore step. Only useful straight after another script has already restored.

.EXAMPLE
    ./scripts/build.ps1
.EXAMPLE
    ./scripts/build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$NoRestore
)

. (Join-Path $PSScriptRoot 'common.ps1')

Assert-DotnetSdk
$solution = Get-SolutionPath

if (-not $NoRestore) {
    Write-Step 'Restoring'
    Invoke-Dotnet @('restore', $solution)
}

# --no-restore either way: we have just restored, or the caller asked us not to.
Write-Step "Building ($Configuration)"
Invoke-Dotnet @('build', $solution, '-c', $Configuration, '--no-restore', '--nologo')

Write-Step "Build succeeded ($Configuration)"
