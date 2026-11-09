using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

public partial class SwitchSettingViewModel : SettingControlViewModel, ISettingValue
{
    public SwitchSettingViewModel(SwitchControlDescriptor descriptor)
        : base(descriptor)
    {
        Key = descriptor.Key;
        _isChecked = descriptor.DefaultValue;
    }

    public string Key { get; }

    [ObservableProperty]
    private bool _isChecked;

    public string Value
    {
        get => IsChecked.ToString();
        set => IsChecked = bool.TryParse(value, out var isChecked) && isChecked;
    }

    // Stored as true/false. Text is still read: settings saved before values
    // had types wrote "True"/"False".
    public JsonNode? ToJson() => JsonValue.Create(IsChecked);

    public void FromJson(JsonNode? node)
    {
        if (node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            IsChecked = node.GetValue<bool>();
        else if (node?.GetValueKind() == JsonValueKind.String)
            Value = node.GetValue<string>();
    }
}
