namespace ZeroZero.Config.Sections;

/// <summary>Writes a new settings document from an old one. The old file is opened to read and nothing
/// else: it is never written, renamed or deleted, not even on success. What is carried, refused and
/// proven: docs/zerozero-config.md.</summary>
public static class SettingsMigration
{
    /// <summary>Runs one migration. Never throws for anything the file system or the documents can
    /// do; the outcome carries the reason.</summary>
    public static SettingsMigrationResult Run(SettingsMigrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetPath);

        if (Refuse(request) is { } refusal) return refusal;

        if (File.Exists(request.TargetPath))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.TargetAlreadyExists);
        }

        if (!File.Exists(request.SourcePath))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceMissing);
        }

        byte[] source;
        try
        {
            // Read-only, and shared, so a migration cannot be what stops the application it is
            // migrating from reading its own settings.
            using var stream = new FileStream(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            source = new byte[stream.Length];
            stream.ReadExactly(source);
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceUnreadable) { Error = ex };
        }

        if (JsonObjectSpans.TryReadDocument(source) is not { } root)
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceNotADocument);
        }

        if (RefuseAgainst(request, root) is { } mismatch) return mismatch;

        var layout = JsonLayout.Detect(source);
        var plan = MigrationPlan.Build(root, request.Moves);

        var target = MigrationComposer.Compose(source, layout, plan, request.Version);

        if (AtomicFile.Write(request.TargetPath, target) is { } failure)
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.WriteFailed) { Error = failure };
        }

        return MigrationProof.ProveTarget(request, source);
    }

    // What the request and the document say together, which cannot be checked until the document has
    // been read. Both refusals exist because a key differing from another only in case leaves a reader
    // taking the last of the two and the other one governing nothing.
    private static SettingsMigrationResult? RefuseAgainst(SettingsMigrationRequest request, JsonObjectSpan root)
    {
        foreach (var move in request.Moves)
        {
            if (root.Find(move.Section, StringComparison.OrdinalIgnoreCase) is not { } clash) continue;

            return Contradiction(string.Equals(clash.Name, move.Section, StringComparison.Ordinal)
                ? $"The old document already carries a top-level key '{move.Section}', so moving keys into a section of that name would write it twice."
                : $"The old document carries a top-level key '{clash.Name}', which differs from the section '{move.Section}' only in case, so the new document would carry both and a reader would take the last.");
        }

        // A move naming a key the document does not carry at all is ordinary — the application may
        // describe more keys than a given file holds. A move naming one the document carries under a
        // different case is a mistake, and carrying on would leave the key at the top level with
        // nothing to say why.
        foreach (var key in request.Moves.SelectMany(static move => move.Keys))
        {
            if (root.Find(key, StringComparison.Ordinal) is not null) continue;
            if (root.Find(key, StringComparison.OrdinalIgnoreCase) is not { } clash) continue;

            return Contradiction(
                $"The move names the key '{key}' and the old document carries '{clash.Name}', which differs from it only in case, so the key would silently stay where it is.");
        }

        return null;
    }

    private static SettingsMigrationResult Contradiction(string message) =>
        new(SettingsMigrationOutcome.RequestRefused) { Error = new ArgumentException(message) };

    private static SettingsMigrationResult? Refuse(SettingsMigrationRequest request)
    {
        if (string.Equals(
                Path.GetFullPath(request.SourcePath),
                Path.GetFullPath(request.TargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.RequestRefused)
            {
                Error = new ArgumentException(
                    "The old and the new document are the same file, so the old one could not be left alone.",
                    nameof(request)),
            };
        }

        // Ignoring case throughout: two names that differ only in case cannot both govern a document,
        // so a request asking for both is as contradictory as one asking for the same name twice.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var move in request.Moves)
        {
            if (!sections.Add(move.Section))
            {
                return Contradiction($"Section '{move.Section}' is named by two moves.");
            }

            foreach (var key in move.Keys)
            {
                if (!claimed.Add(key)) return Contradiction($"Key '{key}' is claimed by two sections.");
            }
        }

        foreach (var section in sections)
        {
            if (claimed.Contains(section))
            {
                return Contradiction($"Key '{section}' is both a section and a key moved into one.");
            }
        }

        return null;
    }
}
