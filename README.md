# Home Assistant Agent

An agent that exposes a PC or a Mac to [Home Assistant](https://www.home-assistant.io/) over MQTT.
Entities appear in Home Assistant through MQTT discovery; what the agent can do comes from
plugins.

## Plugins

The official plugins live in
[Home-Assistant-Agent-Plugins](https://github.com/Navatusein/Home-Assistant-Agent-Plugins):
Windows (volume, audio devices, media, power, launchers, hotkeys, clipboard), Discord, Microsoft
Teams, OBS Studio and Logitech G HUB. They ship with the installer and update on their own.

More catalogs can be added under Settings → Plugins → Sources. A third-party plugin is built
with the [plugin SDK](src/HomeAssistant.Agent.Plugin.Sdk/README.md).

## Installing

Downloads are on the [releases](https://github.com/Navatusein/Home-Assistant-Agent/releases) page.

**Windows** — `HomeAssistantAgent-Setup-<version>.exe`, Windows 10 2004 or newer. The agent runs as
a desktop app and a Windows service; it updates itself and its plugins.

**macOS** — `HomeAssistantAgent-<version>-macos-arm64.dmg`, Apple silicon, macOS 12 or newer. Drag
the app to Applications. The app is not notarized: the first start is blocked — open System
Settings → Privacy & Security and choose *Open Anyway*. On that first start the agent asks for an
administrator password once, to create its data folder. It lives in the menu bar; the background
service (a launchd daemon) is installed from Settings. New versions are downloaded from the
releases page; plugins update on their own. The Windows plugin is Windows-only.

## Repository

| Path                                   | What                                                    |
|----------------------------------------|---------------------------------------------------------|
| `src/HomeAssistant.Agent`              | Desktop app (Avalonia)                                  |
| `src/HomeAssistant.Agent.Service`      | Background service (Windows service, launchd daemon)    |
| `src/HomeAssistant.Agent.Core`         | Shared core: plugins, MQTT, settings, logging, updates |
| `src/HomeAssistant.Agent.Plugin.Base`  | Plugin API                                              |
| `src/HomeAssistant.Connector`          | Home Assistant MQTT entities                            |
| `src/HomeAssistant.Agent.Plugin.Sdk`   | NuGet package for plugin authors                        |
| `templates`                            | `dotnet new ha-agent-plugin`                            |
| `tools/PluginManifest`                 | Catalog entry for a plugin (used by releases)           |
| `build`, `installer`                   | Publish scripts, Inno Setup installer                   |

## Building

.NET 10 SDK. On Windows the app and the service build for Windows; on a Mac, for macOS
(`-p:HostOs=macos` checks the macOS build from Windows).

```
dotnet build HomeAssistant.Agent.sln
dotnet test tests/HomeAssistant.Agent.Tests
```

To work on the plugins too, check out
[Home-Assistant-Agent-Plugins](https://github.com/Navatusein/Home-Assistant-Agent-Plugins) next to
this repository and open `HomeAssistant.Agent.Workspace.sln`: the plugins build against these
sources and land in the app's `Plugins` folder.

`build/Publish.ps1` produces what the installer packs, bundled plugins included;
`build/Publish-MacOS.ps1` the macOS app bundle — on a Mac packed into the disk image; on Windows
into a zip with `prepare.sh`, which signs and installs the app on the Mac
(`unzip <zip> -d agent && sh agent/prepare.sh`).

## Releasing

| Tag        | Releases                                                    |
|------------|-------------------------------------------------------------|
| `v1.2.0`   | The agent: installer and macOS disk image, GitHub release, entry in the catalog |
| `sdk/v2.0.1` | The plugin SDK package on nuget.org                       |

## License

[MIT](LICENSE)
