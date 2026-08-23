using StreamClipStudio;

if (args.Length == 1 && args[0] == "ui")
{
    using var form = new MainForm();
    form.CreateControl();
    Console.WriteLine($"PASS: full application UI constructed at {form.Width}x{form.Height}.");
    return 0;
}

if (args.Length == 1 && args[0] == "ui-layout")
{
    using var form = new MainForm { Size = new Size(1024, 760) };
    form.CreateControl();
    form.PerformLayout();
    foreach (var name in new[] { "ClipConfigurationHost", "ClipActionBar" })
    {
        if (form.Controls.Find(name, true).FirstOrDefault() is not Control)
        {
            Console.Error.WriteLine($"Compact layout is missing visible control: {name}");
            return 10;
        }
    }
    var clipHost = (FlowLayoutPanel)form.Controls.Find("ClipConfigurationHost", true).Single();
    if (clipHost.HorizontalScroll.Visible)
    {
        Console.Error.WriteLine("Compact clip layout unexpectedly has a horizontal scrollbar.");
        return 11;
    }
    ((Button)form.Controls.Find("ComposeNavigationButton", true).Single()).PerformClick();
    form.PerformLayout();
    var composeHost = (FlowLayoutPanel)form.Controls.Find("ComposeConfigurationHost", true).Single();
    var composeBar = form.Controls.Find("ComposeActionBar", true).Single();
    if (composeBar.Parent is not TableLayoutPanel composeShell || composeShell.GetRow(composeBar) != 2)
    {
        Console.Error.WriteLine("Compact compose layout clipped its action bar or gained a horizontal scrollbar.");
        return 12;
    }
    Console.WriteLine("PASS: compact 1024x760 clip and compose layouts keep fixed action bars without horizontal scrolling.");
    return 0;
}

if (args.Length == 2 && args[0] is "ui-snapshot" or "ui-snapshot-compose" or "ui-snapshot-compose-lower")
{
    Exception? snapshotError = null;
    var outputPath = args[1];
    var thread = new Thread(() =>
    {
        try
        {
            using var form = new MainForm();
            var timer = new System.Windows.Forms.Timer { Interval = 750 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.ClientSize);
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                form.Close();
                Application.ExitThread();
            };
            form.Shown += (_, _) =>
            {
                if (args[0].StartsWith("ui-snapshot-compose", StringComparison.Ordinal) &&
                    form.Controls.Find("ComposeNavigationButton", true).FirstOrDefault() is Button composeButton)
                {
                    composeButton.PerformClick();
                }
                if (args[0] == "ui-snapshot-compose-lower" &&
                    form.Controls.Find("ComposeConfigurationHost", true).FirstOrDefault() is FlowLayoutPanel configuration)
                {
                    configuration.AutoScrollPosition = new Point(0, 1180);
                }
                timer.Start();
            };
            Application.Run(form);
        }
        catch (Exception exception)
        {
            snapshotError = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(20)))
    {
        Console.Error.WriteLine("UI snapshot timed out.");
        return 9;
    }
    if (snapshotError is not null) throw snapshotError;
    Console.WriteLine($"PASS: UI snapshot saved to {args[1]}");
    return 0;
}

