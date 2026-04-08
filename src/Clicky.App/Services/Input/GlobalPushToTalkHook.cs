using System.Diagnostics;
using System.Runtime.InteropServices;
using Clicky.App.Native;

namespace Clicky.App.Services.Input;

public sealed class GlobalPushToTalkHook : IDisposable
{
    private static readonly HashSet<int> ControlVirtualKeys =
    [
        User32.VkControl,
        User32.VkLeftControl,
        User32.VkRightControl
    ];

    private static readonly HashSet<int> AltVirtualKeys =
    [
        User32.VkMenu,
        User32.VkLeftMenu,
        User32.VkRightMenu
    ];

    private readonly HashSet<int> pressedVirtualKeys = [];
    private readonly User32.LowLevelKeyboardProcedure keyboardHookProcedure;

    private nint keyboardHookHandle;
    private bool isShortcutPressed;

    public GlobalPushToTalkHook()
    {
        keyboardHookProcedure = HandleLowLevelKeyboardEvent;
    }

    public event EventHandler? PushToTalkPressed;

    public event EventHandler? PushToTalkReleased;

    public void Start()
    {
        if (keyboardHookHandle != nint.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        var moduleHandle = User32.GetModuleHandle(currentProcess.MainModule?.ModuleName);

        keyboardHookHandle = User32.SetWindowsHookEx(
            User32.WhKeyboardLl,
            keyboardHookProcedure,
            moduleHandle,
            0);

        if (keyboardHookHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install low-level keyboard hook. Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        if (keyboardHookHandle == nint.Zero)
        {
            return;
        }

        User32.UnhookWindowsHookEx(keyboardHookHandle);
        keyboardHookHandle = nint.Zero;
        pressedVirtualKeys.Clear();
        isShortcutPressed = false;
    }

    private nint HandleLowLevelKeyboardEvent(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var keyboardMessage = wParam.ToInt32();
            var keyboardData = Marshal.PtrToStructure<User32.KbdllHookStruct>(lParam);
            var virtualKeyCode = unchecked((int)keyboardData.VirtualKeyCode);

            if (keyboardMessage is User32.WmKeyDown or User32.WmSysKeyDown)
            {
                pressedVirtualKeys.Add(virtualKeyCode);
                EvaluateShortcutState();
            }
            else if (keyboardMessage is User32.WmKeyUp or User32.WmSysKeyUp)
            {
                pressedVirtualKeys.Remove(virtualKeyCode);
                EvaluateShortcutState();
            }
        }

        return User32.CallNextHookEx(keyboardHookHandle, code, wParam, lParam);
    }

    private void EvaluateShortcutState()
    {
        var isControlPressed = pressedVirtualKeys.Any(ControlVirtualKeys.Contains);
        var isAltPressed = pressedVirtualKeys.Any(AltVirtualKeys.Contains);
        var shouldShortcutBeActive = isControlPressed && isAltPressed;

        if (shouldShortcutBeActive && !isShortcutPressed)
        {
            isShortcutPressed = true;
            PushToTalkPressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!shouldShortcutBeActive && isShortcutPressed)
        {
            isShortcutPressed = false;
            PushToTalkReleased?.Invoke(this, EventArgs.Empty);
        }
    }
}

