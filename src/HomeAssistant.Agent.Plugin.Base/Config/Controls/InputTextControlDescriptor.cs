using HomeAssistant.Agent.Plugin.Base.Enums;

namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

public class InputTextControlDescriptor : BaseControl
{
    public InputTextControlDescriptor(
        string key,
        string label,
        string? description,
        string? defaultValue = null,
        InputTextControlTypesEnum type = InputTextControlTypesEnum.Text): base(label, description)
    {
        Key = key;
        DefaultValue = defaultValue;
        Type = type;
    }

    // Config property name this control edits (e.g. "ClientId").
    public string Key { get; }

    public string? DefaultValue { get; }

    public InputTextControlTypesEnum Type { get; }
}
