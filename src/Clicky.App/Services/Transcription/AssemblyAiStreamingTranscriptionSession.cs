using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Clicky.App.Services.Transcription;

public sealed class AssemblyAiStreamingTranscriptionSession : IAsyncDisposable
{
    private const string AssemblyAiWebSocketBaseUrl = "wss://streaming.assemblyai.com/v3/ws";
    private const int TargetSampleRate = 16_000;
    private const double ExplicitFinalTranscriptGracePeriodSeconds = 1.4;

    private readonly ClientWebSocket clientWebSocket = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly TaskCompletionSource<bool> readyCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> finalTranscriptCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, StoredTurnTranscript> storedTurnTranscriptsByOrder = [];
    private readonly Action<string> onTranscriptUpdated;
    private readonly IReadOnlyList<string> keyterms;
    private readonly string temporaryToken;
    private readonly object sessionStateLock = new();

    private Task? receiveLoopTask;
    private CancellationTokenSource? receiveLoopCancellationTokenSource;
    private CancellationTokenSource? explicitFinalTranscriptDeadlineCancellationTokenSource;
    private bool hasDeliveredFinalTranscript;
    private bool isAwaitingExplicitFinalTranscript;
    private int? activeTurnOrder;
    private string activeTurnTranscriptText = string.Empty;
    private string latestTranscriptText = string.Empty;

    public AssemblyAiStreamingTranscriptionSession(
        string temporaryToken,
        IReadOnlyList<string> keyterms,
        Action<string> onTranscriptUpdated)
    {
        this.temporaryToken = temporaryToken;
        this.keyterms = keyterms;
        this.onTranscriptUpdated = onTranscriptUpdated;
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        var webSocketUri = BuildWebSocketUri(temporaryToken, keyterms);

        receiveLoopCancellationTokenSource = new CancellationTokenSource();
        await clientWebSocket.ConnectAsync(webSocketUri, cancellationToken);

        receiveLoopTask = Task.Run(
            () => ReceiveLoopAsync(receiveLoopCancellationTokenSource.Token),
            receiveLoopCancellationTokenSource.Token);

        await readyCompletionSource.Task.WaitAsync(cancellationToken);
    }

    public async Task SendAudioChunkAsync(byte[] pcm16AudioChunk, CancellationToken cancellationToken = default)
    {
        if (pcm16AudioChunk.Length == 0 || clientWebSocket.State != WebSocketState.Open)
        {
            return;
        }

        await sendGate.WaitAsync(cancellationToken);

        try
        {
            await clientWebSocket.SendAsync(
                pcm16AudioChunk,
                WebSocketMessageType.Binary,
                true,
                cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
    }

    public async Task<string> RequestFinalTranscriptAsync(CancellationToken cancellationToken = default)
    {
        lock (sessionStateLock)
        {
            if (hasDeliveredFinalTranscript)
            {
                return BestAvailableTranscriptText();
            }

            isAwaitingExplicitFinalTranscript = true;
            ScheduleExplicitFinalTranscriptDeadline();
        }

        await SendJsonMessageAsync("""{"type":"ForceEndpoint"}""", cancellationToken);
        return await finalTranscriptCompletionSource.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (clientWebSocket.State == WebSocketState.Open)
            {
                await SendJsonMessageAsync("""{"type":"Terminate"}""");
                await clientWebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Clicky session completed",
                    CancellationToken.None);
            }
        }
        catch
        {
        }

        receiveLoopCancellationTokenSource?.Cancel();

        if (receiveLoopTask is not null)
        {
            try
            {
                await receiveLoopTask;
            }
            catch
            {
            }
        }

        explicitFinalTranscriptDeadlineCancellationTokenSource?.Cancel();
        explicitFinalTranscriptDeadlineCancellationTokenSource?.Dispose();
        receiveLoopCancellationTokenSource?.Dispose();
        sendGate.Dispose();
        clientWebSocket.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && clientWebSocket.State == WebSocketState.Open)
            {
                using var messageBuffer = new MemoryStream();
                WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await clientWebSocket.ReceiveAsync(receiveBuffer, cancellationToken);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        DeliverFinalTranscriptIfNeeded(BestAvailableTranscriptText());
                        return;
                    }

