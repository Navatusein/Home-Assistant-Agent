namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

// A drop-down whose options the plugin supplies at runtime through one of its
// registered tools (see PluginTool). For choices that are discovered on the
// machine rather than known when the descriptor is written — audio endpoints,
// for instance.
//
// The named tool must answer with PluginToolOptions.Serialize(...). The host
// asks for the options when the settings section is opened and offers a
// refresh, so a device plugged in meanwhile shows up without a restart.
public class DynamicSelectControlDescriptor : BaseControl
{
    public DynamicSelectControlDescriptor(
        string key,
        string label,
        string? description,
        string optionsTool,
        string? defaultValue = null) : base(label, description)
    {
        Key = key;
        OptionsTool = optionsTool;
        DefaultValue = defaultValue;
    }

    // Config property name this control edits (e.g. "DeviceName").
    public string Key { get; }

    // Name of the PluginTool that lists the options.
    public string OptionsTool { get; }

    public string? DefaultValue { get; }
}