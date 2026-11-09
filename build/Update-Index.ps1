<#
.SYNOPSIS
    Adds or replaces one entry of a plugin catalog (index.json).

.DESCRIPTION
    A catalog is index.json in the "index" release of a repository, at the
    stable address
        https://github.com/<owner>/<repo>/releases/download/index/index.json
    The agent reads it to offer plugins and updates. This script updates one
    entry in it: the agent's own block (-Kind agent, official repository only)
    or a plugin, matched by its code (-Kind plugin).

    With -Repository it works on the release through the GitHub CLI (gh),
    creating the "index" release on first use. With -IndexFile it works on a
    local file instead — for trying it out.

.EXAMPLE
    ./build/Update-Index.ps1 -Kind plugin -EntryFile entry.json -Repository Navatusein/Home-Assistant-Agent
#>
param(
    [Parameter(Mandatory)] [ValidateSet('agent', 'plugin')] [string] $Kind,
    [Parameter(Mandatory)] [string] $EntryFile,
    [string] $Repository,
    [string] $IndexFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ReleaseTag = 'index'
$AssetName = 'index.json'

if (-not $Repository -and -not $IndexFile) { throw 'Pass -Repository (owner/repo) or -IndexFile.' }

function Invoke-Gh {
    & gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

$work = $null

if ($Repository) {
    $work = New-Item (Join-Path ([System.IO.Path]::GetTempPath()) ("catalog-" + [guid]::NewGuid().ToString('N').Substring(0, 8))) -ItemType Directory
    $IndexFile = Join-Path $work $AssetName

    & gh release view $ReleaseTag -R $Repository *> $null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating the '$ReleaseTag' release in $Repository"
        Invoke-Gh release create $ReleaseTag -R $Repository --title 'Plugin catalog' `
            --notes 'index.json lists the releases of this repository for Home Assistant Agent. Updated automatically.' `
            --latest=false
    }
    else {
        & gh release download $ReleaseTag -R $Repository -p $AssetName -D $work --clobber *> $null
        # A release without the asset yet is fine: the catalog starts empty.
    }
}

$index = if (Test-Path $IndexFile) {
    Get-Content $IndexFile -Raw | ConvertFrom-Json -AsHashtable
}
else {
    [ordered]@{ schema = 1; plugins = @() }
}

if (-not $index.Contains('plugins') -or $null -eq $index['plugins']) { $index['plugins'] = @() }

$entry = Get-Content $EntryFile -Raw | ConvertFrom-Json -AsHashtable

if ($Kind -eq 'agent') {
    $index['agent'] = $entry
    Write-Host "Agent: $($entry['version'])"
}
else {
    if (-not $entry['code']) { throw "The plugin entry has no code." }

    $others = @($index['plugins'] | Where-Object { $_['code'] -ne $entry['code'] })
    $index['plugins'] = @($others + $entry | Sort-Object { $_['code'] })
    Write-Host "Plugin $($entry['code']): $($entry['version'])"
}

$index['updated'] = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$index | ConvertTo-Json -Depth 10 | Set-Content $IndexFile -Encoding utf8NoBOM

if ($Repository) {
    Invoke-Gh release upload $ReleaseTag $IndexFile -R $Repository --clobber
    Remove-Item $work -Recurse -Force
    Write-Host "Updated https://github.com/$Repository/releases/download/$ReleaseTag/$AssetName"
}
else {
    Write-Host "Updated $IndexFile"
}
