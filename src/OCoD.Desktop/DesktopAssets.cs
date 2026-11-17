using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;

using OCoD.Localization;

namespace OCoD.Desktop;

/// <summary>Shared Avalonia resources used by the character runtime and the global daemon.</summary>
public static class DesktopAssets
{
    private const string IconResourceName = "OCoD.Desktop.Assets.tray.png";

    public static WindowIcon LoadIcon()
    {
        using var stream = typeof(DesktopAssets).Assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException(
                $"Missing embedded window icon '{IconResourceName}'.");

        return new WindowIcon(stream);
    }

    public static void ConfigureApplication(Application application, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(localization);

        application.Name = localization.Get(LocKeys.AppName);
        application.Styles.Add(new FluentTheme());
    }
}