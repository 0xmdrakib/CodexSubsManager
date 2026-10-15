using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace CodexAccounts;

// Locally embedded Lucide vectors. License and pinned revision are included in the release.
public sealed class Glyph : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Glyph), new FrameworkPropertyMetadata("Account", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    static readonly Dictionary<string, string> Assets = new()
    {
        ["Settings"]="settings", ["Close"]="x", ["Minimize"]="minus", ["Search"]="search", ["Refresh"]="refresh-cw",
        ["Add"]="plus", ["Import"]="download", ["Launch"]="external-link", ["More"]="ellipsis", ["Arrow"]="arrow-right",
        ["Chevron"]="chevron-right", ["Lock"]="lock-keyhole", ["Shield"]="shield-check", ["Account"]="user-round",
        ["Stack"]="layers", ["Return"]="undo-2", ["Check"]="check"
    };
    static readonly Dictionary<string, Geometry> Shapes = Assets.ToDictionary(p => p.Key, p => Load(p.Value));
    internal static IEnumerable<string> Kinds => Assets.Keys;
    static Geometry Load(string name)
    {
        using var stream = typeof(Glyph).Assembly.GetManifestResourceStream("CodexAccounts.Icons." + name + ".svg") ?? throw new InvalidDataException("A bundled icon is missing: " + name);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var svg = XDocument.Load(reader).Root ?? throw new InvalidDataException("Invalid bundled icon.");
        var group = new GeometryGroup();
        foreach (var element in svg.Elements())
        {
            double Number(string attribute) => double.Parse((string?)element.Attribute(attribute) ?? "0", CultureInfo.InvariantCulture);
            Geometry shape = element.Name.LocalName switch
            {
                "path" => Geometry.Parse((string?)element.Attribute("d") ?? ""),
                "circle" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("r"), Number("r")),
                "line" => new LineGeometry(new Point(Number("x1"), Number("y1")), new Point(Number("x2"), Number("y2"))),
                "rect" => new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")), Number("rx"), element.Attribute("ry") is null ? Number("rx") : Number("ry")),
                _ => throw new InvalidDataException("Unsupported bundled icon element: " + element.Name.LocalName)
            };
            group.Children.Add(shape);
        }
        if (group.Children.Count == 0) throw new InvalidDataException("Empty bundled icon.");
        group.Freeze(); return group;
    }
    static Glyph() => ForegroundProperty.OverrideMetadata(typeof(Glyph), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public Glyph() { Width = 18; Height = 18; Focusable = false; IsHitTestVisible = false; UseLayoutRounding = true; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!Shapes.TryGetValue(Kind, out var geometry)) throw new InvalidOperationException("Unknown icon: " + Kind);
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        var pen = new Pen(Foreground, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale)); dc.DrawGeometry(null, pen, geometry); dc.Pop(); dc.Pop();
    }
}
