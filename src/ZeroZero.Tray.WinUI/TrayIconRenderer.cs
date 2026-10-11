using H.NotifyIcon;

namespace ZeroZero.Tray.WinUI;

/// <summary>What the icon shows: the application's image for the taskbar's slot and theme, its
/// cache file, the handle loaded from it and the request it was drawn for.</summary>
internal sealed class TrayIconRenderer(Func<TrayIconRequest, TrayIconImage> icon, TrayIconFileCache cache)
{
    private System.Drawing.Icon? _loaded;

    /// <summary>The request last rendered for; null before the first render and after <see cref="Release"/>.</summary>
    public TrayIconRequest? Request { get; private set; }

    public string CachePath => cache.Path;

    /// <summary>The shell gets the frame of the slot's own size, so nothing is resampled.</summary>
    public void Render(TaskbarIcon target)
    {
        var request = ReadRequest();
        TrayIconImage image = icon(request);
        var (path, changed) = cache.Resolve(image);
        if (!changed && _loaded is not null)
        {
            Request = request;
            Push(target);
            return;
        }

        var previous = _loaded;
        // The handle holds the file's bytes; a later overwrite of the cache file does not reach it.
        _loaded = new System.Drawing.Icon(path, request.SlotPixels, request.SlotPixels);
        // The library keeps the object: it re-adds this handle after a shell restart.
        target.Icon = _loaded;
        // Recorded only once loaded, or a throw here leaves the next reconcile keeping the old icon.
        Request = request;
        previous?.Dispose();
    }

    /// <summary>A changed request is a new render; an unchanged one is a push, which repairs an
    /// icon the shell dropped without saying so.</summary>
    public void Reconcile(TaskbarIcon target)
    {
        if (ReadRequest() != Request) Render(target);
        else Push(target);
    }

    /// <summary>A refused update means the shell lost the icon without a TaskbarCreated.</summary>
    public void Push(TaskbarIcon target)
    {
        if (_loaded is null || !target.IsCreated) return;
        if (target.TrayIcon.UpdateIcon(_loaded.Handle)) return;
        TrayHost.AddAgain(target);
    }

    public void Release()
    {
        _loaded?.Dispose();
        _loaded = null;
        Request = null;
    }

    private static TrayIconRequest ReadRequest()
    {
        var theme = TaskbarThemes.Read();
        return new TrayIconRequest(TrayIconSlot.PixelsForTaskbar(), theme, TaskbarThemes.StrokeToneFor(theme));
    }
}
