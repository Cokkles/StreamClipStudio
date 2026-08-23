using System.Drawing.Drawing2D;

namespace StreamClipStudio;

internal sealed class GradientWorkspacePanel : Panel
{
    private Bitmap? _cache;
    public GradientWorkspacePanel() { DoubleBuffered = true; Resize += (_, _) => Rebuild(); }
    private void Rebuild()
    {
        _cache?.Dispose();
        if (Width < 1 || Height < 1) return;
        _cache = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(_cache);
        graphics.Clear(UiTheme.Background);
        using var purple = new PathGradientBrush(new[] { new Point(0, 0), new Point(Math.Max(1, Width * 2 / 3), 0), new Point(0, Math.Max(1, Height * 2 / 3)) })
        { CenterColor = Color.FromArgb(72, UiTheme.Purple), SurroundColors = new[] { Color.Transparent, Color.Transparent, Color.Transparent } };
        graphics.FillRectangle(purple, ClientRectangle);
        using var cyan = new PathGradientBrush(new[] { new Point(Width, Height), new Point(Math.Max(0, Width / 3), Height), new Point(Width, Math.Max(0, Height / 3)) })
        { CenterColor = Color.FromArgb(48, UiTheme.Cyan), SurroundColors = new[] { Color.Transparent, Color.Transparent, Color.Transparent } };
        graphics.FillRectangle(cyan, ClientRectangle);
        Invalidate();
    }
    protected override void OnPaintBackground(PaintEventArgs e) { if (_cache is null) Rebuild(); if (_cache is not null) e.Graphics.DrawImageUnscaled(_cache, 0, 0); else base.OnPaintBackground(e); }
    protected override void Dispose(bool disposing) { if (disposing) _cache?.Dispose(); base.Dispose(disposing); }
}

internal sealed class GradientFlowLayoutPanel : FlowLayoutPanel
{
    private Bitmap? _cache;
    public GradientFlowLayoutPanel() { DoubleBuffered = true; Resize += (_, _) => Rebuild(); }
    private void Rebuild()
    {
        _cache?.Dispose(); if (Width < 1 || Height < 1) return; _cache = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(_cache); g.Clear(UiTheme.Background);
        using var top = new LinearGradientBrush(ClientRectangle, Color.FromArgb(48, 43, 32, 80), Color.FromArgb(24, 8, 42, 67), LinearGradientMode.ForwardDiagonal); g.FillRectangle(top, ClientRectangle);
        using var bottom = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.FromArgb(34, UiTheme.Cyan), LinearGradientMode.BackwardDiagonal); g.FillRectangle(bottom, ClientRectangle);
        Invalidate();
    }
    protected override void OnPaintBackground(PaintEventArgs e) { if (_cache is null) Rebuild(); if (_cache is not null) e.Graphics.DrawImageUnscaled(_cache, 0, 0); else base.OnPaintBackground(e); }
    protected override void Dispose(bool disposing) { if (disposing) _cache?.Dispose(); base.Dispose(disposing); }
}

internal sealed class ModernButton : Button
{
    public bool Primary { get; set; }
    private bool _hover;
    private bool _pressed;
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; ForeColor = Color.White;
        Cursor = Cursors.Hand; UseVisualStyleBackColor = false; DoubleBuffered = true;
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _pressed = false; Invalidate(); };
        MouseDown += (_, _) => { _pressed = true; Invalidate(); };
        MouseUp += (_, _) => { _pressed = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded(bounds, 9);
        var start = Primary ? UiTheme.Accent : UiTheme.Field;
        var end = Primary ? UiTheme.Purple : Color.FromArgb(30, 62, 91);
        if (_hover) { start = Light(start, 18); end = Light(end, 18); }
        if (_pressed) { start = Light(start, -20); end = Light(end, -20); }
        if (!Enabled) { start = Color.FromArgb(35, 42, 58); end = Color.FromArgb(28, 34, 47); }
        using var brush = new LinearGradientBrush(bounds, start, end, LinearGradientMode.Horizontal);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Primary ? Color.FromArgb(150, UiTheme.Cyan) : UiTheme.Border);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) { using var focus = new Pen(UiTheme.Cyan) { DashStyle = DashStyle.Dot }; e.Graphics.DrawPath(focus, path); }
    }
    private static GraphicsPath Rounded(Rectangle bounds, int radius) { var p = new GraphicsPath(); var d = radius * 2; p.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); p.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90); p.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); p.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
    private static Color Light(Color color, int amount) => Color.FromArgb(color.A, Math.Clamp(color.R + amount, 0, 255), Math.Clamp(color.G + amount, 0, 255), Math.Clamp(color.B + amount, 0, 255));
}

internal sealed class MixerSlider : Control
{
    private int _value = 100;
    private bool _dragging;
    public event EventHandler? ValueChanged;
    public int Value { get => _value; set { var next = Math.Clamp(value, 0, 200); if (_value == next) return; _value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public MixerSlider() { Height = 42; MinimumSize = new Size(180, 42); DoubleBuffered = true; Cursor = Cursors.Hand; }
    protected override void OnMouseDown(MouseEventArgs e) { _dragging = true; Capture = true; SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; }
    private void SetFromX(int x)
    {
        var fraction = Math.Clamp((x - 10d) / Math.Max(1, Width - 20d), 0, 1);
        Value = fraction <= .7 ? (int)Math.Round(fraction / .7 * 100) : 100 + (int)Math.Round((fraction - .7) / .3 * 100);
    }
    private float XFor(int value) => 10 + (Width - 20) * (value <= 100 ? value / 100f * .7f : .7f + (value - 100) / 100f * .3f);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var y = 15f;
        using var track = new Pen(Color.FromArgb(72, 91, 122), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, 10, y, Width - 10, y);
        using var fill = new LinearGradientBrush(new Rectangle(10, 10, Math.Max(1, (int)XFor(Value) - 10), 10), UiTheme.Cyan, UiTheme.Purple, LinearGradientMode.Horizontal);
        using var fillPen = new Pen(fill, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(fillPen, 10, y, XFor(Value), y);
        foreach (var marker in new[] { 50, 100, 150, 200 }) { var x = XFor(marker); using var pen = new Pen(marker == 100 ? Color.White : UiTheme.Muted, marker == 100 ? 2 : 1); e.Graphics.DrawLine(pen, x, 7, x, 23); }
        using var knob = new SolidBrush(Value == 100 ? Color.White : UiTheme.Cyan); e.Graphics.FillEllipse(knob, XFor(Value) - 6, y - 6, 12, 12);
        using var font = new Font("Segoe UI", 7F); using var brush = new SolidBrush(UiTheme.Muted);
        e.Graphics.DrawString("−6", font, brush, XFor(50) - 7, 26); e.Graphics.DrawString("0 dB", font, Brushes.White, XFor(100) - 12, 26); e.Graphics.DrawString("+3.5", font, brush, XFor(150) - 11, 26); e.Graphics.DrawString("+6", font, brush, XFor(200) - 10, 26);
    }
}
