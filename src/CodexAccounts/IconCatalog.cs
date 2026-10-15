using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccounts;

internal static class IconCatalog
{
    internal static void Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        var page = new StackPanel { Width = 800, Background = Brushes.White };
        foreach (bool dark in new[] { false, true })
        {
            var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#252939" : "#E9ECF3"));
            var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#F2F1FA" : "#24283F"));
            var section = new StackPanel { Background = background };
            section.Children.Add(new TextBlock { Text = (dark ? "Dark" : "Light") + " · 14, 16, 18 and 24 DIP", Foreground = foreground, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Margin = new Thickness(14, 12, 14, 8) });
            var grid = new UniformGrid { Columns = 6, Rows = 3, Margin = new Thickness(8) };
            foreach (var kind in Glyph.Kinds)
            {
                var tile = new StackPanel { Margin = new Thickness(6) };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (double size in new[] { 14d, 16d, 18d, 24d }) row.Children.Add(new Glyph { Kind = kind, Width = size, Height = size, Foreground = foreground, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                tile.Children.Add(row); tile.Children.Add(new TextBlock { Text = kind, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = foreground, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 5) }); grid.Children.Add(tile);
            }
            section.Children.Add(grid); page.Children.Add(section);
        }
        page.Measure(new Size(800, double.PositiveInfinity)); page.Arrange(new Rect(page.DesiredSize)); page.UpdateLayout();
        foreach (double scale in new[] { 1d, 1.25d, 1.5d, 2d })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * scale), (int)Math.Ceiling(page.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(page); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"icons-{scale * 100:0}.png")); encoder.Save(file);
        }
    }
}
