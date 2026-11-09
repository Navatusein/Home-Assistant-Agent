using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.Plugin.Base.Config;

// Declarative description of a configuration form: the UI walks the control
// tree to render inputs and maps values back to config keys.
public class ConfigDescriptor(params BaseControl[] controls)
{
    public IReadOnlyList<BaseControl> Controls { get; } = controls;
}