namespace HomeAssistant.Connector.Mqtt;

[Flags]
public enum MqttSubscribeFlags
{
    None = 0,

    // A retained message is replayed on every (re)subscribe, so a one-shot
    // command left retained by someone would run again on each start. Such a
    // message is dropped and cleared from the broker.
    IgnoreRetained = 1,

    // The payload is user content (clipboard text) and is not written to the
    // log — only its length is.
    PrivatePayload = 2,
}
