using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using OCoD.Protocol;

namespace OCoD.CharacterHost;

/// <summary>Static layer composition of an imported character's normal-state images.</summary>
internal sealed class CharacterView : ViewBase
{
    private static readonly string[] LayerOrderDefaults =
    [
        "L:back", "body", "head", "L:side", "L:ex1", "L:ex2", "L:ex3", "L:ex4",
    ];

    private readonly CharacterRecord _record;
    private readonly IReadOnlyDictionary<string, Bitmap> _images;

    public CharacterView(CharacterRecord record, IReadOnlyDictionary<string, Bitmap> images)
        : base(ViewInitializationStrategy.Lazy)
    {
        _record = record;
        _images = images;
        Initialize();
    }

    public Control? FlipTarget { get; private set; }

    protected override object Build()
    {
        var canvasWidth = _images.Values.Max(image => image.Size.Width);
        var canvasHeight = _images.Values.Max(image => image.Size.Height);
        var canvas = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
        };

        foreach (var slot in BuildRenderOrder())
        {
            if (!_images.TryGetValue(slot, out var bitmap))
            {
                continue;
            }

            var image = new Image
            {
                Source = bitmap,
                Width = canvasWidth,
                Height = canvasHeight,
                Stretch = Stretch.Uniform,
            };

            var (x, y) = GetLayerOffset(slot);
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, y);
            canvas.Children.Add(image);
        }

        var viewbox = new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        FlipTarget = viewbox;

        return new Border
        {
            Background = Brushes.Transparent,
            Child = viewbox,
        };
    }

    private IEnumerable<string> BuildRenderOrder()
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string slot)
        {
            if (seen.Add(slot))
            {
                order.Add(slot);
            }
        }

        foreach (var item in _record.PartOrder)
        {
            switch (item)
            {
                case "body":
                    Add("body");
                    break;
                case "head":
                    Add("head");
                    break;
                case "L:back":
                    Add("back");
                    break;
                case "L:side":
                    Add("side");
                    break;
                case "L:ex1":
                case "L:ex2":
                case "L:ex3":
                case "L:ex4":
                    Add(item[2..]);
                    break;
            }
        }

        foreach (var item in LayerOrderDefaults)
        {
            switch (item)
            {
                case "body":
                case "head":
                    Add(item);
                    break;
                case "L:back":
                    Add("back");
                    break;
                case "L:side":
                    Add("side");
                    break;
                default:
                    if (item.StartsWith("L:", StringComparison.Ordinal))
                    {
                        Add(item[2..]);
                    }

                    break;
            }
        }

        return order;
    }

    private (int X, int Y) GetLayerOffset(string slot)
    {
        var layer = slot switch
        {
            "back" => "back",
            "side" => "side",
            "ex1" or "ex2" or "ex3" or "ex4" => slot,
            _ => null,
        };

        if (layer is null)
        {
            return (0, 0);
        }

        var offset = _record.LayerOffsets.FirstOrDefault(item =>
            string.Equals(item.Layer, layer, StringComparison.Ordinal));

        return offset is null ? (0, 0) : (offset.X, offset.Y);
    }
}