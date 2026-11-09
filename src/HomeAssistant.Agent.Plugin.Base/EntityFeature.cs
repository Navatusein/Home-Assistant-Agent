using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Base;

// A feature that is nothing but a group of entities: the plugin owns the
// runtime state (a connection that feeds them), the feature only lets the user
// switch the group off. The entities are read through a function, so a group
// rebuilt from the config (one sensor per configured device) stays current.
public sealed class EntityFeature : PluginFeature
{
    private readonly Func<IEntity[]> _entities;

    public EntityFeature(string code, string name, string description, PluginHostMode host,
        Func<IEntity[]> entities, bool enabledByDefault = true)
        : base(code, name, description, host, enabledByDefault)
    {
        _entities = entities;
    }

    public EntityFeature(string code, string name, string description, PluginHostMode host,
        params IEntity[] entities)
        : this(code, name, description, host, () => entities)
    {
    }

    public override IEntity[] GetEntities() => _entities();
}
