namespace StreamClipStudio;

internal sealed class SleekProgressBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            _maximum = Math.Max(_maximum, _minimum + 1);
            Value = _value;
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum + 1);
            Value = _value;
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, _minimum, _maximum);
            Invalidate();
        }
    }

    public ProgressBarStyle Style { get; set; } = ProgressBarStyle.Continuous;

    public SleekProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
        BackColor = UiTheme.Field;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var ratio = (_value - _minimum) / (double)(_maximum - _minimum);
        var fillWidth = (int)Math.Round(ClientSize.Width * ratio);
        if (fillWidth > 0)
        {
            using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(
                ClientRectangle, UiTheme.Cyan, UiTheme.Purple, 0f);
            e.Graphics.FillRectangle(fill, 0, 0, fillWidth, ClientSize.Height);
        }

        if (ClientSize.Width > 1 && ClientSize.Height > 1)
        {
            using var border = new Pen(UiTheme.Border);
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }
}
