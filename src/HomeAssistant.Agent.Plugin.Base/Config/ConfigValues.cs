using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Base.Config;

// The values of a plugin's settings form, as CreateConfig receives them: one
// JSON value per control key — a string for a text field, a number for a
// number field, true/false for a switch, an array of objects (keyed by the
// item control keys) for a list. Read-only.
public sealed class ConfigValues
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly JsonObject _values;

    public ConfigValues(JsonObject? values = null)
    {
        _values = values ?? new JsonObject();
    }

    // Whether the form has a value for the key at all.
    public bool Contains(string key) => _values[key] != null;

    // Trimmed; null when missing or blank, so "unset" has one representation.
    public string? GetString(string key)
    {
        var value = _values[key] switch
        {
            null => null,
            JsonValue node when node.GetValueKind() == JsonValueKind.String => node.GetValue<string>(),
            JsonValue node => node.ToJsonString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // A number field holds a number; what the user typed into it that is not
    // one is kept as text, and reads as the fallback.
    public int GetInt(string key, int fallback)
    {
        return _values[key] switch
        {
            // Through the JSON text: a value the form created wraps a long (or
            // whatever it was made from), and TryGetValue<int> does not convert.
            JsonValue node when node.GetValueKind() == JsonValueKind.Number
                && int.TryParse(node.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            JsonValue node when node.GetValueKind() == JsonValueKind.String
                && int.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };
    }

    public bool GetBool(string key, bool fallback)
    {
        return _values[key] switch
        {
            JsonValue node when node.GetValueKind() is JsonValueKind.True or JsonValueKind.False => node.GetValue<bool>(),
            _ => fallback,
        };
    }

    // A list setting (ComplexListControlDescriptor): the item control keys are
    // the model's property names. Returns null when the form has no list for
    // the key or it does not fit T; the caller decides what to keep then.
    public List<T>? GetList<T>(string key)
    {
        if (_values[key] is not JsonArray array)
            return null;

        try
        {
            return array.Deserialize<List<T>>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
