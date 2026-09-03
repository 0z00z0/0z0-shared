namespace ZeroZero.Config;

/// <summary>Writes a file whole or not at all.</summary>
/// <remarks>
/// <para>The content goes to a temporary sibling, is flushed through to the disk, and only then
/// replaces the target, so neither a crash nor a power loss can leave a half-written settings file
/// where a whole one was. A replace the operating system refuses for a moment is retried briefly.</para>
/// <para>It never throws: the exception that stopped the write is returned, because a settings save
/// that fails has to be reported to the person whose settings did not reach the disk, and an
/// exception thrown out of a save path is routinely swallowed by the caller above it.</para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>The suffix of the temporary sibling a write goes through.</summary>
    public const string TempSuffix = ".tmp";

    /// <summary>How many times the replace is attempted in all — one try and four retries, not five
    /// retries. Internal so the number the guides quote is held to the number the code uses.</summary>
    internal const int ReplaceAttempts = 5;

    internal static readonly TimeSpan ReplacePause = TimeSpan.FromMilliseconds(20);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, creating the directory
    /// if it is absent. Returns null on success, or the exception that stopped it.</summary>
    public static Exception? Write(string path, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var temp = path + TempSuffix;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);

                // Without this the bytes may still be in the operating system's cache when the
                // rename lands, so a power loss can leave the new name pointing at nothing.
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            TryDelete(temp);
            return ex;
        }

        return Replace(temp, path);
    }

    /// <summary>Writes <paramref name="content"/> as UTF-8, with no byte-order mark.</summary>
    public static Exception? WriteText(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Write(path, System.Text.Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Deletes a file, reporting nothing: a leftover temporary file is replaced by the next
    /// write, and a failure to tidy up must not fail the operation that asked for it.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            // Deliberately unreported.
        }
    }

    /// <summary>Whether an exception is the file system refusing rather than the program being
    /// wrong. These are the failures a settings path reports and carries on from; anything else is a
    /// defect and is left to propagate.</summary>
    public static bool IsFileFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException;

    // Windows denies a replace for a moment while a scanner, an indexer or a closing handle still
    // holds the file, so a burst of saves meets a refusal that clears on its own. A file that is
    // genuinely locked or read-only still fails, a few milliseconds later.
    private static Exception? Replace(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return null;
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                if (attempt >= ReplaceAttempts)
                {
                    TryDelete(temp);
                    return ex;
                }

                Thread.Sleep(ReplacePause);
            }
        }
    }
}
