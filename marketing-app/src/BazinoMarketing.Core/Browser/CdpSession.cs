using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BazinoMarketing.Core.Browser;

/// <summary>
/// A minimal Chrome DevTools Protocol session over a local WebSocket. No third-party package: the
/// base class library already speaks WebSocket. One session is one browser tab.
/// </summary>
public sealed class CdpSession : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _nextId;

    public string TargetId { get; }
    public bool IsOpen => _socket.State == WebSocketState.Open;

    public CdpSession(string targetId) => TargetId = targetId;

    public async Task ConnectAsync(string webSocketDebuggerUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webSocketDebuggerUrl))
            throw new InvalidOperationException("این تب آدرس WebSocket برای اتصال ندارد.");
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await _socket.ConnectAsync(new Uri(webSocketDebuggerUrl), ct).ConfigureAwait(false);
    }

    /// <summary>Sends one CDP method and waits for the reply that carries the same id. Events in between are skipped.</summary>
    public async Task<JsonNode?> SendAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_socket.State is WebSocketState.Aborted or WebSocketState.Closed, _socket);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            var payload = new JsonObject
            {
                ["id"] = id,
                ["method"] = method
            };
            if (parameters is not null) payload["params"] = parameters;
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            var buffer = new byte[512 * 1024];
            while (true)
            {
                if (limit.IsCancellationRequested)
                    throw new TimeoutException($"مرورگر به فرمان «{method}» در زمان مجاز جواب نداد.");
                var message = await ReceiveWholeMessageAsync(buffer, limit.Token).ConfigureAwait(false);
                if (message is null) return null; // the tab closed
                JsonObject? reply;
                try { reply = JsonNode.Parse(message) as JsonObject; }
                catch (JsonException) { continue; }
                if (reply is null) continue;
                if (reply["id"]?.GetValue<long>() != id) continue; // an event, not our reply
                if (reply["error"] is JsonObject err)
                    throw new InvalidOperationException($"مرورگر فرمان «{method}» را رد کرد: {err["message"]?.GetValue<string>() ?? "دلیل نامشخص"}");
                return reply["result"];
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads one complete WebSocket message (which may arrive in several frames) and returns it as text.</summary>
    private async Task<string?> ReceiveWholeMessageAsync(byte[] buffer, CancellationToken ct)
    {
        using var text = new MemoryStream();
        WebSocketReceiveResult frame;
        do
        {
            frame = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (frame.MessageType == WebSocketMessageType.Close) return null;
            if (frame.Count > 0) text.Write(buffer, 0, frame.Count);
        }
        while (!frame.EndOfMessage);
        return Encoding.UTF8.GetString(text.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Closing a tab that already went away is not an error worth reporting.
        }
        _socket.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
