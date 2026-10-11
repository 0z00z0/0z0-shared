using Xunit;

namespace ZeroZero.Tests;

internal static class Polling
{
    /// <summary>Returns once the condition holds, checking every 10 ms; fails the test when it
    /// still does not hold after <paramref name="within"/>.</summary>
    public static async Task WaitUntil(Func<bool> condition, TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10);
        }
    }
}
