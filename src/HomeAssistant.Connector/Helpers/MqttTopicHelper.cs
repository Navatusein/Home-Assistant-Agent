using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Helpers;

public static class MqttTopicHelper
{
    public static string NormalizeTopic(string topic)
    {
        topic = topic.Replace('.', '/');

        if (topic.StartsWith('/'))
            topic = topic.Remove(0, 1);
        
        return topic;
    }

    public static string GetDiscoveryTopic(string deviceTopic, MqttDiscoveryConfig config)
    {
        return NormalizeTopic($"homeassistant/{config.Component}/{deviceTopic}/{config.UniqueId}/config");
    }
    
    public static string GetDiscoveryTopicFilter(string deviceTopic)
    {
        return NormalizeTopic($"homeassistant/+/{deviceTopic}/+/config");
    }
    
    public static string GetAvailabilityTopic(string rootTopic, string deviceTopic)
    {
        return NormalizeTopic($"{rootTopic}/{deviceTopic}/availability");
    }

    public static string GetDeviceBaseTopic(string rootTopic, string deviceTopic)
    {
        return NormalizeTopic($"{rootTopic}/{deviceTopic}");
    }
}