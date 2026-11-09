using HomeAssistant.Agent.Plugin.Base.Enums;

namespace HomeAssistant.Agent.Plugin.Base.Config.Controls;

// A path setting: a text box the user can still type into, with a browse
// button next to it. The stored value is the plain path — the descriptor only
// says what may be picked, the host owns the dialog.
public class FilePickerControlDescriptor : BaseControl
{
    public FilePickerControlDescriptor(
        string key,
        string label,
        string? description,
        FilePickerModeEnum mode = FilePickerModeEnum.File,
        string? filter = null,
        string? defaultValue = null) : base(label, description)
    {
        Key = key;
        Mode = mode;
        Filter = filter;
        DefaultValue = defaultValue;
    }

    // Config property name this control edits (e.g. "Path").
    public string Key { get; }

    public FilePickerModeEnum Mode { get; }

    // Win32 dialog filter, e.g. "Applications|*.exe;*.lnk|All files|*.*".
    // Ignored when picking a folder; null means every file.
    public string? Filter { get; }

    public string? DefaultValue { get; }
}