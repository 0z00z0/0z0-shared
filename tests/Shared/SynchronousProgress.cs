namespace ZeroZero.Tests;

/// <summary>Reports on the caller's thread, so the last report has landed when the reporting call
/// returns. <see cref="Progress{T}"/> posts instead, and may land after the test has asserted.</summary>
internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
