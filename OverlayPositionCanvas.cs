namespace StreamClipStudio;

internal sealed class OverlayPositionCanvas : Control
{
    private const int CanvasPadding = 12;
    private DragTarget _dragTarget;
    private PointF _dragOffset;
    private int _xPercent = 2;
    private int _yPercent = 70;
    private int _widthPercent = 28;
    private int _cropLeft;
    private int _cropTop;
    private int _cropRight;
    private int _cropBottom;

    public event EventHandler? PositionChanged;
    public event EventHandler? WatermarkPositionChanged;

    public int CropLeftPercent { get => _cropLeft; set { _cropLeft = Math.Clamp(value, 0, 45); Invalidate(); } }
    public int CropTopPercent { get => _cropTop; set { _cropTop = Math.Clamp(value, 0, 45); Invalidate(); } }
    public int CropRightPercent { get => _cropRight; set { _cropRight = Math.Clamp(value, 0, 45); Invalidate(); } }
    public int CropBottomPercent { get => _cropBottom; set { _cropBottom = Math.Clamp(value, 0, 45); Invalidate(); } }
    public int GameplayCropLeftPercent { get; set; }
    public int GameplayCropTopPercent { get; set; }
    public int GameplayCropRightPercent { get; set; }
    public int GameplayCropBottomPercent { get; set; }
    public bool ShowWatermark { get; set; }
    public int WatermarkXPercent { get; set; } = 82;
    public int WatermarkYPercent { get; set; } = 92;
    public int WatermarkWidthPercent { get; set; } = 15;
    public bool ShowYouTubeGuides { get; set; } = true;
    public bool PortraitMode { get; set; }
    public string PlatformGuide { get; set; } = "youtube";
    public int GameplayFocusXPercent { get; set; } = 50;

    public int XPercent
    {
        get => _xPercent;
        set
        {
            var next = Math.Clamp(value, -50, 100);
            if (_xPercent == next) return;
            _xPercent = next;
            Invalidate();
        }
    }

    public int YPercent
    {
        get => _yPercent;
        set
        {
            var next = Math.Clamp(value, -50, 100);
            if (_yPercent == next) return;
            _yPercent = next;
            Invalidate();
        }
    }

    public int WebcamWidthPercent
    {
        get => _widthPercent;
        set
        {
            var next = Math.Clamp(value, 10, 70);
            if (_widthPercent == next) return;
            _widthPercent = next;
            Invalidate();
        }
    }

    public OverlayPositionCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Header;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        MinimumSize = new Size(420, 255);
        Height = 280;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var canvas = GetCanvasBounds();

        using var canvasBrush = new SolidBrush(Color.FromArgb(13, 16, 23));
        using var borderPen = new Pen(Color.FromArgb(81, 91, 112), 1.5f);
        e.Graphics.FillRectangle(canvasBrush, canvas);
        e.Graphics.DrawRectangle(borderPen, canvas.X, canvas.Y, canvas.Width, canvas.Height);

        DrawGameplayCrop(e.Graphics, canvas);

        using var gridPen = new Pen(Color.FromArgb(46, 53, 68), 1);
        for (var division = 1; division < 3; division++)
        {
            var x = canvas.Left + canvas.Width * division / 3f;
            var y = canvas.Top + canvas.Height * division / 3f;
            e.Graphics.DrawLine(gridPen, x, canvas.Top, x, canvas.Bottom);
            e.Graphics.DrawLine(gridPen, canvas.Left, y, canvas.Right, y);
        }

