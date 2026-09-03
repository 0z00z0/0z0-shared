using System.Security.Cryptography;

namespace ZeroZero.Tray.WinUI;

/// <summary>
/// The file the shell is pointed at for a rendered icon. A render arrives as PNG frames; the
/// cache composes the icon file and writes it only when its bytes differ from the last write, so
/// an application that re-renders the same picture on every state change costs no disk write
/// and no icon reload. A file the application owns passes through untouched.
/// </summary>
/// <remarks>Framework-free on purpose, so the write-or-skip decision is pinned by a plain test.</remarks>
public sealed class TrayIconFileCache
{
    private readonly string _path;
    private byte[]? _lastHash;

    /// <summary>The cache file lives in <paramref name="directory"/> under a name derived from
    /// <paramref name="name"/>, so two hosts in one process never share a file.</summary>
    public TrayIconFileCache(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _path = System.IO.Path.Combine(directory, SafeFileName(name) + ".ico");
    }

    /// <summary>Where a render is written.</summary>
    public string Path => _path;

    /// <summary>How many times the file was written; a render that repeats the last one adds nothing.</summary>
    public int Writes { get; private set; }

    /// <summary>
    /// The path the shell is to load: the application's own file, or the cache file with this
    /// render in it. Says whether the file to load changed, so the caller knows an icon reload is
    /// due.
    /// </summary>
    public (string Path, bool Changed) Resolve(TrayIconImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Path is { } own)
        {
            // The application's file is its own to keep current; the cache forgets its last render
            // so a later render is written even when it repeats the one before the switch.
            _lastHash = null;
            return (own, true);
        }

        byte[] file = IcoFile.Build(image.Frames!);
        byte[] hash = SHA256.HashData(file);
        if (_lastHash is not null && hash.AsSpan().SequenceEqual(_lastHash) && File.Exists(_path))
            return (_path, false);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, file);
        _lastHash = hash;
        Writes++;
        return (_path, true);
    }

    private static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray();
        return new string(chars);
    }
}
