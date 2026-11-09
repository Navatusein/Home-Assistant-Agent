using HomeAssistant.Agent.Plugin.Base.Tools;

namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

// A drop-down with a fixed set of choices, known when the descriptor is
// written. For choices that only exist on the running machine use
// DynamicSelectControlDescriptor instead.
public class SelectControlDescriptor : BaseControl
{
    public SelectControlDescriptor(
        string key,
        string label,
        string? description,
        IReadOnlyList<PluginToolOption> options,
        string? defaultValue = null) : base(label, description)
    {
        Key = key;
        Options = options;
        DefaultValue = defaultValue;
    }

    // Convenience for the common case where the stored value is also what the
    // user should see.
    public SelectControlDescriptor(string key, string label, string? description, params string[] options)
        : this(key, label, description, options.Select(option => new PluginToolOption(option)).ToArray())
    {
    }

    // Config property name this control edits.
    public string Key { get; }

    public IReadOnlyList<PluginToolOption> Options { get; }

    public string? DefaultValue { get; }
}