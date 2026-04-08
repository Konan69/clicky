using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Clicky.App.Models;

namespace Clicky.App.Services.Chat;

public sealed class ClaudeWorkerChatClient
{
    private readonly HttpClient httpClient;
    private readonly Uri chatEndpointUri;

    public ClaudeWorkerChatClient(string workerBaseUrl)
    {
        httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        chatEndpointUri = new Uri($"{workerBaseUrl.TrimEnd('/')}/chat");
    }

    public async IAsyncEnumerable<string> StreamVisionResponseAsync(
        string selectedModel,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<CapturedScreen> capturedScreens,
        IReadOnlyList<ConversationTurn> conversationHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, chatEndpointUri);
        var requestBody = BuildRequestBody(selectedModel, systemPrompt, userPrompt, capturedScreens, conversationHistory);
        var serializedRequestBody = JsonSerializer.Serialize(requestBody);

        httpRequestMessage.Content = new StringContent(serializedRequestBody, Encoding.UTF8, "application/json");

        using var httpResponseMessage = await httpClient.SendAsync(
            httpRequestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var errorBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Claude proxy returned {(int)httpResponseMessage.StatusCode}: {errorBody}");
        }

        await using var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);
        using var streamReader = new StreamReader(responseStream);

        while (!streamReader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var eventLine = await streamReader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(eventLine) || !eventLine.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var rawJsonPayload = eventLine["data: ".Length..];
            if (rawJsonPayload == "[DONE]")
            {
                yield break;
            }

            using var jsonDocument = JsonDocument.Parse(rawJsonPayload);
            var rootElement = jsonDocument.RootElement;

            if (!rootElement.TryGetProperty("type", out var typeProperty))
            {
                continue;
            }

            if (!string.Equals(typeProperty.GetString(), "content_block_delta", StringComparison.Ordinal))
            {
                continue;
            }

            if (!rootElement.TryGetProperty("delta", out var deltaElement))
            {
                continue;
            }

            if (!deltaElement.TryGetProperty("type", out var deltaTypeProperty)
                || !string.Equals(deltaTypeProperty.GetString(), "text_delta", StringComparison.Ordinal))
            {
                continue;
            }

            if (!deltaElement.TryGetProperty("text", out var textProperty))
            {
                continue;
            }

            var textChunk = textProperty.GetString();
            if (!string.IsNullOrEmpty(textChunk))
            {
                yield return textChunk;
            }
        }
    }

    private static object BuildRequestBody(
        string selectedModel,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<CapturedScreen> capturedScreens,
        IReadOnlyList<ConversationTurn> conversationHistory)
    {
        var messages = new List<object>();

        foreach (var conversationTurn in conversationHistory)
        {
            messages.Add(new
            {
                role = "user",
                content = conversationTurn.UserPlaceholderText
            });

            messages.Add(new
            {
                role = "assistant",
                content = conversationTurn.AssistantResponseText
            });
        }

        var contentBlocks = new List<object>();

        foreach (var capturedScreen in capturedScreens)
        {
            contentBlocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = DetectImageMediaType(capturedScreen.ImageBytes),
                    data = Convert.ToBase64String(capturedScreen.ImageBytes)
                }
            });

            contentBlocks.Add(new
            {
                type = "text",
                text = capturedScreen.Label
            });
        }

        contentBlocks.Add(new
        {
            type = "text",
            text = userPrompt
        });

        messages.Add(new
        {
            role = "user",
            content = contentBlocks
        });

        return new
        {
            model = selectedModel,
            max_tokens = 1024,
            stream = true,
            system = systemPrompt,
            messages
        };
    }

    private static string DetectImageMediaType(byte[] imageBytes)
    {
        if (imageBytes.Length >= 4
            && imageBytes[0] == 0x89
            && imageBytes[1] == 0x50
            && imageBytes[2] == 0x4E
            && imageBytes[3] == 0x47)
        {
            return "image/png";
        }

        return "image/jpeg";
    }
}

