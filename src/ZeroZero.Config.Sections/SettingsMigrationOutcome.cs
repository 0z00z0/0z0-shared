namespace ZeroZero.Config.Sections;

/// <summary>How a migration ended.</summary>
public enum SettingsMigrationOutcome
{
    /// <summary>The new document was written and read back with everything the old one carried.</summary>
    Migrated,

    /// <summary>The new document is already there. Nothing was read and nothing was written.</summary>
    TargetAlreadyExists,

    /// <summary>There is no old document, so there is nothing to carry across.</summary>
    SourceMissing,

    /// <summary>The old document could not be read. It may be perfectly intact, so nothing was
    /// written.</summary>
    SourceUnreadable,

    /// <summary>The old document is not a JSON object, so nothing can be read out of it without
    /// guessing.</summary>
    SourceNotADocument,

    /// <summary>The request is contradictory — the same path twice, or one key claimed by two
    /// sections.</summary>
    RequestRefused,

    /// <summary>The new document could not be written.</summary>
    WriteFailed,

    /// <summary>The new document was written, read back, and did not carry everything the old one
    /// did. It has been removed.</summary>
    NotProven,
}
