using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StreamClipStudio;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(8, 13, 27);
    public static readonly Color Header = Color.FromArgb(10, 16, 31);
    public static readonly Color Panel = Color.FromArgb(20, 31, 55);
    public static readonly Color Field = Color.FromArgb(28, 46, 71);
    public static readonly Color Muted = Color.FromArgb(153, 171, 205);
    public static readonly Color Accent = Color.FromArgb(71, 126, 255);
    public static readonly Color Cyan = Color.FromArgb(26, 203, 244);
    public static readonly Color Purple = Color.FromArgb(168, 55, 255);
    public static readonly Color Border = Color.FromArgb(51, 73, 111);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    public static void ApplyDarkScrollBar(ScrollableControl control)
    {
        void Apply() => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    public static void Round(Control control, int radius = 12)
    {
        void UpdateRegion()
        {
            if (control.Width <= 1 || control.Height <= 1) return;
            using var path = new GraphicsPath();
            var diameter = radius * 2;
            var bounds = new Rectangle(0, 0, control.Width, control.Height);
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            control.Region?.Dispose();
            control.Region = new Region(path);
        }

        control.SizeChanged += (_, _) => UpdateRegion();
        control.HandleCreated += (_, _) => UpdateRegion();
    }
}
