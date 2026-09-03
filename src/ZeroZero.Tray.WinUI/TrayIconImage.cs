namespace ZeroZero.Tray.WinUI;

/// <summary>
/// The icon the application returns for a request: either a file it wrote itself, one per state
/// and written once, or the PNG frames of a render made for this request, which the host writes
/// into its own cache file. Both reach the shell the same way; the difference is who owns the file.
/// </summary>
public sealed class TrayIconImage
{
    private TrayIconImage(string? path, IReadOnlyList<byte[]>? frames)
    {
        Path = path;
        Frames = frames;
    }

    /// <summary>The application's own icon file, when it supplied one.</summary>
    public string? Path { get; }

    /// <summary>The PNG frames of a render, when the application supplied those.</summary>
    public IReadOnlyList<byte[]>? Frames { get; }

    /// <summary>An icon file the application owns. The file must exist when the host loads it.</summary>
    public static TrayIconImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TrayIconImage(path, null);
    }

    /// <summary>PNG frames of a render for the request, one per size, which the host writes into its
    /// cache as one icon file. Each frame's size is read from the PNG itself.</summary>
    public static TrayIconImage FromFrames(IReadOnlyList<byte[]> pngFrames)
    {
        ArgumentNullException.ThrowIfNull(pngFrames);
        if (pngFrames.Count == 0) throw new ArgumentException("An icon needs at least one frame.", nameof(pngFrames));
        return new TrayIconImage(null, pngFrames);
    }
}
