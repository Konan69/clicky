using System.ComponentModel;
using System.Windows;
using Clicky.App.Configuration;
using Clicky.App.Services.Audio;
using Clicky.App.Services;
using Clicky.App.Services.Chat;
using Clicky.App.Services.Input;
using Clicky.App.Services.ScreenCapture;
using Clicky.App.Services.Tts;
using Clicky.App.Services.Transcription;
using Clicky.App.ViewModels;
using Clicky.App.Views;

namespace Clicky.App;

public sealed class AppBootstrapper : IDisposable
{
    private readonly ClickyAppConfiguration clickyAppConfiguration;
    private readonly CompanionPanelViewModel companionPanelViewModel;
    private readonly OverlayViewModel overlayViewModel;
    private readonly CompanionCoordinator companionCoordinator;
    private readonly CompanionPanelWindow companionPanelWindow;
    private readonly OverlayWindow overlayWindow;
    private readonly TrayIconHost trayIconHost;
    private readonly GlobalPushToTalkHook globalPushToTalkHook;

    private bool isDisposed;

    public AppBootstrapper()
    {
        clickyAppConfiguration = AppConfigurationLoader.Load();

        companionPanelViewModel = new CompanionPanelViewModel
        {
            SelectedModel = clickyAppConfiguration.DefaultClaudeModel,
            WorkerBaseUrl = clickyAppConfiguration.WorkerBaseUrl,
            StatusText = "Ready"
        };

        overlayViewModel = new OverlayViewModel();

        var windowsScreenCaptureService = new WindowsScreenCaptureService();
        var claudeWorkerChatClient = new ClaudeWorkerChatClient(clickyAppConfiguration.WorkerBaseUrl);
        var elevenLabsTtsClient = new ElevenLabsTtsClient(clickyAppConfiguration.WorkerBaseUrl);
        var pointerInstructionParser = new PointerInstructionParser();
        var windowsMicrophoneCaptureService = new WindowsMicrophoneCaptureService();
        var assemblyAiStreamingTranscriptionClient = new AssemblyAiStreamingTranscriptionClient(clickyAppConfiguration.WorkerBaseUrl);

        companionCoordinator = new CompanionCoordinator(
            companionPanelViewModel,
            overlayViewModel,
            windowsScreenCaptureService,
            claudeWorkerChatClient,
            elevenLabsTtsClient,
            pointerInstructionParser,
            windowsMicrophoneCaptureService,
            assemblyAiStreamingTranscriptionClient);

        companionPanelWindow = new CompanionPanelWindow(companionPanelViewModel, companionCoordinator);
        overlayWindow = new OverlayWindow(overlayViewModel);
        trayIconHost = new TrayIconHost();
        globalPushToTalkHook = new GlobalPushToTalkHook();

        trayIconHost.TogglePanelRequested += HandleTogglePanelRequested;
        trayIconHost.ExitRequested += HandleExitRequested;
        globalPushToTalkHook.PushToTalkPressed += HandlePushToTalkPressed;
        globalPushToTalkHook.PushToTalkReleased += HandlePushToTalkReleased;
        companionPanelViewModel.PropertyChanged += HandleCompanionPanelViewModelPropertyChanged;
    }

    public Task StartAsync()
    {
        trayIconHost.UpdateStatusText(companionPanelViewModel.CurrentStateLabel);
        globalPushToTalkHook.Start();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        trayIconHost.TogglePanelRequested -= HandleTogglePanelRequested;
        trayIconHost.ExitRequested -= HandleExitRequested;
        globalPushToTalkHook.PushToTalkPressed -= HandlePushToTalkPressed;
        globalPushToTalkHook.PushToTalkReleased -= HandlePushToTalkReleased;
        companionPanelViewModel.PropertyChanged -= HandleCompanionPanelViewModelPropertyChanged;

        globalPushToTalkHook.Dispose();
        trayIconHost.Dispose();
        companionCoordinator.Dispose();

        if (overlayWindow.IsLoaded)
        {
            overlayWindow.Close();
        }

        if (companionPanelWindow.IsLoaded)
        {
            companionPanelWindow.Close();
        }
    }

    private void HandleTogglePanelRequested(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (companionPanelWindow.IsVisible)
            {
                companionPanelWindow.HidePanel();
                return;
            }

            companionPanelWindow.ShowPanel();
        });
    }

    private void HandleExitRequested(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    private void HandlePushToTalkPressed(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await companionCoordinator.BeginPushToTalkAsync();
        });
    }

    private void HandlePushToTalkReleased(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await companionCoordinator.EndPushToTalkAsync();
        });
    }

    private void HandleCompanionPanelViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompanionPanelViewModel.CurrentStateLabel))
        {
            trayIconHost.UpdateStatusText(companionPanelViewModel.CurrentStateLabel);
        }
    }
}
