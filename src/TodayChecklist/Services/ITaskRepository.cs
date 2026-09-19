using TodayChecklist.Models;

namespace TodayChecklist.Services;

public interface ITaskRepository
{
    Task<TaskDataDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TaskDataDocument document, CancellationToken cancellationToken = default);

    Task ExportAsync(TaskDataDocument document, string destination, CancellationToken cancellationToken = default);

    Task<TaskDataDocument> ImportAsync(string source, CancellationToken cancellationToken = default);
}

