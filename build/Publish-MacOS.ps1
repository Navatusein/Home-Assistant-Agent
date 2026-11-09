<#
.SYNOPSIS
    Builds the agent for macOS: Home Assistant Agent.app with the desktop app,
    the launchd service and the bundled plugins — as a disk image on a Mac, or
    as a zip to finish on the Mac when built elsewhere.

.DESCRIPTION
    The layout (see docs/PLAN-macos.md):

        Home Assistant Agent.app/Contents/
          Info.plist            LSUIElement: a menu bar app, no Dock icon
          MacOS/                the app and the service, self-contained
                                osx-arm64, sharing one runtime; Plugins/
          Resources/icon.icns

    On a Mac (PowerShell 7) the bundle is signed ad hoc and packed into
    HomeAssistantAgent-<version>-macos-arm64.dmg with a link to /Applications.

    Anywhere else (Windows) the same bundle goes into
    HomeAssistantAgent-<version>-macos-arm64.zip, unsigned: codesign exists
    only on macOS, and a zip made on Windows keeps no Unix permissions. Next to
    the app it carries prepare.sh, which does the rest on the Mac — marks the
    executables executable, clears the quarantine, signs ad hoc (Apple silicon
    runs nothing unsigned) and copies the app to /Applications:

        unzip HomeAssistantAgent-<version>-macos-arm64.zip -d agent && sh agent/prepare.sh

    The agent is not notarized: the first start of a downloaded disk image
    needs "Open Anyway" in System Settings > Privacy & Security.

    The bundled plugins come from Home-Assistant-Agent-Plugins (-PluginsRepo),
    built against the plugin API of this checkout; the Windows plugin is left
    out.

.EXAMPLE
    ./build/Publish-MacOS.ps1 -Version 1.2.0
