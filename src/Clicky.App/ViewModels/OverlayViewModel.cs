using System.Windows;

namespace Clicky.App.ViewModels;

public sealed class OverlayViewModel : ObservableObject
{
    private bool isVisible;
    private bool isPinned;
    private string bubbleTitleText = "Clicky";
    private string bubbleBodyText = string.Empty;
    private string statusText = "Ready";
    private double currentAudioLevel;
    private Thickness cursorMargin = new(42, 42, 0, 0);

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    public bool IsPinned
    {
        get => isPinned;
        set => SetProperty(ref isPinned, value);
    }

    public string BubbleTitleText
    {
        get => bubbleTitleText;
        set => SetProperty(ref bubbleTitleText, value);
    }

    public string BubbleBodyText
    {
        get => bubbleBodyText;
        set => SetProperty(ref bubbleBodyText, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public double CurrentAudioLevel
    {
        get => currentAudioLevel;
        set => SetProperty(ref currentAudioLevel, value);
    }

    public Thickness CursorMargin
    {
        get => cursorMargin;
        private set => SetProperty(ref cursorMargin, value);
    }

    public void SetCursorPosition(
        double absoluteX,
        double absoluteY,
        double virtualScreenLeft,
        double virtualScreenTop)
    {
        CursorMargin = new Thickness(
            Math.Max(absoluteX - virtualScreenLeft, 24),
            Math.Max(absoluteY - virtualScreenTop, 24),
            0,
            0);
    }

    public void ResetCursorToCenter(double virtualScreenWidth, double virtualScreenHeight)
    {
        CursorMargin = new Thickness(
            Math.Max(virtualScreenWidth / 2.0, 24),
            Math.Max(virtualScreenHeight / 2.0, 24),
            0,
            0);
    }
}
