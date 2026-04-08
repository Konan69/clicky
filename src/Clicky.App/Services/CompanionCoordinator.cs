using System.Text;
using System.Windows;
using Clicky.App.Models;
using Clicky.App.Services.Audio;
using Clicky.App.Services.Chat;
using Clicky.App.Services.ScreenCapture;
using Clicky.App.Services.Tts;
using Clicky.App.Services.Transcription;
using Clicky.App.SystemPrompts;
using Clicky.App.ViewModels;

namespace Clicky.App.Services;

public sealed class CompanionCoordinator : IDisposable
{
    private readonly CompanionPanelViewModel companionPanelViewModel;
    private readonly OverlayViewModel overlayViewModel;
    private readonly WindowsScreenCaptureService windowsScreenCaptureService;
    private readonly ClaudeWorkerChatClient claudeWorkerChatClient;
    private readonly ElevenLabsTtsClient elevenLabsTtsClient;
    private readonly PointerInstructionParser pointerInstructionParser;
    private readonly WindowsMicrophoneCaptureService windowsMicrophoneCaptureService;
    private readonly AssemblyAiStreamingTranscriptionClient assemblyAiStreamingTranscriptionClient;
    private readonly List<ConversationTurn> conversationHistory = [];
    private readonly SemaphoreSlim sendPromptGate = new(1, 1);

    private IReadOnlyList<CapturedScreen> mostRecentCapturedScreens = [];
    private AssemblyAiStreamingTranscriptionSession? activeAssemblyAiStreamingTranscriptionSession;
    private CancellationTokenSource? activePushToTalkCancellationTokenSource;
    private int pendingOverlayHideVersion;
    private bool isDisposed;

