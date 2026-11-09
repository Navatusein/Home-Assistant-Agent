using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

// A list whose entries all share one group of controls. The whole list travels
// through the settings storage as a single string: a JSON array of objects
// keyed by the item control keys. That encoding is what the WinForms host wrote
// and what the plugin configs parse, so it must not change.
public partial class ComplexListSettingViewModel : SettingControlViewModel, ISettingValue
{
    private readonly ComplexListControlDescriptor _descriptor;
    private readonly PluginToolOptionsProvider? _options;

    public ComplexListSettingViewModel(ComplexListControlDescriptor descriptor,
        PluginToolOptionsProvider? options = null)
        : base(descriptor)
    {
        _descriptor = descriptor;
        _options = options;
        Key = descriptor.Key;

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public string Key { get; }

    public ObservableCollection<ComplexListItemViewModel> Items { get; } = [];

    // Shown on the collapsed card so the list is not a blind box.
    public string Summary => Items.Count == 1 ? "1 entry" : $"{Items.Count} entries";

    // The card is bound to this rather than expanding itself, so closing the
    // window can put every list back to collapsed.
    [ObservableProperty]
    private bool _isExpanded;

    public override void ResetViewState() => IsExpanded = false;

    // The list as JSON text; stored as the JSON itself (ToJson).
    public string Value
    {
        get => ToJson().ToJsonString();
        set
        {
            JsonNode? node = null;

            try
            {
                node = JsonNode.Parse(value);
            }
            catch (JsonException)
            {
                // Malformed — start with an empty list.
            }

            FromJson(node);
        }
    }

    // An array of objects keyed by the item control keys.
    public JsonNode ToJson() => new JsonArray(Items.Select(item => (JsonNode?) item.ToJson()).ToArray());

    public void FromJson(JsonNode? node)
    {
        Items.Clear();

        if (node is not JsonArray array)
            return;

        foreach (var entry in array)
        {
            if (entry is JsonObject values)
                Items.Add(CreateItem(values));
        }
    }

    [RelayCommand]
    private void Add() => Items.Add(CreateItem(null));

    [RelayCommand]
    private void Remove(ComplexListItemViewModel? item)
    {
        if (item != null)
            Items.Remove(item);
    }

    private ComplexListItemViewModel CreateItem(JsonObject? values)
    {
        var item = new ComplexListItemViewModel(
            _descriptor.ItemControls
                .Select(control => SettingViewModelFactory.Create(control, _options))
                .ToArray());

        if (values != null)
            item.ApplyValues(values);

        // A row added while the section is open must fill its selects too;
        // the provider caches, so this costs nothing after the first entry.
        _ = SettingValueCollector.LoadOptionsAsync(item.Controls);

        return item;
    }

}

public class ComplexListItemViewModel(IReadOnlyList<SettingControlViewModel> controls)
{
    public IReadOnlyList<SettingControlViewModel> Controls { get; } = controls;

    public JsonObject ToJson()
    {
        var values = new JsonObject();
        SettingValueCollector.Collect(Controls, values);
        return values;
    }

    public void ApplyValues(JsonObject values) => SettingValueCollector.Apply(Controls, values);
}