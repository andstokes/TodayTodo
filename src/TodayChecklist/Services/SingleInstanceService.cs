using System.IO.Pipes;

namespace TodayChecklist.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private Task? _listener;

    public SingleInstanceService(string instanceKey = "Production")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        if (instanceKey.Any(static character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-'))
        {
            throw new ArgumentException("单实例标识只能包含英文字母、数字、点和连字符。", nameof(instanceKey));
        }

        if (string.Equals(instanceKey, "Production", StringComparison.Ordinal))
        {
            _mutexName = "Local\\TodayChecklist.SingleInstance.7D2ED3E8";
            _pipeName = "TodayChecklist.Activate.7D2ED3E8";
        }
        else
        {
            _mutexName = $"Local\\TodayChecklist.SingleInstance.7D2ED3E8.{instanceKey}";
            _pipeName = $"TodayChecklist.Activate.7D2ED3E8.{instanceKey}";
        }
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    public void StartListening()
    {
        _listener = ListenAsync(_cancellation.Token);
    }

    public async Task SignalExistingAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(750).ConfigureAwait(false);
            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("activate").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            // The first process may still be starting. There is no unsafe retry loop.
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) == "activate")
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // Accept a future activation request after a disconnected client.
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Shutdown is already in progress.
        }

        _cancellation.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
