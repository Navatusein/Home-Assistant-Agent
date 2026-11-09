namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

public class GroupControlDescriptor : BaseControl
{
    public GroupControlDescriptor(string label, string? description, params BaseControl[] controls)
        : base(label, description)
    {
        Controls = controls;
    }

    public IReadOnlyList<BaseControl> Controls { get; }
}
