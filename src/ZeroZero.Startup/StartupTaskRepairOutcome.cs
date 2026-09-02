namespace ZeroZero.Startup;

/// <summary>What a repair found and did.</summary>
public enum StartupTaskRepairOutcome
{
    /// <summary>No task of that name exists. Repair never creates one.</summary>
    NotRegistered,

    /// <summary>The task is as it should be.</summary>
    AlreadyCorrect,

    /// <summary>The task differed and was rewritten — and, where verification was asked for, ran.</summary>
    Repaired,

    /// <summary>The task differed and the rewrite, or a read before it, failed. The result carries
    /// the exception.</summary>
    RepairFailed,

    /// <summary>The task was rewritten but a demand start did not end in a successful run.</summary>
    VerificationFailed,
}
