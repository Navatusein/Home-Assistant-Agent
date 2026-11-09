using HomeAssistant.Agent.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HomeAssistant.Agent.Services;

// Which page the window shows. Pages are their view models (singletons, so a
// page keeps its state between visits); the window maps each one to its view.
//
// Resolves the pages lazily: the settings page navigates too (into a plugin's
// page and back), and taking the pages in the constructor would make that a
// circular dependency.
public sealed partial class NavigationService(IServiceProvider services) : ObservableObject
{
    [ObservableProperty]
    private object? _currentPage;

    public void Navigate<TPage>() where TPage : class => Navigate(typeof(TPage));

    public void Navigate(Type page)
    {
        var next = services.GetRequiredService(page);

        if (ReferenceEquals(next, CurrentPage))
            return;

        (CurrentPage as INavigationAware)?.OnNavigatedFrom();

        CurrentPage = next;

        (next as INavigationAware)?.OnNavigatedTo();
    }

    // The window was hidden: nothing is on screen, so the page stops whatever
    // it polls, and the next open starts from a fresh navigation.
    public void Clear()
    {
        (CurrentPage as INavigationAware)?.OnNavigatedFrom();
        CurrentPage = null;
    }
}
