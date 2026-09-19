using TodayChecklist.Models;

namespace TodayChecklist.Services;

public interface IDataLocationService
{
    string BootstrapDirectory { get; }

    string DefaultDataRoot { get; }

    StorageLocator Current { get; }

    Task<AppPaths> ResolveAsync(CancellationToken cancellationToken = default);

    Task ActivateAsync(string dataRoot, Guid storeId, CancellationToken cancellationToken = default);
}

public enum StorageLocationFailure
{
    InvalidLocator,
    Unavailable,
    InvalidStore,
}

public sealed class StorageLocationException : Exception
{
    public StorageLocationException(StorageLocationFailure failure, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Path = path;
    }

    public StorageLocationFailure Failure { get; }

    public string Path { get; }
}

