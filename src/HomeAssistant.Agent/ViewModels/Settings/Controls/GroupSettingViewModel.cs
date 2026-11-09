using HomeAssistant.Agent.Plugin.Base.Config.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

public class GroupSettingViewModel : SettingControlViewModel
{
    public GroupSettingViewModel(GroupControlDescriptor descriptor, PluginToolOptionsProvider? options = null)
        : base(descriptor)
    {
        // Natural order. The WinForms host built groups back-to-front because
        // AntdUI's StackPanel laid children out in reverse; an ItemsControl
        // does not, and carrying that over would flip every group upside down.
        Children = descriptor.Controls
            .Select(control => SettingViewModelFactory.Create(control, options))
            .ToArray();
    }

    public IReadOnlyList<SettingControlViewModel> Children { get; }

    public override void ResetViewState()
    {
        foreach (var child in Children)
            child.ResetViewState();
    }
}