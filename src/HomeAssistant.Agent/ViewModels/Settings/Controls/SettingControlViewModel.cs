using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

// A plugin's ConfigDescriptor is a tree of BaseControl descriptors; this is the
// view model mirroring one node of it. The XAML picks a template per concrete
// type, and values travel through ISettingValue rather than through the visual
// tree the way the WinForms host did it.
public abstract class SettingControlViewModel : ObservableObject
{
    protected SettingControlViewModel(BaseControl descriptor)
    {
        Label = descriptor.Label;
        Description = descriptor.Description;
    }

    public string Label { get; }

    public string? Description { get; }

    // Lets the template hide the description line without a null converter.
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // Drops whatever is view-only state (an expanded card) so a control looks
    // the same on a reopened window as it did on a fresh one. Values are not
    // touched — those are reloaded by the section.
    public virtual void ResetViewState() { }
}

// Implemented by the controls that own a config key.
public interface ISettingValue
{
    string Key { get; }

    // What the form edits.
    string Value { get; set; }

    // How the value is stored and reaches the plugin: text as a string unless
    // the control has a type of its own (a number, true/false, a list).
    JsonNode? ToJson() => JsonValue.Create(Value);

    void FromJson(JsonNode? node)
    {
        if (node != null)
            Value = node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }
}