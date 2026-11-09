# Home Assistant Agent

A Windows agent that exposes a PC to [Home Assistant](https://www.home-assistant.io/) over MQTT.
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

Download `HomeAssistantAgent-Setup-<version>.exe` from the
[releases](https://github.com/Navatusein/Home-Assistant-Agent/releases). Windows 10 2004 or
newer. The agent runs as a desktop app and a Windows service; it updates itself and its plugins.

## Repository

| Path                                   | What                                                    |
|----------------------------------------|---------------------------------------------------------|
| `src/HomeAssistant.Agent`              | Desktop app (Avalonia)                                  |
| `src/HomeAssistant.Agent.Service`      | Windows service                                         |
| `src/HomeAssistant.Agent.Core`         | Shared core: plugins, MQTT, settings, logging, updates |
| `src/HomeAssistant.Agent.Plugin.Base`  | Plugin API                                              |
| `src/HomeAssistant.Connector`          | Home Assistant MQTT entities                            |
| `src/HomeAssistant.Agent.Plugin.Sdk`   | NuGet package for plugin authors                        |
| `templates`                            | `dotnet new ha-agent-plugin`                            |
| `tools/PluginManifest`                 | Catalog entry for a plugin (used by releases)           |
| `build`, `installer`                   | Publish script and Inno Setup installer                 |

## Building

.NET 10 SDK on Windows.

```
dotnet build HomeAssistant.Agent.sln
dotnet test tests/HomeAssistant.Agent.Tests
```

To work on the plugins too, check out
[Home-Assistant-Agent-Plugins](https://github.com/Navatusein/Home-Assistant-Agent-Plugins) next to
this repository and open `HomeAssistant.Agent.Workspace.sln`: the plugins build against these
sources and land in the app's `Plugins` folder.

`build/Publish.ps1` produces what the installer packs, bundled plugins included.

## Releasing

| Tag        | Releases                                                    |
|------------|-------------------------------------------------------------|
| `v1.2.0`   | The agent: installer, GitHub release, entry in the catalog |
| `sdk/v2.0.1` | The plugin SDK package on nuget.org                       |

## License

[MIT](LICENSE)
