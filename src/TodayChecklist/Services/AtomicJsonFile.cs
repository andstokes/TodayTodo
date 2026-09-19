using System.Text.Json;

namespace TodayChecklist.Services;

internal sealed class AtomicJsonFile<T>
    where T : class
{
    private readonly JsonSerializerOptions _options = JsonDefaults.Create();
    private readonly Action<T> _validate;

    public AtomicJsonFile(Action<T> validate)
    {
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
    }

    public async Task<T> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
            .ConfigureAwait(false);
        if (value is null)
        {
            throw new DocumentValidationException("JSON 文件内容为空。");
        }

        _validate(value);
        return value;
    }

    public async Task WriteAsync(
        string path,
        T value,
        string? replacedFileBackup = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        _validate(value);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("目标文件缺少目录。");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            _ = await ReadAsync(tempPath, cancellationToken).ConfigureAwait(false);

            if (File.Exists(path))
            {
                if (replacedFileBackup is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(replacedFileBackup)!);
                    File.Replace(tempPath, path, replacedFileBackup, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

