namespace StreamClipStudio;

internal sealed class AuxiliaryAudioRow : TableLayoutPanel
{
    private readonly TextBox _path = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.FromArgb(38, 44, 57),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 5, 6, 5)
    };

    private readonly NumericUpDown _startMilliseconds = new WheelSafeNumericUpDown
    {
        Minimum = -3_600_000,
        Maximum = 3_600_000,
        Increment = 100,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(38, 44, 57),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 5, 6, 5)
    };

    private readonly NumericUpDown _volume = new WheelSafeNumericUpDown
    {
        Minimum = 0,
        Maximum = 200,
        Value = 100,
        Increment = 5,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(38, 44, 57),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 5, 6, 5)
    };
    private readonly MixerSlider _slider = new() { Dock = DockStyle.Fill, Margin = new Padding(4, 1, 4, 1) };
    private readonly CheckBox _mute = new() { Text = "Mute", AutoSize = true, ForeColor = Color.White, Anchor = AnchorStyles.Left };
    private bool _updating;

    public string AudioPath => _path.Text;
    public int StartMilliseconds => (int)_startMilliseconds.Value;
    public int VolumePercent => (int)_volume.Value;

    public AuxiliaryAudioRow(int number)
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        ColumnCount = 10;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Controls.Add(MakeLabel($"Audio {number}"), 0, 0);
        Controls.Add(_path, 1, 0);
        var browse = MakeButton("Browse...");
        browse.Click += (_, _) => Browse();
        Controls.Add(browse, 2, 0);
        Controls.Add(MakeLabel("Start ms"), 3, 0);
        Controls.Add(_startMilliseconds, 4, 0);
        Controls.Add(_slider, 5, 0);
        Controls.Add(_volume, 6, 0);
        Controls.Add(_mute, 7, 0);
        var reset = MakeButton("100%");
        reset.Click += (_, _) => { _mute.Checked = false; _volume.Value = 100; };
        Controls.Add(reset, 8, 0);
        var clear = MakeButton("Clear");
        clear.Click += (_, _) => _path.Clear();
        Controls.Add(clear, 9, 0);
        _slider.Value = 100;
        _slider.ValueChanged += (_, _) => { if (_updating) return; _updating = true; _volume.Value = _slider.Value; _updating = false; };
        _volume.ValueChanged += (_, _) => { if (_updating) return; _updating = true; _slider.Value = (int)_volume.Value; _updating = false; };
    }

    public AudioOverlay? BuildOverlay()
    {
        return string.IsNullOrWhiteSpace(_path.Text)
            ? null
            : new AudioOverlay(_path.Text, (int)_startMilliseconds.Value, _mute.Checked ? 0 : (int)_volume.Value);
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an audio file",
            Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _path.Text = dialog.FileName;
        }
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.White,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 9, 6, 3)
    };

    private static Button MakeButton(string text)
    {
        var normal = Color.FromArgb(38, 44, 57);
        var hover = Color.FromArgb(52, 60, 76);
        var pressed = Color.FromArgb(29, 34, 45);
        var button = new ModernButton
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(3, 4, 3, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = normal,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(64, 72, 90);
        button.MouseEnter += (_, _) => button.BackColor = hover;
        button.MouseLeave += (_, _) => button.BackColor = normal;
        button.MouseDown += (_, _) => button.BackColor = pressed;
        button.MouseUp += (_, _) => button.BackColor = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)) ? hover : normal;
        return button;
    }
}
