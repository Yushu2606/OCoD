using Avalonia.Controls;

namespace OCoD.CharacterHost;

/// <summary>
/// Turns OS-level click-through on and off for a top-level window.
/// A click-through window passes mouse input to the desktop; the tray menu remains reachable.
/// </summary>
internal static class ClickThroughService
{
    public static bool TrySet(Window window, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (OperatingSystem.IsWindows())
        {
            return WindowsClickThrough.TrySet(window, enabled);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacClickThrough.TrySet(window, enabled);
        }

        return false;
    }

    public static bool TryGet(Window window, out bool enabled)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (OperatingSystem.IsWindows())
        {
            return WindowsClickThrough.TryGet(window, out enabled);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacClickThrough.TryGet(window, out enabled);
        }

        enabled = false;
        return false;
    }
}