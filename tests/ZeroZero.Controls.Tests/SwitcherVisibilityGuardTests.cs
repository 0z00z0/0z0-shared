using System.Runtime.InteropServices;
using Xunit;
using ZeroZero.Controls.WinUI;
using ZeroZero.Primitives;

namespace ZeroZero.Controls.Tests;

public class SwitcherVisibilityGuardTests
{
    private sealed class Lines : ILogSink
    {
        public List<string> Written { get; } = [];
        public void Info(string message) => Written.Add(message);
        public void Error(string source, Exception? ex) => Written.Add($"{source}: {ex}");
    }

    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public void AnAcceptedCall_SetsTheValueAsked()
    {
        var guard = new SwitcherVisibilityGuard();
        bool? applied = null;

        Assert.True(guard.TrySet(value => applied = value, shown: false, new Lines()));
        Assert.False(applied);
    }

    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public void TheMissingTaskbarRefusal_IsAnsweredAndLoggedOnce()
    {
        var guard = new SwitcherVisibilityGuard();
        var log = new Lines();
        Action<bool> refused = _ => throw new NotImplementedException("SetShownInSwitchers failed.");

        Assert.False(guard.TrySet(refused, shown: false, log));
        Assert.False(guard.TrySet(refused, shown: false, log));
        Assert.Single(log.Written);
    }

    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public void AnyOtherFailure_Propagates()
    {
        var guard = new SwitcherVisibilityGuard();

        Assert.Throws<COMException>(() =>
            guard.TrySet(_ => throw new COMException("RO_E_CLOSED", unchecked((int)0x80000013)), shown: false, new Lines()));
    }
}
