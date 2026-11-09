using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Tools;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

// Backs both select descriptors. They differ only in where the options come
// from — a fixed list in the descriptor, or a plugin tool asked at runtime —
// so the control, the template and the stored value are shared.
public partial class SelectSettingViewModel : SettingControlViewModel, ISettingValue
{
    private readonly PluginToolOptionsProvider? _provider;
    private readonly string? _optionsTool;

    public SelectSettingViewModel(SelectControlDescriptor descriptor)
        : base(descriptor)
    {
        Key = descriptor.Key;
        _value = descriptor.DefaultValue ?? string.Empty;

        foreach (var option in descriptor.Options)
            Options.Add(option);
    }

    public SelectSettingViewModel(DynamicSelectControlDescriptor descriptor, PluginToolOptionsProvider? provider)
        : base(descriptor)
    {
        Key = descriptor.Key;
        _value = descriptor.DefaultValue ?? string.Empty;
        _provider = provider;
        _optionsTool = descriptor.OptionsTool;
    }

    public string Key { get; }

    public ObservableCollection<PluginToolOption> Options { get; } = [];

    // Only a tool-backed list can be re-read; a fixed one has nothing to
    // refresh, so the button is hidden for it.
    public bool IsDynamic => _optionsTool != null;

    [ObservableProperty]
    private bool _isLoading;

    // The selected option's value. Bound through SelectedValue rather than
    // SelectedItem so the stored string stays the source of truth.
    //
    // Written by hand rather than generated because the binding pushes null
    // whenever the ComboBox has no matching item — emptying the list during a
    // refresh does exactly that — and the rest of the settings machinery
    // treats a value as a non-null string.
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    private string _value;

    public async Task LoadOptionsAsync()
    {
        if (_provider == null || _optionsTool == null || IsLoading)
        {
            // A fixed list still has to accept whatever was stored.
            KeepSelectable(Value);
            return;
        }

        IsLoading = true;

        try
        {
            var options = await _provider.GetAsync(_optionsTool);

            // Emptying the list drops the ComboBox's selection, and the binding
            // writes that back — so remember the value first and restore it
            // once the new list can actually hold it.
            var selected = Value;

            Options.Clear();

            foreach (var option in options)
                Options.Add(option);

            KeepSelectable(selected);

            Value = selected;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _provider?.Invalidate();

        await LoadOptionsAsync();
    }

    // A configured device that is currently switched off is not in the list any
    // more. Dropping it would silently clear a setting the user never touched,
    // so it is added back as an option and marked instead.
    private void KeepSelectable(string value)
    {
        if (value.Length == 0 || Options.Any(option => option.Value == value))
            return;

        Options.Insert(0, new PluginToolOption(value, $"{value} — not available"));
    }
}