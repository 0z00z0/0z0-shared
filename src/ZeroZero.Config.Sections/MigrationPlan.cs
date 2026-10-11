namespace ZeroZero.Config.Sections;

internal readonly record struct MigrationPlanEntry(string? Section, IReadOnlyList<JsonMemberSpan> Members);

// What the new document's top level holds, in the old document's own order: a section takes the
// place of the first key that moves into it, and a key that moves nowhere stays where it was.
internal sealed class MigrationPlan
{
    private readonly Dictionary<string, string> _into;

    private MigrationPlan(List<MigrationPlanEntry> entries, Dictionary<string, string> into)
    {
        Entries = entries;
        _into = into;
    }

    internal List<MigrationPlanEntry> Entries { get; }

    internal static MigrationPlan Build(JsonObjectSpan root, IReadOnlyList<SettingsSectionMove> moves)
    {
        var into = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var move in moves)
        {
            foreach (var key in move.Keys) into[key] = move.Section;
        }

        var entries = new List<MigrationPlanEntry>();
        var sections = new Dictionary<string, List<JsonMemberSpan>>(StringComparer.Ordinal);

        foreach (var member in root.Members)
        {
            if (member.Name == SettingsDocument.VersionKey) continue;

            if (!into.TryGetValue(member.Name, out var section))
            {
                entries.Add(new MigrationPlanEntry(null, [member]));
                continue;
            }

            if (!sections.TryGetValue(section, out var members))
            {
                members = [];
                sections[section] = members;
                entries.Add(new MigrationPlanEntry(section, members));
            }

            members.Add(member);
        }

        return new MigrationPlan(entries, into);
    }

    internal string? SectionOf(string key) => _into.GetValueOrDefault(key);
}
