namespace TodayChecklist.Services;

public sealed class DocumentValidationException : Exception
{
    public DocumentValidationException(string message)
        : base(message)
    {
    }
}

public sealed class DataRecoveryException : Exception
{
    public DataRecoveryException(string dataFile, Exception primaryFailure, Exception? recoveryFailure)
        : base($"任务数据无法读取，原文件已保留：{dataFile}", recoveryFailure ?? primaryFailure)
    {
        DataFile = dataFile;
        PrimaryFailure = primaryFailure;
        RecoveryFailure = recoveryFailure;
    }

    public string DataFile { get; }

    public Exception PrimaryFailure { get; }

    public Exception? RecoveryFailure { get; }
}

