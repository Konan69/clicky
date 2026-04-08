using System.Windows;
using System.Windows.Input;
using Clicky.App.Native;
using Clicky.App.Services;
using Clicky.App.ViewModels;

namespace Clicky.App.Views;

public partial class CompanionPanelWindow : Window
{
    private readonly CompanionPanelViewModel companionPanelViewModel;
    private readonly CompanionCoordinator companionCoordinator;

    public CompanionPanelWindow(
        CompanionPanelViewModel companionPanelViewModel,
        CompanionCoordinator companionCoordinator)
    {
        InitializeComponent();

        this.companionPanelViewModel = companionPanelViewModel;
        this.companionCoordinator = companionCoordinator;

        DataContext = companionPanelViewModel;

        SourceInitialized += HandleSourceInitialized;
        Deactivated += HandleWindowDeactivated;
        PreviewKeyDown += HandlePreviewKeyDown;
    }

    public void ShowPanel()
    {
        PositionNearBottomRightCorner();

        Show();
        Activate();
        ManualPromptTextBox.Focus();
    }

    public void HidePanel()
    {
        Hide();
    }

    private async void HandleSendPromptButtonClick(object sender, RoutedEventArgs e)
    {
        await companionCoordinator.SendManualPromptAsync();
    }

    private void HandleOverlayToggleButtonClick(object sender, RoutedEventArgs e)
    {
        companionCoordinator.ToggleOverlayPin();
    }

    private void HandleQuitButtonClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        WindowStyleExtensions.ApplyPanelWindowStyles(this);
    }

    private void HandleWindowDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private async void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await companionCoordinator.SendManualPromptAsync();
        }
    }

    private void PositionNearBottomRightCorner()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Right - Width - 20, workArea.Left + 20);
        Top = Math.Max(workArea.Bottom - Height - 20, workArea.Top + 20);
    }
}

