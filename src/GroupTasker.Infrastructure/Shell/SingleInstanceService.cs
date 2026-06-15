using System.IO.Pipes;
using System.Text;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Coordinates the launcher flyout: at most one process listens on the named pipe;
/// further invocations send the group name to that listener instead of starting a
/// second flyout.
///
/// Wire format: <c>uint32 length</c> (little-endian) followed by <c>length</c> UTF-8 bytes.
/// Replaces the original line-delimited protocol which would truncate on any newline
/// in the group name.
/// </summary>
public sealed class SingleInstanceService : IAsyncDisposable, IDisposable
{
    private static readonly string PipeName = "GroupTasker-Launcher-" + UserSuffix;
    private const int MaxPayloadBytes = 4 * 1024;

    private static readonly string UserSuffix =
        System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "anon";

    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private Task? _listenerTask;

    public event Action<string>? OnShowGroup;

    public SingleInstanceService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In,
                    maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var groupName = await ReadFramedStringAsync(server, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(groupName))
                    OnShowGroup?.Invoke(groupName);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Named pipe listener encountered an error");
            }
        }
    }

    private static async Task<string?> ReadFramedStringAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var read = await stream.ReadAsync(header.AsMemory(0, 4), ct).ConfigureAwait(false);
        if (read != 4) return null;

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxPayloadBytes) return null;

        var buffer = new byte[length];
        var got = 0;
        while (got < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(got, length - got), ct).ConfigureAwait(false);
            if (n == 0) return null;
            got += n;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>
    /// Try to deliver a "show group X" message to an already-running primary instance.
    /// Returns true if delivered (we should exit), false if no server responded (we
    /// should become the primary instance).
    /// </summary>
    public static bool TryActivate(string groupName, ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            var payload = Encoding.UTF8.GetBytes(groupName);
            if (payload.Length > MaxPayloadBytes) return false;
            var header = BitConverter.GetBytes(payload.Length);
            client.Write(header, 0, header.Length);
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to activate running instance for group {GroupName}", groupName);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { /* already disposed */ }
        if (_listenerTask is not null)
        {
            try { await _listenerTask.ConfigureAwait(false); }
            catch { /* the loop swallowed any meaningful error already */ }
        }
        _cts.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