                    messageBuffer.Write(receiveBuffer, 0, receiveResult.Count);
                } while (!receiveResult.EndOfMessage);

                if (receiveResult.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var messageText = Encoding.UTF8.GetString(messageBuffer.ToArray());
                HandleIncomingTextMessage(messageText);
            }
        }
        catch (Exception exception)
        {
            if (isAwaitingExplicitFinalTranscript)
            {
                DeliverFinalTranscriptIfNeeded(BestAvailableTranscriptText());
                return;
            }

            readyCompletionSource.TrySetException(exception);
            finalTranscriptCompletionSource.TrySetException(exception);
        }
    }

    private void HandleIncomingTextMessage(string messageText)
    {
        using var jsonDocument = JsonDocument.Parse(messageText);
        var rootElement = jsonDocument.RootElement;

        if (!rootElement.TryGetProperty("type", out var typeProperty))
        {
            return;
        }

        var messageType = typeProperty.GetString();
        switch (messageType?.ToLowerInvariant())
        {
            case "begin":
                readyCompletionSource.TrySetResult(true);
                break;
            case "turn":
                HandleTurnMessage(rootElement);
                break;
            case "termination":
                readyCompletionSource.TrySetResult(true);
                DeliverFinalTranscriptIfNeeded(BestAvailableTranscriptText());
                break;
            case "error":
                var errorMessage = rootElement.TryGetProperty("error", out var errorProperty)
                    ? errorProperty.GetString()
                    : rootElement.TryGetProperty("message", out var messageProperty)
                        ? messageProperty.GetString()
                        : "AssemblyAI returned an unknown error";

                var exception = new InvalidOperationException(errorMessage);
                readyCompletionSource.TrySetException(exception);
                finalTranscriptCompletionSource.TrySetException(exception);
                break;
        }
    }

    private void HandleTurnMessage(JsonElement turnElement)
    {
        var transcriptText = turnElement.TryGetProperty("transcript", out var transcriptProperty)
            ? transcriptProperty.GetString()?.Trim() ?? string.Empty
            : string.Empty;

        lock (sessionStateLock)
        {
            var turnOrder = turnElement.TryGetProperty("turn_order", out var turnOrderProperty)
                ? turnOrderProperty.GetInt32()
                : activeTurnOrder ?? (storedTurnTranscriptsByOrder.Keys.DefaultIfEmpty(-1).Max() + 1);

            var isEndOfTurn = turnElement.TryGetProperty("end_of_turn", out var endOfTurnProperty)
                && endOfTurnProperty.ValueKind == JsonValueKind.True;

            var isFormattedTurn = turnElement.TryGetProperty("turn_is_formatted", out var formattedTurnProperty)
                                  && formattedTurnProperty.ValueKind == JsonValueKind.True;

            if (isEndOfTurn || isFormattedTurn)
            {
                activeTurnOrder = null;
                activeTurnTranscriptText = string.Empty;
                StoreTurnTranscript(transcriptText, turnOrder, isFormattedTurn);
            }
            else
            {
                activeTurnOrder = turnOrder;
                activeTurnTranscriptText = transcriptText;
            }

            latestTranscriptText = ComposeFullTranscript();
        }

        if (!string.IsNullOrWhiteSpace(latestTranscriptText))
        {
            onTranscriptUpdated(latestTranscriptText);
        }

        if (isAwaitingExplicitFinalTranscript)
        {
            var turnEnded = turnElement.TryGetProperty("end_of_turn", out var endOfTurnProperty)
                            && endOfTurnProperty.ValueKind == JsonValueKind.True;
            var turnFormatted = turnElement.TryGetProperty("turn_is_formatted", out var formattedTurnProperty)
                                && formattedTurnProperty.ValueKind == JsonValueKind.True;

            if (turnEnded || turnFormatted)
            {
                explicitFinalTranscriptDeadlineCancellationTokenSource?.Cancel();
                DeliverFinalTranscriptIfNeeded(BestAvailableTranscriptText());
            }
        }
    }

    private void StoreTurnTranscript(string transcriptText, int turnOrder, bool isFormatted)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return;
        }

        if (storedTurnTranscriptsByOrder.TryGetValue(turnOrder, out var existingTurnTranscript)
            && existingTurnTranscript.IsFormatted
            && !isFormatted)
        {
            return;
        }

        storedTurnTranscriptsByOrder[turnOrder] = new StoredTurnTranscript(transcriptText, isFormatted);
    }

    private string ComposeFullTranscript()
    {
        var transcriptSegments = storedTurnTranscriptsByOrder
            .OrderBy(turnTranscript => turnTranscript.Key)
            .Select(turnTranscript => turnTranscript.Value.TranscriptText)
            .Where(transcriptText => !string.IsNullOrWhiteSpace(transcriptText))
            .ToList();

        if (!string.IsNullOrWhiteSpace(activeTurnTranscriptText))
        {
            transcriptSegments.Add(activeTurnTranscriptText);
        }

        return string.Join(" ", transcriptSegments);
    }

    private string BestAvailableTranscriptText()
    {
        var composedTranscriptText = ComposeFullTranscript().Trim();
        if (!string.IsNullOrWhiteSpace(composedTranscriptText))
        {
            return composedTranscriptText;
        }

        return latestTranscriptText.Trim();
    }

    private void DeliverFinalTranscriptIfNeeded(string transcriptText)
    {
        lock (sessionStateLock)
        {
            if (hasDeliveredFinalTranscript)
            {
                return;
            }

            hasDeliveredFinalTranscript = true;
        }

        explicitFinalTranscriptDeadlineCancellationTokenSource?.Cancel();
        finalTranscriptCompletionSource.TrySetResult(transcriptText);
    }

    private void ScheduleExplicitFinalTranscriptDeadline()
    {
        explicitFinalTranscriptDeadlineCancellationTokenSource?.Cancel();
        explicitFinalTranscriptDeadlineCancellationTokenSource?.Dispose();

        explicitFinalTranscriptDeadlineCancellationTokenSource = new CancellationTokenSource();
        var deadlineCancellationToken = explicitFinalTranscriptDeadlineCancellationTokenSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ExplicitFinalTranscriptGracePeriodSeconds), deadlineCancellationToken);
                DeliverFinalTranscriptIfNeeded(BestAvailableTranscriptText());
            }
            catch (OperationCanceledException)
            {
            }
        }, deadlineCancellationToken);
    }

    private async Task SendJsonMessageAsync(string jsonPayload, CancellationToken cancellationToken = default)
    {
        if (clientWebSocket.State != WebSocketState.Open)
        {
            return;
        }

        var messageBytes = Encoding.UTF8.GetBytes(jsonPayload);

        await sendGate.WaitAsync(cancellationToken);

        try
        {
            await clientWebSocket.SendAsync(messageBytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
    }

    private static Uri BuildWebSocketUri(string temporaryToken, IReadOnlyList<string> keyterms)
    {
        var queryParameters = new List<string>
        {
            $"sample_rate={TargetSampleRate}",
            "encoding=pcm_s16le",
            "format_turns=true",
            "speech_model=u3-rt-pro",
            $"token={Uri.EscapeDataString(temporaryToken)}"
        };

        var normalizedKeyterms = keyterms
            .Select(keyterm => keyterm.Trim())
            .Where(keyterm => !string.IsNullOrWhiteSpace(keyterm))
            .ToArray();

        if (normalizedKeyterms.Length > 0)
        {
            var keytermsJson = JsonSerializer.Serialize(normalizedKeyterms);
            queryParameters.Add($"keyterms_prompt={Uri.EscapeDataString(keytermsJson)}");
        }

        return new Uri($"{AssemblyAiWebSocketBaseUrl}?{string.Join("&", queryParameters)}");
    }

    private sealed record StoredTurnTranscript(string TranscriptText, bool IsFormatted);
}
