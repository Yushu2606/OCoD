using System.Runtime.InteropServices;

using Avalonia.Controls;

namespace OCoD.CharacterHost;

/// <summary>Windows implementation using the WS_EX_TRANSPARENT extended window style.</summary>
internal static partial class WindowsClickThrough
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x0020;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static bool TrySet(Window window, bool enabled)
    {
        if (!TryGetWindowHandle(window, out var hwnd) || !TryGetStyle(hwnd, out var style))
        {
            return false;
        }

        var updatedStyle = enabled ? style | (long)WsExTransparent : style & ~(long)WsExTransparent;
        if (updatedStyle == style)
        {
            return true;
        }

        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLongPtr(hwnd, GwlExStyle, (nint)updatedStyle);
        if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        return SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static bool TryGet(Window window, out bool enabled)
    {
        enabled = false;

        if (!TryGetWindowHandle(window, out var hwnd) || !TryGetStyle(hwnd, out var style))
        {
            return false;
        }

        enabled = (style & (long)WsExTransparent) != 0;
        return true;
    }

    private static bool TryGetWindowHandle(Window window, out nint hwnd)
    {
        hwnd = 0;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null
            || platformHandle.Handle == 0
            || !string.Equals(platformHandle.HandleDescriptor, "HWND", StringComparison.Ordinal))
        {
            return false;
        }

        hwnd = platformHandle.Handle;
        return true;
    }

    private static bool TryGetStyle(nint hwnd, out long style)
    {
        style = 0;

        Marshal.SetLastPInvokeError(0);
        var currentStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        if (currentStyle == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        style = (long)currentStyle;
        return true;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}