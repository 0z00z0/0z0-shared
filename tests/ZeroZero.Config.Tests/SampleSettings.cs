namespace ZeroZero.Config.Tests;

/// <summary>A settings shape with one of everything the file has to survive: a flag, a string, a
/// number, an enum and a dictionary.</summary>
public sealed class SampleSettings
{
    public bool Enabled { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Retries { get; set; } = 3;
    public SampleMode Mode { get; set; } = SampleMode.Automatic;
    public Dictionary<string, bool> Groups { get; set; } = [];
}

public enum SampleMode
{
    Automatic,
    Warm,
    Cold,
}
