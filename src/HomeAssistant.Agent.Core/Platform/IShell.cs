namespace HomeAssistant.Agent.Core.Platform;

// The system file manager.
public interface IShell
{
    void OpenFolder(string path);

    // Opens the folder holding the file with the file selected.
    void RevealFile(string path);
}
