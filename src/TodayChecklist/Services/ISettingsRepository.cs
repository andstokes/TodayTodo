using TodayChecklist.Models;

namespace TodayChecklist.Services;

public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly AppPaths _paths;
    private readonly AtomicJsonFile<AppSettings> _json = new(DocumentValidator.Validate);

    public SettingsRepository(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            return await _json.ReadAsync(_paths.SettingsFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or DocumentValidationException)
        {
            return new AppSettings();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return _json.WriteAsync(_paths.SettingsFile, settings,
            replacedFileBackup: _paths.SettingsFile + ".bak", cancellationToken: cancellationToken);
    }
}
