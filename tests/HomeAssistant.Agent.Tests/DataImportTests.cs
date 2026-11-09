using HomeAssistant.Agent.Core.Services;

namespace HomeAssistant.Agent.Tests;

public class DataImportTests
{
    [Fact]
    public void Copies_the_desktop_data_and_leaves_the_rest()
    {
        using var source = new TempDirectory();
        using var target = new TempDirectory();

        File.WriteAllText(source.File("Settings.json"), "{}");
        File.WriteAllText(source.File("PersistentStorage.json"), "{}");
        File.WriteAllText(source.File("PersistentStorage.Service.json"), "{}");
        File.WriteAllText(source.File("Discovery.my-pc.json"), "[]");
        File.WriteAllText(source.File("HomeAssistant.Agent.dll"), "");

        var copied = DataImport.Import(source.Path, target.Path);

        Assert.Equal(["Discovery.my-pc.json", "PersistentStorage.json", "Settings.json"], copied.Order());
        Assert.False(File.Exists(target.File("PersistentStorage.Service.json")));
        Assert.False(File.Exists(target.File("HomeAssistant.Agent.dll")));
    }

    [Fact]
    public void Refuses_a_folder_without_settings()
    {
        using var source = new TempDirectory();
        using var target = new TempDirectory();

        Assert.False(DataImport.ContainsData(source.Path));
        Assert.Throws<InvalidOperationException>(() => DataImport.Import(source.Path, target.Path));
    }

    [Fact]
    public void Refuses_its_own_data_folder()
    {
        using var folder = new TempDirectory();
        File.WriteAllText(folder.File("Settings.json"), "{}");

        Assert.Throws<InvalidOperationException>(() => DataImport.Import(folder.Path, folder.Path + "\\"));
    }
}
