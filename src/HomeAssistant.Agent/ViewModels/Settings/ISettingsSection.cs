using HomeAssistant.Agent.Core.Settings;

namespace HomeAssistant.Agent.ViewModels.Settings;

// One entry in the settings navigation. A section owns one part of
// Settings.json (an application section, or a plugin's entry) and converts
// between the stored values and whatever it shows on screen. Sections describing the application itself (General,
// Windows Service) implement this with hand-written XAML; plugin sections are
// built from their ConfigDescriptor.
public interface ISettingsSection
{
    // "general", or the plugin code.
    string Name { get; }

    // Shown on the section's tab.
    string Title { get; }

    // Whether the section is currently blocking the save (invalid input).
    bool HasErrors { get; }

    void Load(AgentSettings settings);

    // Writes the section's part into settings, leaving the rest as it is.
    void Save(AgentSettings settings);

    // Sections that poll external state only do so while they are on screen.
    void OnActivated() { }

    void OnDeactivated() { }

    // Collapses whatever the user expanded. Sections live as long as the
    // application does, so without this a reopened window still shows the
    // cards the previous visit left open.
    void ResetViewState() { }
}