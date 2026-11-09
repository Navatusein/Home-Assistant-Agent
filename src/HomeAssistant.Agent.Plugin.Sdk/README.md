# HomeAssistant.Agent.Plugin.Sdk

Build plugins for [Home Assistant Agent](https://github.com/Navatusein/Home-Assistant-Agent) — the
Windows agent that exposes a PC to Home Assistant over MQTT.

## Start a plugin

```
dotnet new install HomeAssistant.Agent.Plugin.Templates
dotnet new ha-agent-plugin -n MyPlugin --code my-plugin
```

Or reference the SDK from any class library:

```xml
<PackageReference Include="HomeAssistant.Agent.Plugin.Sdk" Version="2.0.*" />
```

## What the package does

- Compiles your plugin against the plugin API (`HomeAssistant.Agent.Plugin.Base`,
  `HomeAssistant.Connector`). The agent provides these at run time, so they are never copied
  next to your plugin.
- Packs your own dependencies into the plugin dll: a plugin ships as **one file**.
- Writes the plugin's metadata into the dll, from these properties in your project file:

| Property            | Meaning                                  |
|---------------------|------------------------------------------|
| `PluginName`        | Display name                             |
| `PluginDescription` | One line on what the plugin does         |
| `PluginAuthor`      | Author or organization                   |
| `PluginHomepage`    | http(s) link to the plugin's page        |
| `PluginRepository`  | http(s) link to the source repository    |
| `PluginIcon`        | Path to a square PNG, 64×64 or larger    |

## Versions

The package version is the plugin API version. A plugin built with `2.0.x` runs on any agent
with plugin API `2.x` of the same or a newer minor version; the agent rejects it otherwise with
a clear message instead of failing at run time.
