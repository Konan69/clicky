using System.ComponentModel;
using System.Windows;
using Clicky.App.Native;
using Clicky.App.ViewModels;

namespace Clicky.App.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel overlayViewModel;

    public OverlayWindow(OverlayViewModel overlayViewModel)
    {
        InitializeComponent();

        this.overlayViewModel = overlayViewModel;
        DataContext = overlayViewModel;

        SourceInitialized += HandleSourceInitialized;
        Loaded += HandleLoaded;
        overlayViewModel.PropertyChanged += HandleOverlayViewModelPropertyChanged;
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        WindowStyleExtensions.ApplyOverlayWindowStyles(this);
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        PositionAcrossVirtualDesktop();
    }

    private void HandleOverlayViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
        {
            Dispatcher.Invoke(SynchronizeWindowVisibility);
        }
    }

    private void SynchronizeWindowVisibility()
    {
        if (overlayViewModel.IsVisible)
        {
            if (!IsVisible)
            {
                PositionAcrossVirtualDesktop();
                Show();
            }

            return;
        }

        if (IsVisible)
        {
            Hide();
        }
    }

    private void PositionAcrossVirtualDesktop()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }
}
