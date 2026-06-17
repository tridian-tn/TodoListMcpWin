using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TodoListMcp.App.Tray;

/// <summary>Builds the tray icon at runtime so the app ships without a binary .ico asset.</summary>
internal static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var fill = new SolidBrush(Color.FromArgb(0x21, 0x96, 0xF3));
            g.FillRoundedRectangle(fill, new Rectangle(0, 0, 15, 15), 3);

            using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[] { new PointF(3.5f, 8f), new PointF(6.5f, 11f), new PointF(12f, 4.5f) });
        }

        var hicon = bmp.GetHicon();
        try
        {
            // Clone so the managed Icon owns its own copy, then release the GDI handle.
            using var temp = Icon.FromHandle(hicon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
