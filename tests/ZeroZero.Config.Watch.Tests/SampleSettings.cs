namespace ZeroZero.Config.Watch.Tests;

/// <summary>The settings shape the watcher tests edit: something worth reacting to, something that
/// is only where a window sits, and something nested.</summary>
public sealed class AppSettings
{
    public bool StartMinimised { get; set; }
    public string Broker { get; set; } = "localhost";
    public int Retries { get; set; } = 3;
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;
    public Placement Window { get; set; } = new();

    /// <summary>The property added after the cosmetic list was written. Nobody has classified it, so
    /// it counts — which is the whole point of the list naming what to skip rather than what to
    /// weigh.</summary>
    public string Nickname { get; set; } = string.Empty;
}

/// <summary>A nested object, so a cosmetic entry can name one value inside it or the whole of it.</summary>
public sealed class Placement
{
    public int Left { get; set; }
    public int Top { get; set; }
}

/// <summary>A shape that is not a JSON object, which nothing can be named inside of.</summary>
public sealed class NotAnObject : List<int>;
