namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

// Describes a list setting whose entries all share one group of controls:
// ItemControls is the template for a single entry, the UI clones it per
// entry ("Add") and stores the whole list as one value under Key — a JSON
// array of objects keyed by the item control keys.
public class ComplexListControlDescriptor : BaseControl
{
    public ComplexListControlDescriptor(string key, string label, string? description, params BaseControl[] itemControls)
        : base(label, description)
    {
        Key = key;
        ItemControls = itemControls;
    }

    // Config property name the whole list is stored under (e.g. "Applications").
    public string Key { get; }

    public IReadOnlyList<BaseControl> ItemControls { get; }
}
