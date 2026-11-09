using StrEnum;

namespace HomeAssistant.Connector.Enums;

public class MqttAvailabilityEnum: StringEnum<MqttAvailabilityEnum>
{
    public static readonly MqttAvailabilityEnum Online = Define("online");
    public static readonly MqttAvailabilityEnum Offline = Define("offline");
}