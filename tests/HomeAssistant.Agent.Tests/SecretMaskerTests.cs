using HomeAssistant.Agent.Core.Logging;

namespace HomeAssistant.Agent.Tests;

public class SecretMaskerTests
{
    [Theory]
    [InlineData("""{"args":{"access_token":"SECRET"}}""", """{"args":{"access_token":"***"}}""")]
    [InlineData("""{"Password": "SECRET", "user": "u"}""", """{"Password": "***", "user": "u"}""")]
    [InlineData("grant_type=refresh_token&refresh_token=SECRET&client_secret=SECRET2",
        "grant_type=refresh_token&refresh_token=***&client_secret=***")]
    public void Masks_secrets_in_text(string text, string expected)
    {
        Assert.Equal(expected, SecretMasker.MaskText(text));
    }

    [Theory]
    [InlineData("tokens: 5")]
    [InlineData("token_type is fine")]
    [InlineData("nothing to see here")]
    public void Leaves_ordinary_text_alone(string text)
    {
        Assert.Equal(text, SecretMasker.MaskText(text));
    }

    [Theory]
    [InlineData("MqttPassword", true)]
    [InlineData("ClientSecret", true)]
    [InlineData("api-token", true)]
    [InlineData("DeviceName", false)]
    public void Recognises_secret_names(string name, bool secret)
    {
        Assert.Equal(secret, SecretMasker.IsSecretName(name));
    }
}