if (args.Length == 5 && args[0] == "portrait")
{
    var portraitFfmpeg = args[1]; var portraitFfprobe = args[2]; var portraitPrimary = args[3]; var portraitSecondary = args[4];
    var portraitPrimaryInfo = await MediaProbe.ReadAsync(portraitFfprobe, portraitPrimary, CancellationToken.None);
    var portraitOutput = Path.Combine(Environment.CurrentDirectory, ".test-media", "portrait-v0.6-test.mp4");
    CompositionRequest Make(string template) => new(
        portraitFfmpeg, portraitPrimary, portraitSecondary, portraitOutput, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4), 1.25, 1,
        "portrait", template, 55, "overlay", "Custom / drag on canvas", 70, 15, 68,
        0, 0, 0, 0, 0, 0, 0, 0, "black", .18, false, string.Empty, 82, 92, 15, 75,
        "none", .18, new[] { new AudioTrackMix(portraitPrimaryInfo.AudioStreams[0].Index, "Gameplay", 100, false) },
        Array.Empty<AudioTrackMix>(), Array.Empty<AudioOverlay>(), ComposeOutputProfile.All.Single(item => item.Id == "vertical-universal"), true);
    foreach (var template in new[] { "focus", "blur", "stack" })
    {
        var arguments = CompositionRunner.BuildArguments(Make(template));
        if (!arguments.Contains("-filter_complex")) return 20;
        var templateResult = await CompositionRunner.RunAsync(Make(template), new Progress<double>(_ => { }), Console.WriteLine, CancellationToken.None);
        if (!templateResult.Success) { Console.Error.WriteLine($"{template} failed:\n{templateResult.Log}"); return 21; }
    }
    var portraitInfo = await MediaProbe.ReadAsync(portraitFfprobe, portraitOutput, CancellationToken.None);
    if (portraitInfo.Width != 720 || portraitInfo.Height != 1280 || Math.Abs(portraitInfo.Duration.TotalSeconds - 4) > .2)
    { Console.Error.WriteLine($"Unexpected portrait output: {portraitInfo.Summary}"); return 22; }
    Console.WriteLine($"PASS: all portrait templates rendered and produced a 720x1280 preview at {portraitOutput}");
    return 0;
}

if (args.Length == 5 && args[4] == "visual")
{
    var visualPrimary = await MediaProbe.ReadAsync(args[1], args[2], CancellationToken.None);
    var visualSecondary = await MediaProbe.ReadAsync(args[1], args[3], CancellationToken.None);
    var microphone = visualPrimary.AudioStreams.Where(track => !track.IsLikelySilent).OrderBy(track => track.BitRate).First();
    var visual = await SilentWebcamSynchronizer.AnalyzeAsync(
        args[0], args[2], visualPrimary, microphone.Index, args[3], visualSecondary,
        TimeSpan.Zero, 8, new Progress<string>(Console.WriteLine), CancellationToken.None);
    Console.WriteLine($"Visual offset: {visual.OffsetSeconds:+0.000;-0.000;0.000}s; confidence: {visual.Confidence:P1}; metric: {visual.Metric}");
    if (Math.Abs(visual.OffsetSeconds) > 2)
    {
        Console.Error.WriteLine("Visual analyzer did not reject the misleading eight-second filename difference.");
        return 7;
    }
    var previewPath = Path.Combine(Environment.CurrentDirectory, ".test-media", "StreamClipStudio-visual-sync-test.mp4");
    var previewRequest = new CompositionRequest(
        args[0], args[2], args[3], previewPath,
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), visual.OffsetSeconds, 1, "landscape", "focus", 50,
        "overlay", "Bottom center", 32, 34, 66, 0, 0, 0, 0, 0, 0, 0, 0, "black", 0.18, true,
        string.Empty, 82, 92, 15, 75, "none", 0.18,
        new[] { new AudioTrackMix(visualPrimary.AudioStreams[0].Index, "Gameplay", 100, false) }, Array.Empty<AudioTrackMix>(), Array.Empty<AudioOverlay>(),
        ComposeOutputProfile.All.Single(profile => profile.Id == "edit1440"), true);
    var previewResult = await CompositionRunner.RunAsync(
        previewRequest, new Progress<double>(_ => { }), Console.WriteLine, CancellationToken.None);
    if (!previewResult.Success)
    {
        Console.Error.WriteLine(previewResult.Log);
        return 8;
    }
    Console.WriteLine("PASS: visual rough sync stayed within two seconds of the clip-relative match.");
    Console.WriteLine($"PASS: flipped, black-keyed preview created at {previewPath}");
    return 0;
}

if (args.Length is not (4 or 6 or 8))
{
    Console.Error.WriteLine("Usage: StreamClipStudio.Tests <ffmpeg> <ffprobe> <primary> <secondary> [expected-offset expected-PTS-scale [watermark audio] | visual]");
    return 2;
}

