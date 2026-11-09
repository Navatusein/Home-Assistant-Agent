namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

public class SwitchControlDescriptor : BaseControl
{
    public SwitchControlDescriptor(string key, string label, string? description, bool defaultValue = false)
        : base(label, description)
    {
        Key = key;
        DefaultValue = defaultValue;
    }

    // Config property name this control edits (e.g. "AutoStart").
    public string Key { get; }

    public bool DefaultValue { get; }
}
