using System.Runtime.InteropServices;

using Avalonia.Controls;

namespace OCoD.CharacterHost;

/// <summary>macOS implementation using NSWindow.ignoresMouseEvents.</summary>
internal static partial class MacClickThrough
{
    private static nint _setIgnoresMouseEventsSelector;
    private static nint _ignoresMouseEventsSelector;

    public static bool TrySet(Window window, bool enabled)
    {
        if (!TryGetWindowHandle(window, out var nsWindow))
        {
            return false;
        }

        var selector = _setIgnoresMouseEventsSelector;
        if (selector == 0)
        {
            selector = sel_registerName("setIgnoresMouseEvents:");
            _setIgnoresMouseEventsSelector = selector;
        }

        objc_msgSend_SetIgnoresMouseEvents(nsWindow, selector, enabled);
        return true;
    }

    public static bool TryGet(Window window, out bool enabled)
    {
        enabled = false;

        if (!TryGetWindowHandle(window, out var nsWindow))
        {
            return false;
        }

        var selector = _ignoresMouseEventsSelector;
        if (selector == 0)
        {
            selector = sel_registerName("ignoresMouseEvents");
            _ignoresMouseEventsSelector = selector;
        }

        enabled = objc_msgSend_GetIgnoresMouseEvents(nsWindow, selector);
        return true;
    }

    private static bool TryGetWindowHandle(Window window, out nint nsWindow)
    {
        nsWindow = 0;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null
            || platformHandle.Handle == 0
            || !string.Equals(platformHandle.HandleDescriptor, "NSWindow", StringComparison.Ordinal))
        {
            return false;
        }

        nsWindow = platformHandle.Handle;
        return true;
    }

    [LibraryImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_SetIgnoresMouseEvents(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool ignoresMouseEvents);

    [LibraryImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool objc_msgSend_GetIgnoresMouseEvents(nint receiver, nint selector);

    [LibraryImport("libobjc.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);
}