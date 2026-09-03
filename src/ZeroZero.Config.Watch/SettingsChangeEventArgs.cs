namespace ZeroZero.Config.Watch;

/// <summary>What one examination of the file found.</summary>
/// <typeparam name="T">The settings shape.</typeparam>
/// <param name="question">What the classifier's answer means to the application asking.</param>
/// <param name="before">The state held before the file was re-read.</param>
/// <param name="after">The state held after it.</param>
/// <param name="isSubstantive">Whether the difference is one the question cares about.</param>
public sealed class SettingsChangeEventArgs<T>(string question, T before, T after, bool isSubstantive)
    : EventArgs
    where T : class
{
    /// <summary>What the answer means to the application asking.</summary>
    public string Question { get; } = question;

    /// <summary>The state held before the file was re-read.</summary>
    public T Before { get; } = before;

    /// <summary>The state held after it.</summary>
    public T After { get; } = after;

    /// <summary>Whether the difference is one the question cares about. False both when the file
    /// turned out to hold what was already held — which is what the application's own write looks
    /// like — and when everything that moved was named cosmetic.</summary>
    public bool IsSubstantive { get; } = isSubstantive;
}
