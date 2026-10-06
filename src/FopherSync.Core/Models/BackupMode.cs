namespace FopherSync.Core.Models;

public enum BackupMode
{
    /// <summary>
    /// Safe default: Copies new and modified files (/E /XO). Never deletes anything from either side.
    /// </summary>
    Incremental = 0,

    /// <summary>
    /// Clones source to destination (/MIR). Files present in destination but missing in source are purged.
    /// Requires explicit confirmation and passes empty-source guard checks.
    /// </summary>
    Mirror = 1,

    /// <summary>
    /// Copies files and deletes them from source upon successful copy (/MOVE or /MOV).
    /// </summary>
    Move = 2
}

public enum JobStatus
{
    Idle,
    Running,
    Success,
    Warning,
    Failed,
    AbortedBySafetyGuard
}
