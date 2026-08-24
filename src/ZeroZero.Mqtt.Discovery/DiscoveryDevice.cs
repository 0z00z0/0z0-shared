namespace ZeroZero.Mqtt.Discovery;

/// <summary>The device block every entity is grouped under. The identifiers and the display name
/// come from the identity in force at publish time, so this holds only what does not vary with the
/// machine.</summary>
/// <param name="ConfigurationUrl">Where the device is administered, if anywhere reachable. Omitted
/// from the document when null.</param>
/// <param name="SerialNumber">What tells two otherwise identical machines apart on the receiver's
/// device page. Omitted when null.</param>
/// <param name="HardwareVersion">The version of what the software runs on, as against
/// <paramref name="SoftwareVersion"/>. Omitted when null.</param>
public sealed record DiscoveryDevice(
    string Manufacturer,
    string Model,
    string SoftwareVersion,
    string? ConfigurationUrl = null,
    string? SerialNumber = null,
    string? HardwareVersion = null);

/// <summary>What produced the document. A receiver shows it against the device and uses it to tell
/// one publisher's entities from another's.</summary>
/// <param name="SupportUrl">Omitted from the document when null.</param>
public sealed record DiscoveryOrigin(
    string Name,
    string SoftwareVersion,
    string? SupportUrl = null);
