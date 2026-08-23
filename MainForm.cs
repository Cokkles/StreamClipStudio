using System.Diagnostics;
using System.Globalization;

namespace StreamClipStudio;

internal sealed class MainForm : Form
{
    private static readonly Color Background = UiTheme.Background;
    private static readonly Color PanelColor = UiTheme.Panel;
    private static readonly Color FieldColor = UiTheme.Field;
    private static readonly Color Muted = UiTheme.Muted;
    private static readonly Color Accent = UiTheme.Accent;
    private static readonly Color Success = Color.FromArgb(67, 190, 126);

    private readonly AppSettings _settings = AppSettings.Load();
    private MediaInfo? _mediaInfo;
    private CancellationTokenSource? _operationCancellation;

    private readonly TextBox _inputPath = CreateTextBox();
    private readonly TextBox _outputFolder = CreateTextBox();
    private readonly TextBox _startTime = CreateTextBox("00:00:00");
    private readonly TextBox _endTime = CreateTextBox("00:00:00");
    private readonly Label _mediaSummary = CreateMutedLabel("Choose a recording to read its details.");
    private readonly Label _clipDuration = CreateMutedLabel("Clip duration: —");
    private readonly ComboBox _preset = new();
    private readonly Label _presetDescription = CreateMutedLabel(string.Empty);
    private readonly CheckedListBox _audioTracks = new();
    private readonly TextBox _outputName = CreateTextBox();
    private readonly SleekProgressBar _progress = new();
    private readonly Label _status = CreateMutedLabel("Ready");
    private readonly Button _createButton = CreateButton("Create clip", true);
    private readonly Button _cancelButton = CreateButton("Cancel");
    private readonly Button _openOutputButton = CreateButton("Open output folder");
    private readonly Button _settingsButton = CreateButton("FFmpeg settings");
    private readonly ComposeControl _composeControl;
    private Button? _maximizeButton;
    private readonly System.Windows.Forms.Timer _clipLayoutTimer = new() { Interval = 75 };
    private FlowLayoutPanel? _clipConfigurationHost;

    public MainForm()
    {
        Text = "Stream Clip Studio";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("StreamClipStudio.app.ico"))
        using (var loadedIcon = iconStream is null ? null : new Icon(iconStream))
        {
            Icon = loadedIcon is null ? Icon.ExtractAssociatedIcon(Application.ExecutablePath) : (Icon)loadedIcon.Clone();
        }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        var initialWidth = _settings.WindowWidth < 1180 ? 1280 : _settings.WindowWidth;
        Size = new Size(Math.Clamp(initialWidth, 1100, 2560), Math.Clamp(_settings.WindowHeight, 720, 1600));
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;
        DoubleBuffered = true;