var ffmpeg = args[0];
var ffprobe = args[1];
var primary = args[2];
var secondary = args[3];
var expectedOffset = args.Length >= 6 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 1.25;
var expectedPtsScale = args.Length >= 6 ? double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 1d;
var watermark = args.Length == 8 ? args[6] : string.Empty;
var addedAudio = args.Length == 8 ? new[] { new AudioOverlay(args[7], 500, 35) } : Array.Empty<AudioOverlay>();
var requestedPreviewDuration = args.Length == 8 ? 12d : 4d;
var primaryInfo = await MediaProbe.ReadAsync(ffprobe, primary, CancellationToken.None);
var secondaryInfo = await MediaProbe.ReadAsync(ffprobe, secondary, CancellationToken.None);

var analysis = await WaveformSynchronizer.AnalyzeAsync(
    ffmpeg,
    primary,
    primaryInfo,
    primaryInfo.AudioStreams.Single().Index,
    secondary,
    secondaryInfo,
    secondaryInfo.AudioStreams.Single().Index,
    4,
    new Progress<string>(Console.WriteLine),
    CancellationToken.None);

Console.WriteLine(
    $"Measured offset: {analysis.StartOffsetSeconds:0.000} sec; " +
    $"end: {analysis.EndOffsetSeconds?.ToString("0.000") ?? "n/a"} sec; " +
    $"confidence: {analysis.StartConfidence:P1}; PTS scale: {analysis.SecondaryPtsScale:0.000000}; " +
    $"drift: {analysis.DriftMillisecondsPerHour:+0.0;-0.0;0.0} ms/hour");
if (Math.Abs(analysis.StartOffsetSeconds - expectedOffset) > 0.02)
{
    Console.Error.WriteLine($"Waveform synchronization did not recover the expected {expectedOffset:+0.000;-0.000;0.000} second offset.");
    return 3;
}
if (Math.Abs(analysis.SecondaryPtsScale - expectedPtsScale) > 0.00015)
{
    Console.Error.WriteLine($"Drift correction scale {analysis.SecondaryPtsScale:0.000000} was not near expected {expectedPtsScale:0.000000}.");
    return 6;
}

var output = Path.Combine(Path.GetDirectoryName(primary)!, "composition-test.mp4");
var request = new CompositionRequest(
    ffmpeg,
    primary,
    secondary,
    output,
    TimeSpan.FromSeconds(5),
    TimeSpan.FromSeconds(requestedPreviewDuration),
    analysis.StartOffsetSeconds,
    analysis.SecondaryPtsScale,
    "landscape",
    "focus",
    50,
    "overlay",
    "Bottom right",
    30,
    68,
    76,
    2,
    3,
    4,
    5,
    1,
    2,
    3,
    4,
    "black",
    0.18,
    true,
    watermark,
    82,
    92,
    15,
    75,
    "green",
    0.18,
    new[] { new AudioTrackMix(primaryInfo.AudioStreams.Single().Index, "Gameplay", 75, false) },
    Array.Empty<AudioTrackMix>(),
    addedAudio,
    ComposeOutputProfile.All.Single(profile => profile.Id == "edit1440"),
    true);

var result = await CompositionRunner.RunAsync(
    request,
    new Progress<double>(value => Console.WriteLine($"Render: {value:P0}")),
    Console.WriteLine,
    CancellationToken.None);
if (!result.Success)
{
    Console.Error.WriteLine(result.Log);
    return 4;
}

var outputInfo = await MediaProbe.ReadAsync(ffprobe, output, CancellationToken.None);
var expectedOutputDuration = Math.Min(10, requestedPreviewDuration);
if (outputInfo.Width != 1280 || outputInfo.Height != 720 ||
    Math.Abs(outputInfo.Duration.TotalSeconds - expectedOutputDuration) > 0.2)
{
    Console.Error.WriteLine($"Unexpected composition metadata: {outputInfo.Width}x{outputInfo.Height}, {outputInfo.Duration}");
    return 5;
}

Console.WriteLine($"PASS: synchronized preview created at {output}");
return 0;
