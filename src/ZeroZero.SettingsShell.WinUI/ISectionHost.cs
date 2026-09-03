namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// Where the lifecycle puts pages. A page is added hidden and shown or hidden in place, so a
/// page that leaves the screen keeps its state and returns as it was; a removed page is gone for
/// good and its replacement is added afresh.
/// </summary>
internal interface ISectionHost<TPage> where TPage : class
{
    /// <summary>Takes a newly built page, hidden.</summary>
    void Add(TPage page);

    /// <summary>Discards a page that has been rebuilt.</summary>
    void Remove(TPage page);

    void Show(TPage page);

    void Hide(TPage page);
}
