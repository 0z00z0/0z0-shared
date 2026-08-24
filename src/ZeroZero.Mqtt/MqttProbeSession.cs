namespace ZeroZero.Mqtt;

/// <summary>The one line under the Test connection button: the sweep as it happens, then the verdict.
/// Pure — it holds no task, no token source and no control, so the ordering rules it enforces are
/// testable without running a probe.</summary>
/// <remarks>
/// <para>Three rules, each of which is a defect if it is missing. The verdict is the last thing
/// rendered even when a progress report from the same run arrives after it, or a panel ends stuck on
/// "trying…" with the connection already live. A report from a superseded run is dropped, or a
/// cancelled probe overwrites the line of the one that replaced it. And busy-ness is a count of runs
/// that have started and not yet finished, so the spinner and the button are restored by arithmetic
/// rather than by an identity check that a cancellation has already invalidated.</para>
/// <para>The churn while a sweep runs is the point rather than noise: several seconds of probing have
/// no other visible evidence, so each candidate replaces the line and nothing is debounced.</para>
/// </remarks>
public sealed class MqttProbeSession
{
    private readonly MqttPanelText _text;
    private readonly HashSet<long> _live = [];
    private long _next;
    private long _current;
    private bool _settled;

    public MqttProbeSession(MqttPanelText? text = null) => _text = text ?? MqttPanelText.Default;

    /// <summary>What to render, or empty when there is nothing to say yet.</summary>
    public string Line { get; private set; } = "";

    /// <summary>Whether <see cref="Line"/> should be shown in the error colour.</summary>
    public bool IsFailure { get; private set; }

    public bool HasLine => Line.Length > 0;

    /// <summary>Whether any run has started and not yet finished — the spinner, and the disabled
    /// button. Derived, so no path can leave it stuck on.</summary>
    public bool Busy => _live.Count > 0;

    /// <summary>Begins a run and returns its token. Every later call about the run carries it, so a
    /// superseded run cannot write over its successor.</summary>
    public long Start()
    {
        long token = ++_next;
        _live.Add(token);
        _current = token;
        _settled = false;
        Line = _text.Strings.Get("TestRunning");
        IsFailure = false;
        return token;
    }

    /// <summary>One candidate's progress. Dropped once the run has settled, and dropped outright for
    /// a run that is no longer the current one.</summary>
    public void Report(long token, MqttSearchProgress progress)
    {
        if (token != _current || _settled) return;
        Line = _text.Describe(progress);
        IsFailure = false;
    }

    /// <summary>The run's verdict. Nothing from this run renders after it.</summary>
    public void Settle(long token, MqttProbeReport report)
    {
        if (token != _current) return;
        _settled = true;
        Line = _text.Describe(report);
        IsFailure = MqttPanelText.IsFailure(report);
    }

    /// <summary>Ends a run, whatever became of it. Called unconditionally, so a cancelled run with no
    /// successor cannot leave the spinner turning.</summary>
    public void Finish(long token) => _live.Remove(token);

    /// <summary>The answer for a request that never reached the network — a blank host, or anything
    /// else that stops a probe before it starts. Every invocation of the button reports something,
    /// rather than appearing to do nothing at all.</summary>
    public void Refuse(string reason)
    {
        _current = ++_next;
        _settled = true;
        Line = reason;
        IsFailure = false;
    }

    /// <summary>Drops everything in flight at once: nothing later renders, and the controls are free
    /// immediately rather than at the end of a probe budget nobody is waiting for. What a page being
    /// navigated away from calls.</summary>
    public void Abandon()
    {
        _live.Clear();
        _current = ++_next;
        _settled = true;
    }

    /// <summary>Clears the line because the values it was about have been retyped. Leaves any run in
    /// flight to be abandoned separately.</summary>
    public void Clear()
    {
        Line = "";
        IsFailure = false;
    }
}
