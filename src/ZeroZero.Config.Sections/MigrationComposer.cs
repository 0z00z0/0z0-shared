using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>Composes the new document from the old one's bytes, in the order the plan gives.</summary>
internal static class MigrationComposer
{
    internal static byte[] Compose(byte[] source, JsonLayout layout, MigrationPlan plan, int version)
    {
        var text = new StringBuilder();

        text.Append('{').Append(layout.NewLine)
            .Append(layout.Unit).Append('"').Append(SettingsDocument.VersionKey).Append("\": ")
            .Append(version.ToString(CultureInfo.InvariantCulture));

        foreach (var entry in plan.Entries)
        {
            text.Append(',').Append(layout.NewLine);
            Append(text, source, layout, entry, layout.Unit);
        }

        text.Append(layout.NewLine).Append('}').Append(layout.NewLine);

        var body = Encoding.UTF8.GetBytes(text.ToString());
        return layout.ByteOrderMark ? [.. Encoding.UTF8.GetPreamble(), .. body] : body;
    }

    private static void Append(
        StringBuilder text,
        byte[] source,
        JsonLayout layout,
        MigrationPlanEntry entry,
        string indent)
    {
        if (entry.Section is { } section)
        {
            text.Append(indent).Append('"').Append(JsonEncodedText.Encode(section)).Append("\": {");

            var inner = indent + layout.Unit;
            var first = true;
            foreach (var member in entry.Members)
            {
                if (!first) text.Append(',');
                text.Append(layout.NewLine);
                first = false;
                Append(text, source, layout, new MigrationPlanEntry(null, [member]), inner);
            }

            text.Append(layout.NewLine).Append(indent).Append('}');
            return;
        }

        var only = entry.Members[0];
        var value = layout.Shift(source.AsSpan(only.ValueStart..only.ValueEnd), layout.ExtraIndent(indent));

        // The name is carried as the file's own bytes, colon included, so a key holding an escape
        // sequence is written back exactly as it was rather than escaped a second time.
        text.Append(indent)
            .Append(Encoding.UTF8.GetString(source.AsSpan(only.NameStart..only.NameEnd)))
            .Append(' ')
            .Append(Encoding.UTF8.GetString(value));
    }
}
