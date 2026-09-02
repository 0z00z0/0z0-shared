namespace ZeroZero.Diagnostics.Tests;

/// <summary>A directory of the test's own under the temp folder, removed with it.</summary>
public sealed class Scratch : IDisposable
{
    public Scratch()
    {
        Directory = Path.Combine(Path.GetTempPath(), "ZeroZero.Diagnostics.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string File(string name) => Path.Combine(Directory, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A handle a failed test left open; the temp folder is cleaned by the system in time.
        }
    }
}
