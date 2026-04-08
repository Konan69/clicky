using NAudio.Wave;

namespace Clicky.App.Services.Audio;

public sealed class WindowsMicrophoneCaptureService : IDisposable
{
    private WaveInEvent? waveInEvent;

    public event Action<byte[]>? AudioChunkCaptured;

    public event Action<double>? AudioLevelChanged;

    public void StartCapturing()
    {
        if (waveInEvent is not null)
        {
            return;
        }

        waveInEvent = new WaveInEvent
        {
            DeviceNumber = 0,
            BufferMilliseconds = 100,
            WaveFormat = new WaveFormat(16_000, 16, 1)
        };

        waveInEvent.DataAvailable += HandleAudioDataAvailable;
        waveInEvent.StartRecording();
    }

    public void StopCapturing()
    {
        if (waveInEvent is null)
        {
            return;
        }

        waveInEvent.DataAvailable -= HandleAudioDataAvailable;
        waveInEvent.StopRecording();
        waveInEvent.Dispose();
        waveInEvent = null;
    }

    public void Dispose()
    {
        StopCapturing();
    }

    private void HandleAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var pcm16AudioChunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, pcm16AudioChunk, 0, e.BytesRecorded);

        AudioChunkCaptured?.Invoke(pcm16AudioChunk);
        AudioLevelChanged?.Invoke(CalculateNormalizedAudioLevel(pcm16AudioChunk));
    }

    private static double CalculateNormalizedAudioLevel(byte[] pcm16AudioChunk)
    {
        double maxAbsoluteSampleValue = 0;

        for (var byteIndex = 0; byteIndex < pcm16AudioChunk.Length - 1; byteIndex += 2)
        {
            var sample = BitConverter.ToInt16(pcm16AudioChunk, byteIndex);
            var normalizedSample = Math.Abs(sample) / 32768d;
            maxAbsoluteSampleValue = Math.Max(maxAbsoluteSampleValue, normalizedSample);
        }

        return maxAbsoluteSampleValue;
    }
}

