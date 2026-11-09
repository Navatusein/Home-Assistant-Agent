using HomeAssistant.Agent.Plugin.Base;

namespace HomeAssistant.Agent.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("My Laptop", "my_laptop")]
    [InlineData("  My   Laptop  ", "my_laptop")]
    [InlineData("OBS — Studio 30!", "obs_studio_30")]
    [InlineData("Телеграм", "telegram")]
    [InlineData("Ёлка", "yolka")]
    [InlineData("Їжак і Ґанок", "yizhak_i_ganok")]
    [InlineData("Щука", "shchuka")]
    public void Transliterates_and_collapses(string value, string expected)
    {
        Assert.Equal(expected, Slug.ToObjectId(value));
    }

    [Fact]
    public void Uses_the_given_separator()
    {
        Assert.Equal("my-laptop", Slug.ToObjectId("My Laptop", '-'));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_names_get_the_fallback(string? value)
    {
        Assert.Equal("device", Slug.ToObjectId(value, fallback: "device"));
    }

    [Fact]
    public void Names_without_usable_characters_get_a_stable_distinct_hash()
    {
        var first = Slug.ToObjectId("🎮🎧");

        Assert.Matches("^x[0-9a-f]{8}$", first);
        Assert.Equal(first, Slug.ToObjectId("🎮🎧"));
        Assert.NotEqual(first, Slug.ToObjectId("🎵"));
    }

    [Fact]
    public void Output_is_always_valid_for_home_assistant_topics()
    {
        foreach (var name in new[] { "Привет, мир!", "a/b+c#d", "日本語", "x" })
            Assert.Matches("^[a-z0-9_]+$", Slug.ToObjectId(name));
    }
}