        _composeControl = new ComposeControl(_settings);
        Controls.Add(BuildChrome(BuildTabs()));
        ConfigureControls();
        _clipLayoutTimer.Tick += (_, _) =>
        {
            _clipLayoutTimer.Stop();
            if (_clipConfigurationHost is not null) ResizeClipConfigurationPanels(_clipConfigurationHost);
        };
        LoadPreferences();
        _composeControl.RefreshSettings();
        if (_settings.WindowMaximized)
        {
            Shown += (_, _) => WindowState = FormWindowState.Maximized;
        }
    }

    private Control BuildTabs()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Background
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 0, 0),
            BackColor = UiTheme.Header
        };
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        var clipPage = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        clipPage.Controls.Add(BuildLayout());
        var composePage = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        composePage.Controls.Add(_composeControl);
        content.Controls.Add(composePage);
        content.Controls.Add(clipPage);

        var clipButton = CreateNavigationButton("Clip one recording");
        var composeButton = CreateNavigationButton("Sync and compose two recordings");
        void SelectPage(Control page, Button selected, Button other)
        {
            clipPage.Visible = ReferenceEquals(page, clipPage);
            composePage.Visible = ReferenceEquals(page, composePage);
            page.BringToFront();
            selected.BackColor = PanelColor;
            selected.ForeColor = Color.White;
            selected.FlatAppearance.BorderColor = Accent;
            other.BackColor = UiTheme.Header;
            other.ForeColor = Muted;
            other.FlatAppearance.BorderColor = UiTheme.Header;
        }
        clipButton.Click += (_, _) => SelectPage(clipPage, clipButton, composeButton);
        composeButton.Click += (_, _) => SelectPage(composePage, composeButton, clipButton);
        navigation.Controls.Add(clipButton);
        navigation.Controls.Add(composeButton);
        SelectPage(clipPage, clipButton, composeButton);

        shell.Controls.Add(navigation, 0, 0);
        shell.Controls.Add(content, 0, 1);
        return shell;
    }

    private static Button CreateNavigationButton(string text)
    {
        var button = new ModernButton
        {
            Name = text.StartsWith("Sync", StringComparison.Ordinal) ? "ComposeNavigationButton" : "ClipNavigationButton",
            Text = text,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(16, 3, 16, 3),
            Margin = new Padding(0, 0, 5, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Header,
            ForeColor = Muted,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(15, 18, 25);
        button.MouseDown += (_, _) => button.BackColor = Color.FromArgb(34, 40, 52);
        return button;
    }

    private Control BuildChrome(Control content)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(8, 10, 14)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildTitleBar(), 0, 0);
        shell.Controls.Add(content, 0, 1);
        return shell;
    }

    private Control BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Header };
        var icon = new PictureBox
        {
            Image = Icon?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(27, 27),
            Location = new Point(12, 7)
        };
        var title = new Label
        {
            Text = "Stream Clip Studio",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F),
            ForeColor = Color.FromArgb(230, 234, 242),
            Location = new Point(47, 11)
        };

        var close = CreateWindowButton("×");
        close.Dock = DockStyle.Right;
        close.MouseEnter += (_, _) => close.BackColor = Color.FromArgb(196, 43, 53);
        close.MouseLeave += (_, _) => close.BackColor = UiTheme.Header;
        close.Click += (_, _) => Close();
        _maximizeButton = CreateWindowButton("□");
        _maximizeButton.Dock = DockStyle.Right;
        _maximizeButton.Click += (_, _) => ToggleMaximize();
        var minimize = CreateWindowButton("—");
        minimize.Dock = DockStyle.Right;
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

        void BeginMove(object? _, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) NativeWindowDrag.Begin(Handle);
        }
        bar.MouseDown += BeginMove;
        title.MouseDown += BeginMove;
        icon.MouseDown += BeginMove;
        bar.DoubleClick += (_, _) => ToggleMaximize();
        title.DoubleClick += (_, _) => ToggleMaximize();
        bar.Controls.Add(title);
        bar.Controls.Add(icon);
        bar.Controls.Add(minimize);
        bar.Controls.Add(_maximizeButton);
        bar.Controls.Add(close);
        return bar;
    }

    private static Button CreateWindowButton(string text)
    {
        var button = new ModernButton
        {
            Text = text,
            Width = 48,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Header,
            ForeColor = Color.FromArgb(218, 223, 234),
            Font = new Font("Segoe UI", 12F),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.MouseEnter += (_, _) => button.BackColor = Color.FromArgb(42, 48, 62);
        button.MouseLeave += (_, _) => button.BackColor = UiTheme.Header;
        button.MouseDown += (_, _) => button.BackColor = Color.FromArgb(28, 33, 43);
        return button;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        if (_maximizeButton is not null) _maximizeButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
    }

    private Control BuildLayout()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(BuildClipHeader(), 0, 0);

        var configuration = new GradientFlowLayoutPanel
        {
            Name = "ClipConfigurationHost",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 14, 18, 18),
            BackColor = Background
        };
        _clipConfigurationHost = configuration;
        UiTheme.ApplyDarkScrollBar(configuration);
        configuration.Controls.Add(BuildSourcePanel());
        configuration.Controls.Add(BuildTrimPanel());
        configuration.Controls.Add(BuildPresetPanel());
        configuration.Controls.Add(BuildOutputPanel());
        configuration.SizeChanged += (_, _) =>
        {
            _clipLayoutTimer.Stop();
            _clipLayoutTimer.Start();
        };
        shell.Controls.Add(configuration, 0, 1);
        shell.Controls.Add(BuildRunPanel(), 0, 2);
        return shell;
    }

    private Control BuildClipHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = UiTheme.Header,
            Padding = new Padding(22, 11, 22, 8)
        };
        var title = new Label
        {
            Text = "Stream Clip Studio",
            Font = new Font("Segoe UI Semibold", 19F),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(22, 10)
        };
        var subtitle = CreateMutedLabel("Fast, repeatable FFmpeg clips from your OBS recordings");
        subtitle.Location = new Point(24, 49);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        return header;
    }

    private static void ResizeClipConfigurationPanels(FlowLayoutPanel host)
    {
        var width = Math.Max(700, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control child in host.Controls)
        {
            child.MaximumSize = new Size(width, 0);
            child.Width = width;
        }
    }

    private Control BuildSourcePanel()
    {
        var content = NewGrid(3);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        content.Controls.Add(CreateLabel("Recording"), 0, 0);
        content.Controls.Add(_inputPath, 1, 0);
        var browse = CreateButton("Browse…");
        browse.Click += async (_, _) => await BrowseInputAsync();
        content.Controls.Add(browse, 2, 0);
        content.Controls.Add(_mediaSummary, 1, 1);
        content.SetColumnSpan(_mediaSummary, 2);
        return CreatePanel("Source recording", content);
    }

    private Control BuildTrimPanel()
    {
        var content = NewGrid(5);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        content.Controls.Add(CreateLabel("Start"), 0, 0);
        content.Controls.Add(_startTime, 1, 0);
        content.Controls.Add(CreateLabel("End"), 2, 0);
        content.Controls.Add(_endTime, 3, 0);
        var useEnd = CreateButton("Use file end");
        useEnd.Click += (_, _) =>
        {
            if (_mediaInfo is not null)
            {
                _endTime.Text = FormatTime(_mediaInfo.Duration);
            }
        };
        content.Controls.Add(useEnd, 4, 0);
        content.Controls.Add(_clipDuration, 1, 1);
        content.SetColumnSpan(_clipDuration, 4);

        var hint = CreateMutedLabel("Accepted formats: 1:23, 01:23, 1:02:03, or 00:02:03.500");
        content.Controls.Add(hint, 1, 2);
        content.SetColumnSpan(hint, 4);
        return CreatePanel("Clip timing", content);
    }

    private Control BuildPresetPanel()
    {
        var content = NewGrid(2);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        content.Controls.Add(CreateLabel("Quality"), 0, 0);
        content.Controls.Add(_preset, 1, 0);
        content.Controls.Add(_presetDescription, 1, 1);
        content.Controls.Add(CreateLabel("Audio"), 0, 2);
        content.Controls.Add(_audioTracks, 1, 2);
        return CreatePanel("Encoding", content);
    }

    private Control BuildOutputPanel()
    {
        var content = NewGrid(3);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        content.Controls.Add(CreateLabel("Folder"), 0, 0);
        content.Controls.Add(_outputFolder, 1, 0);
        var browse = CreateButton("Browse…");
        browse.Click += (_, _) => BrowseOutput();
        content.Controls.Add(browse, 2, 0);
        content.Controls.Add(CreateLabel("File name"), 0, 1);
        content.Controls.Add(_outputName, 1, 1);
        content.SetColumnSpan(_outputName, 2);
        return CreatePanel("Output", content);
    }

    private Control BuildRunPanel()
    {
        var container = new TableLayoutPanel
        {
            Name = "ClipActionBar",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(18, 10, 18, 12),
            BackColor = UiTheme.Header
        };
        _progress.Dock = DockStyle.Top;
        _progress.Height = 10;
        _progress.Style = ProgressBarStyle.Continuous;
        container.Controls.Add(_progress);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 5,
            Margin = new Padding(0, 10, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(_status, 0, 0);
        row.Controls.Add(_settingsButton, 1, 0);
        row.Controls.Add(_openOutputButton, 2, 0);
        row.Controls.Add(_cancelButton, 3, 0);
        row.Controls.Add(_createButton, 4, 0);
        container.Controls.Add(row);
        return container;
    }

    private void ConfigureControls()
    {
        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.BackColor = FieldColor;
        _preset.ForeColor = Color.White;
        _preset.FlatStyle = FlatStyle.Flat;
        _preset.Dock = DockStyle.Fill;
        _preset.Items.AddRange(ClipPreset.All.Cast<object>().ToArray());
        _preset.SelectedIndexChanged += (_, _) =>
        {
            if (_preset.SelectedItem is ClipPreset preset)
            {
                _presetDescription.Text = preset.Description;
                UpdateSuggestedName();
            }
        };

        _audioTracks.BackColor = FieldColor;
        _audioTracks.ForeColor = Color.White;
        _audioTracks.BorderStyle = BorderStyle.FixedSingle;
        _audioTracks.CheckOnClick = true;
        _audioTracks.Height = 82;
        _audioTracks.Dock = DockStyle.Fill;

        _startTime.TextChanged += (_, _) => UpdateTimingAndName();
        _endTime.TextChanged += (_, _) => UpdateTimingAndName();
        _inputPath.TextChanged += (_, _) => UpdateSuggestedName();

        _createButton.Click += async (_, _) => await CreateClipAsync();
        _cancelButton.Click += (_, _) => _operationCancellation?.Cancel();
        _cancelButton.Enabled = false;
        _openOutputButton.Click += (_, _) => OpenOutputFolder();
        _settingsButton.Click += (_, _) => ShowFfmpegSettings();

        DragEnter += (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                eventArgs.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += async (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                await LoadInputAsync(files[0]);
            }
        };
    }

    private void LoadPreferences()
    {
        _settings.FfmpegPath = ResolveTool(_settings.FfmpegPath, "ffmpeg.exe");
        _settings.FfprobePath = ResolveTool(_settings.FfprobePath, "ffprobe.exe");

        var preferredOutput = @"C:\Users\Brian\Videos\Clipped stuff";
        _outputFolder.Text = !string.IsNullOrWhiteSpace(_settings.OutputFolder)
            ? _settings.OutputFolder
            : Directory.Exists(preferredOutput)
                ? preferredOutput
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        var preset = ClipPreset.All.FirstOrDefault(item => item.Id == _settings.PresetId) ?? ClipPreset.All[0];
        _preset.SelectedItem = preset;
    }

    private async Task BrowseInputAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an OBS recording",
            Filter = "Video files|*.mkv;*.mp4;*.mov;*.webm;*.avi|All files|*.*",
            InitialDirectory = Directory.Exists(_settings.LastInputFolder)
                ? _settings.LastInputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadInputAsync(dialog.FileName);
        }
    }

    private async Task LoadInputAsync(string path)
    {
        if (!File.Exists(path))
        {
            ShowError("That recording no longer exists.");
            return;
        }

        if (!File.Exists(_settings.FfprobePath))
        {
            ShowError("FFprobe was not found. Open FFmpeg settings and choose its location.");
            return;
        }

        SetBusy(true, "Reading recording metadata…");
        try
        {
            _mediaInfo = await MediaProbe.ReadAsync(_settings.FfprobePath, path, CancellationToken.None);
            _inputPath.Text = path;
            _mediaSummary.Text = _mediaInfo.Summary;
            _endTime.Text = FormatTime(_mediaInfo.Duration);
            _audioTracks.Items.Clear();
            foreach (var audio in _mediaInfo.AudioStreams)
            {
                _audioTracks.Items.Add(audio, true);
            }

            _settings.LastInputFolder = Path.GetDirectoryName(path) ?? string.Empty;
            _settings.Save();
            _status.ForeColor = Success;
            _status.Text = "Recording loaded";
        }
        catch (Exception exception)
        {
            _mediaInfo = null;
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where completed clips are saved",
            InitialDirectory = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : string.Empty,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputFolder.Text = dialog.SelectedPath;
            _settings.OutputFolder = dialog.SelectedPath;
            _settings.Save();
        }
    }

    private async Task CreateClipAsync()
    {
        if (!TryBuildRequest(out var request, out var validationError))
        {
            ShowError(validationError);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request!.OutputPath)!);
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, "Creating clip…");
        _progress.Value = 0;

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                _progress.Value = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
                _status.Text = $"Creating clip… {_progress.Value}%";
            });

            var result = await FfmpegRunner.RunAsync(
                request,
                progress,
                text => BeginInvoke(() => _status.Text = text),
                _operationCancellation.Token);

            if (!result.Success)
            {
                var logPath = Path.ChangeExtension(request.OutputPath, ".ffmpeg.log.txt");
                await File.WriteAllTextAsync(logPath, result.Log);
                ShowError($"FFmpeg could not complete the clip. A diagnostic log was saved beside the output file.\n\n{LastMeaningfulLine(result.Log)}");
                return;
            }

            _progress.Value = 100;
            _status.ForeColor = Success;
            _status.Text = $"Completed in {FormatElapsed(result.Elapsed)}";
            System.Media.SystemSounds.Asterisk.Play();

            var rendered = await MediaProbe.ReadAsync(_settings.FfprobePath, request.OutputPath, CancellationToken.None);
            var expectedWidth = request.Preset.Id == "youtube4k" ? 3840 : _mediaInfo?.Width ?? 0;
            var expectedHeight = request.Preset.Id == "youtube4k" ? 2160 : _mediaInfo?.Height ?? 0;
            using var summary = new PostRenderSummaryDialog(request.OutputPath, rendered, request.Preset.Name, expectedWidth, expectedHeight, _mediaInfo?.FramesPerSecond ?? 0);
            summary.ShowDialog(this);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled";
            _status.ForeColor = Muted;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
        }
    }

    private bool TryBuildRequest(out ClipRequest? request, out string error)
    {
        request = null;
        error = string.Empty;

        if (!File.Exists(_inputPath.Text))
        {
            error = "Choose a valid source recording first.";
            return false;
        }
        if (!File.Exists(_settings.FfmpegPath))
        {
            error = "FFmpeg was not found. Open FFmpeg settings and choose its location.";
            return false;
        }
        if (!TryParseTime(_startTime.Text, out var start) || !TryParseTime(_endTime.Text, out var end))
        {
            error = "Enter valid start and end times, such as 24:25 or 00:24:25.500.";
            return false;
        }
        if (start < TimeSpan.Zero || end <= start)
        {
            error = "The end time must be later than the start time.";
            return false;
        }
        if (_mediaInfo is not null && end > _mediaInfo.Duration + TimeSpan.FromMilliseconds(100))
        {
            error = "The end time is beyond the recording duration.";
            return false;
        }
        if (_preset.SelectedItem is not ClipPreset preset)
        {
            error = "Choose an encoding preset.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            error = "Choose an output folder.";
            return false;
        }

        var fileName = SanitizeFileName(_outputName.Text.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "Enter an output file name.";
            return false;
        }
        if (!fileName.EndsWith(preset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += preset.Extension;
        }

        var outputPath = MakeUniquePath(Path.Combine(_outputFolder.Text, fileName));
        var selectedAudio = _audioTracks.CheckedItems
            .Cast<AudioStreamInfo>()
            .Select(item => item.Index)
            .ToArray();

        _settings.OutputFolder = _outputFolder.Text;
        _settings.PresetId = preset.Id;
        _settings.Save();

        request = new ClipRequest(
            _settings.FfmpegPath,
            _inputPath.Text,
            outputPath,
            start,
            end - start,
            preset,
            selectedAudio);
        return true;
    }

    private void UpdateTimingAndName()
    {
        if (TryParseTime(_startTime.Text, out var start) && TryParseTime(_endTime.Text, out var end) && end > start)
        {
            _clipDuration.Text = $"Clip duration: {FormatTime(end - start)}";
        }
        else
        {
            _clipDuration.Text = "Clip duration: —";
        }
        UpdateSuggestedName();
    }

    private void UpdateSuggestedName()
    {
        if (string.IsNullOrWhiteSpace(_inputPath.Text) || _preset.SelectedItem is not ClipPreset preset)
        {
            return;
        }
        if (!TryParseTime(_startTime.Text, out var start) || !TryParseTime(_endTime.Text, out var end))
        {
            return;
        }

        var sourceName = Path.GetFileNameWithoutExtension(_inputPath.Text);
        _outputName.Text = $"{sourceName} - {TimeToken(start)}-{TimeToken(end)} - {preset.ShortName}{preset.Extension}";
    }

    private void OpenOutputFolder()
    {
        if (!Directory.Exists(_outputFolder.Text))
        {
            ShowError("The output folder does not exist yet.");
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = _outputFolder.Text,
            UseShellExecute = true
        });
    }

    private void ShowFfmpegSettings()
    {
        using var dialog = new FfmpegSettingsForm(_settings.FfmpegPath, _settings.FfprobePath);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.FfmpegPath = dialog.FfmpegPath;
            _settings.FfprobePath = dialog.FfprobePath;
            _settings.Save();
            _status.Text = "FFmpeg settings saved";
            _composeControl.RefreshSettings();
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _createButton.Enabled = !busy;
        _cancelButton.Enabled = busy && _operationCancellation is not null;
        _inputPath.Enabled = !busy;
        _startTime.Enabled = !busy;
        _endTime.Enabled = !busy;
        _preset.Enabled = !busy;
        _audioTracks.Enabled = !busy;
        _outputFolder.Enabled = !busy;
        _outputName.Enabled = !busy;
        _settingsButton.Enabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _status.Text = message;
            _status.ForeColor = Muted;
        }
    }

    private static string ResolveTool(string savedPath, string fileName)
    {
        if (File.Exists(savedPath))
        {
            return savedPath;
        }

        var pinokioPath = Path.Combine(@"C:\pinokio\bin\miniconda\Library\bin", fileName);
        if (File.Exists(pinokioPath))
        {
            return pinokioPath;
        }

        var pathFolders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return pathFolders.Select(folder => Path.Combine(folder.Trim('"'), fileName)).FirstOrDefault(File.Exists)
            ?? string.Empty;
    }

    private static bool TryParseTime(string text, out TimeSpan value)
    {
        text = text.Trim();
        var parts = text.Split(':');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            if (minutes < 0 || seconds < 0 || seconds >= 60)
            {
                value = default;
                return false;
            }
            value = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
        {
            return value >= TimeSpan.Zero;
        }

        value = default;
        return false;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff").TrimEnd('0').TrimEnd('.')
            : value.ToString(@"mm\:ss\.fff").TrimEnd('0').TrimEnd('.');

    private static string TimeToken(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}h{value.Minutes:D2}m{value.Seconds:D2}s";

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }
        return fileName.Trim().TrimEnd('.');
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string LastMeaningfulLine(string log) =>
        log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        ?? "See the diagnostic log for FFmpeg details.";

    private static string FormatElapsed(TimeSpan value) =>
        value.TotalMinutes >= 1 ? $"{(int)value.TotalMinutes}m {value.Seconds}s" : $"{value.TotalSeconds:0.0}s";

    private void ShowError(string message)
    {
        _status.Text = "Needs attention";
        _status.ForeColor = Color.FromArgb(236, 112, 112);
        MessageBox.Show(this, message, "Stream Clip Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static TableLayoutPanel NewGrid(int columns) => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = columns,
        Padding = new Padding(14, 4, 14, 12)
    };

    private static Control CreatePanel(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = PanelColor,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
            ColumnCount = 1
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(14, 12, 14, 4)
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(content);
        UiTheme.Round(panel, 12);
        return panel;
    }

    private static TextBox CreateTextBox(string text = "") => new()
    {
        Text = text,
        BackColor = FieldColor,
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 5, 8, 5)
    };

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.White,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 9, 8, 3)
    };

    private static Label CreateMutedLabel(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        MaximumSize = new Size(760, 0),
        Margin = new Padding(3, 5, 3, 5)
    };

    private static Button CreateButton(string text, bool primary = false)
    {
        var normal = primary ? Accent : FieldColor;
        var hover = primary ? Color.FromArgb(120, 143, 246) : Color.FromArgb(52, 60, 76);
        var pressed = primary ? Color.FromArgb(82, 104, 218) : Color.FromArgb(29, 34, 45);
        var button = new ModernButton
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(6, 4, 0, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = normal,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Primary = primary
        };
        return button;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowWidth = Math.Max(1100, bounds.Width);
        _settings.WindowHeight = Math.Max(720, bounds.Height);
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        _composeControl.SaveCurrentPreferences();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmNcHitTest = 0x0084;
        if (message.Msg == wmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref message);
            if ((int)message.Result == 1)
            {
                var screenPoint = new Point((short)(message.LParam.ToInt64() & 0xffff), (short)((message.LParam.ToInt64() >> 16) & 0xffff));
                var point = PointToClient(screenPoint);
                const int grip = 7;
                var left = point.X <= grip;
                var right = point.X >= ClientSize.Width - grip;
                var top = point.Y <= grip;
                var bottom = point.Y >= ClientSize.Height - grip;
                message.Result = (IntPtr)(
                    left && top ? 13 :
                    right && top ? 14 :
                    left && bottom ? 16 :
                    right && bottom ? 17 :
                    left ? 10 :
                    right ? 11 :
                    top ? 12 :
                    bottom ? 15 : 1);
            }
            return;
        }
        base.WndProc(ref message);
    }
}

