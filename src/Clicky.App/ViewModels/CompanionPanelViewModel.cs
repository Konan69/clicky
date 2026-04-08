using System.Collections.ObjectModel;
using Clicky.App.Models;

namespace Clicky.App.ViewModels;

public sealed class CompanionPanelViewModel : ObservableObject
{
    private string manualPromptText = string.Empty;
    private string latestTranscriptText = string.Empty;
    private string latestResponseText = string.Empty;
    private string statusText = "Ready";
    private string selectedModel = "claude-sonnet-4-6";
    private string workerBaseUrl = string.Empty;
    private string attachedDisplaySummary = "No screens captured yet";
    private bool isOverlayPinned;
    private CompanionVoiceState currentVoiceState = CompanionVoiceState.Idle;

    public CompanionPanelViewModel()
    {
        AvailableModels =
        [
            "claude-sonnet-4-6",
            "claude-opus-4-6"
        ];
    }

    public ObservableCollection<string> AvailableModels { get; }

    public string ManualPromptText
    {
        get => manualPromptText;
        set
        {
            if (SetProperty(ref manualPromptText, value))
            {
                OnPropertyChanged(nameof(CanSendManualPrompt));
            }
        }
    }

    public string LatestTranscriptText
    {
        get => latestTranscriptText;
        set => SetProperty(ref latestTranscriptText, value);
    }

    public string LatestResponseText
    {
        get => latestResponseText;
        set => SetProperty(ref latestResponseText, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedModel
    {
        get => selectedModel;
        set => SetProperty(ref selectedModel, value);
    }

    public string WorkerBaseUrl
    {
        get => workerBaseUrl;
        set => SetProperty(ref workerBaseUrl, value);
    }

    public string AttachedDisplaySummary
    {
        get => attachedDisplaySummary;
        set => SetProperty(ref attachedDisplaySummary, value);
    }

    public bool IsOverlayPinned
    {
        get => isOverlayPinned;
        set
        {
            if (SetProperty(ref isOverlayPinned, value))
            {
                OnPropertyChanged(nameof(OverlayToggleButtonLabel));
            }
        }
    }

    public CompanionVoiceState CurrentVoiceState
    {
        get => currentVoiceState;
        set
        {
            if (SetProperty(ref currentVoiceState, value))
            {
                OnPropertyChanged(nameof(CurrentStateLabel));
                OnPropertyChanged(nameof(CanSendManualPrompt));
            }
        }
    }

    public bool CanSendManualPrompt =>
        CurrentVoiceState == CompanionVoiceState.Idle
        && !string.IsNullOrWhiteSpace(ManualPromptText);

    public string OverlayToggleButtonLabel => IsOverlayPinned ? "Hide overlay" : "Show overlay";

    public string CurrentStateLabel => CurrentVoiceState switch
    {
        CompanionVoiceState.Idle => "Idle",
        CompanionVoiceState.Listening => "Listening",
        CompanionVoiceState.CapturingScreen => "Capturing desktop",
        CompanionVoiceState.Processing => "Thinking",
        CompanionVoiceState.Responding => "Responding",
        CompanionVoiceState.Speaking => "Speaking",
        _ => "Idle"
    };
}

