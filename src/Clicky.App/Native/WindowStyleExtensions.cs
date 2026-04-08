using System.Windows;
using System.Windows.Interop;

namespace Clicky.App.Native;

public static class WindowStyleExtensions
{
    public static void ApplyOverlayWindowStyles(Window window)
    {
        ApplyExtendedStyles(
            window,
            User32.WsExToolWindow
            | User32.WsExNoActivate
            | User32.WsExLayered
            | User32.WsExTransparent);
    }

    public static void ApplyPanelWindowStyles(Window window)
    {
        ApplyExtendedStyles(window, User32.WsExToolWindow);
    }

    private static void ApplyExtendedStyles(Window window, long additionalExtendedStyles)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource hwndSource)
        {
            return;
        }

        var existingExtendedStyles = User32.GetWindowLongPtr(hwndSource.Handle, User32.GwlExStyle).ToInt64();
        var combinedExtendedStyles = existingExtendedStyles | additionalExtendedStyles;

        User32.SetWindowLongPtr(hwndSource.Handle, User32.GwlExStyle, new nint(combinedExtendedStyles));
        User32.SetWindowPos(
            hwndSource.Handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            User32.SwpNoMove | User32.SwpNoSize | User32.SwpNoZOrder | User32.SwpFrameChanged);
    }
}

