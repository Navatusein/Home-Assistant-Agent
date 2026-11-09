# MyPlugin

A plugin for [Home Assistant Agent](https://github.com/Navatusein/Home-Assistant-Agent).

## Build

```
dotnet build -c Release
```

The plugin is `bin/Release/<tfm>/MyPlugin.dll` — one file, its dependencies are packed into it.
To try it, copy it into the agent's `Plugins` folder and restart the agent.

## Release

Push a tag:

```
git tag v1.0.0
git push origin v1.0.0
```

The release workflow builds the plugin, attaches it to a GitHub release and adds it to the
catalog of this repository. Users add the catalog to the agent once:

```
https://github.com/<you>/<repo>/releases/download/index/index.json
```

## Plugin API

- **Features** (`AddFeature`): groups of entities the user switches on and off, each for the
  desktop app or the Windows service. `EntityFeature` for a plain group; derive from
  `PluginFeature` for one with runtime state of its own.
- **Entities** (`HomeAssistant.Connector.Entities`): switch, number, select, sensor,
  binary sensor, button.
- **Settings** (`ConfigDescriptor`, `CreateConfig`): the form the agent renders, and the typed
  config built from its values.
- **Context** (from `OnInitialized` on): `CreateLogger<T>()`, `Storage` for state that survives
  a restart, `HostMode`.
