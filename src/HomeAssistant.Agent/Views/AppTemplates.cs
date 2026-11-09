using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace HomeAssistant.Agent.Views;

// The application-level templates (App.axaml) as one ItemTemplate.
//
// A SettingsCardGroup without an ItemTemplate wraps every item in a card of
// its own, so a setting control — which is a card already — ends up inside
// another one: indented, narrowed, with two separators. Handing the group an
// ItemTemplate that builds the card lets it use that card directly.
public sealed class AppTemplates : IDataTemplate
{
    public static AppTemplates Instance { get; } = new();

    public bool Match(object? data) => Find(data) != null;

    public Control? Build(object? param) => Find(param)?.Build(param);

    private static IDataTemplate? Find(object? data) =>
        Application.Current?.DataTemplates.FirstOrDefault(template => template.Match(data));
}
