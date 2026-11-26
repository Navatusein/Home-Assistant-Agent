using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

// A picture in Home Assistant (MQTT image platform), such as the cover of what
// is playing. The bytes go base64-encoded: payloads stay text like every other
// topic of the agent. There is no default state — the feature publishes the
// first picture itself; until then Home Assistant keeps the retained one.
public class ImageEntity: BaseEntity
{
    // What Home Assistant serves the picture as; every picture published to
    // this entity must be of this type.
    public string ContentType { get; init; } = "image/jpeg";

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttImageDiscoveryConfig>();

        config.ImageTopic = GetStateTopic();
        config.ImageEncoding = "b64";
        config.ContentType = ContentType;

        return config;
    }

    // Retained so Home Assistant shows the picture again after it restarts.
    // An empty payload is no picture to Home Assistant — a broken image — so
    // "nothing to show" has to be a picture as well (a transparent one).
    public async Task<bool> SetImageAsync(ReadOnlyMemory<byte> image)
    {
        if (image.IsEmpty)
            throw new ArgumentException("An image cannot be empty.", nameof(image));

        return await MqttClient.PublishAsync(GetStateTopic(), Convert.ToBase64String(image.Span), retain: true);
    }
}
