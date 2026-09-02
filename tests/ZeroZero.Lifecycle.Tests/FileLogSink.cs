using ZeroZero.Primitives;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>Appends to one file from several processes at once: the parent's exit hook and the
/// child it spawned write within milliseconds of each other, so the file is opened shared and a
/// collision is retried rather than lost.</summary>
internal sealed class FileLogSink(string path) : ILogSink
{
    public void Info(string message) => Append("INFO " + message);

    public void Error(string source, Exception? ex) => Append($"ERROR {source}: {ex?.GetType().Name}: {ex?.Message}");

    private void Append(string line)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(25);
            }
        }
    }
}
