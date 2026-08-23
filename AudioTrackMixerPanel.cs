namespace StreamClipStudio;

internal sealed record AudioTrackMix(int StreamIndex, string Label, int VolumePercent, bool Muted);

internal sealed class AudioTrackMixerPanel : TableLayoutPanel
{
    private readonly List<AudioTrackMixerRow> _rows = new();
    public event EventHandler? MixChanged;

    public AudioTrackMixerPanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        ColumnCount = 1;
        BackColor = UiTheme.Panel;
        Margin = new Padding(3, 4, 8, 6);
    }

    public IReadOnlyList<AudioTrackMix> SelectedMixes => _rows
        .Where(row => row.Included)
        .Select(row => row.BuildMix())
        .ToArray();

    public int ActiveGainTotal => SelectedMixes.Where(mix => !mix.Muted).Sum(mix => mix.VolumePercent);

    public void SetTracks(IEnumerable<AudioStreamInfo> tracks, bool selectFirst, IEnumerable<SavedAudioMix>? saved = null)
    {
        Controls.Clear();
        _rows.Clear();
        var savedByIndex = (saved ?? Array.Empty<SavedAudioMix>()).ToDictionary(item => item.StreamIndex);
        var index = 0;
        foreach (var track in tracks)
        {
            savedByIndex.TryGetValue(track.Index, out var preference);
            var row = new AudioTrackMixerRow(
                track,
                preference?.Label,
                preference?.VolumePercent ?? 100,
                preference?.Muted ?? false,
                preference?.Selected ?? (selectFirst && index == 0));
            row.MixChanged += (_, _) => MixChanged?.Invoke(this, EventArgs.Empty);
            _rows.Add(row);
            Controls.Add(row);
            index++;
        }
        MixChanged?.Invoke(this, EventArgs.Empty);
    }

    public List<SavedAudioMix> CaptureSettings(string source) => _rows.Select(row =>
    {
        var mix = row.BuildMix();
        return new SavedAudioMix
        {
            Source = source,
            StreamIndex = mix.StreamIndex,
            Label = mix.Label,
            VolumePercent = mix.VolumePercent,
            Muted = mix.Muted,
            Selected = row.Included
        };
    }).ToList();

    private sealed class AudioTrackMixerRow : TableLayoutPanel
    {
        private readonly AudioStreamInfo _track;
        private readonly CheckBox _included = new() { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly TextBox _label = new()
        {
            Width = 118,
            BackColor = UiTheme.Field,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3, 5, 6, 5)
        };
        private readonly MixerSlider _slider = new() { Dock = DockStyle.Fill, Margin = new Padding(4, 1, 4, 1) };
        private readonly NumericUpDown _percent = new WheelSafeNumericUpDown
        {
            Minimum = 0,
            Maximum = 200,
            Value = 100,
            Increment = 5,
            Width = 68,
            BackColor = UiTheme.Field,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3, 5, 3, 5)
        };
        private readonly CheckBox _muted = new() { Text = "Mute", AutoSize = true, ForeColor = Color.White, Anchor = AnchorStyles.Left };
        private readonly NumericUpDown _dbGain = new WheelSafeNumericUpDown { Minimum = -60, Maximum = 6, DecimalPlaces = 1, Increment = .5m, Width = 64, BackColor = UiTheme.Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 5, 3, 5) };
        private bool _updating;

        public event EventHandler? MixChanged;
        public bool Included => _included.Checked;

        public AudioTrackMixerRow(AudioStreamInfo track, string? label, int volume, bool muted, bool selected)
        {
            _track = track;
            Dock = DockStyle.Top;
            AutoSize = true;
            ColumnCount = 10;
            Margin = new Padding(0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4));
            _included.Checked = selected;
            _label.Text = string.IsNullOrWhiteSpace(label) ? DefaultLabel(track) : label;
            _percent.Value = Math.Clamp(volume, 0, 200);
            _slider.Value = (int)_percent.Value;
            _muted.Checked = muted;

            Controls.Add(_included, 0, 0);
            Controls.Add(_label, 1, 0);
            Controls.Add(_slider, 2, 0);
            Controls.Add(_percent, 3, 0);
            Controls.Add(MakeLabel("%"), 4, 0);
            Controls.Add(_dbGain, 5, 0);
            Controls.Add(MakeLabel("dB"), 6, 0);
            Controls.Add(_muted, 7, 0);
            var reset = MakeButton("100%");
            Controls.Add(reset, 8, 0);
            UpdateDb();

            _included.CheckedChanged += Changed;
            _label.TextChanged += Changed;
            _muted.CheckedChanged += Changed;
            _percent.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _updating = true;
                _slider.Value = (int)_percent.Value;
                _updating = false;
                UpdateDb();
                Changed(this, EventArgs.Empty);
            };
            _slider.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _updating = true;
                _percent.Value = _slider.Value;
                _updating = false;
                UpdateDb();
                Changed(this, EventArgs.Empty);
            };
            _dbGain.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _updating = true;
                var percent = _dbGain.Value <= -59.9m ? 0 : (int)Math.Round(100 * Math.Pow(10, (double)_dbGain.Value / 20));
                _percent.Value = Math.Clamp(percent, 0, 200);
                _slider.Value = (int)_percent.Value;
                _updating = false;
                Changed(this, EventArgs.Empty);
            };
            reset.Click += (_, _) => { _muted.Checked = false; _percent.Value = 100; };
        }

        public AudioTrackMix BuildMix() => new(_track.Index, _label.Text.Trim(), (int)_percent.Value, _muted.Checked);

        private void Changed(object? sender, EventArgs e) => MixChanged?.Invoke(this, EventArgs.Empty);

        private void UpdateDb()
        {
            _updating = true;
            _dbGain.Value = _percent.Value <= 0 ? -60 : Math.Clamp((decimal)(20 * Math.Log10((double)_percent.Value / 100d)), -60, 6);
            _updating = false;
        }

        private static string DefaultLabel(AudioStreamInfo track) => !string.IsNullOrWhiteSpace(track.Title)
            ? track.Title
            : $"Track {track.Index}";

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            ForeColor = UiTheme.Muted,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        private static Button MakeButton(string text)
        {
            var button = new ModernButton
            {
                Text = text,
                AutoSize = true,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.Field,
                ForeColor = Color.White,
                Margin = new Padding(4, 3, 2, 3),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = UiTheme.Border;
            return button;
        }
    }
}
