namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

// One card of settings on screen: a run of controls, with the heading of the
// plugin group they belong to.
//
// A plugin's controls are a tree (groups in groups), but a card group cannot
// sit inside another one. So the tree is laid out flat, in order: the controls
// before a group make one card, the group's own controls the next one under
// its heading, and so on. The values still live in the tree — this only
// decides what is drawn together.
public sealed record SettingBlock(string? Header, string? Description, IReadOnlyList<SettingControlViewModel> Controls)
{
    public bool HasHeader => !string.IsNullOrWhiteSpace(Header);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public static IReadOnlyList<SettingBlock> From(IEnumerable<SettingControlViewModel> controls)
    {
        var blocks = new List<SettingBlock>();
        Add(blocks, controls, null, null);
        return blocks;
    }

    private static void Add(List<SettingBlock> blocks, IEnumerable<SettingControlViewModel> controls,
        string? header, string? description)
    {
        var run = new List<SettingControlViewModel>();

        // The heading goes on the first card drawn for the group, even when
        // the group opens with a nested group and has no controls of its own.
        void Flush(bool force)
        {
            if (run.Count == 0 && !(force && header != null))
                return;

            blocks.Add(new SettingBlock(header, description, run.ToArray()));
            run.Clear();
            header = null;
            description = null;
        }

        foreach (var control in controls)
        {
            if (control is GroupSettingViewModel group)
            {
                Flush(force: true);
                Add(blocks, group.Children, group.Label, group.Description);
            }
            else
            {
                run.Add(control);
            }
        }

        Flush(force: true);
    }
}
