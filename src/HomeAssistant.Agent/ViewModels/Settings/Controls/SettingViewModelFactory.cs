using System.Text.Json.Nodes;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

public static class SettingViewModelFactory
{
    // Closed over the descriptor types in Plugin.Base — the public contract
    // between a plugin and the host. Controls belonging to the application
    // itself are hand-written XAML instead and never come through here.
    //
    // The options provider is only needed by selects, but it has to be handed
    // down the whole tree: a select can sit inside a group or a list entry.
    public static SettingControlViewModel Create(BaseControl descriptor, PluginToolOptionsProvider? options = null)
        => descriptor switch
        {
            GroupControlDescriptor group => new GroupSettingViewModel(group, options),
            InputTextControlDescriptor input => new InputTextSettingViewModel(input),
            FilePickerControlDescriptor picker => new FilePickerSettingViewModel(picker),
            SelectControlDescriptor select => new SelectSettingViewModel(select),
            DynamicSelectControlDescriptor select => new SelectSettingViewModel(select, options),
            SwitchControlDescriptor toggle => new SwitchSettingViewModel(toggle),
            ComplexListControlDescriptor complexList => new ComplexListSettingViewModel(complexList, options),
            _ => throw new NotSupportedException($"No view model for control type {descriptor.GetType().Name}.")
        };
}

// Walks the view-model tree instead of the visual tree, so values survive
// controls that are scrolled out of view or sitting in a collapsed card.
public static class SettingValueCollector
{
    public static void Collect(IEnumerable<SettingControlViewModel> controls, JsonObject values)
    {
        foreach (var value in GetValues(controls))
            values[value.Key] = value.ToJson();
    }

    private static IEnumerable<ISettingValue> GetValues(IEnumerable<SettingControlViewModel> controls)
    {
        foreach (var control in controls)
        {
            switch (control)
            {
                // A list owns its entries' values and serializes them itself,
                // so its children are not visited separately.
                case ISettingValue value:
                    yield return value;
                    break;

                case GroupSettingViewModel group:
                    foreach (var child in GetValues(group.Children))
                        yield return child;
                    break;
            }
        }
    }

    public static void Apply(IEnumerable<SettingControlViewModel> controls, JsonObject values)
    {
        foreach (var target in GetValues(controls))
        {
            if (values.TryGetPropertyValue(target.Key, out var value))
                target.FromJson(value);
        }
    }

    // Selects only know their choices once the plugin has been asked, which is
    // deferred until the section is actually opened.
    public static async Task LoadOptionsAsync(IEnumerable<SettingControlViewModel> controls)
    {
        foreach (var control in controls)
        {
            switch (control)
            {
                case SelectSettingViewModel select:
                    await select.LoadOptionsAsync();
                    break;

                case GroupSettingViewModel group:
                    await LoadOptionsAsync(group.Children);
                    break;

                case ComplexListSettingViewModel list:
                    foreach (var item in list.Items)
                        await LoadOptionsAsync(item.Controls);
                    break;
            }
        }
    }
}