#>
param(
    [string] $Version = '1.0.0',
    [string] $Configuration = 'Release',
    [string] $Runtime = 'osx-arm64',
    [string] $Output = (Join-Path $PSScriptRoot '../artifacts/macos'),
    [string] $PluginsRepo = (Join-Path $PSScriptRoot '../../Home-Assistant-Agent-Plugins')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$src = Join-Path $root 'src'

if (-not (Test-Path (Join-Path $PluginsRepo 'src'))) { throw "The plugins repository is not found at $PluginsRepo (pass -PluginsRepo)." }
$pluginsSrc = Join-Path (Resolve-Path $PluginsRepo) 'src'

# The plugins shipped with the agent on macOS: all but the Windows one.
$plugins = 'Discord', 'Logitech', 'Obs', 'Teams'

$appName = 'Home Assistant Agent'
$bundleId = 'com.navatusein.homeassistant-agent'
$executable = 'HomeAssistant.Agent'
$packageName = "HomeAssistantAgent-$Version-macos-arm64"

# A plain function on purpose: an advanced one would try to bind "-z", "-c"
# or "-o" meant for the tool as its own parameters.
function Invoke-Native {
    $file = $args[0]
    $arguments = @($args | Select-Object -Skip 1)

    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# Text for the Mac: Unix line endings, no BOM (a shell script with CRLF fails).
function Write-UnixText([string] $Path, [string] $Text) {
    [IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

# An .icns is a list of PNG images of standard sizes: what iconutil writes on
# a Mac, made here from the app icon for builds elsewhere.
function New-Icns([string] $Png, [string] $Path) {
    Add-Type -AssemblyName System.Drawing

    $source = [System.Drawing.Image]::FromFile($Png)
    $body = [IO.MemoryStream]::new()

    try {
        foreach ($entry in @(('ic07', 128), ('ic08', 256), ('ic09', 512), ('ic10', 1024))) {
            $size = $entry[1]
            $bitmap = [System.Drawing.Bitmap]::new($size, $size)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $graphics.Dispose()

            $image = [IO.MemoryStream]::new()
            $bitmap.Save($image, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()

            $bytes = $image.ToArray()
            Write-Bytes $body ([Text.Encoding]::ASCII.GetBytes($entry[0]))
            Write-Bytes $body (Get-BigEndian ($bytes.Length + 8))
            Write-Bytes $body $bytes
        }
    }
    finally {
        $source.Dispose()
    }

    $file = [IO.File]::Create($Path)

    try {
        Write-Bytes $file ([Text.Encoding]::ASCII.GetBytes('icns'))
        Write-Bytes $file (Get-BigEndian ($body.Length + 8))
        $body.WriteTo($file)
    }
    finally {
        $file.Dispose()
    }
}

function Write-Bytes([IO.Stream] $Stream, [byte[]] $Bytes) {
    $Stream.Write($Bytes, 0, $Bytes.Length)
}

function Get-BigEndian([long] $Value) {
    $bytes = [BitConverter]::GetBytes([uint32] $Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    return , $bytes
}

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

$app = Join-Path $Output "$appName.app"
$contents = Join-Path $app 'Contents'
$macOS = Join-Path $contents 'MacOS'
$resources = Join-Path $contents 'Resources'

New-Item $macOS, $resources -ItemType Directory -Force | Out-Null

$publish = @(
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:HostOs=macos',
    "-p:Version=$Version",
    '-o', $macOS
)

Write-Host "Publishing the desktop app $Version"
Invoke-Native dotnet publish (Join-Path $src 'HomeAssistant.Agent/HomeAssistant.Agent.csproj') @publish

Write-Host "Publishing the service $Version"
Invoke-Native dotnet publish (Join-Path $src 'HomeAssistant.Agent.Service/HomeAssistant.Agent.Service.csproj') @publish

$pluginsOutput = Join-Path $macOS 'Plugins'
New-Item $pluginsOutput -ItemType Directory -Force | Out-Null

foreach ($plugin in $plugins) {
    $project = Join-Path $pluginsSrc "HomeAssistant.Agent.Plugin.$plugin/HomeAssistant.Agent.Plugin.$plugin.csproj"

    Write-Host "Building plugin $plugin"
    # Against this checkout's API; no copy into the local agent's Plugins.
    Invoke-Native dotnet build $project -c $Configuration "-p:AgentRepoPath=$root/" '-p:AgentHostOutput='

    # One self-contained dll per plugin (dependencies embedded).
    $dll = Get-ChildItem (Join-Path $pluginsSrc "HomeAssistant.Agent.Plugin.$plugin/bin/$Configuration") `
        -Recurse -Filter "HomeAssistant.Agent.Plugin.$plugin.dll" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $dll) { throw "No build output found for plugin $plugin" }

    Copy-Item $dll.FullName $pluginsOutput
}

# A development marker must never ship: it would keep the data inside the
# (read-only, signed) bundle. Debug symbols stay out too.
Remove-Item (Join-Path $macOS 'portable') -ErrorAction SilentlyContinue
Get-ChildItem $macOS -Filter '*.pdb' | Remove-Item

Write-Host 'Creating the icon'
$png = Join-Path $src 'HomeAssistant.Agent/Assets/icon.png'
$icns = Join-Path $resources 'icon.icns'

if ($IsMacOS) {
    $iconset = Join-Path $Output 'icon.iconset'
    New-Item $iconset -ItemType Directory | Out-Null

    foreach ($size in 16, 32, 128, 256, 512) {
        Invoke-Native sips -z $size $size $png --out (Join-Path $iconset "icon_${size}x${size}.png") | Out-Null
        $double = $size * 2
        Invoke-Native sips -z $double $double $png --out (Join-Path $iconset "icon_${size}x${size}@2x.png") | Out-Null
    }

    Invoke-Native iconutil -c icns $iconset -o $icns
    Remove-Item $iconset -Recurse -Force
}
else {
    New-Icns $png $icns
}

Write-Host 'Writing Info.plist'
Write-UnixText (Join-Path $contents 'Info.plist') @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$appName</string>
  <key>CFBundleDisplayName</key>
  <string>$appName</string>
  <key>CFBundleIdentifier</key>
  <string>$bundleId</string>
  <key>CFBundleExecutable</key>
  <string>$executable</string>
  <key>CFBundleIconFile</key>
  <string>icon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$Version</string>
  <key>CFBundleVersion</key>
  <string>$Version</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>Navatusein, MIT License</string>
</dict>
</plist>

"@

$installers = Join-Path $root 'artifacts/installer'
New-Item $installers -ItemType Directory -Force | Out-Null

if ($IsMacOS) {
    # Ad hoc: every Mach-O on Apple silicon must carry a signature, and
    # changing the bundle after publish invalidated the ones dotnet wrote.
    # Each one on its own, the inside first: the service gets an identifier of
    # its own (dotnet leaves "apphost" on it), the app takes the bundle's.
    Write-Host 'Signing (ad hoc)'

    # Every file in Contents/MacOS counts as code to codesign, the managed
    # dlls and the json files too (their signature goes into an extended
    # attribute), so all of them, the plugins included.
    $executables = 'HomeAssistant.Agent', 'HomeAssistant.Agent.Service' |
        ForEach-Object { [IO.Path]::GetFullPath((Join-Path $macOS $_)) }
    foreach ($file in Get-ChildItem $macOS -File -Recurse | Where-Object { $_.FullName -notin $executables }) {
        Invoke-Native codesign --force --sign - $file.FullName
    }

    Invoke-Native codesign --force --sign - --identifier "$bundleId.service" (Join-Path $macOS 'HomeAssistant.Agent.Service')
    Invoke-Native codesign --force --sign - $app
    Invoke-Native codesign --verify --deep --strict $app

    Write-Host 'Creating the disk image'
    $staging = Join-Path $Output 'dmg'
    New-Item $staging -ItemType Directory | Out-Null
    Invoke-Native ditto $app (Join-Path $staging "$appName.app")
    New-Item -ItemType SymbolicLink -Path (Join-Path $staging 'Applications') -Target '/Applications' | Out-Null

    $package = Join-Path $installers "$packageName.dmg"
    Invoke-Native hdiutil create -volname $appName -srcfolder $staging -ov -format UDZO $package
    Remove-Item $staging -Recurse -Force
}
else {
    Write-Host 'Writing prepare.sh'
    Write-UnixText (Join-Path $Output 'prepare.sh') @"
#!/bin/sh
# Finishes the app built on another system and installs it, on the Mac:
#
#   sh prepare.sh
#
# A zip made on Windows keeps no Unix permissions and nothing there can sign
# for macOS: the executables are marked executable, the download quarantine
# is cleared, the bundle is signed ad hoc (Apple silicon runs nothing
# unsigned), and the app is copied to /Applications.
set -e

cd "`$(dirname "`$0")"
APP="$appName.app"

MACOS="`$APP/Contents/MacOS"

find "`$MACOS" -maxdepth 1 -type f ! -name '*.*' -exec chmod +x {} +
xattr -cr "`$APP"

# Each Mach-O on its own, the inside first: the service gets an identifier of
# its own, the app takes the bundle's.
find "`$MACOS" -type f ! -path "`$MACOS/HomeAssistant.Agent" ! -path "`$MACOS/HomeAssistant.Agent.Service" -exec codesign --force --sign - {} \;
codesign --force --sign - --identifier "$bundleId.service" "`$MACOS/HomeAssistant.Agent.Service"
codesign --force --sign - "`$APP"
codesign --verify --deep --strict "`$APP"

rm -rf "/Applications/`$APP"
ditto "`$APP" "/Applications/`$APP"

echo "Installed to /Applications/`$APP. Start it from there; it appears in the menu bar."

"@

    Write-Host 'Creating the zip'
    $package = Join-Path $installers "$packageName.zip"
    Remove-Item $package -ErrorAction SilentlyContinue
    Compress-Archive -Path $app, (Join-Path $Output 'prepare.sh') -DestinationPath $package
}

Write-Host "Published to $app and $package"
