namespace ZeroZero.Lifecycle.Tests;

/// <summary>A clock the test moves, so a ten-minute window costs nothing to cross.</summary>
internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
