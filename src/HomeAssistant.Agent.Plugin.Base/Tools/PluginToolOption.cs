using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Base.Tools;

// One choice offered by a select control. Value is what ends up in the config;
// Label is only what the user sees and falls back to Value.
public sealed record PluginToolOption(string Value, string? Label = null)
{
    // Derived, so it is not part of the wire format — it would otherwise be
    // serialized as a third property and duplicate the whole list.
    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Value : Label!;
}

// The wire format between a plugin tool and a select control. Tools answer
// with a string, so the options travel as JSON; both sides go through here so
// the format stays one implementation instead of a convention described in
// prose and re-typed by every plugin.
public static class PluginToolOptions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(IEnumerable<PluginToolOption> options) =>
        JsonSerializer.Serialize(options, JsonOptions);

    public static string Serialize(IEnumerable<string> values) =>
        Serialize(values.Select(value => new PluginToolOption(value)));

    public static IReadOnlyList<PluginToolOption> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PluginToolOption>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // A tool that answers with something else is a plugin bug, but it
            // must not take the settings form down with it.
            return [];
        }
    }
}