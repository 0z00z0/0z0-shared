namespace ZeroZero.Diagnostics.Dumps;

/// <summary>What a dump holds. The values are Windows Error Reporting's own DumpType values.</summary>
public enum DumpType
{
    /// <summary>Stacks and module lists, no heap: a stowed exception's type and message are absent.</summary>
    Mini = 1,

    /// <summary>The whole process memory. Large, and the only kind that carries a managed heap.</summary>
    Full = 2,
}
