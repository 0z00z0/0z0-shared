namespace ZeroZero.Config.Sections;

/// <summary>Which top-level keys of the old document become members of which section.</summary>
/// <param name="Section">The section key the members land in.</param>
/// <param name="Keys">The old document's top-level keys that move into it. A key the old document
/// does not carry is simply absent from the result.</param>
public sealed record SettingsSectionMove(string Section, IReadOnlyList<string> Keys);
