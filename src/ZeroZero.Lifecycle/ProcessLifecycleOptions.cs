using ZeroZero.Primitives;

namespace ZeroZero.Lifecycle;

/// <summary>What the application supplies to the exit hook.</summary>
public sealed class ProcessLifecycleOptions
{
    /// <summary>Where the relaunch limiter keeps its file: the product's data folder, as
    /// <see cref="ProductDataPath.Root"/> gives it.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>What to start again. The running executable when null.</summary>
    public string? ExecutablePath { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;
}
