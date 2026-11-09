<#
.SYNOPSIS
    Builds the agent as it is installed: the desktop app and the Windows
    service, self-contained for win-x64, in one folder, with the bundled
    plugins in Plugins\.

.DESCRIPTION
    The output is what the installer packs (installer\HomeAssistantAgent.iss).
    Both executables share the folder on purpose: one Plugins directory, and
    the runtime files they have in common are identical. No "portable" marker
    is written, so the result keeps its data in %ProgramData%.

    The bundled plugins come from Home-Assistant-Agent-Plugins (-PluginsRepo,
    by default checked out next to this repository). They are built against
    the plugin API of this very checkout.

.EXAMPLE
    ./build/Publish.ps1 -Version 1.2.0
#>
param(
    [string] $Version = '1.0.0',
    [string] $Configuration = 'Release',
    [string] $Output = (Join-Path $PSScriptRoot '..\artifacts\app'),
    [string] $PluginsRepo = (Join-Path $PSScriptRoot '..\..\Home-Assistant-Agent-Plugins')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$src = Join-Path $root 'src'

if (-not (Test-Path (Join-Path $PluginsRepo 'src'))) { throw "The plugins repository is not found at $PluginsRepo (pass -PluginsRepo)." }
$pluginsSrc = Join-Path (Resolve-Path $PluginsRepo) 'src'

# The plugins shipped with the agent. Each versions itself (its own <Version>).
$plugins = 'Discord', 'Logitech', 'Obs', 'Teams', 'Windows'

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

$pluginsOutput = Join-Path $Output 'Plugins'
New-Item $pluginsOutput -ItemType Directory -Force | Out-Null

foreach ($plugin in $plugins) {
    $project = Join-Path $pluginsSrc "HomeAssistant.Agent.Plugin.$plugin\HomeAssistant.Agent.Plugin.$plugin.csproj"

    Write-Host "Building plugin $plugin"
    # Against this checkout's API; no copy into the local agent's Plugins.
    Invoke-DotNet build $project -c $Configuration "-p:AgentRepoPath=$root\" '-p:AgentHostOutput='

    # One self-contained dll per plugin (dependencies embedded).
    $dll = Get-ChildItem (Join-Path $pluginsSrc "HomeAssistant.Agent.Plugin.$plugin\bin\$Configuration") `
        -Recurse -Filter "HomeAssistant.Agent.Plugin.$plugin.dll" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $dll) { throw "No build output found for plugin $plugin" }

    Copy-Item $dll.FullName $pluginsOutput
}

# A development marker must never ship: it would keep an installed copy's data
# in Program Files.
Remove-Item (Join-Path $Output 'portable') -ErrorAction SilentlyContinue

Write-Host "Published to $Output"
