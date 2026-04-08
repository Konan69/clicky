using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Clicky.App.Services.Transcription;

public sealed class AssemblyAiStreamingTranscriptionClient
{
    private readonly HttpClient httpClient;
    private readonly Uri tokenEndpointUri;

    public AssemblyAiStreamingTranscriptionClient(string workerBaseUrl)
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        tokenEndpointUri = new Uri($"{workerBaseUrl.TrimEnd('/')}/transcribe-token");
    }

    public async Task<AssemblyAiStreamingTranscriptionSession> StartStreamingSessionAsync(
        IReadOnlyList<string> keyterms,
        Action<string> onTranscriptUpdated,
        CancellationToken cancellationToken = default)
    {
        var temporaryToken = await FetchTemporaryTokenAsync(cancellationToken);

        var assemblyAiStreamingTranscriptionSession = new AssemblyAiStreamingTranscriptionSession(
            temporaryToken,
            keyterms,
            onTranscriptUpdated);

        await assemblyAiStreamingTranscriptionSession.OpenAsync(cancellationToken);
        return assemblyAiStreamingTranscriptionSession;
    }

    private async Task<string> FetchTemporaryTokenAsync(CancellationToken cancellationToken)
    {
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, tokenEndpointUri);
        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var errorBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"AssemblyAI token proxy returned {(int)httpResponseMessage.StatusCode}: {errorBody}");
        }

        var rawJson = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDocument = JsonDocument.Parse(rawJson);

        if (!jsonDocument.RootElement.TryGetProperty("token", out var tokenProperty))
        {
            throw new InvalidOperationException("AssemblyAI token response did not include a token");
        }

        return tokenProperty.GetString()
               ?? throw new InvalidOperationException("AssemblyAI token response included an empty token");
    }
}

