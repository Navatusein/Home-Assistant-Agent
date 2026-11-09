using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Services;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

public partial class FilePickerSettingViewModel : SettingControlViewModel, ISettingValue
{
    private readonly FilePickerControlDescriptor _descriptor;

    public FilePickerSettingViewModel(FilePickerControlDescriptor descriptor)
        : base(descriptor)
    {
        _descriptor = descriptor;
        Key = descriptor.Key;
        _value = descriptor.DefaultValue ?? string.Empty;
    }

    public string Key { get; }

    public bool IsFolder => _descriptor.Mode == FilePickerModeEnum.Folder;

    // The field stays editable: a path can be pasted, and network or
    // environment-variable paths are easier to type than to browse to.
    [ObservableProperty]
    private string _value = string.Empty;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var title = $"Select {Label}";

        var picked = IsFolder
            ? await Dialogs.OpenFolderAsync(title, InitialDirectory())
            : await Dialogs.OpenFileAsync(title, _descriptor.Filter, InitialDirectory());

        if (picked != null)
            Value = picked;
    }

    // Reopen where the current value points, so editing an entry does not
    // start over from the default location every time.
    private string InitialDirectory()
    {
        var current = Value.Trim().Trim('"');

        if (current.Length == 0)
            return string.Empty;

        try
        {
            if (Directory.Exists(current))
                return current;

            var directory = Path.GetDirectoryName(current);

            return Directory.Exists(directory) ? directory! : string.Empty;
        }
        catch (ArgumentException)
        {
            // Whatever is in the box is not a usable path — let the dialog
            // pick its own starting point.
            return string.Empty;
        }
    }
}