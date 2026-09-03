namespace ZeroZero.Config.Sections.Tests;

/// <summary>The section a test writes: one of everything a settings file has to survive — a flag, a
/// string, a number, an enum and a dictionary.</summary>
public sealed class GeneralSection
{
    public bool StartMinimised { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Retries { get; set; } = 3;
    public SampleMode Mode { get; set; } = SampleMode.Automatic;
    public Dictionary<string, bool> Groups { get; set; } = [];
}

/// <summary>A section with one member, so a write of it adds nothing the file does not already
/// carry — which is what lets a test see whether a write happened at all.</summary>
public sealed class CounterSection
{
    public int Retries { get; set; } = 3;
}

/// <summary>A second section, so a test can prove that writing one leaves the other alone.</summary>
public sealed class GraphSection
{
    public string Span { get; set; } = "P1D";
    public int Points { get; set; } = 24;
}

/// <summary>A third, for the order a new section takes.</summary>
public sealed class WindowSection
{
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
}

public enum SampleMode
{
    Automatic,
    Warm,
    Cold,
}

/// <summary>The shape a migrated flat document leaves behind: a migration groups keys, it never
/// renames them, so the member names inside the new section are the old file's own keys.</summary>
public sealed class MigratedGraphSection
{
    public string GraphSpan { get; set; } = "P1D";
    public double ThresholdWarn { get; set; }
}

/// <summary>A type that serialises to an array rather than an object, which no section can be.</summary>
public sealed class NotASection : List<int>;
