namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// One section as the lifecycle sees it: a tag, how to build its page, what to do on entering
/// and leaving it, and whether it is built once for the life of the window. Framework-free, so
/// the contract the window keeps can be held without the XAML runtime; the window maps each
/// <see cref="SettingsSection"/> onto one.
/// </summary>
internal sealed record SectionPlan<TPage>(
    string Tag,
    Func<TPage> Build,
    Action? Enter,
    Action? Leave,
    bool BuildOnce) where TPage : class;
