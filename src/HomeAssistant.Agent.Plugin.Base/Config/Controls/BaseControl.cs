namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

public abstract class BaseControl
{
    protected BaseControl(string label, string? description)
    {
        Label = label;
        Description = description;
    }

    public string Label { get; }

    // Line under the label explaining what the setting is; null renders the
    // control without one. Required rather than optional on purpose: the value
    // sits next to other string parameters (a text control's default value),
    // and an optional one would let an existing positional argument slide into
    // it without a compiler error.
    public string? Description { get; }
}
