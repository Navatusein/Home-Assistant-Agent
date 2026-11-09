using System.Text.Json;
using System.Text.Json.Nodes;
using HomeAssistant.Agent.Plugin.Base.Config;

namespace HomeAssistant.Agent.Tests;

// The values the settings form stores: strings, numbers, arrays of objects.
internal static class FormValues
{
    public static ConfigValues Of(params (string Key, object Value)[] values)
    {
        var json = new JsonObject();

        foreach (var (key, value) in values)
            json[key] = JsonSerializer.SerializeToNode(value);

        return new ConfigValues(json);
    }
}

public class ConfigValuesTests
{
    // The settings form stores a number field as JsonValue.Create(long); a
    // primitive JsonValue does not convert itself to another numeric type.
    [Fact]
    public void Reads_int_from_a_long_the_form_created()
    {
        var values = new ConfigValues(new System.Text.Json.Nodes.JsonObject
        {
            ["n"] = System.Text.Json.Nodes.JsonValue.Create(4L),
            ["d"] = System.Text.Json.Nodes.JsonValue.Create(4.0),
            ["big"] = System.Text.Json.Nodes.JsonValue.Create(long.MaxValue),
        });

        Assert.Equal(4, values.GetInt("n", 0));
        Assert.Equal(4, values.GetInt("d", 0));
        Assert.Equal(0, values.GetInt("big", 0));
    }

    [Fact]
    public void Reads_typed_values()
    {
        var values = FormValues.Of(("n", 5), ("text-n", "7"), ("s", "  x  "), ("blank", " "), ("on", true));

        Assert.Equal(5, values.GetInt("n", 0));
        Assert.Equal(7, values.GetInt("text-n", 0));
        Assert.Equal(3, values.GetInt("missing", 3));
        Assert.Equal(3, values.GetInt("s", 3));
        Assert.Equal("x", values.GetString("s"));
        Assert.Equal("5", values.GetString("n"));
        Assert.Null(values.GetString("blank"));
        Assert.True(values.GetBool("on", false));
        Assert.True(values.GetBool("missing", true));
        Assert.True(values.Contains("blank"));
        Assert.False(values.Contains("missing"));
        Assert.Null(values.GetList<string>("missing"));
        Assert.Null(values.GetList<string>("s"));
    }
}
