namespace ZeroZero.Mqtt;

/// <summary>A CONNACK reason code, numbered as MQTT 5 numbers them. The module's own declaration
/// rather than the client library's, so a reason code can be classified — and the classification
/// tested — without MQTTnet appearing in a signature.</summary>
public enum MqttConnackCode
{
    Success = 0x00,
    UnspecifiedError = 0x80,
    MalformedPacket = 0x81,
    ProtocolError = 0x82,
    ImplementationSpecificError = 0x83,
    UnsupportedProtocolVersion = 0x84,
    ClientIdentifierNotValid = 0x85,
    BadUserNameOrPassword = 0x86,
    NotAuthorised = 0x87,
    ServerUnavailable = 0x88,
    ServerBusy = 0x89,
    Banned = 0x8A,
    BadAuthenticationMethod = 0x8C,
    TopicNameInvalid = 0x90,
    PacketTooLarge = 0x95,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99,
    RetainNotSupported = 0x9A,
    QosNotSupported = 0x9B,
    UseAnotherServer = 0x9C,
    ServerMoved = 0x9D,
    ConnectionRateExceeded = 0x9F,
}

/// <summary>A PUBACK reason code, numbered as MQTT 5 numbers them. What a QoS 1 publish comes back
/// with, and the only thing that says whether the broker took the message.</summary>
public enum MqttPubackCode
{
    Success = 0x00,

    /// <summary>The broker took the message; nobody is subscribed to the topic. Delivery, not
    /// failure — a retained value is published so a later subscriber finds it.</summary>
    NoMatchingSubscribers = 0x10,

    UnspecifiedError = 0x80,
    ImplementationSpecificError = 0x83,
    NotAuthorised = 0x87,
    TopicNameInvalid = 0x90,
    PacketIdentifierInUse = 0x91,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99,
}

/// <summary>What a reason code means. Pure.</summary>
public static class MqttReason
{
    /// <summary>Whether a PUBACK says the broker took the message. Anything else — an ACL refusing
    /// the topic, a quota, a malformed payload — is a failure, and a value recorded as sent on the
    /// strength of one would leave the topic wrong until it happened to change again.</summary>
    public static bool Delivered(MqttPubackCode code) =>
        code is MqttPubackCode.Success or MqttPubackCode.NoMatchingSubscribers;
}