internal sealed class FfmpegSettingsForm : Form
{
    private readonly TextBox _ffmpeg = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ffprobe = new() { Dock = DockStyle.Fill };

    public string FfmpegPath => _ffmpeg.Text.Trim();
    public string FfprobePath => _ffprobe.Text.Trim();

    public FfmpegSettingsForm(string ffmpegPath, string ffprobePath)
    {
        Text = "FFmpeg settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 190);
        Font = new Font("Segoe UI", 10F);

        _ffmpeg.Text = ffmpegPath;
        _ffprobe.Text = ffprobePath;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 3,
            RowCount = 3
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = "FFmpeg", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_ffmpeg, 1, 0);
        var browseFfmpeg = new Button { Text = "Browse…", AutoSize = true };
        browseFfmpeg.Click += (_, _) => BrowseExecutable(_ffmpeg, "Choose ffmpeg.exe");
        grid.Controls.Add(browseFfmpeg, 2, 0);
        grid.Controls.Add(new Label { Text = "FFprobe", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_ffprobe, 1, 1);
        var browseFfprobe = new Button { Text = "Browse…", AutoSize = true };
        browseFfprobe.Click += (_, _) => BrowseExecutable(_ffprobe, "Choose ffprobe.exe");
        grid.Controls.Add(browseFfprobe, 2, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 1, 2);
        grid.SetColumnSpan(buttons, 2);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(grid);
    }

    private void BrowseExecutable(TextBox target, string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable files|*.exe|All files|*.*",
            InitialDirectory = File.Exists(target.Text) ? Path.GetDirectoryName(target.Text) : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }
}
