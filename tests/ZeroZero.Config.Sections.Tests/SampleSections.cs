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

/// <summary>The graph section of an installed file, spelled the way that file spells it — including
/// the en-GB member name a binder would reach for in en-US.</summary>
public sealed class InstalledGraphSection
{
    public string GraphSpan { get; set; } = "P1D";
    public string GraphLineColouring { get; set; } = string.Empty;
    public bool ShowGrid { get; set; }
    public int PointsPerHour { get; set; }
}

/// <summary>The same section with the en-US spelling. A different word is not a different case, so
/// nothing can match it to the file's own member.</summary>
public sealed class AmericanGraphSection
{
    public string GraphSpan { get; set; } = "P1D";
    public string GraphLineColoring { get; set; } = string.Empty;
}

/// <summary>The lid-close section as an installed file spells it: a lower-case second letter, not the
/// initialism.</summary>
public sealed class InstalledLidCloseSection
{
    public int LidDelaySeconds { get; set; }
    public string LidDelaySavedAcAction { get; set; } = string.Empty;
    public string LidDelaySavedDcAction { get; set; } = string.Empty;
}

/// <summary>The same section with the initialism, which is the spelling a type is written with when
/// nobody has looked at the file.</summary>
public sealed class InitialismLidCloseSection
{
    public int LidDelaySeconds { get; set; }
    public string LidDelaySavedACAction { get; set; } = string.Empty;
    public string LidDelaySavedDCAction { get; set; } = string.Empty;
}
