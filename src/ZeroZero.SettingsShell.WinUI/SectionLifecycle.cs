namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// The two contract points the plan measures the settings window on, held in one place: the
/// enter and leave hooks around every change of section, and the per-section build-once flag
/// that a rebuild honours. Framework-free — pages are whatever the host says they are — so the
/// order of every call can be pinned by a test with no XAML runtime.
/// </summary>
/// <remarks>
/// Every page is built up front by <see cref="BuildAll"/>, the way both applications build
/// theirs, and stays in the host hidden while another is current. Leaving a section hides its
/// page and never discards it, so a staged edit survives a visit elsewhere. Only
/// <see cref="Rebuild()"/> discards, and it leaves a build-once section alone; asking for that
/// section by name is refused rather than obeyed, because the flag exists to protect a page whose
/// state a rebuild would throw away.
/// </remarks>
internal sealed class SectionLifecycle<TPage> where TPage : class
{
    private readonly IReadOnlyList<SectionPlan<TPage>> _plans;
    private readonly Dictionary<string, SectionPlan<TPage>> _byTag = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TPage> _pages = new(StringComparer.Ordinal);
    private readonly ISectionHost<TPage> _host;

    public SectionLifecycle(IReadOnlyList<SectionPlan<TPage>> plans, ISectionHost<TPage> host)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(host);
        if (plans.Count == 0)
            throw new ArgumentException("A settings window needs at least one section.", nameof(plans));

        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(plan.Tag))
                throw new ArgumentException("Every section needs a tag.", nameof(plans));
            if (plan.Build is null)
                throw new ArgumentException($"Section '{plan.Tag}' has no build function.", nameof(plans));
            if (!_byTag.TryAdd(plan.Tag, plan))
                throw new ArgumentException($"Two sections are tagged '{plan.Tag}'.", nameof(plans));
        }

        _plans = plans;
        _host = host;
    }

    /// <summary>The tags, in the order the sections were declared.</summary>
    public IEnumerable<string> Tags => _plans.Select(p => p.Tag);

    /// <summary>The section on screen, or null before the first selection and after
    /// <see cref="Close"/>.</summary>
    public string? Current { get; private set; }

    /// <summary>The pages built so far, in declaration order.</summary>
    public IEnumerable<TPage> Pages => _plans.Where(p => _pages.ContainsKey(p.Tag)).Select(p => _pages[p.Tag]);

    public bool Contains(string tag) => _byTag.ContainsKey(tag);

    /// <summary>Builds every section that has no page yet. Building is the application's code,
    /// and anything it throws comes straight back.</summary>
    public void BuildAll()
    {
        foreach (var plan in _plans)
            if (!_pages.ContainsKey(plan.Tag)) Build(plan);
    }

    /// <summary>
    /// Makes a section current: the old section's leave hook, then its page hidden, then the new
    /// page shown, then its enter hook — so a hook always sees its own page on screen and never
    /// the other's. Selecting the current section again does nothing.
    /// </summary>
    public void Select(string tag)
    {
        var plan = PlanFor(tag);
        if (string.Equals(Current, tag, StringComparison.Ordinal)) return;
        if (!_pages.ContainsKey(tag)) Build(plan);

        if (Current is { } old)
        {
            _byTag[old].Leave?.Invoke();
            _host.Hide(_pages[old]);
        }

        Current = tag;
        _host.Show(_pages[tag]);
        plan.Enter?.Invoke();
    }

    /// <summary>Discards and builds again every page whose section is not build-once. The
    /// current section, if rebuilt, leaves before its page goes and enters again on the new one.</summary>
    public void Rebuild()
    {
        foreach (var plan in _plans)
            if (!plan.BuildOnce) Rebuild(plan);
    }

    /// <summary>Discards and builds again one section's page.</summary>
    /// <exception cref="InvalidOperationException">The section is build-once. The flag is there to
    /// keep a page whose state a rebuild would lose, so a request naming it is a mistake.</exception>
    public void Rebuild(string tag)
    {
        var plan = PlanFor(tag);
        if (plan.BuildOnce)
            throw new InvalidOperationException(
                $"Section '{tag}' is built once for the life of the window and cannot be rebuilt.");
        Rebuild(plan);
    }

    /// <summary>Leaves the current section. Nothing is discarded: the pages go with the host.</summary>
    public void Close()
    {
        if (Current is { } current) _byTag[current].Leave?.Invoke();
        Current = null;
    }

    private void Rebuild(SectionPlan<TPage> plan)
    {
        bool current = string.Equals(Current, plan.Tag, StringComparison.Ordinal);
        if (_pages.Remove(plan.Tag, out var old))
        {
            if (current) plan.Leave?.Invoke();
            _host.Remove(old);
        }

        var page = Build(plan);
        if (current)
        {
            _host.Show(page);
            plan.Enter?.Invoke();
        }
    }

    private TPage Build(SectionPlan<TPage> plan)
    {
        var page = plan.Build()
            ?? throw new InvalidOperationException($"Section '{plan.Tag}' built no page.");
        _pages[plan.Tag] = page;
        _host.Add(page);
        return page;
    }

    private SectionPlan<TPage> PlanFor(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return _byTag.TryGetValue(tag, out var plan)
            ? plan
            : throw new ArgumentException($"No section is tagged '{tag}'.", nameof(tag));
    }
}
