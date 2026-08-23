using System.Drawing.Drawing2D;

namespace StreamClipStudio;

internal sealed class BannerControl : Control
{
    private readonly Image? _image;

    public BannerControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        using var stream = typeof(BannerControl).Assembly.GetManifestResourceStream("StreamClipStudio.gaming-banner.png");
        if (stream is not null)
        {
            using var loaded = Image.FromStream(stream);
            _image = new Bitmap(loaded);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(UiTheme.Background);
        if (_image is null || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        var destination = ClientRectangle;
        var targetRatio = destination.Width / (float)destination.Height;
        var sourceRatio = _image.Width / (float)_image.Height;
        RectangleF source;
        if (sourceRatio > targetRatio)
        {
            var width = _image.Height * targetRatio;
            source = new RectangleF((_image.Width - width) / 2f, 0, width, _image.Height);
        }
        else
        {
            var height = _image.Width / targetRatio;
            source = new RectangleF(0, (_image.Height - height) / 2f, _image.Width, height);
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, destination, source, GraphicsUnit.Pixel);
        using var shade = new SolidBrush(Color.FromArgb(55, UiTheme.Background));
        e.Graphics.FillRectangle(shade, destination);
        using var fade = new LinearGradientBrush(destination, Color.Transparent, UiTheme.Background, LinearGradientMode.Vertical);
        fade.Blend = new Blend { Factors = new[] { 0f, 0.12f, 1f }, Positions = new[] { 0f, 0.68f, 1f } };
        e.Graphics.FillRectangle(fade, destination);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
