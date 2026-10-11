namespace ZeroZero.Config.Sections;

/// <summary>What a migration did, and what it carried.</summary>
public sealed record SettingsMigrationResult(SettingsMigrationOutcome Outcome)
{
    /// <summary>Whether the new document is on disk and proven.</summary>
    public bool Migrated => Outcome == SettingsMigrationOutcome.Migrated;

    /// <summary>Every top-level key of the old document that landed in the new one, in the old
    /// document's order. <c>ConfigVersion</c> is not among them: it is the one value a migration
    /// replaces. A document's own <c>Version</c> or <c>version</c> key is a different word and is
    /// carried like any other, with <c>ConfigVersion</c> stamped ahead of it.</summary>
    public IReadOnlyList<string> Carried { get; init; } = [];

    /// <summary>Every comment the old document carried outside a value, in file order. None of them is
    /// in the new document: the migration writes no comment of its own, because a reader that
    /// disallows comments fails the file rather than degrading, and the person sees a settings file
    /// that has stopped working. They are named here, and the old document — never touched — still
    /// holds them.</summary>
    public IReadOnlyList<string> CommentsNotCarried { get; init; } = [];

    /// <summary>Every comment that sat inside a value, in file order. A value is carried as the bytes
    /// the old file held, so these are in the new document: preserved, not authored. An application
    /// whose reader disallows comments cannot read the new file while this list is non-empty.</summary>
    public IReadOnlyList<string> CommentsInsideValues { get; init; } = [];

    /// <summary>Why it stopped, where an exception is what stopped it.</summary>
    public Exception? Error { get; init; }

    /// <summary>What the read-back found missing, when the outcome is
    /// <see cref="SettingsMigrationOutcome.NotProven"/>.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];
}
