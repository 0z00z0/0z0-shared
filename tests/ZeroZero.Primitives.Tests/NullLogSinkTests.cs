using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Primitives.Tests;

/// <summary>The sink a component runs on when the host supplies none. It has no behaviour to
/// observe, which is the point; what is asserted is that it stays that way under the use a component
/// actually makes of it — an exception that is null, and several components writing at once.</summary>
public class NullLogSinkTests
{
    [Fact]
    public void EveryDefaultIsTheSameObject()
    {
        // Two components that were each given nothing hold the one instance, so a host can tell
        // "nothing wired" by identity and no sink is ever constructed on a consumer's behalf.
        Assert.Same(NullLogSink.Instance, NullLogSink.Instance);
        Assert.Empty(typeof(NullLogSink).GetConstructors());
    }

    [Fact]
    public void AnErrorWithNoExceptionIsAccepted()
    {
        // The module reports a refused publish with a message and no exception, so the sink must
        // take a null there rather than throw on the one path that was reporting a failure.
        var exception = Record.Exception(() => NullLogSink.Instance.Error("source", null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SeveralWritersAtOnceCostNothingAndChangeNothing()
    {
        ILogSink sink = NullLogSink.Instance;

        var writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                sink.Info($"line {i}");
                sink.Error("writer", new InvalidOperationException("nothing to record"));
            }
        }));

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(writers));

        Assert.Null(exception);
    }
}
