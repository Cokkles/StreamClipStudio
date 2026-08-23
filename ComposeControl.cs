using System.Diagnostics;
using System.Globalization;

namespace StreamClipStudio;

internal sealed class ComposeControl : UserControl
{
    private static readonly Color Background = UiTheme.Background;
    private static readonly Color PanelColor = UiTheme.Panel;
    private static readonly Color FieldColor = UiTheme.Field;
    private static readonly Color Muted = UiTheme.Muted;
    private static readonly Color Accent = UiTheme.Accent;
    private static readonly Color Success = Color.FromArgb(67, 190, 126);
    private static readonly Color Warning = Color.FromArgb(232, 178, 84);

    private readonly AppSettings _settings;
    private MediaInfo? _primaryInfo;
    private MediaInfo? _secondaryInfo;
    private SyncAnalysis? _syncAnalysis;
    private CancellationTokenSource? _operationCancellation;
    private bool _settingOffset;
    private bool _offsetManuallyEdited;
    private bool _settingPosition;
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 75 };
    private FlowLayoutPanel? _configurationHost;

    private readonly TextBox _primaryPath = CreateTextBox();
    private readonly TextBox _secondaryPath = CreateTextBox();
    private readonly Label _primarySummary = CreateMutedLabel("Choose the gameplay recording.");
    private readonly Label _secondarySummary = CreateMutedLabel("Choose the webcam or second recording.");
    private readonly ComboBox _primarySyncTrack = CreateComboBox();
    private readonly ComboBox _secondarySyncTrack = CreateComboBox();
    private readonly AudioTrackMixerPanel _primaryMixTracks = new();
    private readonly AudioTrackMixerPanel _secondaryMixTracks = new();
    private readonly Label _mixRisk = CreateMutedLabel("Select audio tracks to see mix headroom guidance.");

    private readonly TextBox _startTime = CreateTextBox("00:00:00");
    private readonly TextBox _endTime = CreateTextBox("00:00:30");
    private readonly TextBox _previewAtTime = CreateTextBox("00:00:00");
    private readonly NumericUpDown _searchRange = CreateNumeric(1, 60, 8, 0);
    private readonly NumericUpDown _offsetMilliseconds = CreateNumeric(-3_600_000, 3_600_000, 0, 1);
    private readonly CheckBox _applyDrift = CreateCheckBox("Apply measured long-session drift correction", true);
    private readonly Label _syncResult = CreateMutedLabel("Load both recordings, choose shared-audio tracks, then analyze sync.");
    private readonly Button _analyzeButton = CreateButton("Analyze waveform sync", true);

    private readonly ComboBox _layout = CreateComboBox();
    private readonly ComboBox _orientation = CreateComboBox();
    private readonly ComboBox _portraitLayout = CreateComboBox();
    private readonly NumericUpDown _gameplayFocusX = CreateNumeric(0, 100, 50, 0);
    private readonly ComboBox _safeAreaPlatform = CreateComboBox();
    private readonly ComboBox _position = CreateComboBox();
    private readonly NumericUpDown _webcamWidth = CreateNumeric(10, 70, 28, 0);
    private readonly NumericUpDown _webcamX = CreateNumeric(-50, 100, 2, 0);
    private readonly NumericUpDown _webcamY = CreateNumeric(-50, 100, 70, 0);
    private readonly OverlayPositionCanvas _positionCanvas = new();
    private readonly CheckBox _showSafeAreas = CreateCheckBox("Show platform safe-area and interface-obstruction guides", true);
    private readonly NumericUpDown _cropLeft = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _cropTop = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _cropRight = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _cropBottom = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _gameplayCropLeft = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _gameplayCropTop = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _gameplayCropRight = CreateNumeric(0, 45, 0, 0);
    private readonly NumericUpDown _gameplayCropBottom = CreateNumeric(0, 45, 0, 0);
    private readonly ComboBox _backgroundRemoval = CreateComboBox();
    private readonly NumericUpDown _keySensitivity = CreateNumeric(5, 45, 18, 0);
    private readonly CheckBox _flipWebcam = CreateCheckBox("Flip webcam horizontally (mirror it)", false);
    private readonly ComboBox _quality = CreateComboBox();
    private readonly Label _alphaStatus = CreateMutedLabel("Secondary transparency will be detected after loading.");

    private readonly TextBox _watermarkPath = CreateTextBox();
    private readonly NumericUpDown _watermarkX = CreateNumeric(-50, 100, 82, 0);
    private readonly NumericUpDown _watermarkY = CreateNumeric(-50, 100, 92, 0);
    private readonly NumericUpDown _watermarkWidth = CreateNumeric(2, 50, 15, 0);
    private readonly NumericUpDown _watermarkOpacity = CreateNumeric(1, 100, 75, 0);
    private readonly ComboBox _watermarkBackgroundRemoval = CreateComboBox();
    private readonly NumericUpDown _watermarkKeySensitivity = CreateNumeric(5, 45, 18, 0);
    private readonly AuxiliaryAudioRow[] _audioRows = Enumerable.Range(1, 5)
        .Select(number => new AuxiliaryAudioRow(number))
        .ToArray();
    private readonly ComboBox _savedPreset = CreateComboBox();
    private readonly TextBox _presetName = CreateTextBox("Banana - tucked left");

    private readonly TextBox _outputFolder = CreateTextBox();
    private readonly TextBox _outputName = CreateTextBox();
    private readonly SleekProgressBar _progress = new();
    private readonly Label _status = CreateMutedLabel("Ready");
    private readonly Button _previewButton = CreateButton("Render 10-second preview");
    private readonly Button _createButton = CreateButton("Compose video", true);
    private readonly Button _cancelButton = CreateButton("Cancel");
    private readonly Button _openOutputButton = CreateButton("Open output folder");

    public ComposeControl(AppSettings settings)
    {
        _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        Controls.Add(BuildShell());
        ConfigureControls();
        _layoutTimer.Tick += (_, _) =>
        {
            _layoutTimer.Stop();
            if (_configurationHost is not null) ResizeConfigurationPanels(_configurationHost);
        };
    }

    public void RefreshSettings()
    {
        var preferredOutput = @"C:\Users\Brian\Videos\Clipped stuff";
        _outputFolder.Text = !string.IsNullOrWhiteSpace(_settings.ComposeOutputFolder)
            ? _settings.ComposeOutputFolder
            : !string.IsNullOrWhiteSpace(_settings.OutputFolder)
                ? _settings.OutputFolder
                : Directory.Exists(preferredOutput)
                    ? preferredOutput
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        SelectByValue(_layout, _settings.ComposeLayout);
        SelectByValue(_orientation, _settings.ComposeOrientation);
        SelectByValue(_portraitLayout, _settings.PortraitLayout);
        _gameplayFocusX.Value = Math.Clamp(_settings.GameplayFocusXPercent, 0, 100);
        SelectByValue(_safeAreaPlatform, _settings.SafeAreaPlatform);
        SelectByValue(_quality, _settings.ComposeQuality);
        _settingPosition = true;
        _position.SelectedItem = _position.Items.Cast<object>()
            .FirstOrDefault(item => string.Equals(item.ToString(), _settings.WebcamPosition, StringComparison.OrdinalIgnoreCase))
            ?? _position.Items[0];
        _webcamWidth.Value = Math.Clamp(_settings.WebcamWidthPercent, 10, 70);
        if (_settings.WebcamUseCustomPosition)
        {
            _position.SelectedItem = "Custom / drag on canvas";
            _webcamX.Value = Math.Clamp(_settings.WebcamXPercent, -50, 100);
            _webcamY.Value = Math.Clamp(_settings.WebcamYPercent, -50, 100);
        }
        else
        {
            ApplyPositionPreset();
        }
        _settingPosition = false;
        UpdatePositionCanvas();
        _cropLeft.Value = Math.Clamp(_settings.WebcamCropLeftPercent, 0, 45);
        _cropTop.Value = Math.Clamp(_settings.WebcamCropTopPercent, 0, 45);
        _cropRight.Value = Math.Clamp(_settings.WebcamCropRightPercent, 0, 45);
        _cropBottom.Value = Math.Clamp(_settings.WebcamCropBottomPercent, 0, 45);
        _gameplayCropLeft.Value = Math.Clamp(_settings.GameplayCropLeftPercent, 0, 45);
        _gameplayCropTop.Value = Math.Clamp(_settings.GameplayCropTopPercent, 0, 45);
        _gameplayCropRight.Value = Math.Clamp(_settings.GameplayCropRightPercent, 0, 45);
        _gameplayCropBottom.Value = Math.Clamp(_settings.GameplayCropBottomPercent, 0, 45);
        var savedRemoval = _settings.WebcamBackgroundRemoval;
        if (savedRemoval == "none" && _settings.ApplyGreenScreen)
        {
            savedRemoval = "green";
        }
        SelectByValue(_backgroundRemoval, savedRemoval);
        _keySensitivity.Value = Math.Clamp(_settings.WebcamKeySensitivityPercent, 5, 45);
        _flipWebcam.Checked = _settings.FlipWebcamHorizontally;
        _watermarkPath.Text = _settings.WatermarkPath;
        _watermarkX.Value = Math.Clamp(_settings.WatermarkXPercent, -50, 100);
        _watermarkY.Value = Math.Clamp(_settings.WatermarkYPercent, -50, 100);
        _watermarkWidth.Value = Math.Clamp(_settings.WatermarkWidthPercent, 2, 50);
        _watermarkOpacity.Value = Math.Clamp(_settings.WatermarkOpacityPercent, 1, 100);
        SelectByValue(_watermarkBackgroundRemoval, _settings.WatermarkBackgroundRemoval);
        _watermarkKeySensitivity.Value = Math.Clamp(_settings.WatermarkKeySensitivityPercent, 5, 45);
        _searchRange.Value = Math.Clamp(_settings.SyncSearchSeconds, 1, 60);
        _showSafeAreas.Checked = _settings.ShowYouTubeSafeAreas;
        _positionCanvas.ShowYouTubeGuides = _showSafeAreas.Checked;
        RefreshPresetList();
    }

    private Control BuildShell()
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
        shell.Controls.Add(BuildHeader(), 0, 0);

        var configuration = new GradientFlowLayoutPanel
        {
            Name = "ComposeConfigurationHost",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 14, 18, 18),
            BackColor = Background
        };
        _configurationHost = configuration;
        UiTheme.ApplyDarkScrollBar(configuration);
        configuration.Controls.Add(BuildSourcesPanel());
        configuration.Controls.Add(BuildSyncPanel());
        configuration.Controls.Add(BuildCanvasPanel());
        configuration.Controls.Add(BuildCompositionPanel());
        configuration.Controls.Add(BuildSavedConfigurationsPanel());
        configuration.Controls.Add(BuildWatermarkPanel());
        configuration.Controls.Add(BuildAuxiliaryAudioPanel());
        configuration.Controls.Add(BuildOutputPanel());
        configuration.SizeChanged += (_, _) =>
        {
            _layoutTimer.Stop();
            _layoutTimer.Start();
        };
        shell.Controls.Add(configuration, 0, 1);
        shell.Controls.Add(BuildRunPanel(), 0, 2);
        return shell;
    }

    private Control BuildHeader()
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
            Text = "Synchronize and compose",
            Font = new Font("Segoe UI Semibold", 19F),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(22, 10)
        };
        var subtitle = CreateMutedLabel("Align gameplay and webcam recordings by shared audio or a manual lip-sync preview, then render them as one clip");
        subtitle.Location = new Point(24, 49);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        return header;
    }

    private static void ResizeConfigurationPanels(FlowLayoutPanel host)
    {
        var width = Math.Max(640, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control child in host.Controls)
        {
            child.MaximumSize = new Size(width, 0);
            child.Width = width;
        }
    }

    private Control BuildSourcesPanel()
    {
        var grid = NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateLabel("Gameplay / primary"), 0, 0);
        grid.Controls.Add(_primaryPath, 1, 0);
        var browsePrimary = CreateButton("Browse...");
        browsePrimary.Click += async (_, _) => await BrowseSourceAsync(true);
        grid.Controls.Add(browsePrimary, 2, 0);
        grid.Controls.Add(_primarySummary, 1, 1);
        grid.SetColumnSpan(_primarySummary, 2);
        grid.Controls.Add(CreateLabel("Sync audio"), 0, 2);
        grid.Controls.Add(_primarySyncTrack, 1, 2);
        grid.SetColumnSpan(_primarySyncTrack, 2);
        grid.Controls.Add(CreateLabel("Final audio mix"), 0, 3);
        grid.Controls.Add(_primaryMixTracks, 1, 3);
        grid.SetColumnSpan(_primaryMixTracks, 2);

        var divider = new Label
        {
            Height = 1,
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(59, 67, 84),
            Margin = new Padding(0, 12, 0, 12)
        };
        grid.Controls.Add(divider, 0, 4);
        grid.SetColumnSpan(divider, 3);

        grid.Controls.Add(CreateLabel("Webcam / secondary"), 0, 5);
        grid.Controls.Add(_secondaryPath, 1, 5);
        var browseSecondary = CreateButton("Browse...");
        browseSecondary.Click += async (_, _) => await BrowseSourceAsync(false);
        grid.Controls.Add(browseSecondary, 2, 5);
        grid.Controls.Add(_secondarySummary, 1, 6);
        grid.SetColumnSpan(_secondarySummary, 2);
        grid.Controls.Add(CreateLabel("Sync audio"), 0, 7);
        grid.Controls.Add(_secondarySyncTrack, 1, 7);
        grid.SetColumnSpan(_secondarySyncTrack, 2);
        grid.Controls.Add(CreateLabel("Final audio mix"), 0, 8);
        grid.Controls.Add(_secondaryMixTracks, 1, 8);
        grid.SetColumnSpan(_secondaryMixTracks, 2);
        grid.Controls.Add(_mixRisk, 1, 9);
        grid.SetColumnSpan(_mixRisk, 2);
        grid.Controls.Add(_alphaStatus, 1, 10);
        grid.SetColumnSpan(_alphaStatus, 2);
        return CreatePanel("Two source recordings", grid);
    }

    private Control BuildSyncPanel()
    {
        var grid = NewGrid(6);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        grid.Controls.Add(CreateLabel("Gameplay start"), 0, 0);
        grid.Controls.Add(_startTime, 1, 0);
        grid.Controls.Add(CreateLabel("End"), 2, 0);
        grid.Controls.Add(_endTime, 3, 0);
        grid.Controls.Add(CreateLabel("Search +/- sec"), 4, 0);
        grid.Controls.Add(_searchRange, 5, 0);

        grid.Controls.Add(CreateLabel("Preview from"), 0, 1);
        grid.Controls.Add(_previewAtTime, 1, 1);
        var previewHint = CreateMutedLabel("Gameplay timestamp used only for the 10-second sync preview; it does not change the final clip boundaries.");
        grid.Controls.Add(previewHint, 2, 1);
        grid.SetColumnSpan(previewHint, 4);

        grid.Controls.Add(CreateLabel("Secondary offset"), 0, 2);
        grid.Controls.Add(_offsetMilliseconds, 1, 2);
        var offsetUnit = CreateMutedLabel("milliseconds; positive = secondary began later");
        grid.Controls.Add(offsetUnit, 2, 2);
        grid.SetColumnSpan(offsetUnit, 4);

        var nudgeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
        };
        nudgeRow.Controls.Add(CreateNudgeButton("Webcam earlier 1 sec", -1000));
        nudgeRow.Controls.Add(CreateNudgeButton("Earlier 100 ms", -100));
        nudgeRow.Controls.Add(CreateNudgeButton("Earlier 1 frame", null, -1));
        nudgeRow.Controls.Add(CreateNudgeButton("Later 1 frame", null, 1));
        nudgeRow.Controls.Add(CreateNudgeButton("Later 100 ms", 100));
        nudgeRow.Controls.Add(CreateNudgeButton("Webcam later 1 sec", 1000));
        var resetOffset = CreateButton("Reset to filename time");
        resetOffset.Click += (_, _) => ResetToFilenameOffset();
        nudgeRow.Controls.Add(resetOffset);
        nudgeRow.Controls.Add(_analyzeButton);
        grid.Controls.Add(nudgeRow, 1, 3);
        grid.SetColumnSpan(nudgeRow, 5);

        grid.Controls.Add(_applyDrift, 1, 4);
        grid.SetColumnSpan(_applyDrift, 5);
        grid.Controls.Add(_syncResult, 1, 5);
        grid.SetColumnSpan(_syncResult, 5);
        var manualHint = CreateMutedLabel("No webcam audio: choose a gameplay moment where you speak, enter it in Preview from, render a preview, then move the webcam earlier/later until mouth movement matches the microphone sound.");
        manualHint.ForeColor = Warning;
        grid.Controls.Add(manualHint, 1, 6);
        grid.SetColumnSpan(manualHint, 5);
        return CreatePanel("Timing and waveform synchronization", grid);
    }

    private Control BuildCompositionPanel()
    {
        var grid = NewGrid(4);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        grid.Controls.Add(CreateLabel("Canvas orientation"), 0, 0);
        grid.Controls.Add(_orientation, 1, 0);
        grid.Controls.Add(CreateLabel("Safe-area platform"), 2, 0);
        grid.Controls.Add(_safeAreaPlatform, 3, 0);
        grid.Controls.Add(CreateLabel("Portrait template"), 0, 1);
        grid.Controls.Add(_portraitLayout, 1, 1);
        grid.Controls.Add(CreateLabel("Gameplay focus X %"), 2, 1);
        grid.Controls.Add(_gameplayFocusX, 3, 1);
        grid.Controls.Add(CreateLabel("Layout"), 0, 2);
        grid.Controls.Add(_layout, 1, 2);
        grid.Controls.Add(CreateLabel("Output quality"), 2, 2);
        grid.Controls.Add(_quality, 3, 2);
        grid.Controls.Add(CreateLabel("Webcam position"), 0, 3);
        grid.Controls.Add(_position, 1, 3);
        grid.Controls.Add(CreateLabel("Webcam width %"), 2, 3);
        grid.Controls.Add(_webcamWidth, 3, 3);
        grid.Controls.Add(CreateLabel("Webcam X %"), 0, 4);
        grid.Controls.Add(_webcamX, 1, 4);
        grid.Controls.Add(CreateLabel("Webcam Y %"), 2, 4);
        grid.Controls.Add(_webcamY, 3, 4);
        grid.Controls.Add(CreateLabel("Remove background"), 0, 5);
        grid.Controls.Add(_backgroundRemoval, 1, 5);
        grid.Controls.Add(CreateLabel("Key sensitivity %"), 2, 5);
        grid.Controls.Add(_keySensitivity, 3, 5);
        grid.Controls.Add(_flipWebcam, 1, 6);
        grid.SetColumnSpan(_flipWebcam, 3);

        grid.Controls.Add(CreateLabel("Webcam crop left %"), 0, 7);
        grid.Controls.Add(_cropLeft, 1, 7);
        grid.Controls.Add(CreateLabel("Webcam crop top %"), 2, 7);
        grid.Controls.Add(_cropTop, 3, 7);
        grid.Controls.Add(CreateLabel("Webcam crop right %"), 0, 8);
        grid.Controls.Add(_cropRight, 1, 8);
        grid.Controls.Add(CreateLabel("Webcam crop bottom %"), 2, 8);
        grid.Controls.Add(_cropBottom, 3, 8);
        grid.Controls.Add(CreateLabel("Gameplay crop left %"), 0, 9);
        grid.Controls.Add(_gameplayCropLeft, 1, 9);
        grid.Controls.Add(CreateLabel("Gameplay crop top %"), 2, 9);
        grid.Controls.Add(_gameplayCropTop, 3, 9);
        grid.Controls.Add(CreateLabel("Gameplay crop right %"), 0, 10);
        grid.Controls.Add(_gameplayCropRight, 1, 10);
        grid.Controls.Add(CreateLabel("Gameplay crop bottom %"), 2, 10);
        grid.Controls.Add(_gameplayCropBottom, 3, 10);
        return CreatePanel("Composition", grid);
    }

    private Control BuildCanvasPanel()
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.Controls.Add(_positionCanvas, 0, 0);
        stack.Controls.Add(_showSafeAreas, 0, 1);
        stack.Controls.Add(CreateMutedLabel("Drag the webcam or watermark directly. X/Y use each item's top-left corner and may extend beyond the frame."), 0, 2);
        return CreatePanel("Interactive placement", stack);
    }

    private Control BuildWatermarkPanel()
    {
        var grid = NewGrid(5);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));

        _watermarkPath.ReadOnly = true;
        grid.Controls.Add(CreateLabel("Image"), 0, 0);
        grid.Controls.Add(_watermarkPath, 1, 0);
        var browse = CreateButton("Browse...");
        browse.Click += (_, _) => BrowseWatermark();
        grid.Controls.Add(browse, 2, 0);
        var clear = CreateButton("Clear");
        clear.Click += (_, _) => _watermarkPath.Clear();
        grid.Controls.Add(clear, 3, 0);
        grid.SetColumnSpan(clear, 2);

        grid.Controls.Add(CreateLabel("X %"), 0, 1);
        grid.Controls.Add(_watermarkX, 1, 1);
        grid.Controls.Add(CreateLabel("Y %"), 2, 1);
        grid.Controls.Add(_watermarkY, 4, 1);
        grid.Controls.Add(CreateLabel("Width %"), 0, 2);
        grid.Controls.Add(_watermarkWidth, 1, 2);
        grid.Controls.Add(CreateLabel("Opacity %"), 2, 2);
        grid.Controls.Add(_watermarkOpacity, 4, 2);
        grid.Controls.Add(CreateLabel("Remove background"), 0, 3);
        grid.Controls.Add(_watermarkBackgroundRemoval, 1, 3);
        grid.Controls.Add(CreateLabel("Key sensitivity %"), 2, 3);
        grid.Controls.Add(_watermarkKeySensitivity, 4, 3);
        var hint = CreateMutedLabel("Drag the watermark directly on the composition canvas or use X/Y. PNG transparency is recommended; green/black removal is available for keyed images.");
        grid.Controls.Add(hint, 1, 4);
        grid.SetColumnSpan(hint, 4);
        return CreatePanel("Optional image watermark", grid);
    }

    private Control BuildSavedConfigurationsPanel()
    {
        var grid = NewGrid(6);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateLabel("Saved configuration"), 0, 0);
        grid.Controls.Add(_savedPreset, 1, 0);
        var load = CreateButton("Load");
        load.Click += (_, _) => LoadSelectedPreset();
        grid.Controls.Add(load, 2, 0);
        grid.Controls.Add(_presetName, 3, 0);
        var save = CreateButton("Save current");
        save.Click += (_, _) => SaveCurrentPreset();
        grid.Controls.Add(save, 4, 0);
        var delete = CreateButton("Delete");
        delete.Click += (_, _) => DeleteSelectedPreset();
        grid.Controls.Add(delete, 5, 0);

        var hint = CreateMutedLabel("Saves layout, webcam placement/crop/keying, gameplay crop, output quality, and watermark settings. Source files and timestamps are intentionally excluded.");
        grid.Controls.Add(hint, 1, 1);
        grid.SetColumnSpan(hint, 5);
        grid.Controls.Add(CreateLabel("Project file"), 0, 2);
        var saveProject = CreateButton("Save project...");
        saveProject.Click += (_, _) => SaveProject();
        grid.Controls.Add(saveProject, 1, 2);
        var loadProject = CreateButton("Open project...");
        loadProject.Click += async (_, _) => await LoadProjectAsync();
        grid.Controls.Add(loadProject, 2, 2);
        var projectHint = CreateMutedLabel("Versioned .scsproj files preserve sources, synchronization, clip range, orientation, and render layout for the future multi-segment timeline.");
        grid.Controls.Add(projectHint, 3, 2);
        grid.SetColumnSpan(projectHint, 3);
        return CreatePanel("Saved composition configurations", grid);
    }

    private Control BuildAuxiliaryAudioPanel()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(14, 4, 14, 12)
        };
        var hint = CreateMutedLabel("Add up to five MP3/WAV/M4A/etc. files. Start ms is relative to the beginning of the final clip; negative values trim the beginning of that audio file.");
        content.Controls.Add(hint);
        foreach (var row in _audioRows)
        {
            content.Controls.Add(row);
        }
        return CreatePanel("Optional music, voiceover, and sound effects", content, initiallyCollapsed: true);
    }

    private Control BuildOutputPanel()
    {
        var grid = NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateLabel("Output folder"), 0, 0);
        grid.Controls.Add(_outputFolder, 1, 0);
        var browse = CreateButton("Browse...");
        browse.Click += (_, _) => BrowseOutput();
        grid.Controls.Add(browse, 2, 0);
        grid.Controls.Add(CreateLabel("File name"), 0, 1);
        grid.Controls.Add(_outputName, 1, 1);
        grid.SetColumnSpan(_outputName, 2);
        return CreatePanel("Output", grid);
    }

    private Control BuildRunPanel()
    {
        var container = new TableLayoutPanel
        {
            Name = "ComposeActionBar",
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
        for (var index = 0; index < 4; index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        row.Controls.Add(_status, 0, 0);
        row.Controls.Add(_openOutputButton, 1, 0);
        row.Controls.Add(_cancelButton, 2, 0);
        row.Controls.Add(_previewButton, 3, 0);
        row.Controls.Add(_createButton, 4, 0);
        container.Controls.Add(row);
        return container;
    }

    private void ConfigureControls()
    {
        foreach (var control in new Control[]
        {
            _startTime, _endTime, _previewAtTime, _searchRange, _offsetMilliseconds,
            _webcamWidth, _webcamX, _webcamY, _cropLeft, _cropTop, _cropRight, _cropBottom,
            _gameplayCropLeft, _gameplayCropTop, _gameplayCropRight, _gameplayCropBottom,
            _gameplayFocusX,
            _keySensitivity, _watermarkX, _watermarkY, _watermarkWidth, _watermarkOpacity,
            _watermarkKeySensitivity
        })
        {
            control.Dock = DockStyle.None;
            control.Width = control is TextBox ? 130 : 90;
            control.Anchor = AnchorStyles.Left;
        }
        _primaryPath.ReadOnly = true;
        _secondaryPath.ReadOnly = true;
        _layout.Items.Add(new ChoiceItem("overlay", "Webcam overlay on gameplay"));
        _layout.Items.Add(new ChoiceItem("sidebyside", "Side by side"));
        _orientation.Items.Add(new ChoiceItem("landscape", "Landscape 16:9"));
        _orientation.Items.Add(new ChoiceItem("portrait", "Portrait / Reel 9:16"));
        _portraitLayout.Items.Add(new ChoiceItem("focus", "Focused gameplay crop"));
        _portraitLayout.Items.Add(new ChoiceItem("blur", "Full gameplay with blurred fill"));
        _portraitLayout.Items.Add(new ChoiceItem("stack", "Gameplay top / banana lower"));
        _safeAreaPlatform.Items.Add(new ChoiceItem("youtube", "YouTube Shorts"));
        _safeAreaPlatform.Items.Add(new ChoiceItem("tiktok", "TikTok"));
        _safeAreaPlatform.Items.Add(new ChoiceItem("meta", "Facebook / Instagram Reels"));
        _quality.Items.AddRange(ComposeOutputProfile.All.Cast<object>().ToArray());
        _backgroundRemoval.Items.Add(new ChoiceItem("none", "None / already transparent"));
        _backgroundRemoval.Items.Add(new ChoiceItem("black", "Remove black background"));
        _backgroundRemoval.Items.Add(new ChoiceItem("green", "Remove green background"));
        _watermarkBackgroundRemoval.Items.Add(new ChoiceItem("none", "None / already transparent"));
        _watermarkBackgroundRemoval.Items.Add(new ChoiceItem("black", "Remove black background"));
        _watermarkBackgroundRemoval.Items.Add(new ChoiceItem("green", "Remove green background"));
        _position.Items.AddRange(new object[]
        {
            "Bottom center", "Bottom left", "Bottom right", "Top center", "Top left", "Top right",
            "Custom / drag on canvas"
        });
        _layout.SelectedIndex = 0;
        _quality.SelectedIndex = 1;
        _backgroundRemoval.SelectedIndex = 0;
        _watermarkBackgroundRemoval.SelectedIndex = 0;
        _position.SelectedIndex = 0;
        _orientation.SelectedIndex = 0;
        _portraitLayout.SelectedIndex = 0;
        _safeAreaPlatform.SelectedIndex = 0;

        _layout.SelectedIndexChanged += (_, _) => UpdateLayoutState();
        _orientation.SelectedIndexChanged += (_, _) => OrientationChanged();
        _portraitLayout.SelectedIndexChanged += (_, _) => { UpdateLayoutState(); UpdatePositionCanvas(); };
        _safeAreaPlatform.SelectedIndexChanged += (_, _) => UpdatePositionCanvas();
        _gameplayFocusX.ValueChanged += (_, _) => UpdatePositionCanvas();
        _position.SelectedIndexChanged += (_, _) =>
        {
            if (!_settingPosition && _position.SelectedItem?.ToString() != "Custom / drag on canvas")
            {
                ApplyPositionPreset();
                UpdatePositionCanvas();
            }
        };
        _webcamWidth.ValueChanged += (_, _) =>
        {
            UpdatePositionCanvas();
            if (!_settingPosition && _position.SelectedItem?.ToString() != "Custom / drag on canvas")
            {
                ApplyPositionPreset();
            }
        };
        _webcamX.ValueChanged += (_, _) => PositionNumericChanged();
        _webcamY.ValueChanged += (_, _) => PositionNumericChanged();
        foreach (var cropControl in new[]
                 {
                     _cropLeft, _cropTop, _cropRight, _cropBottom,
                     _gameplayCropLeft, _gameplayCropTop, _gameplayCropRight, _gameplayCropBottom
                 })
        {
            cropControl.ValueChanged += (_, _) => UpdatePositionCanvas();
        }
        _watermarkPath.TextChanged += (_, _) => UpdatePositionCanvas();
        _watermarkX.ValueChanged += (_, _) => UpdatePositionCanvas();
        _watermarkY.ValueChanged += (_, _) => UpdatePositionCanvas();
        _watermarkWidth.ValueChanged += (_, _) => UpdatePositionCanvas();
        _positionCanvas.PositionChanged += (_, _) =>
        {
            _settingPosition = true;
            _webcamX.Value = _positionCanvas.XPercent;
            _webcamY.Value = _positionCanvas.YPercent;
            _position.SelectedItem = "Custom / drag on canvas";
            _settingPosition = false;
        };
        _positionCanvas.WatermarkPositionChanged += (_, _) =>
        {
            _watermarkX.Value = _positionCanvas.WatermarkXPercent;
            _watermarkY.Value = _positionCanvas.WatermarkYPercent;
        };
        _showSafeAreas.CheckedChanged += (_, _) =>
        {
            _positionCanvas.ShowYouTubeGuides = _showSafeAreas.Checked;
            _positionCanvas.Invalidate();
        };
        _primaryMixTracks.MixChanged += (_, _) => UpdateMixRisk();
        _secondaryMixTracks.MixChanged += (_, _) => UpdateMixRisk();
        _backgroundRemoval.SelectedIndexChanged += (_, _) => UpdateLayoutState();
        _quality.SelectedIndexChanged += (_, _) => UpdateSuggestedName();
        _startTime.TextChanged += (_, _) =>
        {
            UpdateSuggestedName();
            UpdateAnalyzedOffsetForStart();
        };
        _endTime.TextChanged += (_, _) => UpdateSuggestedName();
        _primaryPath.TextChanged += (_, _) => UpdateSuggestedName();
        _offsetMilliseconds.ValueChanged += (_, _) =>
        {
            if (!_settingOffset)
            {
                _offsetManuallyEdited = true;
                _syncResult.ForeColor = Warning;
                _syncResult.Text = $"Manual offset: {_offsetMilliseconds.Value:0.0} ms. Render a preview to verify lip/audio sync.";
            }
        };

        _analyzeButton.Click += async (_, _) => await AnalyzeSyncAsync();
        _previewButton.Click += async (_, _) => await RenderAsync(true);
        _createButton.Click += async (_, _) => await RenderAsync(false);
        _cancelButton.Click += (_, _) => _operationCancellation?.Cancel();
        _cancelButton.Enabled = false;
        _openOutputButton.Click += (_, _) => OpenOutputFolder();
        UpdateLayoutState();
    }

    private void PositionNumericChanged()
    {
        if (_settingPosition) return;
        _settingPosition = true;
        _position.SelectedItem = "Custom / drag on canvas";
        _settingPosition = false;
        UpdatePositionCanvas();
    }

    private void ApplyPositionPreset()
    {
        var width = (int)_webcamWidth.Value;
        var edge = 2;
        var center = (100 - width) / 2;
        var bottom = 100 - width - edge;
        var (x, y) = _position.SelectedItem?.ToString() switch
        {
            "Bottom left" => (edge, bottom),
            "Bottom right" => (100 - width - edge, bottom),
            "Top left" => (edge, edge),
            "Top center" => (center, edge),
            "Top right" => (100 - width - edge, edge),
            _ => (center, bottom)
        };
        _settingPosition = true;
        _webcamX.Value = Math.Clamp(x, (int)_webcamX.Minimum, (int)_webcamX.Maximum);
        _webcamY.Value = Math.Clamp(y, (int)_webcamY.Minimum, (int)_webcamY.Maximum);
        _settingPosition = false;
    }

    private void UpdatePositionCanvas()
    {
        _positionCanvas.PortraitMode = (_orientation.SelectedItem as ChoiceItem)?.Value == "portrait";
        _positionCanvas.PlatformGuide = (_safeAreaPlatform.SelectedItem as ChoiceItem)?.Value ?? "youtube";
        _positionCanvas.GameplayFocusXPercent = (int)_gameplayFocusX.Value;
        _positionCanvas.WebcamWidthPercent = (int)_webcamWidth.Value;
        _positionCanvas.XPercent = (int)_webcamX.Value;
        _positionCanvas.YPercent = (int)_webcamY.Value;
        _positionCanvas.CropLeftPercent = (int)_cropLeft.Value;
        _positionCanvas.CropTopPercent = (int)_cropTop.Value;
        _positionCanvas.CropRightPercent = (int)_cropRight.Value;
        _positionCanvas.CropBottomPercent = (int)_cropBottom.Value;
        _positionCanvas.GameplayCropLeftPercent = (int)_gameplayCropLeft.Value;
        _positionCanvas.GameplayCropTopPercent = (int)_gameplayCropTop.Value;
        _positionCanvas.GameplayCropRightPercent = (int)_gameplayCropRight.Value;
        _positionCanvas.GameplayCropBottomPercent = (int)_gameplayCropBottom.Value;
        _positionCanvas.ShowWatermark = !string.IsNullOrWhiteSpace(_watermarkPath.Text);
        _positionCanvas.WatermarkXPercent = (int)_watermarkX.Value;
        _positionCanvas.WatermarkYPercent = (int)_watermarkY.Value;
        _positionCanvas.WatermarkWidthPercent = (int)_watermarkWidth.Value;
        _positionCanvas.Invalidate();
    }

    private void OrientationChanged()
    {
        var portrait = (_orientation.SelectedItem as ChoiceItem)?.Value == "portrait";
        _positionCanvas.Height = portrait ? 510 : 280;
        var current = _quality.SelectedItem as ComposeOutputProfile;
        if (portrait && current?.IsVertical != true)
            _quality.SelectedItem = ComposeOutputProfile.All.First(profile => profile.Id == "vertical-universal");
        else if (!portrait && current?.IsVertical == true)
            _quality.SelectedItem = ComposeOutputProfile.All.First(profile => profile.Id == "youtube4k");
        if (portrait && _position.SelectedItem?.ToString() != "Custom / drag on canvas")
        {
            _position.SelectedItem = "Custom / drag on canvas";
            _webcamWidth.Value = 70;
            _webcamX.Value = 15;
            _webcamY.Value = 68;
        }
        UpdateLayoutState();
        UpdatePositionCanvas();
        UpdateSuggestedName();
    }

    private void BrowseWatermark()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a watermark image",
            Filter = "Image files|*.png;*.webp;*.jpg;*.jpeg;*.bmp|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _watermarkPath.Text = dialog.FileName;
        }
    }

    private void RefreshPresetList(string? selectName = null)
    {
        _savedPreset.Items.Clear();
        foreach (var preset in _settings.CompositionPresets.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _savedPreset.Items.Add(preset);
        }
        if (!string.IsNullOrWhiteSpace(selectName))
        {
            _savedPreset.SelectedItem = _savedPreset.Items.Cast<CompositionPreset>()
                .FirstOrDefault(item => string.Equals(item.Name, selectName, StringComparison.OrdinalIgnoreCase));
        }
        else if (_savedPreset.Items.Count > 0)
        {
            _savedPreset.SelectedIndex = 0;
        }
    }

    private void SaveCurrentPreset()
    {
        var name = _presetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter a name for this composition configuration.");
            return;
        }

        var existing = _settings.CompositionPresets.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var overwrite = MessageBox.Show(
                FindForm(),
                $"Replace the saved configuration '{existing.Name}'?",
                "Replace configuration",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (overwrite != DialogResult.Yes) return;
            _settings.CompositionPresets.Remove(existing);
        }

        _settings.CompositionPresets.Add(CapturePreset(name));
        SaveCurrentPreferences();
        RefreshPresetList(name);
        _status.ForeColor = Success;
        _status.Text = $"Saved configuration: {name}";
    }

    private void LoadSelectedPreset()
    {
        if (_savedPreset.SelectedItem is not CompositionPreset preset)
        {
            ShowError("Choose a saved composition configuration to load.");
            return;
        }

        SelectByValue(_layout, preset.Layout);
        SelectByValue(_quality, preset.Quality);
        SelectByValue(_orientation, preset.Orientation);
        SelectByValue(_portraitLayout, preset.PortraitLayout);
        _gameplayFocusX.Value = Math.Clamp(preset.GameplayFocusXPercent, 0, 100);
        SelectByValue(_safeAreaPlatform, preset.SafeAreaPlatform);
        _settingPosition = true;
        _position.SelectedItem = "Custom / drag on canvas";
        _webcamWidth.Value = Math.Clamp(preset.WebcamWidthPercent, 10, 70);
        _webcamX.Value = Math.Clamp(preset.WebcamXPercent, -50, 100);
        _webcamY.Value = Math.Clamp(preset.WebcamYPercent, -50, 100);
        _settingPosition = false;
        _cropLeft.Value = Math.Clamp(preset.WebcamCropLeftPercent, 0, 45);
        _cropTop.Value = Math.Clamp(preset.WebcamCropTopPercent, 0, 45);
        _cropRight.Value = Math.Clamp(preset.WebcamCropRightPercent, 0, 45);
        _cropBottom.Value = Math.Clamp(preset.WebcamCropBottomPercent, 0, 45);
        _gameplayCropLeft.Value = Math.Clamp(preset.GameplayCropLeftPercent, 0, 45);
        _gameplayCropTop.Value = Math.Clamp(preset.GameplayCropTopPercent, 0, 45);
        _gameplayCropRight.Value = Math.Clamp(preset.GameplayCropRightPercent, 0, 45);
        _gameplayCropBottom.Value = Math.Clamp(preset.GameplayCropBottomPercent, 0, 45);
        SelectByValue(_backgroundRemoval, preset.BackgroundRemoval);
        _keySensitivity.Value = Math.Clamp(preset.KeySensitivityPercent, 5, 45);
        _flipWebcam.Checked = preset.FlipWebcamHorizontally;
        _watermarkPath.Text = preset.WatermarkPath;
        _watermarkX.Value = Math.Clamp(preset.WatermarkXPercent, -50, 100);
        _watermarkY.Value = Math.Clamp(preset.WatermarkYPercent, -50, 100);
        _watermarkWidth.Value = Math.Clamp(preset.WatermarkWidthPercent, 2, 50);
        _watermarkOpacity.Value = Math.Clamp(preset.WatermarkOpacityPercent, 1, 100);
        SelectByValue(_watermarkBackgroundRemoval, preset.WatermarkBackgroundRemoval);
        _watermarkKeySensitivity.Value = Math.Clamp(preset.WatermarkKeySensitivityPercent, 5, 45);
        _showSafeAreas.Checked = preset.ShowYouTubeSafeAreas;
        if (_primaryInfo is not null) _primaryMixTracks.SetTracks(_primaryInfo.AudioStreams, true, preset.AudioMixes.Where(item => item.Source == "primary"));
        if (_secondaryInfo is not null) _secondaryMixTracks.SetTracks(_secondaryInfo.AudioStreams, false, preset.AudioMixes.Where(item => item.Source == "secondary"));
        _presetName.Text = preset.Name;
        UpdatePositionCanvas();
        UpdateLayoutState();
        SaveCurrentPreferences();
        _status.ForeColor = Success;
        _status.Text = $"Loaded configuration: {preset.Name}";
    }

    private void DeleteSelectedPreset()
    {
        if (_savedPreset.SelectedItem is not CompositionPreset preset) return;
        var confirm = MessageBox.Show(
            FindForm(),
            $"Delete the saved configuration '{preset.Name}'?",
            "Delete configuration",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;
        _settings.CompositionPresets.Remove(preset);
        _settings.Save();
        RefreshPresetList();
        _status.Text = "Saved configuration deleted";
    }

    private void SaveProject()
    {
        if (_primaryInfo is null || _secondaryInfo is null || !TryParseTime(_startTime.Text, out var start) || !TryParseTime(_endTime.Text, out var end))
        {
            ShowError("Load both recordings and enter a valid clip range before saving a project.");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Save Stream Clip Studio project",
            Filter = "Stream Clip Studio project|*.scsproj",
            FileName = string.IsNullOrWhiteSpace(_settings.LastProjectPath) ? $"{Path.GetFileNameWithoutExtension(_primaryPath.Text)}.scsproj" : Path.GetFileName(_settings.LastProjectPath),
            InitialDirectory = string.IsNullOrWhiteSpace(_settings.LastProjectPath) ? _outputFolder.Text : Path.GetDirectoryName(_settings.LastProjectPath)
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        var pair = new ProjectSourcePair { GameplayPath = _primaryPath.Text, WebcamPath = _secondaryPath.Text, SecondaryOffsetSeconds = (double)_offsetMilliseconds.Value / 1000d, SecondaryPtsScale = _applyDrift.Checked && _syncAnalysis is not null ? _syncAnalysis.SecondaryPtsScale : 1 };
        var project = new StreamClipProject
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            SourcePairs = new List<ProjectSourcePair> { pair },
            Segments = new List<ProjectSegment> { new() { SourcePairId = pair.Id, StartSeconds = start.TotalSeconds, DurationSeconds = (end - start).TotalSeconds, Order = 0 } },
            Composition = new ProjectComposition
            {
                Orientation = (_orientation.SelectedItem as ChoiceItem)?.Value ?? "landscape",
                Layout = (_layout.SelectedItem as ChoiceItem)?.Value ?? "overlay",
                PortraitLayout = (_portraitLayout.SelectedItem as ChoiceItem)?.Value ?? "focus",
                GameplayFocusXPercent = (int)_gameplayFocusX.Value,
                OutputProfileId = (_quality.SelectedItem as ComposeOutputProfile)?.Id ?? "youtube4k"
            },
            AudioLayers = _audioRows.Select(row => row.BuildOverlay()).Where(item => item is not null).Select(item => new ProjectAudioLayer { Path = item!.Path, StartMilliseconds = item.StartMilliseconds, VolumePercent = item.VolumePercent, Label = Path.GetFileNameWithoutExtension(item.Path) }).ToList()
        };
        project.Save(dialog.FileName);
        _settings.LastProjectPath = dialog.FileName; _settings.Save();
        _status.ForeColor = Success; _status.Text = $"Project saved: {Path.GetFileName(dialog.FileName)}";
    }

    private async Task LoadProjectAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open Stream Clip Studio project",
            Filter = "Stream Clip Studio project|*.scsproj",
            CheckFileExists = true,
            InitialDirectory = string.IsNullOrWhiteSpace(_settings.LastProjectPath) ? string.Empty : Path.GetDirectoryName(_settings.LastProjectPath)
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var project = StreamClipProject.Load(dialog.FileName);
            var pair = project.SourcePairs.FirstOrDefault() ?? throw new InvalidDataException("The project has no source pair.");
            var segment = project.Segments.OrderBy(item => item.Order).FirstOrDefault() ?? throw new InvalidDataException("The project has no clip segment.");
            if (!File.Exists(pair.GameplayPath) || !File.Exists(pair.WebcamPath)) throw new FileNotFoundException("One or more source recordings referenced by this project could not be found.");
            await LoadSourceAsync(pair.GameplayPath, true); await LoadSourceAsync(pair.WebcamPath, false);
            _startTime.Text = FormatTime(TimeSpan.FromSeconds(segment.StartSeconds));
            _endTime.Text = FormatTime(TimeSpan.FromSeconds(segment.StartSeconds + segment.DurationSeconds));
            SetOffset(pair.SecondaryOffsetSeconds * 1000);
            SelectByValue(_orientation, project.Composition.Orientation); SelectByValue(_layout, project.Composition.Layout);
            SelectByValue(_portraitLayout, project.Composition.PortraitLayout); _gameplayFocusX.Value = Math.Clamp(project.Composition.GameplayFocusXPercent, 0, 100);
            SelectByValue(_quality, project.Composition.OutputProfileId);
            _settings.LastProjectPath = dialog.FileName; _settings.Save();
            _status.ForeColor = Success; _status.Text = $"Project opened: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private CompositionPreset CapturePreset(string name) => new()
    {
        Name = name,
        Layout = (_layout.SelectedItem as ChoiceItem)?.Value ?? "overlay",
        Quality = (_quality.SelectedItem as ComposeOutputProfile)?.Id ?? "youtube4k",
        Orientation = (_orientation.SelectedItem as ChoiceItem)?.Value ?? "landscape",
        PortraitLayout = (_portraitLayout.SelectedItem as ChoiceItem)?.Value ?? "focus",
        GameplayFocusXPercent = (int)_gameplayFocusX.Value,
        SafeAreaPlatform = (_safeAreaPlatform.SelectedItem as ChoiceItem)?.Value ?? "youtube",
        Position = _position.SelectedItem?.ToString() ?? "Custom / drag on canvas",
        WebcamWidthPercent = (int)_webcamWidth.Value,
        WebcamXPercent = (int)_webcamX.Value,
        WebcamYPercent = (int)_webcamY.Value,
        WebcamCropLeftPercent = (int)_cropLeft.Value,
        WebcamCropTopPercent = (int)_cropTop.Value,
        WebcamCropRightPercent = (int)_cropRight.Value,
        WebcamCropBottomPercent = (int)_cropBottom.Value,
        GameplayCropLeftPercent = (int)_gameplayCropLeft.Value,
        GameplayCropTopPercent = (int)_gameplayCropTop.Value,
        GameplayCropRightPercent = (int)_gameplayCropRight.Value,
        GameplayCropBottomPercent = (int)_gameplayCropBottom.Value,
        BackgroundRemoval = (_backgroundRemoval.SelectedItem as ChoiceItem)?.Value ?? "none",
        KeySensitivityPercent = (int)_keySensitivity.Value,
        FlipWebcamHorizontally = _flipWebcam.Checked,
        WatermarkPath = _watermarkPath.Text,
        WatermarkXPercent = (int)_watermarkX.Value,
        WatermarkYPercent = (int)_watermarkY.Value,
        WatermarkWidthPercent = (int)_watermarkWidth.Value,
        WatermarkOpacityPercent = (int)_watermarkOpacity.Value,
        WatermarkBackgroundRemoval = (_watermarkBackgroundRemoval.SelectedItem as ChoiceItem)?.Value ?? "none",
        WatermarkKeySensitivityPercent = (int)_watermarkKeySensitivity.Value,
        ShowYouTubeSafeAreas = _showSafeAreas.Checked,
        AudioMixes = _primaryMixTracks.CaptureSettings("primary").Concat(_secondaryMixTracks.CaptureSettings("secondary")).ToList()
    };

    public void SaveCurrentPreferences()
    {
        var layout = (_layout.SelectedItem as ChoiceItem)?.Value ?? "overlay";
        var quality = (_quality.SelectedItem as ComposeOutputProfile)?.Id ?? "youtube4k";
        SavePreferences(layout, quality);
    }

    private async Task BrowseSourceAsync(bool primary)
    {
        using var dialog = new OpenFileDialog
        {
            Title = primary ? "Choose the gameplay recording" : "Choose the webcam or second recording",
            Filter = "Video files|*.mkv;*.mp4;*.mov;*.webm;*.avi|All files|*.*",
            InitialDirectory = primary
                ? Directory.Exists(_settings.LastInputFolder) ? _settings.LastInputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                : Directory.Exists(_settings.LastSecondaryFolder) ? _settings.LastSecondaryFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await LoadSourceAsync(dialog.FileName, primary);
        }
    }

    private async Task LoadSourceAsync(string path, bool primary)
    {
        if (!File.Exists(_settings.FfprobePath))
        {
            ShowError("FFprobe was not found. Configure it from the single-clip tab first.");
            return;
        }

        SetBusy(true, primary ? "Reading gameplay metadata..." : "Reading secondary metadata...");
        try
        {
            var info = await MediaProbe.ReadAsync(_settings.FfprobePath, path, CancellationToken.None);
            if (primary)
            {
                _primaryInfo = info;
                _primaryPath.Text = path;
                _primarySummary.Text = info.Summary;
                _settings.LastInputFolder = Path.GetDirectoryName(path) ?? string.Empty;
                PopulateAudio(info, _primarySyncTrack, _primaryMixTracks, checkFirstMixTrack: true, "primary");
                _endTime.Text = FormatTime(TimeSpan.FromSeconds(Math.Min(info.Duration.TotalSeconds, 30)));
                _previewAtTime.Text = _startTime.Text;
            }
            else
            {
                _secondaryInfo = info;
                _secondaryPath.Text = path;
                _secondarySummary.Text = info.Summary;
                _settings.LastSecondaryFolder = Path.GetDirectoryName(path) ?? string.Empty;
                PopulateAudio(info, _secondarySyncTrack, _secondaryMixTracks, checkFirstMixTrack: false, "secondary");
                _alphaStatus.Text = info.HasAlpha
                    ? $"Alpha-capable pixel format detected: {info.PixelFormat}. Direct overlay is available."
                    : $"Pixel format {info.PixelFormat} has no stored alpha channel. Choose black or green background removal when needed.";
                if (info.AudioStreams.Count == 0 || info.AudioStreams.All(track => track.IsLikelySilent))
                {
                    _alphaStatus.Text += " Webcam audio is absent or silent; visual lip-sync mode is available.";
                }
                _alphaStatus.ForeColor = info.HasAlpha ? Success : Muted;
            }

            _syncAnalysis = null;
            _offsetManuallyEdited = false;
            UpdateRoughOffsetMessage();
            UpdateSyncAvailability();
            _settings.Save();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateAudio(
        MediaInfo info,
        ComboBox syncCombo,
        AudioTrackMixerPanel mixList,
        bool checkFirstMixTrack,
        string source)
    {
        syncCombo.Items.Clear();
        foreach (var track in info.AudioStreams)
        {
            if (!track.IsLikelySilent)
            {
                syncCombo.Items.Add(track);
            }
        }
        mixList.SetTracks(info.AudioStreams, checkFirstMixTrack, _settings.AudioMixes.Where(item => item.Source == source));
        if (syncCombo.Items.Count > 0)
        {
            syncCombo.SelectedIndex = 0;
        }
    }

    private void UpdateRoughOffsetMessage()
    {
        if (_primaryInfo is null || _secondaryInfo is null)
        {
            return;
        }
        var rough = WaveformSynchronizer.EstimateFilenameOffset(_primaryPath.Text, _secondaryPath.Text);
        SetOffset(rough * 1000);
        _syncResult.ForeColor = Muted;
        if (!HasWaveformSyncTracks)
        {
            _syncResult.ForeColor = Warning;
            _syncResult.Text = $"Silent-webcam mode: filename timing starts at {rough:+0.000;-0.000;0.000} sec. Choose a talking moment in Preview from, run visual lip sync, then verify with a preview.";
        }
        else
        {
            _syncResult.Text = Math.Abs(rough) > 0.001
                ? $"Filename timestamps suggest a {rough:+0.000;-0.000;0.000} second offset. Waveform analysis will refine it."
                : "Filename timestamps did not provide a sub-second offset. Waveform analysis will determine it.";
        }
    }

    private bool HasWaveformSyncTracks =>
        _primaryInfo?.AudioStreams.Any(track => !track.IsLikelySilent) == true &&
        _secondaryInfo?.AudioStreams.Any(track => !track.IsLikelySilent) == true;

    private bool HasVisualSyncOption =>
        _primaryInfo?.AudioStreams.Any(track => !track.IsLikelySilent) == true && _secondaryInfo is not null;

    private void UpdateSyncAvailability()
    {
        var available = HasVisualSyncOption && _operationCancellation is null;
        _analyzeButton.Enabled = available;
        _analyzeButton.Text = HasWaveformSyncTracks ? "Analyze waveform sync" : "Analyze visual lip sync";
        _applyDrift.Enabled = HasWaveformSyncTracks && _operationCancellation is null;
        _searchRange.Enabled = available;
        if (!HasWaveformSyncTracks)
        {
            if (_primarySyncTrack.Items.Cast<AudioStreamInfo>()
                    .Where(track => track.BitRate > 0)
                    .OrderBy(track => track.BitRate)
                    .FirstOrDefault() is { } likelyMicrophone)
            {
                _primarySyncTrack.SelectedItem = likelyMicrophone;
            }
            _applyDrift.Checked = false;
            _syncAnalysis = null;
        }
    }

    private async Task AnalyzeSyncAsync()
    {
        var savedScroll = _configurationHost is null ? 0 : -_configurationHost.AutoScrollPosition.Y;
        if (_primaryInfo is null || _secondaryInfo is null ||
            _primarySyncTrack.SelectedItem is not AudioStreamInfo primaryTrack)
        {
            ShowError("Load both recordings and select a suitable gameplay synchronization track. For visual lip sync, choose a track containing your speech.");
            return;
        }
        if (!File.Exists(_settings.FfmpegPath))
        {
            ShowError("FFmpeg was not found. Configure it from the single-clip tab first.");
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, HasWaveformSyncTracks ? "Starting waveform analysis..." : "Starting visual lip-sync analysis...");
        try
        {
            var status = new Progress<string>(text => _status.Text = text);
            if (HasWaveformSyncTracks && _secondarySyncTrack.SelectedItem is AudioStreamInfo secondaryTrack)
            {
                _syncAnalysis = await WaveformSynchronizer.AnalyzeAsync(
                    _settings.FfmpegPath,
                    _primaryPath.Text,
                    _primaryInfo,
                    primaryTrack.Index,
                    _secondaryPath.Text,
                    _secondaryInfo,
                    secondaryTrack.Index,
                    (int)_searchRange.Value,
                    status,
                    _operationCancellation.Token);

                _offsetManuallyEdited = false;
                UpdateAnalyzedOffsetForStart();
                _syncResult.ForeColor = _syncAnalysis.StartConfidence >= 0.35 ? Success : Warning;
                var confidence = _syncAnalysis.StartConfidence.ToString("P0", CultureInfo.InvariantCulture);
                _syncResult.Text = _syncAnalysis.HasDriftMeasurement
                    ? $"Matched offset {_offsetMilliseconds.Value / 1000m:+0.000;-0.000;0.000} sec ({confidence} confidence). Drift: {_syncAnalysis.DriftMillisecondsPerHour:+0.0;-0.0;0.0} ms/hour."
                    : $"Matched offset {_offsetMilliseconds.Value / 1000m:+0.000;-0.000;0.000} sec ({confidence} confidence). Recording overlap was too short to require a drift measurement.";
            }
            else
            {
                if (!TryParseTime(_previewAtTime.Text, out var previewAt))
                {
                    throw new InvalidOperationException("Enter a valid Preview from timestamp where you are clearly talking.");
                }
                var visual = await SilentWebcamSynchronizer.AnalyzeAsync(
                    _settings.FfmpegPath,
                    _primaryPath.Text,
                    _primaryInfo,
                    primaryTrack.Index,
                    _secondaryPath.Text,
                    _secondaryInfo,
                    previewAt,
                    (int)_searchRange.Value,
                    status,
                    _operationCancellation.Token);
                _syncAnalysis = null;
                _offsetManuallyEdited = false;
                SetOffset(visual.OffsetSeconds * 1000);
                _syncResult.ForeColor = Warning;
                _syncResult.Text = $"Visual rough match: {visual.OffsetSeconds:+0.000;-0.000;0.000} sec using {visual.Metric} ({visual.Confidence:P0} correlation). Render a preview and nudge for final lip sync.";
            }
            _status.ForeColor = Success;
            _status.Text = "Synchronization ready - render a preview to verify";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Synchronization cancelled";
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
            BeginInvoke(() =>
            {
                if (_configurationHost is not null)
                    _configurationHost.AutoScrollPosition = new Point(0, savedScroll);
            });
        }
    }

    private async Task RenderAsync(bool preview)
    {
        if (!TryBuildRequest(preview, out var request, out var error))
        {
            ShowError(error);
            return;
        }

        if (!preview)
        {
            using var confirmation = new CompositionConfirmDialog(request!);
            if (confirmation.ShowDialog(FindForm()) != DialogResult.OK)
            {
                _status.Text = "Composition cancelled before rendering";
                _status.ForeColor = Muted;
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request!.OutputPath)!);
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, preview ? "Rendering synchronized preview..." : "Creating synchronized composition...");
        _progress.Value = 0;
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                _progress.Value = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
                _status.Text = preview
                    ? $"Rendering preview... {_progress.Value}%"
                    : $"Creating composition... {_progress.Value}%";
            });
            var result = await CompositionRunner.RunAsync(
                request,
                progress,
                text => BeginInvoke(() => _status.Text = text),
                _operationCancellation.Token);

            if (!result.Success)
            {
                var logPath = Path.ChangeExtension(request.OutputPath, ".ffmpeg.log.txt");
                await File.WriteAllTextAsync(logPath, result.Log);
                ShowError($"FFmpeg could not complete the composition. A diagnostic log was saved beside the output.\n\n{LastMeaningfulLine(result.Log)}");
                return;
            }

            _progress.Value = 100;
            _status.ForeColor = Success;
            _status.Text = preview ? "Preview ready" : $"Composition completed in {FormatElapsed(result.Elapsed)}";
            if (preview)
            {
                Process.Start(new ProcessStartInfo { FileName = request.OutputPath, UseShellExecute = true });
            }
            else
            {
                System.Media.SystemSounds.Asterisk.Play();
                var rendered = await MediaProbe.ReadAsync(_settings.FfprobePath, request.OutputPath, CancellationToken.None);
                using var summary = new PostRenderSummaryDialog(request.OutputPath, rendered, request.Profile.Name, request.Profile.Width, request.Profile.Height, 60);
                summary.ShowDialog(FindForm());
            }
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

    private bool TryBuildRequest(bool preview, out CompositionRequest? request, out string error)
    {
        request = null;
        error = string.Empty;
        if (_primaryInfo is null || !File.Exists(_primaryPath.Text))
        {
            error = "Choose a valid gameplay recording.";
            return false;
        }
        if (_secondaryInfo is null || !File.Exists(_secondaryPath.Text))
        {
            error = "Choose a valid webcam or secondary recording.";
            return false;
        }
        if (!File.Exists(_settings.FfmpegPath))
        {
            error = "FFmpeg was not found. Configure it from the single-clip tab first.";
            return false;
        }
        if (!TryParseTime(_startTime.Text, out var start) || !TryParseTime(_endTime.Text, out var end) || end <= start)
        {
            error = "Enter a valid gameplay start and end time.";
            return false;
        }
        if (end > _primaryInfo.Duration + TimeSpan.FromMilliseconds(100))
        {
            error = "The gameplay end time is beyond the recording duration.";
            return false;
        }
        if (_layout.SelectedItem is not ChoiceItem layout ||
            _quality.SelectedItem is not ComposeOutputProfile profile ||
            _backgroundRemoval.SelectedItem is not ChoiceItem backgroundRemoval)
        {
            error = "Choose a composition layout and output quality.";
            return false;
        }
        var portrait = (_orientation.SelectedItem as ChoiceItem)?.Value == "portrait";
        if (portrait != profile.IsVertical)
        {
            error = portrait ? "Choose a vertical 1080x1920 output profile for the portrait canvas." : "Choose a landscape output profile for the 16:9 canvas.";
            return false;
        }
        if ((int)_cropLeft.Value + (int)_cropRight.Value >= 95 ||
            (int)_cropTop.Value + (int)_cropBottom.Value >= 95)
        {
            error = "Webcam crop values remove the entire image. Reduce the combined left/right or top/bottom crop.";
            return false;
        }
        if ((int)_gameplayCropLeft.Value + (int)_gameplayCropRight.Value >= 95 ||
            (int)_gameplayCropTop.Value + (int)_gameplayCropBottom.Value >= 95)
        {
            error = "Gameplay crop values remove the entire image. Reduce the combined left/right or top/bottom crop.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(_watermarkPath.Text) && !File.Exists(_watermarkPath.Text))
        {
            error = "The selected watermark image no longer exists.";
            return false;
        }

        var auxiliaryAudio = _audioRows
            .Select(row => row.BuildOverlay())
            .Where(item => item is not null)
            .Cast<AudioOverlay>()
            .ToArray();
        if (auxiliaryAudio.FirstOrDefault(item => !File.Exists(item.Path)) is { } missingAudio)
        {
            error = $"An added audio file no longer exists: {missingAudio.Path}";
            return false;
        }

        var renderStart = start;
        var duration = end - start;
        if (preview)
        {
            if (!TryParseTime(_previewAtTime.Text, out renderStart) || renderStart >= _primaryInfo.Duration)
            {
                error = "Enter a valid Preview from timestamp within the gameplay recording.";
                return false;
            }
            duration = TimeSpan.FromSeconds(Math.Min(10, _primaryInfo.Duration.TotalSeconds - renderStart.TotalSeconds));
        }

        var offsetSeconds = (double)_offsetMilliseconds.Value / 1000d;
        var ptsScale = _applyDrift.Checked && _syncAnalysis is not null
            ? _syncAnalysis.SecondaryPtsScale
            : 1d;
        var secondaryStart = renderStart.TotalSeconds - offsetSeconds;
        if (secondaryStart < 0)
        {
            error = "This gameplay interval begins before the secondary recording. Move the start later or correct the offset.";
            return false;
        }
        var requiredSecondaryDuration = duration.TotalSeconds / ptsScale;
        if (secondaryStart + requiredSecondaryDuration > _secondaryInfo.Duration.TotalSeconds + 0.1)
        {
            error = "The selected interval extends beyond the available secondary recording.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            error = "Choose an output folder.";
            return false;
        }

        var outputPath = preview
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StreamClipStudio",
                "Previews",
                $"sync-preview-{DateTime.Now:yyyyMMdd-HHmmss}.mp4")
            : MakeUniquePath(Path.Combine(
                _outputFolder.Text,
                EnsureMp4Extension(SanitizeFileName(_outputName.Text.Trim()))));

        var primaryAudio = _primaryMixTracks.SelectedMixes.ToArray();
        var secondaryAudio = _secondaryMixTracks.SelectedMixes.ToArray();
        request = new CompositionRequest(
            _settings.FfmpegPath,
            _primaryPath.Text,
            _secondaryPath.Text,
            outputPath,
            renderStart,
            duration,
            offsetSeconds,
            ptsScale,
            (_orientation.SelectedItem as ChoiceItem)?.Value ?? "landscape",
            (_portraitLayout.SelectedItem as ChoiceItem)?.Value ?? "focus",
            (int)_gameplayFocusX.Value,
            layout.Value,
            _position.SelectedItem?.ToString() ?? "Bottom center",
            (int)_webcamWidth.Value,
            (int)_webcamX.Value,
            (int)_webcamY.Value,
            (int)_cropLeft.Value,
            (int)_cropTop.Value,
            (int)_cropRight.Value,
            (int)_cropBottom.Value,
            (int)_gameplayCropLeft.Value,
            (int)_gameplayCropTop.Value,
            (int)_gameplayCropRight.Value,
            (int)_gameplayCropBottom.Value,
            backgroundRemoval.Value,
            (double)_keySensitivity.Value / 100d,
            _flipWebcam.Checked,
            _watermarkPath.Text,
            (int)_watermarkX.Value,
            (int)_watermarkY.Value,
            (int)_watermarkWidth.Value,
            (int)_watermarkOpacity.Value,
            (_watermarkBackgroundRemoval.SelectedItem as ChoiceItem)?.Value ?? "none",
            (double)_watermarkKeySensitivity.Value / 100d,
            primaryAudio,
            secondaryAudio,
            auxiliaryAudio,
            profile,
            preview);

        SavePreferences(layout.Value, profile.Id);
        return true;
    }

    private void SavePreferences(string layout, string quality)
    {
        _settings.ComposeOutputFolder = _outputFolder.Text;
        _settings.ComposeLayout = layout;
        _settings.ComposeQuality = quality;
        _settings.ComposeOrientation = (_orientation.SelectedItem as ChoiceItem)?.Value ?? "landscape";
        _settings.PortraitLayout = (_portraitLayout.SelectedItem as ChoiceItem)?.Value ?? "focus";
        _settings.GameplayFocusXPercent = (int)_gameplayFocusX.Value;
        _settings.SafeAreaPlatform = (_safeAreaPlatform.SelectedItem as ChoiceItem)?.Value ?? "youtube";
        _settings.WebcamPosition = _position.SelectedItem?.ToString() ?? "Bottom center";
        _settings.WebcamWidthPercent = (int)_webcamWidth.Value;
        _settings.WebcamXPercent = (int)_webcamX.Value;
        _settings.WebcamYPercent = (int)_webcamY.Value;
        _settings.WebcamUseCustomPosition = _position.SelectedItem?.ToString() == "Custom / drag on canvas";
        _settings.WebcamCropLeftPercent = (int)_cropLeft.Value;
        _settings.WebcamCropTopPercent = (int)_cropTop.Value;
        _settings.WebcamCropRightPercent = (int)_cropRight.Value;
        _settings.WebcamCropBottomPercent = (int)_cropBottom.Value;
        _settings.GameplayCropLeftPercent = (int)_gameplayCropLeft.Value;
        _settings.GameplayCropTopPercent = (int)_gameplayCropTop.Value;
        _settings.GameplayCropRightPercent = (int)_gameplayCropRight.Value;
        _settings.GameplayCropBottomPercent = (int)_gameplayCropBottom.Value;
        _settings.WebcamBackgroundRemoval = (_backgroundRemoval.SelectedItem as ChoiceItem)?.Value ?? "none";
        _settings.WebcamKeySensitivityPercent = (int)_keySensitivity.Value;
        _settings.FlipWebcamHorizontally = _flipWebcam.Checked;
        _settings.WatermarkPath = _watermarkPath.Text;
        _settings.WatermarkXPercent = (int)_watermarkX.Value;
        _settings.WatermarkYPercent = (int)_watermarkY.Value;
        _settings.WatermarkWidthPercent = (int)_watermarkWidth.Value;
        _settings.WatermarkOpacityPercent = (int)_watermarkOpacity.Value;
        _settings.WatermarkBackgroundRemoval = (_watermarkBackgroundRemoval.SelectedItem as ChoiceItem)?.Value ?? "none";
        _settings.WatermarkKeySensitivityPercent = (int)_watermarkKeySensitivity.Value;
        _settings.ApplyGreenScreen = false;
        _settings.SyncSearchSeconds = (int)_searchRange.Value;
        _settings.ShowYouTubeSafeAreas = _showSafeAreas.Checked;
        _settings.AudioMixes = _primaryMixTracks.CaptureSettings("primary").Concat(_secondaryMixTracks.CaptureSettings("secondary")).ToList();
        _settings.Save();
    }

    private void UpdateMixRisk()
    {
        var mixes = _primaryMixTracks.SelectedMixes.Concat(_secondaryMixTracks.SelectedMixes).Where(item => !item.Muted).ToArray();
        var boosted = mixes.Where(item => item.VolumePercent > 100).ToArray();
        var theoreticalGain = mixes.Length == 0 ? double.NegativeInfinity : 20 * Math.Log10(mixes.Sum(item => item.VolumePercent / 100d));
        var risk = boosted.Length > 0 || mixes.Length >= 4;
        _mixRisk.ForeColor = risk ? Warning : Success;
        _mixRisk.Text = risk
            ? $"Potential mix-headroom risk: {mixes.Length} active tracks, theoretical coincident gain {theoreticalGain:+0.0;-0.0;0.0} dB. Actual peaks determine clipping; the final limiter remains enabled."
            : mixes.Length == 0 ? "No source audio is currently included." : mixes.Length == 1
                ? "Single track at or below unity gain; the final limiter remains enabled."
                : $"{mixes.Length} tracks are at or below 0 dB gain. Normal 100% levels do not by themselves mean clipping; overlapping peaks still matter.";
    }

    private void UpdateAnalyzedOffsetForStart()
    {
        if (_syncAnalysis is null || _offsetManuallyEdited || !TryParseTime(_startTime.Text, out var start))
        {
            return;
        }
        SetOffset(_syncAnalysis.OffsetAt(start) * 1000);
    }

    private void SetOffset(double milliseconds)
    {
        _settingOffset = true;
        try
        {
            _offsetMilliseconds.Value = Math.Clamp(
                (decimal)milliseconds,
                _offsetMilliseconds.Minimum,
                _offsetMilliseconds.Maximum);
        }
        finally
        {
            _settingOffset = false;
        }
    }

    private Button CreateNudgeButton(string text, int? milliseconds = null, int frameDirection = 0)
    {
        var button = CreateButton(text);
        button.Click += (_, _) =>
        {
            var delta = milliseconds ?? (int)Math.Round(
                frameDirection * 1000d / Math.Max(1, _primaryInfo?.FramesPerSecond ?? 60));
            SetOffset((double)_offsetMilliseconds.Value + delta);
            _offsetManuallyEdited = true;
            _syncResult.ForeColor = Warning;
            _syncResult.Text = $"Manual offset: {_offsetMilliseconds.Value:0.0} ms. Render a preview to verify sync.";
        };
        return button;
    }

    private void ResetToFilenameOffset()
    {
        if (_primaryInfo is null || _secondaryInfo is null)
        {
            ShowError("Load both recordings before resetting the offset.");
            return;
        }

        _syncAnalysis = null;
        _offsetManuallyEdited = false;
        UpdateRoughOffsetMessage();
    }

    private void UpdateLayoutState()
    {
        var portrait = (_orientation.SelectedItem as ChoiceItem)?.Value == "portrait";
        var overlay = (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _layout.Enabled = !portrait;
        _portraitLayout.Enabled = portrait;
        _gameplayFocusX.Enabled = portrait && (_portraitLayout.SelectedItem as ChoiceItem)?.Value == "focus";
        _safeAreaPlatform.Enabled = portrait;
        _position.Enabled = overlay;
        _webcamWidth.Enabled = overlay;
        _webcamX.Enabled = overlay;
        _webcamY.Enabled = overlay;
        _positionCanvas.Enabled = overlay;
        _cropLeft.Enabled = overlay;
        _cropTop.Enabled = overlay;
        _cropRight.Enabled = overlay;
        _cropBottom.Enabled = overlay;
        _gameplayCropLeft.Enabled = _operationCancellation is null;
        _gameplayCropTop.Enabled = _operationCancellation is null;
        _gameplayCropRight.Enabled = _operationCancellation is null;
        _gameplayCropBottom.Enabled = _operationCancellation is null;
        _backgroundRemoval.Enabled = overlay;
        _keySensitivity.Enabled = overlay && (_backgroundRemoval.SelectedItem as ChoiceItem)?.Value != "none";
        _flipWebcam.Enabled = !(_operationCancellation is not null);
    }

    private void UpdateSuggestedName()
    {
        if (string.IsNullOrWhiteSpace(_primaryPath.Text) ||
            _quality.SelectedItem is not ComposeOutputProfile profile ||
            !TryParseTime(_startTime.Text, out var start) ||
            !TryParseTime(_endTime.Text, out var end))
        {
            return;
        }
        var sourceName = Path.GetFileNameWithoutExtension(_primaryPath.Text);
        _outputName.Text = $"{sourceName} - {TimeToken(start)}-{TimeToken(end)} - {profile.ShortName}.mp4";
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where synchronized compositions are saved",
            InitialDirectory = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : string.Empty,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _outputFolder.Text = dialog.SelectedPath;
        }
    }

    private void OpenOutputFolder()
    {
        if (!Directory.Exists(_outputFolder.Text))
        {
            ShowError("The output folder does not exist yet.");
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = _outputFolder.Text, UseShellExecute = true });
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _analyzeButton.Enabled = !busy && HasVisualSyncOption;
        _previewButton.Enabled = !busy;
        _createButton.Enabled = !busy;
        _cancelButton.Enabled = busy && _operationCancellation is not null;
        _primarySyncTrack.Enabled = !busy;
        _secondarySyncTrack.Enabled = !busy;
        _primaryMixTracks.Enabled = !busy;
        _secondaryMixTracks.Enabled = !busy;
        _startTime.Enabled = !busy;
        _endTime.Enabled = !busy;
        _offsetMilliseconds.Enabled = !busy;
        _previewAtTime.Enabled = !busy;
        _layout.Enabled = !busy;
        _quality.Enabled = !busy;
        _applyDrift.Enabled = !busy && HasWaveformSyncTracks;
        _searchRange.Enabled = !busy && HasVisualSyncOption;
        _backgroundRemoval.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _keySensitivity.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside" &&
                                  (_backgroundRemoval.SelectedItem as ChoiceItem)?.Value != "none";
        _flipWebcam.Enabled = !busy;
        _position.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _webcamWidth.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _webcamX.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _webcamY.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _positionCanvas.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _cropLeft.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _cropTop.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _cropRight.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _cropBottom.Enabled = !busy && (_layout.SelectedItem as ChoiceItem)?.Value != "sidebyside";
        _gameplayCropLeft.Enabled = !busy;
        _gameplayCropTop.Enabled = !busy;
        _gameplayCropRight.Enabled = !busy;
        _gameplayCropBottom.Enabled = !busy;
        _watermarkPath.Enabled = !busy;
        _watermarkX.Enabled = !busy;
        _watermarkY.Enabled = !busy;
        _watermarkWidth.Enabled = !busy;
        _watermarkOpacity.Enabled = !busy;
        _watermarkBackgroundRemoval.Enabled = !busy;
        _watermarkKeySensitivity.Enabled = !busy;
        foreach (var row in _audioRows)
        {
            row.Enabled = !busy;
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            _status.Text = message;
            _status.ForeColor = Muted;
        }
    }

    private void ShowError(string message)
    {
        _status.Text = "Needs attention";
        _status.ForeColor = Color.FromArgb(236, 112, 112);
        MessageBox.Show(FindForm(), message, "Stream Clip Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void SelectByValue(ComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            var itemValue = combo.Items[index] switch
            {
                ChoiceItem choice => choice.Value,
                ComposeOutputProfile profile => profile.Id,
                _ => string.Empty
            };
            if (string.Equals(itemValue, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }

    private static bool TryParseTime(string text, out TimeSpan value)
    {
        text = text.Trim();
        var parts = text.Split(':');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            minutes >= 0 && seconds is >= 0 and < 60)
        {
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

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.fff").TrimEnd('0').TrimEnd('.')
        : value.ToString(@"mm\:ss\.fff").TrimEnd('0').TrimEnd('.');

    private static string TimeToken(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}h{value.Minutes:D2}m{value.Seconds:D2}s";

    private static string EnsureMp4Extension(string fileName) =>
        fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".mp4";

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

    private static TableLayoutPanel NewGrid(int columns) => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = columns,
        Padding = new Padding(14, 4, 14, 12)
    };

    private static Control CreatePanel(string title, Control content, bool initiallyCollapsed = false)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = PanelColor,
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1
        };
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Margin = new Padding(0),
            Cursor = Cursors.Hand
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(14, 10),
            Cursor = Cursors.Hand
        };
        var toggle = new Label
        {
            Text = initiallyCollapsed ? "+" : "−",
            Font = new Font("Segoe UI Semibold", 13F),
            ForeColor = Muted,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Math.Max(20, panel.Width - 34), 7),
            Cursor = Cursors.Hand
        };
        header.SizeChanged += (_, _) => toggle.Left = header.ClientSize.Width - 30;
        void TogglePanel(object? _, EventArgs __)
        {
            content.Visible = !content.Visible;
            toggle.Text = content.Visible ? "−" : "+";
            panel.PerformLayout();
        }
        header.Click += TogglePanel;
        titleLabel.Click += TogglePanel;
        toggle.Click += TogglePanel;
        header.Controls.Add(titleLabel);
        header.Controls.Add(toggle);
        panel.Controls.Add(header);
        content.Visible = !initiallyCollapsed;
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

    private static ComboBox CreateComboBox() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = FieldColor,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 5, 8, 5)
    };

    private static CheckedListBox CreateCheckedList() => new()
    {
        BackColor = FieldColor,
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        CheckOnClick = true,
        Height = 78,
        Dock = DockStyle.Fill
    };

    private static NumericUpDown CreateNumeric(decimal minimum, decimal maximum, decimal value, int decimalPlaces) => new WheelSafeNumericUpDown
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        DecimalPlaces = decimalPlaces,
        Increment = decimalPlaces > 0 ? 10 : 1,
        BackColor = FieldColor,
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        ThousandsSeparator = true,
        Margin = new Padding(3, 5, 8, 5)
    };

    private static CheckBox CreateCheckBox(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        AutoSize = true,
        ForeColor = Color.White,
        Margin = new Padding(3, 8, 3, 6)
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
        MaximumSize = new Size(880, 0),
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

    private sealed record ChoiceItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
