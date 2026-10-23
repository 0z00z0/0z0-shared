using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>What the application has going on that no installer may interrupt. Handed to both
/// update paths; never stops a download or an installer already running. Thread-safe.</summary>
public sealed class InstallHolds
{
    /// <summary>Asked first for <see cref="UpdateMessages.HoldUnreadable"/>, the reason where a
    /// hold's function throws or answers blank. Null asks nothing.</summary>
    public IStringLocalizer? Strings { get; init; }

    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    /// <summary>Holds every install until the returned object is disposed, which is idempotent.</summary>
    public IDisposable Hold(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Add(new Entry(this, reason, null));
    }

    /// <summary>Holds every install while <paramref name="reason"/> returns non-null; called at every ask.</summary>
    public IDisposable Hold(Func<string?> reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return Add(new Entry(this, null, reason));
    }

    /// <summary>The first hold that refuses, in registration order, or <see cref="InstallMoment.Now"/>.</summary>
    internal InstallMoment Ask(ILogSink log)
    {
        Entry[] entries;
        lock (_gate) entries = [.. _entries];

        // Outside the lock: an application's function may take its own locks or register a hold.
        foreach (Entry entry in entries)
        {
            if (entry.Read(log) is { } reason) return InstallMoment.NotNow(reason);
        }
        return InstallMoment.Now;
    }

    private Entry Add(Entry entry)
    {
        lock (_gate) _entries.Add(entry);
        return entry;
    }

    private void Remove(Entry entry)
    {
        lock (_gate) _entries.Remove(entry);
    }

    private sealed class Entry(InstallHolds owner, string? fixedReason, Func<string?>? read) : IDisposable
    {
        private int _reported;

        public string? Read(ILogSink log)
        {
            if (read is null) return fixedReason;
            try
            {
                // A blank reason still holds; it would only leave the person's sentence empty.
                string? reason = read();
                return reason is not null && string.IsNullOrWhiteSpace(reason) ? Unreadable : reason;
            }
            catch (Exception ex)
            {
                // Once per hold: a broken check asked every tick would otherwise fill the log.
                if (Interlocked.Exchange(ref _reported, 1) == 0) log.Error(nameof(InstallHolds), ex);
                return Unreadable;
            }
        }

        private string Unreadable => new UpdateMessages(owner.Strings).HoldUnreadable;

        public void Dispose() => owner.Remove(this);
    }
}