        if (ShowYouTubeGuides)
        {
            var safe = RectangleF.Inflate(canvas, -canvas.Width * 0.05f, -canvas.Height * 0.05f);
            using var safePen = new Pen(Color.FromArgb(185, UiTheme.Cyan), 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(safePen, safe.X, safe.Y, safe.Width, safe.Height);
            using var obstruction = new SolidBrush(Color.FromArgb(45, 236, 90, 112));
            var bottomRatio = PortraitMode ? (PlatformGuide == "tiktok" ? .19f : .16f) : .13f;
            var rightRatio = PortraitMode ? (PlatformGuide == "meta" ? .20f : .17f) : .18f;
            var controls = new RectangleF(canvas.Left, canvas.Bottom - canvas.Height * bottomRatio, canvas.Width, canvas.Height * bottomRatio);
            var topRight = new RectangleF(canvas.Right - canvas.Width * rightRatio, canvas.Top, canvas.Width * rightRatio, canvas.Height * (PortraitMode ? .15f : .12f));
            e.Graphics.FillRectangle(obstruction, controls);
            e.Graphics.FillRectangle(obstruction, topRight);
            using var guideFont = new Font("Segoe UI Semibold", 7.5F);
            using var guideBrush = new SolidBrush(Color.FromArgb(215, 242, 151, 162));
            e.Graphics.DrawString($"{PlatformGuide.ToUpperInvariant()} UI ZONE", guideFont, guideBrush, controls.Left + 5, controls.Top + 3);
            e.Graphics.DrawString("FRAME SAFE", guideFont, Brushes.LightCyan, safe.Left + 4, safe.Top + 3);
        }

        using var gameFont = new Font("Segoe UI Semibold", 11F);
        using var hintFont = new Font("Segoe UI", 8.5F);
        using var gameBrush = new SolidBrush(Color.FromArgb(95, 108, 132));
        e.Graphics.DrawString(PortraitMode ? "PORTRAIT 9:16" : "GAMEPLAY 16:9", gameFont, gameBrush, canvas.Left + 12, canvas.Top + 10);
        e.Graphics.DrawString("Drag the webcam. It may extend beyond the frame.", hintFont, gameBrush, canvas.Left + 12, canvas.Bottom - 25);

        if (PortraitMode)
        {
            using var focusPen = new Pen(Color.FromArgb(210, UiTheme.Purple), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawLine(focusPen, canvas.Left + canvas.Width / 2, canvas.Top, canvas.Left + canvas.Width / 2, canvas.Bottom);
            e.Graphics.DrawString($"PORTRAIT FOCUS {GameplayFocusXPercent}%", hintFont, Brushes.Plum, canvas.Left + 8, canvas.Top + 30);
        }

        var camera = GetCameraBounds(canvas);
        using var cameraBrush = new SolidBrush(Color.FromArgb(145, 234, 199, 64));
        using var cameraBorder = new Pen(Color.FromArgb(255, 245, 220, 92), 2);
        e.Graphics.FillRectangle(cameraBrush, camera);
        e.Graphics.DrawRectangle(cameraBorder, camera.X, camera.Y, camera.Width, camera.Height);

        using var cameraFont = new Font("Segoe UI Semibold", 9F);
        using var textBrush = new SolidBrush(Color.FromArgb(20, 24, 30));
        var cropText = _cropLeft + _cropTop + _cropRight + _cropBottom > 0
            ? $"\nCrop L{_cropLeft} T{_cropTop} R{_cropRight} B{_cropBottom}"
            : string.Empty;
        var label = $"WEBCAM\nX {_xPercent}%  Y {_yPercent}%{cropText}";
        e.Graphics.DrawString(label, cameraFont, textBrush, camera.X + 7, camera.Y + 6);

        if (ShowWatermark)
        {
            var watermark = GetWatermarkBounds(canvas);
            using var watermarkBrush = new SolidBrush(Color.FromArgb(190, UiTheme.Purple));
            using var watermarkPen = new Pen(UiTheme.Cyan, 2f);
            e.Graphics.FillRectangle(watermarkBrush, watermark);
            e.Graphics.DrawRectangle(watermarkPen, watermark.X, watermark.Y, watermark.Width, watermark.Height);
            e.Graphics.DrawString("WATERMARK", hintFont, Brushes.White, watermark.X + 4, watermark.Y + 1);
        }

        using var outsideBrush = new SolidBrush(Color.FromArgb(95, BackColor));
        if (camera.Left < canvas.Left) e.Graphics.FillRectangle(outsideBrush, camera.Left, camera.Top, canvas.Left - camera.Left, camera.Height);
        if (camera.Top < canvas.Top) e.Graphics.FillRectangle(outsideBrush, camera.Left, camera.Top, camera.Width, canvas.Top - camera.Top);
        if (camera.Right > canvas.Right) e.Graphics.FillRectangle(outsideBrush, canvas.Right, camera.Top, camera.Right - canvas.Right, camera.Height);
        if (camera.Bottom > canvas.Bottom) e.Graphics.FillRectangle(outsideBrush, camera.Left, canvas.Bottom, camera.Width, camera.Bottom - canvas.Bottom);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var canvas = GetCanvasBounds();
        var watermark = GetWatermarkBounds(canvas);
        if (ShowWatermark && watermark.Contains(e.Location))
        {
            _dragTarget = DragTarget.Watermark;
            _dragOffset = new PointF(e.X - watermark.X, e.Y - watermark.Y);
        }
        else
        {
            var camera = GetCameraBounds(canvas);
            if (!camera.Contains(e.Location))
            {
                SetPositionFromPoint(e.Location, center: true);
                camera = GetCameraBounds(canvas);
            }
            _dragTarget = DragTarget.Webcam;
            _dragOffset = new PointF(e.X - camera.X, e.Y - camera.Y);
        }
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTarget == DragTarget.None)
        {
            var hoverCanvas = GetCanvasBounds();
            Cursor = ShowWatermark && GetWatermarkBounds(hoverCanvas).Contains(e.Location) || GetCameraBounds(hoverCanvas).Contains(e.Location)
                ? Cursors.SizeAll : Cursors.Hand;
            return;
        }

        var canvas = GetCanvasBounds();
        var x = (e.X - _dragOffset.X - canvas.Left) / canvas.Width * 100f;
        var y = (e.Y - _dragOffset.Y - canvas.Top) / canvas.Height * 100f;
        if (_dragTarget == DragTarget.Watermark)
        {
            var nextX = Math.Clamp((int)Math.Round(x), -50, 100);
            var nextY = Math.Clamp((int)Math.Round(y), -50, 100);
            if (WatermarkXPercent != nextX || WatermarkYPercent != nextY)
            {
                WatermarkXPercent = nextX;
                WatermarkYPercent = nextY;
                Invalidate();
                WatermarkPositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else SetPosition((int)Math.Round(x), (int)Math.Round(y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragTarget = DragTarget.None;
        Capture = false;
    }

    private void SetPositionFromPoint(Point point, bool center)
    {
        var canvas = GetCanvasBounds();
        var x = (point.X - canvas.Left) / canvas.Width * 100f;
        var y = (point.Y - canvas.Top) / canvas.Height * 100f;
        if (center)
        {
            x -= _widthPercent / 2f;
            y -= _widthPercent / 2f;
        }
        SetPosition((int)Math.Round(x), (int)Math.Round(y));
    }

    private void SetPosition(int x, int y)
    {
        x = Math.Clamp(x, -50, 100);
        y = Math.Clamp(y, -50, 100);
        if (_xPercent == x && _yPercent == y) return;
        _xPercent = x;
        _yPercent = y;
        Invalidate();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private RectangleF GetCanvasBounds()
    {
        var availableWidth = Math.Max(1, ClientSize.Width - CanvasPadding * 2);
        var availableHeight = Math.Max(1, ClientSize.Height - CanvasPadding * 2);
        var width = (float)availableWidth;
        var height = width * (PortraitMode ? 16f / 9f : 9f / 16f);
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * (PortraitMode ? 9f / 16f : 16f / 9f);
        }
        return new RectangleF(
            (ClientSize.Width - width) / 2f,
            (ClientSize.Height - height) / 2f,
            width,
            height);
    }

    private RectangleF GetCameraBounds(RectangleF canvas)
    {
        var width = canvas.Width * _widthPercent / 100f;
        var remainingWidth = Math.Max(10, 100 - _cropLeft - _cropRight);
        var remainingHeight = Math.Max(10, 100 - _cropTop - _cropBottom);
        var height = canvas.Height * _widthPercent / 100f * remainingHeight / remainingWidth;
        return new RectangleF(
            canvas.Left + canvas.Width * _xPercent / 100f,
            canvas.Top + canvas.Height * _yPercent / 100f,
            width,
            height);
    }

    private RectangleF GetWatermarkBounds(RectangleF canvas) => new(
        canvas.Left + canvas.Width * WatermarkXPercent / 100f,
        canvas.Top + canvas.Height * WatermarkYPercent / 100f,
        canvas.Width * WatermarkWidthPercent / 100f,
        Math.Max(18, canvas.Height * WatermarkWidthPercent / 350f));

    private enum DragTarget { None, Webcam, Watermark }

    private void DrawGameplayCrop(Graphics graphics, RectangleF canvas)
    {
        var left = canvas.Width * Math.Clamp(GameplayCropLeftPercent, 0, 45) / 100f;
        var top = canvas.Height * Math.Clamp(GameplayCropTopPercent, 0, 45) / 100f;
        var right = canvas.Width * Math.Clamp(GameplayCropRightPercent, 0, 45) / 100f;
        var bottom = canvas.Height * Math.Clamp(GameplayCropBottomPercent, 0, 45) / 100f;
        if (left + top + right + bottom <= 0) return;

        using var shade = new SolidBrush(Color.FromArgb(155, 7, 9, 14));
        using var cropPen = new Pen(Color.FromArgb(220, 236, 112, 112), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        if (left > 0) graphics.FillRectangle(shade, canvas.Left, canvas.Top, left, canvas.Height);
        if (right > 0) graphics.FillRectangle(shade, canvas.Right - right, canvas.Top, right, canvas.Height);
        if (top > 0) graphics.FillRectangle(shade, canvas.Left, canvas.Top, canvas.Width, top);
        if (bottom > 0) graphics.FillRectangle(shade, canvas.Left, canvas.Bottom - bottom, canvas.Width, bottom);
        var visible = new RectangleF(canvas.Left + left, canvas.Top + top, canvas.Width - left - right, canvas.Height - top - bottom);
        graphics.DrawRectangle(cropPen, visible.X, visible.Y, visible.Width, visible.Height);
    }
}
