using System.Text.Json;
using System.Text.Json.Nodes;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

public class InputTextSettingViewModel : SettingControlViewModel, ISettingValue
{
    private string _value;

    public InputTextSettingViewModel(InputTextControlDescriptor descriptor)
        : base(descriptor)
    {
        Key = descriptor.Key;
        IsPassword = descriptor.Type == InputTextControlTypesEnum.Password;
        IsNumeric = descriptor.Type == InputTextControlTypesEnum.Number;

        _value = descriptor.DefaultValue ?? string.Empty;
    }

    public string Key { get; }

    public bool IsPassword { get; }

    // BooleanToVisibilityConverter has no inverse, and one extra property beats
    // shipping a converter just to negate a flag.
    public bool IsText => !IsPassword;

    public bool IsNumeric { get; }

    public string Value
    {
        get => _value;
        set
        {
            // Number inputs only accept digits — the WinForms host enforced this
            // with a per-character filter, here it also covers pasting.
            var accepted = IsNumeric ? Digits(value) : value;

            // When the text was rejected the stored value does not change, so
            // SetProperty stays quiet and the box would keep showing what was
            // typed. Notify anyway to push the cleaned text back into it.
            if (!SetProperty(ref _value, accepted) && !string.Equals(accepted, value, StringComparison.Ordinal))
                OnPropertyChanged(nameof(Value));
        }
    }

    // A number field is stored as a number; left empty, it stores nothing
    // and the plugin uses its default.
    public JsonNode? ToJson()
    {
        if (!IsNumeric)
            return JsonValue.Create(Value);

        return long.TryParse(Value, out var number) ? JsonValue.Create(number) : null;
    }

    public void FromJson(JsonNode? node)
    {
        if (node != null)
            Value = node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }

    private static string Digits(string value) =>
        new(value.Where(char.IsAsciiDigit).ToArray());
}