    public CompanionCoordinator(
        CompanionPanelViewModel companionPanelViewModel,
        OverlayViewModel overlayViewModel,
        WindowsScreenCaptureService windowsScreenCaptureService,
        ClaudeWorkerChatClient claudeWorkerChatClient,
        ElevenLabsTtsClient elevenLabsTtsClient,
        PointerInstructionParser pointerInstructionParser,
        WindowsMicrophoneCaptureService windowsMicrophoneCaptureService,
        AssemblyAiStreamingTranscriptionClient assemblyAiStreamingTranscriptionClient)
    {
        this.companionPanelViewModel = companionPanelViewModel;
        this.overlayViewModel = overlayViewModel;
        this.windowsScreenCaptureService = windowsScreenCaptureService;
        this.claudeWorkerChatClient = claudeWorkerChatClient;
        this.elevenLabsTtsClient = elevenLabsTtsClient;
        this.pointerInstructionParser = pointerInstructionParser;
        this.windowsMicrophoneCaptureService = windowsMicrophoneCaptureService;
        this.assemblyAiStreamingTranscriptionClient = assemblyAiStreamingTranscriptionClient;

        overlayViewModel.StatusText = "Ready";
        overlayViewModel.BubbleTitleText = "Clicky";
        overlayViewModel.BubbleBodyText = "Press Ctrl + Alt to talk, or send a manual prompt from the tray panel.";
        overlayViewModel.ResetCursorToCenter(
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        windowsMicrophoneCaptureService.AudioChunkCaptured += HandleAudioChunkCaptured;
        windowsMicrophoneCaptureService.AudioLevelChanged += HandleAudioLevelChanged;
    }

    public async Task BeginPushToTalkAsync()
    {
        if (companionPanelViewModel.CurrentVoiceState != CompanionVoiceState.Idle
            || activePushToTalkCancellationTokenSource is not null)
        {
            return;
        }

        try
        {
            await elevenLabsTtsClient.StopPlaybackAsync();

            activePushToTalkCancellationTokenSource = new CancellationTokenSource();

            EnsureOverlayIsVisible();
            overlayViewModel.CurrentAudioLevel = 0;
            overlayViewModel.BubbleTitleText = "Listening";
            overlayViewModel.BubbleBodyText = "Connecting to AssemblyAI";
            overlayViewModel.StatusText = "Starting microphone stream";
            companionPanelViewModel.StatusText = "Connecting to AssemblyAI";

            TransitionToState(CompanionVoiceState.Listening);

            activeAssemblyAiStreamingTranscriptionSession =
                await assemblyAiStreamingTranscriptionClient.StartStreamingSessionAsync(
                    [],
                    HandleTranscriptUpdated,
                    activePushToTalkCancellationTokenSource.Token);

            windowsMicrophoneCaptureService.StartCapturing();

            companionPanelViewModel.StatusText = "Listening";
            overlayViewModel.BubbleBodyText = "Speak now";
            overlayViewModel.StatusText = "Release Ctrl + Alt when you are done";
        }
        catch (Exception exception)
        {
            await CleanupActivePushToTalkSessionAsync();
            ShowErrorState("Could not start microphone capture", exception.Message);
        }
    }

    public async Task EndPushToTalkAsync()
    {
        if (companionPanelViewModel.CurrentVoiceState != CompanionVoiceState.Listening
            || activeAssemblyAiStreamingTranscriptionSession is null
            || activePushToTalkCancellationTokenSource is null)
        {
            return;
        }

        try
        {
            windowsMicrophoneCaptureService.StopCapturing();
            overlayViewModel.CurrentAudioLevel = 0;
            TransitionToState(CompanionVoiceState.Processing);

            companionPanelViewModel.StatusText = "Finalizing transcript";
            overlayViewModel.BubbleTitleText = "Finalizing";
            overlayViewModel.StatusText = "Waiting for AssemblyAI to finish the turn";

            var finalTranscriptText = await activeAssemblyAiStreamingTranscriptionSession.RequestFinalTranscriptAsync(
                activePushToTalkCancellationTokenSource.Token);

            await CleanupActivePushToTalkSessionAsync();

            if (string.IsNullOrWhiteSpace(finalTranscriptText))
            {
                TransitionToState(CompanionVoiceState.Idle);
                companionPanelViewModel.StatusText = "No speech detected";
                overlayViewModel.BubbleTitleText = "Ready";
                overlayViewModel.BubbleBodyText = "No speech detected";
                overlayViewModel.StatusText = "Idle";
                await HideOverlayIfNeededAsync();
                return;
            }

            companionPanelViewModel.LatestTranscriptText = finalTranscriptText;
            await SendPromptCoreAsync(finalTranscriptText, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await CleanupActivePushToTalkSessionAsync();
            ShowErrorState("Voice request failed", exception.Message);
        }
    }

    public void ToggleOverlayPin()
    {
        companionPanelViewModel.IsOverlayPinned = !companionPanelViewModel.IsOverlayPinned;
        overlayViewModel.IsPinned = companionPanelViewModel.IsOverlayPinned;

        if (companionPanelViewModel.IsOverlayPinned)
        {
            EnsureOverlayIsVisible();
            overlayViewModel.StatusText = "Pinned";
            return;
        }

        if (companionPanelViewModel.CurrentVoiceState == CompanionVoiceState.Idle)
        {
            overlayViewModel.IsVisible = false;
        }
    }

    public async Task SendManualPromptAsync(CancellationToken cancellationToken = default)
    {
        if (companionPanelViewModel.CurrentVoiceState != CompanionVoiceState.Idle)
        {
            return;
        }

        var manualPrompt = companionPanelViewModel.ManualPromptText.Trim();
        if (string.IsNullOrWhiteSpace(manualPrompt))
        {
            return;
        }

        await sendPromptGate.WaitAsync(cancellationToken);

        try
        {
            companionPanelViewModel.ManualPromptText = string.Empty;
            await SendManualPromptCoreAsync(manualPrompt, cancellationToken);
        }
        finally
        {
            sendPromptGate.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        sendPromptGate.Dispose();
        windowsMicrophoneCaptureService.AudioChunkCaptured -= HandleAudioChunkCaptured;
        windowsMicrophoneCaptureService.AudioLevelChanged -= HandleAudioLevelChanged;
        windowsMicrophoneCaptureService.Dispose();
        activePushToTalkCancellationTokenSource?.Cancel();
        activeAssemblyAiStreamingTranscriptionSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        activePushToTalkCancellationTokenSource?.Dispose();
        elevenLabsTtsClient.Dispose();
    }

    private async Task SendManualPromptCoreAsync(string manualPrompt, CancellationToken cancellationToken)
    {
        try
        {
            await elevenLabsTtsClient.StopPlaybackAsync();
            EnsureOverlayIsVisible();
            TransitionToState(CompanionVoiceState.CapturingScreen);
            overlayViewModel.CurrentAudioLevel = 0;

            companionPanelViewModel.LatestTranscriptText = manualPrompt;
            companionPanelViewModel.StatusText = "Capturing desktop";
            overlayViewModel.BubbleTitleText = "Seeing your desktop";
            overlayViewModel.BubbleBodyText = manualPrompt;
            overlayViewModel.StatusText = "Capturing every connected display";

            mostRecentCapturedScreens = await windowsScreenCaptureService.CaptureAllDisplaysAsync(cancellationToken);
            companionPanelViewModel.AttachedDisplaySummary = $"{mostRecentCapturedScreens.Count} display(s) attached";

            TransitionToState(CompanionVoiceState.Processing);
            companionPanelViewModel.StatusText = "Streaming Claude response";
            overlayViewModel.BubbleTitleText = "Thinking";
            overlayViewModel.BubbleBodyText = string.Empty;
            overlayViewModel.StatusText = "Claude is streaming through the worker";

            var responseBuilder = new StringBuilder();

            await foreach (var responseChunk in claudeWorkerChatClient.StreamVisionResponseAsync(
                               companionPanelViewModel.SelectedModel,
                               ClickySystemPrompts.CompanionVisionPrompt,
                               manualPrompt,
                               mostRecentCapturedScreens,
                               conversationHistory,
                               cancellationToken))
            {
                TransitionToState(CompanionVoiceState.Responding);

                responseBuilder.Append(responseChunk);
                var partialResponseText = responseBuilder.ToString();

                companionPanelViewModel.LatestResponseText = partialResponseText;
                companionPanelViewModel.StatusText = "Claude is responding";
                overlayViewModel.BubbleTitleText = "Clicky";
                overlayViewModel.BubbleBodyText = partialResponseText;
                overlayViewModel.StatusText = "Streaming response";
            }

            var finalResponseText = responseBuilder.ToString();
            var parsedPointerResponse = pointerInstructionParser.Parse(finalResponseText);
            var cleanedResponseText = string.IsNullOrWhiteSpace(parsedPointerResponse.CleanedResponseText)
                ? finalResponseText
                : parsedPointerResponse.CleanedResponseText;

            companionPanelViewModel.LatestResponseText = cleanedResponseText;
            overlayViewModel.BubbleBodyText = cleanedResponseText;

            if (parsedPointerResponse.PointInstructions.Count > 0)
            {
                MoveOverlayCursorToPointInstruction(parsedPointerResponse.PointInstructions[0]);
            }
            else
            {
                overlayViewModel.StatusText = "No point tag returned";
            }

            conversationHistory.Add(new ConversationTurn(
                $"User shared the current desktop and said: {manualPrompt}",
                cleanedResponseText));

            if (!string.IsNullOrWhiteSpace(cleanedResponseText))
            {
                TransitionToState(CompanionVoiceState.Speaking);
                companionPanelViewModel.StatusText = "Speaking through ElevenLabs";
                overlayViewModel.BubbleTitleText = "Speaking";
                overlayViewModel.StatusText = "Playing response audio";

                await elevenLabsTtsClient.SpeakAsync(cleanedResponseText, cancellationToken);
            }

            TransitionToState(CompanionVoiceState.Idle);
            companionPanelViewModel.StatusText = "Ready";
            overlayViewModel.BubbleTitleText = "Ready";
            overlayViewModel.StatusText = "Idle";
            overlayViewModel.CurrentAudioLevel = 0;

            await HideOverlayIfNeededAsync();
        }
        catch (OperationCanceledException)
        {
            TransitionToState(CompanionVoiceState.Idle);
            companionPanelViewModel.StatusText = "Canceled";
            overlayViewModel.BubbleTitleText = "Canceled";
            overlayViewModel.StatusText = "Idle";
            overlayViewModel.CurrentAudioLevel = 0;
            await HideOverlayIfNeededAsync();
        }
        catch (Exception exception)
        {
            ShowErrorState("Request failed", exception.Message);
        }
    }

    private async Task CleanupActivePushToTalkSessionAsync()
    {
        windowsMicrophoneCaptureService.StopCapturing();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            overlayViewModel.CurrentAudioLevel = 0;
        });

        if (activeAssemblyAiStreamingTranscriptionSession is not null)
        {
            await activeAssemblyAiStreamingTranscriptionSession.DisposeAsync();
            activeAssemblyAiStreamingTranscriptionSession = null;
        }

        activePushToTalkCancellationTokenSource?.Cancel();
        activePushToTalkCancellationTokenSource?.Dispose();
        activePushToTalkCancellationTokenSource = null;
    }

    private void HandleTranscriptUpdated(string transcriptText)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            companionPanelViewModel.LatestTranscriptText = transcriptText;
            overlayViewModel.BubbleTitleText = "Listening";
            overlayViewModel.BubbleBodyText = string.IsNullOrWhiteSpace(transcriptText) ? "Speak now" : transcriptText;
            overlayViewModel.StatusText = "Release Ctrl + Alt when you are done";
        });
    }

    private async void HandleAudioChunkCaptured(byte[] pcm16AudioChunk)
    {
        if (activeAssemblyAiStreamingTranscriptionSession is null
            || activePushToTalkCancellationTokenSource is null)
        {
            return;
        }

        try
        {
            await activeAssemblyAiStreamingTranscriptionSession.SendAudioChunkAsync(
                pcm16AudioChunk,
                activePushToTalkCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await CleanupActivePushToTalkSessionAsync();
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ShowErrorState("Microphone stream failed", exception.Message);
            });
        }
    }

    private void HandleAudioLevelChanged(double audioLevel)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            overlayViewModel.CurrentAudioLevel = audioLevel;
        });
    }

    private void MoveOverlayCursorToPointInstruction(PointInstruction pointInstruction)
    {
        var zeroBasedScreenIndex = pointInstruction.OneBasedScreenIndex - 1;
        if (zeroBasedScreenIndex < 0 || zeroBasedScreenIndex >= mostRecentCapturedScreens.Count)
        {
            overlayViewModel.StatusText = $"Point tag used screen {pointInstruction.OneBasedScreenIndex}, but that screen was not captured";
            return;
        }

        var capturedScreen = mostRecentCapturedScreens[zeroBasedScreenIndex];
        var absoluteX = capturedScreen.Bounds.Left + pointInstruction.X;
        var absoluteY = capturedScreen.Bounds.Top + pointInstruction.Y;

        overlayViewModel.SetCursorPosition(
            absoluteX,
            absoluteY,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop);

        overlayViewModel.StatusText = $"Pointing at {pointInstruction.Label}";
    }

    private void EnsureOverlayIsVisible()
    {
        Interlocked.Increment(ref pendingOverlayHideVersion);
        overlayViewModel.IsVisible = true;
    }

    private async Task HideOverlayIfNeededAsync()
    {
        var overlayHideVersion = Interlocked.Increment(ref pendingOverlayHideVersion);

        await Task.Delay(TimeSpan.FromSeconds(1));

        if (overlayHideVersion != pendingOverlayHideVersion)
        {
            return;
        }

        if (companionPanelViewModel.IsOverlayPinned || companionPanelViewModel.CurrentVoiceState != CompanionVoiceState.Idle)
        {
            return;
        }

        overlayViewModel.IsVisible = false;
    }

    private void TransitionToState(CompanionVoiceState companionVoiceState)
    {
        companionPanelViewModel.CurrentVoiceState = companionVoiceState;
    }

    private void ShowErrorState(string headline, string details)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => ShowErrorState(headline, details));
            return;
        }

        TransitionToState(CompanionVoiceState.Idle);
        companionPanelViewModel.StatusText = headline;
        companionPanelViewModel.LatestResponseText = details;
        overlayViewModel.BubbleTitleText = "Error";
        overlayViewModel.BubbleBodyText = details;
        overlayViewModel.StatusText = "Idle";
        overlayViewModel.CurrentAudioLevel = 0;
        EnsureOverlayIsVisible();
    }
}
