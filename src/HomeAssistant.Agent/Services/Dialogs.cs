using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Ursa.Controls;

namespace HomeAssistant.Agent.Services;

// File pickers, confirmations and the clipboard, all owned by the main window.
//
// Static rather than injected: the setting controls that browse for a file
// are created by a factory from a plugin's descriptor, deep below anything
// the container builds.
public static class Dialogs
{
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task<bool> ConfirmAsync(string title, string message, bool warning = false)
    {
        if (Owner is not { } owner)
            return false;

        var result = await MessageBox.ShowAsync(owner, message, title,
            warning ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxButton.OKCancel);

        return result == MessageBoxResult.OK;
    }

    // filter: "Name|*.ext;*.ext2|All files|*.*", the format plugin descriptors use.
    public static async Task<string?> OpenFileAsync(string title, string? filter, string? startIn)
    {
        if (Owner?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = ParseFilter(filter),
            SuggestedStartLocation = await FolderAsync(storage, startIn),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> OpenFolderAsync(string title, string? startIn)
    {
        if (Owner?.StorageProvider is not { CanPickFolder: true } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await FolderAsync(storage, startIn),
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFileAsync(string title, string fileName, string typeName, string pattern, string? startIn)
    {
        if (Owner?.StorageProvider is not { CanSave: true } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = fileName,
            DefaultExtension = Path.GetExtension(pattern).TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern] }],
            SuggestedStartLocation = await FolderAsync(storage, startIn),
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    public static async Task<bool> SetClipboardTextAsync(string text)
    {
        if (Owner?.Clipboard is not { } clipboard)
            return false;

        await clipboard.SetTextAsync(text);
        return true;
    }

    private static async Task<IStorageFolder?> FolderAsync(IStorageProvider storage, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            return await storage.TryGetFolderFromPathAsync(path);
        }
        catch (Exception)
        {
            // Not a location the picker can start in — it picks its own.
            return null;
        }
    }

    private static List<FilePickerFileType>? ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i].Trim())
            {
                Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
        }

        return types.Count > 0 ? types : null;
    }
}
