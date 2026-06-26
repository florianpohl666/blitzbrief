using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BlitzBrief.Windows;

internal static class AppIcon
{
    private static Icon? _tray;
    private static BitmapSource? _wpf;

    public static Icon ForTray() => _tray ??= CreateIcon();

    public static BitmapSource ForWpf()
    {
        if (_wpf is not null) return _wpf;

        const int size = 64;
        using var bmp = CreateBitmap(size);
        var bd = bmp.LockBits(
            new Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        _wpf = BitmapSource.Create(
            size, size, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null, bd.Scan0, bd.Stride * size, bd.Stride);
        bmp.UnlockBits(bd);
        _wpf.Freeze();
        return _wpf;
    }

    private static Icon CreateIcon()
    {
        using var bmp = CreateBitmap(64);
        return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
    }

    // Design is specified in a 200×200 coordinate space and scaled uniformly.
    private static Bitmap CreateBitmap(int size)
    {
        float s = size / 200f;

        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // Background — very dark navy #0F172A
        using (var path = RoundedRect(Scale(new RectangleF(0, 0, 200, 200), s), 40 * s))
        using (var brush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            g.FillPath(brush, path);

        // Lightning bolt — #FACC15 (warm yellow)
        // Zigzag path: top → middle-left notch → bottom → middle-right notch → close
        PointF[] bolt = Scale([
            new(108, 10),  // top tip
            new( 66, 97),  // mid-left outer
            new( 92, 97),  // mid-left inner
            new( 74, 190), // bottom tip
            new(150, 87),  // mid-right outer
            new(118, 87),  // mid-right inner
        ], s);
        using (var brush = new SolidBrush(Color.FromArgb(250, 204, 21)))
            g.FillPolygon(brush, bolt);

        // "B" letters — white, one each side of the bolt
        float fontPx = 85 * s;
        using var font = new Font("Segoe UI", fontPx, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var whiteBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));

        // StringAlignment.Far inside a rect whose bottom = desired baseline gives clean vertical positioning
        float baselineY = 148 * s;
        var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString("B", font, whiteBrush, new RectangleF(  0 * s, 0, fontPx * 1.4f, baselineY), sf);
        g.DrawString("B", font, whiteBrush, new RectangleF(133 * s, 0, fontPx * 1.4f, baselineY), sf);

        return bmp;
    }

    private static RectangleF Scale(RectangleF r, float s) =>
        new(r.X * s, r.Y * s, r.Width * s, r.Height * s);

    private static PointF[] Scale(PointF[] pts, float s) =>
        Array.ConvertAll(pts, p => new PointF(p.X * s, p.Y * s));

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
