using System.Diagnostics;

namespace StreamClipStudio;

internal sealed class PostRenderSummaryDialog : Form
{
    public PostRenderSummaryDialog(string path, MediaInfo info, string profile, int expectedWidth = 0, int expectedHeight = 0, double expectedFps = 0)
    {
        Text = "Render verification";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(590, 390);
        MinimumSize = new Size(520, 360);
        BackColor = UiTheme.Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var mismatch = expectedWidth > 0 && (info.Width != expectedWidth || info.Height != expectedHeight) ||
                       expectedFps > 0 && Math.Abs(info.FramesPerSecond - expectedFps) > .2;
        var title = new Label { Text = mismatch ? "Render completed with a profile mismatch" : "Render verified successfully", AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), ForeColor = mismatch ? Color.FromArgb(232, 178, 84) : Color.FromArgb(67, 190, 126), Location = new Point(24, 22) };
        var details = new Label
        {
            AutoSize = false,
            Location = new Point(27, 72),
            Size = new Size(535, 210),
            ForeColor = Color.White,
            Text = $"Selected profile:  {profile}\n\nActual resolution:  {info.Width} x {info.Height}" +
                   $"\nFrame rate:  {info.FramesPerSecond:0.###} FPS\nVideo bitrate:  {info.BitRate / 1_000_000d:0.00} Mbps" +
                   $"\nDuration:  {FormatDuration(info.Duration)}\nFile size:  {info.SizeBytes / 1_000_000d:0.0} MB" +
                   (mismatch ? $"\n\nExpected:  {expectedWidth} x {expectedHeight}, {expectedFps:0.##} FPS" : string.Empty)
        };
        var openFile = MakeButton("Open file");
        var openFolder = MakeButton("Open folder");
        var close = MakeButton("Close", true);
        openFile.Click += (_, _) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = UiTheme.Header };
        buttons.Controls.Add(close); buttons.Controls.Add(openFolder); buttons.Controls.Add(openFile);
        Controls.Add(title); Controls.Add(details); Controls.Add(buttons);
        AcceptButton = close;
    }

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss\.fff") : value.ToString(@"m\:ss\.fff");
    private static Button MakeButton(string text, bool accent = false) => new ModernButton
    {
        Text = text, AutoSize = true, Height = 35, FlatStyle = FlatStyle.Flat,
        BackColor = accent ? UiTheme.Accent : UiTheme.Field, ForeColor = Color.White,
        Margin = new Padding(7, 1, 0, 1), Cursor = Cursors.Hand, Primary = accent
    };
}
