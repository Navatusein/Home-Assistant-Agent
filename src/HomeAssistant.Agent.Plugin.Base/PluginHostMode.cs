namespace HomeAssistant.Agent.Plugin.Base;

// Which kind of host is running the plugin. The desktop tray app exposes the
// full entity set; the Windows service runs in session 0 before any user logs
// in, so plugins expose only the entities that work without an interactive
// desktop (see BasePlugin.GetServiceEntities).
public enum PluginHostMode
{
    Desktop,
    Service
}
