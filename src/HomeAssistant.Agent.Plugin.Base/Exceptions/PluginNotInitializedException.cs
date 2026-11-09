namespace HomeAssistant.Agent.Plugin.Base.Exceptions;

public class PluginNotInitializedException: Exception
{
    public PluginNotInitializedException(string message) : base(message) {}
}