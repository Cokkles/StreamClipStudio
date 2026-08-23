namespace StreamClipStudio;

internal sealed class CompositionConfirmDialog : Form
{
    private static readonly Color Background = Color.FromArgb(18, 21, 28);
    private static readonly Color PanelColor = Color.FromArgb(28, 33, 43);
    private static readonly Color FieldColor = Color.FromArgb(38, 44, 57);
    private static readonly Color Accent = Color.FromArgb(102, 126, 234);

    public CompositionConfirmDialog(CompositionRequest request)
    {
        Text = "Confirm composition";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 690);
        MinimumSize = new Size(760, 620);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        Padding = new Padding(1);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildCanvas(request), 0, 1);
        root.Controls.Add(BuildSummary(request), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 17, 23) };
        var title = new Label
        {
            Text = "Ready to compose?",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.White,
            Location = new Point(18, 12)
        };
        var close = MakeButton("X", false);
        close.Size = new Size(46, 36);
        close.Location = new Point(Width - 58, 8);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        header.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) NativeWindowDrag.Begin(Handle);
        };
        title.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) NativeWindowDrag.Begin(Handle);
        };
        header.Controls.Add(title);
        header.Controls.Add(close);
        return header;
    }

    private static Control BuildCanvas(CompositionRequest request)
    {
        var canvas = new OverlayPositionCanvas
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(22, 16, 22, 10),
            Enabled = false,
            XPercent = request.WebcamXPercent,
            YPercent = request.WebcamYPercent,
            WebcamWidthPercent = request.WebcamWidthPercent,
            CropLeftPercent = request.WebcamCropLeftPercent,
            CropTopPercent = request.WebcamCropTopPercent,
            CropRightPercent = request.WebcamCropRightPercent,
            CropBottomPercent = request.WebcamCropBottomPercent,
            GameplayCropLeftPercent = request.GameplayCropLeftPercent,
            GameplayCropTopPercent = request.GameplayCropTopPercent,
            GameplayCropRightPercent = request.GameplayCropRightPercent,
            GameplayCropBottomPercent = request.GameplayCropBottomPercent,
            ShowWatermark = !string.IsNullOrWhiteSpace(request.WatermarkPath),
            WatermarkXPercent = request.WatermarkXPercent,
            WatermarkYPercent = request.WatermarkYPercent,
            WatermarkWidthPercent = request.WatermarkWidthPercent,
            PortraitMode = request.Orientation == "portrait",
            GameplayFocusXPercent = request.GameplayFocusXPercent
        };
        return canvas;
    }

    private static Control BuildSummary(CompositionRequest request)
    {
        var webcamCrop = $"L{request.WebcamCropLeftPercent} T{request.WebcamCropTopPercent} R{request.WebcamCropRightPercent} B{request.WebcamCropBottomPercent}";
        var gameplayCrop = $"L{request.GameplayCropLeftPercent} T{request.GameplayCropTopPercent} R{request.GameplayCropRightPercent} B{request.GameplayCropBottomPercent}";
        var watermark = string.IsNullOrWhiteSpace(request.WatermarkPath)
            ? "None"
            : $"{Path.GetFileName(request.WatermarkPath)} at X {request.WatermarkXPercent}%, Y {request.WatermarkYPercent}%, width {request.WatermarkWidthPercent}%, opacity {request.WatermarkOpacityPercent}%";
        var text =
            $"Output: {request.Profile.Width}x{request.Profile.Height}  |  {request.Profile.Name}\r\n" +
            $"Canvas: {request.Orientation}  |  Portrait template: {request.PortraitLayout}  |  Gameplay focus: {request.GameplayFocusXPercent}%\r\n" +
            $"Clip: {request.PrimaryStart:hh\\:mm\\:ss} to {(request.PrimaryStart + request.Duration):hh\\:mm\\:ss}  ({request.Duration:hh\\:mm\\:ss})\r\n" +
            $"Webcam: X {request.WebcamXPercent}%, Y {request.WebcamYPercent}%, width {request.WebcamWidthPercent}%  |  Crop {webcamCrop}\r\n" +
            $"Gameplay crop: {gameplayCrop}  |  Background: {request.WebcamBackgroundRemoval}  |  Flip: {(request.FlipWebcamHorizontally ? "Yes" : "No")}\r\n" +
            $"Watermark: {watermark}\r\n" +
            $"Audio: {request.PrimaryAudioStreams.Count + request.SecondaryAudioStreams.Count} source track(s), {request.AudioOverlays.Count} added file(s)\r\n" +
            $"Save to: {request.OutputPath}";

        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(24, 8, 24, 8),
            Padding = new Padding(14),
            BackColor = PanelColor,
            ForeColor = Color.FromArgb(218, 223, 234),
            BorderStyle = BorderStyle.FixedSingle,
            AutoEllipsis = true
        };
    }

    private Control BuildActions()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(18, 12, 18, 10),
            BackColor = Color.FromArgb(22, 26, 35)
        };
        var compose = MakeButton("Compose video", true);
        compose.DialogResult = DialogResult.OK;
        var back = MakeButton("Go back", false);
        back.DialogResult = DialogResult.Cancel;
        AcceptButton = compose;
        CancelButton = back;
        row.Controls.Add(compose);
        row.Controls.Add(back);
        return row;
    }

    private static Button MakeButton(string text, bool primary)
    {
        var normal = primary ? Accent : FieldColor;
        var hover = primary ? Color.FromArgb(120, 143, 246) : Color.FromArgb(52, 60, 76);
        var pressed = primary ? Color.FromArgb(82, 104, 218) : Color.FromArgb(29, 34, 45);
        var button = new ModernButton
        {
            Text = text,
            AutoSize = true,
            Height = 38,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = normal,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Primary = primary
        };
        return button;
    }
}

internal static class NativeWindowDrag
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Begin(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, 0xA1, new IntPtr(0x2), IntPtr.Zero);
    }
}
