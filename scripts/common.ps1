# Shared by the four build scripts. Dot-source it:  . (Join-Path $PSScriptRoot 'common.ps1')
#
# The one thing here that is not convenience is Invoke-Dotnet's exit-code check (S4 2.6).
# `dotnet` is a native process, so $ErrorActionPreference = 'Stop' does not see it fail: without
# the check, test.ps1 would go on running tests over a build that never succeeded and would then
# exit 0. Green that means nothing is the failure this repository guards against hardest.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# scripts/ sits directly under the repository root.
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Get-RepoRoot {
    $script:RepoRoot
}

function Get-SolutionPath {
    Join-Path $script:RepoRoot 'PoeOverlay.sln'
}

function Get-ShellProjectPath {
    Join-Path $script:RepoRoot 'src\PoeOverlay.Shell\PoeOverlay.Shell.csproj'
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-DotnetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet was not found on PATH. This repository needs the .NET 8 SDK (REQUIREMENTS 10).'
    }
}

function Invoke-Dotnet {
    # Takes the whole command line as ONE array argument — Invoke-Dotnet @('build', $sln, '-c', 'Debug').
    # Passing the parts as separate arguments would let PowerShell bind '-c' as a parameter of this
    # function instead of forwarding it.
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "    dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}
