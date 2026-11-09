namespace HomeAssistant.Agent.Core.Platform;

// Starts the desktop agent when the user signs in.
public interface IAutoStart
{
    void Apply(bool enabled);
}
