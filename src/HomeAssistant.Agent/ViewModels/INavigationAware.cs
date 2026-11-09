namespace HomeAssistant.Agent.ViewModels;

// A page that wants to know when it is shown and when it is left.
public interface INavigationAware
{
    void OnNavigatedTo();

    void OnNavigatedFrom();
}
