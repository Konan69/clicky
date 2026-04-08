using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Clicky.App.Services.Tts;

public sealed class ElevenLabsTtsClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly Uri ttsEndpointUri;
    private readonly MediaPlayer mediaPlayer = new();

    private string? activeAudioFilePath;

    public ElevenLabsTtsClient(string workerBaseUrl)
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        ttsEndpointUri = new Uri($"{workerBaseUrl.TrimEnd('/')}/tts");
    }

    public async Task SpeakAsync(string responseText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return;
        }

        await StopPlaybackAsync();

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, ttsEndpointUri);
        httpRequestMessage.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                text = responseText,
                model_id = "eleven_flash_v2_5"
            }),
            Encoding.UTF8,
            "application/json");

        using var httpResponseMessage = await httpClient.SendAsync(
            httpRequestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var errorBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"ElevenLabs proxy returned {(int)httpResponseMessage.StatusCode}: {errorBody}");
        }

        var audioBytes = await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken);
        var tempAudioFilePath = Path.Combine(Path.GetTempPath(), $"clicky-tts-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tempAudioFilePath, audioBytes, cancellationToken);

        var playbackCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? mediaEndedHandler = null;
        EventHandler<ExceptionEventArgs>? mediaFailedHandler = null;

        mediaEndedHandler = (_, _) => playbackCompletionSource.TrySetResult();
        mediaFailedHandler = (_, exceptionEventArgs) =>
            playbackCompletionSource.TrySetException(
                exceptionEventArgs.ErrorException ?? new InvalidOperationException("Media playback failed"));

        activeAudioFilePath = tempAudioFilePath;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            mediaPlayer.MediaEnded += mediaEndedHandler;
            mediaPlayer.MediaFailed += mediaFailedHandler;
            mediaPlayer.Open(new Uri(tempAudioFilePath));
            mediaPlayer.Play();
        }, DispatcherPriority.Send, cancellationToken);

        try
        {
            await playbackCompletionSource.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                mediaPlayer.Stop();
                mediaPlayer.Close();
                mediaPlayer.MediaEnded -= mediaEndedHandler;
                mediaPlayer.MediaFailed -= mediaFailedHandler;
            }, DispatcherPriority.Send);

            DeleteTempAudioFile(tempAudioFilePath);

            if (activeAudioFilePath == tempAudioFilePath)
            {
                activeAudioFilePath = null;
            }
        }
    }

    public async Task StopPlaybackAsync()
    {
        await StopPlaybackAsyncCore();
    }

    public void Dispose()
    {
        httpClient.Dispose();
        mediaPlayer.Close();

        if (activeAudioFilePath is not null)
        {
            DeleteTempAudioFile(activeAudioFilePath);
        }
    }

    private async Task StopPlaybackAsyncCore()
    {
        var tempAudioFilePath = activeAudioFilePath;
        if (tempAudioFilePath is null)
        {
            return;
        }

        activeAudioFilePath = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            mediaPlayer.Stop();
            mediaPlayer.Close();
        });

        DeleteTempAudioFile(tempAudioFilePath);
    }

    private static void DeleteTempAudioFile(string tempAudioFilePath)
    {
        try
        {
            if (File.Exists(tempAudioFilePath))
            {
                File.Delete(tempAudioFilePath);
            }
        }
        catch
        {
        }
    }
}
