namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>The crash victim: launched by the elevated test under a real registration, it dies of an
/// unhandled exception so Windows Error Reporting writes a real dump. The test host never enters here.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "crash")
            throw new InvalidOperationException("provoked crash for a dump");

        Console.Error.WriteLine("crash victim: crash");
        return 2;
    }
}
