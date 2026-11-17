using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;

using OCoD.Localization;

namespace OCoD.CharacterHost;

/// <summary>
/// The visible placeholder that stands in for a character until the runtime can load one.
/// Its <see cref="FlipTarget"/> is mirrored independently from the caption and menus.
/// </summary>
internal sealed class CharacterPlaceholderView : ViewBase
{
    private readonly LocalizationService _localization;

    public CharacterPlaceholderView(LocalizationService localization)
        : base(ViewInitializationStrategy.Lazy)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Initialize();
    }

    /// <summary>The visual that the horizontal-flip command mirrors.</summary>
    public Control? FlipTarget { get; private set; }

    protected override object Build()
    {
        FlipTarget = BuildCharacterArtwork();

        var title = new TextBlock
        {
            Text = _localization.Get(LocKeys.PlaceholderTitle),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var hint = new TextBlock
        {
            Text = _localization.Get(LocKeys.PlaceholderHint),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var caption = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0F, 0x17, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(12, 4, 12, 12),
            Child = new StackPanel
            {
                Spacing = 3,
                Children = { title, hint },
            },
        };

        var artwork = new Viewbox
        {
            Child = FlipTarget,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(14, 8, 14, 0),
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = Brushes.Transparent,
        };

        layout.Children.Add(artwork);
        Grid.SetRow(caption, 1);
        layout.Children.Add(caption);

        return layout;
    }

    private static Control BuildCharacterArtwork()
    {
        var canvas = new Canvas
        {
            Width = 220,
            Height = 240,
        };

        var body = new Border
        {
            Width = 190,
            Height = 205,
            CornerRadius = new CornerRadius(88, 88, 68, 68),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#8BE8DD"), 0),
                    new GradientStop(Color.Parse("#47C3B6"), 1),
                },
            },
        };
        Canvas.SetLeft(body, 15);
        Canvas.SetTop(body, 26);

        var highlight = new Ellipse
        {
            Width = 44,
            Height = 24,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(highlight, 41);
        Canvas.SetTop(highlight, 49);

        var tuft = new Ellipse
        {
            Width = 46,
            Height = 46,
            Fill = new SolidColorBrush(Color.Parse("#FFC85C")),
        };
        Canvas.SetLeft(tuft, 143);
        Canvas.SetTop(tuft, 6);

        var leftEye = CreateEye();
        Canvas.SetLeft(leftEye, 70);
        Canvas.SetTop(leftEye, 99);

        var rightEye = CreateEye();
        Canvas.SetLeft(rightEye, 132);
        Canvas.SetTop(rightEye, 99);

        var mouth = new Border
        {
            Width = 30,
            Height = 9,
            CornerRadius = new CornerRadius(4.5),
            Background = new SolidColorBrush(Color.Parse("#18263A")),
        };
        Canvas.SetLeft(mouth, 95);
        Canvas.SetTop(mouth, 154);

        var leftBlush = CreateBlush();
        Canvas.SetLeft(leftBlush, 50);
        Canvas.SetTop(leftBlush, 137);

        var rightBlush = CreateBlush();
        Canvas.SetLeft(rightBlush, 146);
        Canvas.SetTop(rightBlush, 137);

        canvas.Children.Add(body);
        canvas.Children.Add(highlight);
        canvas.Children.Add(tuft);
        canvas.Children.Add(leftEye);
        canvas.Children.Add(rightEye);
        canvas.Children.Add(mouth);
        canvas.Children.Add(leftBlush);
        canvas.Children.Add(rightBlush);

        return canvas;
    }

    private static Ellipse CreateEye() => new()
    {
        Width = 18,
        Height = 23,
        Fill = new SolidColorBrush(Color.Parse("#18263A")),
    };

    private static Ellipse CreateBlush() => new()
    {
        Width = 26,
        Height = 12,
        Fill = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x9E, 0xB5)),
    };
}