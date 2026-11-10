<#
.SYNOPSIS
    Builds the agent as it is installed: the desktop app and the Windows
    service, self-contained for win-x64, in one folder. No plugins: they
    are installed from a catalog.

.DESCRIPTION
    The output is what the installer packs (installer\HomeAssistantAgent.iss).
    Both executables share the folder on purpose: one Plugins directory, and
    the runtime files they have in common are identical. No "portable" marker
    is written, so the result keeps its data in %ProgramData%.

.EXAMPLE
    ./build/Publish.ps1 -Version 1.2.0
#>
param(
    [string] $Version = '1.0.0',
    [string] $Configuration = 'Release',
    [string] $Output = (Join-Path $PSScriptRoot '..\artifacts\app')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$src = Join-Path $root 'src'

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item $Output -ItemType Directory | Out-Null

$publish = @(
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    "-p:Version=$Version",
    '-o', $Output
)

Write-Host "Publishing the desktop app $Version"
Invoke-DotNet publish (Join-Path $src 'HomeAssistant.Agent\HomeAssistant.Agent.csproj') @publish

Write-Host "Publishing the Windows service $Version"
Invoke-DotNet publish (Join-Path $src 'HomeAssistant.Agent.Service\HomeAssistant.Agent.Service.csproj') @publish

# A development marker must never ship: it would keep an installed copy's data
# in Program Files.
Remove-Item (Join-Path $Output 'portable') -ErrorAction SilentlyContinue

Write-Host "Published to $Output"
