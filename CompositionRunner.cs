using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StreamClipStudio;

internal sealed record ComposeOutputProfile(
    string Id,
    string Name,
    string ShortName,
    int Width,
    int Height,
    int TargetBitrateMbps,
    int ConstantQp)
{
    public bool IsVertical => Height > Width;
    public override string ToString() => Name;

    public static IReadOnlyList<ComposeOutputProfile> All { get; } = new[]
    {
        new ComposeOutputProfile(
            "edit1440",
            "High-quality 1440p60 editing master - CQP 18",
            "Composed-1440p-CQP18",
            2560,
            1440,
            0,
            18),
        new ComposeOutputProfile(
            "youtube4k",
            "YouTube 4K60 master - 55 Mbps",
            "Composed-YouTube-4K60",
            3840,
            2160,
            55,
            0),
        new ComposeOutputProfile(
            "vertical-universal",
            "Universal vertical 1080x1920 60 FPS - 18 Mbps",
            "Vertical-Universal",
            1080,
            1920,
            18,
            0),
        new ComposeOutputProfile(
            "vertical-compact",
            "TikTok / Reels vertical 1080x1920 60 FPS - 12 Mbps",
            "Vertical-Social",
            1080,
            1920,
            12,
            0),
        new ComposeOutputProfile(
            "vertical-edit",
            "High-quality vertical editing master - CQP 18",
            "Vertical-CQP18",
            1080,
            1920,
            0,
            18)
    };
}

internal sealed record CompositionRequest(
    string FfmpegPath,
    string PrimaryPath,
    string SecondaryPath,
    string OutputPath,
    TimeSpan PrimaryStart,
    TimeSpan Duration,
    double SecondaryOffsetSeconds,
    double SecondaryPtsScale,
    string Orientation,
    string PortraitLayout,
    int GameplayFocusXPercent,
    string Layout,
    string WebcamPosition,
    int WebcamWidthPercent,
    int WebcamXPercent,
    int WebcamYPercent,
    int WebcamCropLeftPercent,
    int WebcamCropTopPercent,
    int WebcamCropRightPercent,
    int WebcamCropBottomPercent,
    int GameplayCropLeftPercent,
    int GameplayCropTopPercent,
    int GameplayCropRightPercent,
    int GameplayCropBottomPercent,
    string WebcamBackgroundRemoval,
    double WebcamKeySimilarity,
    bool FlipWebcamHorizontally,
    string WatermarkPath,
    int WatermarkXPercent,
    int WatermarkYPercent,
    int WatermarkWidthPercent,
    int WatermarkOpacityPercent,
    string WatermarkBackgroundRemoval,
    double WatermarkKeySimilarity,
    IReadOnlyList<AudioTrackMix> PrimaryAudioStreams,
    IReadOnlyList<AudioTrackMix> SecondaryAudioStreams,
    IReadOnlyList<AudioOverlay> AudioOverlays,
    ComposeOutputProfile Profile,
    bool IsPreview);

internal sealed record AudioOverlay(string Path, int StartMilliseconds, int VolumePercent);

internal static class CompositionRunner
{
    public static async Task<ClipRunResult> RunAsync(
        CompositionRequest request,
        IProgress<double> progress,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(request))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var log = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have ended while cancellation was requested.
            }
        });

        var errorReader = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                log.AppendLine(line);
            }
        }, cancellationToken);

        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(line[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                var fraction = request.Duration.TotalSeconds <= 0
                    ? 0
                    : microseconds / 1_000_000d / request.Duration.TotalSeconds;
                progress.Report(Math.Clamp(fraction, 0, 1));
            }
            else if (line == "progress=end")
            {
                progress.Report(1);
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        await errorReader;
        stopwatch.Stop();

        if (process.ExitCode != 0)
        {
            return new ClipRunResult(false, log.ToString(), stopwatch.Elapsed);
        }

        status("Verifying the composed file...");
        if (!File.Exists(request.OutputPath) || new FileInfo(request.OutputPath).Length == 0)
        {
            return new ClipRunResult(false, log + Environment.NewLine + "The composed output file was not created.", stopwatch.Elapsed);
        }

        return new ClipRunResult(true, log.ToString(), stopwatch.Elapsed);
    }

    internal static IReadOnlyList<string> BuildArguments(CompositionRequest request)
    {
        var secondaryStart = request.PrimaryStart.TotalSeconds - request.SecondaryOffsetSeconds;
        if (secondaryStart < 0)
        {
            throw new InvalidOperationException(
                "The selected primary clip begins before the secondary recording started. Move the clip start later or correct the synchronization offset.");
        }

        var outputDuration = request.IsPreview
            ? Math.Min(10, request.Duration.TotalSeconds)
            : request.Duration.TotalSeconds;
        var portrait = request.Orientation == "portrait" || request.Profile.IsVertical;
        var width = request.IsPreview ? (portrait ? 720 : 1280) : request.Profile.Width;
        var height = request.IsPreview ? (portrait ? 1280 : 720) : request.Profile.Height;
        var secondaryScale = request.SecondaryPtsScale is >= 0.98 and <= 1.02
            ? request.SecondaryPtsScale
            : 1d;

        var args = new List<string>
        {
            "-hide_banner", "-y",
            "-ss", FormatSeconds(request.PrimaryStart.TotalSeconds),
            "-i", request.PrimaryPath,
            "-ss", FormatSeconds(secondaryStart),
            "-i", request.SecondaryPath
        };

        if (!string.IsNullOrWhiteSpace(request.WatermarkPath))
        {
            args.AddRange(new[] { "-loop", "1", "-i", request.WatermarkPath });
        }
        foreach (var audio in request.AudioOverlays)
        {
            args.AddRange(new[] { "-i", audio.Path });
        }

        args.AddRange(new[]
        {
            "-filter_complex", BuildFilter(request, width, height, outputDuration, secondaryScale),
            "-map", "[outv]"
        });

        if (request.PrimaryAudioStreams.Count + request.SecondaryAudioStreams.Count + request.AudioOverlays.Count > 0)
        {
            args.AddRange(new[] { "-map", "[outa]" });
        }
        else
        {
            args.Add("-an");
        }

        args.AddRange(new[]
        {
            "-t", FormatSeconds(outputDuration),
            "-map_metadata", "0"
        });

        if (request.IsPreview)
        {
            args.AddRange(new[]
            {
                "-c:v", "h264_nvenc",
                "-preset", "p5",
                "-tune", "hq",
                "-rc", "constqp",
                "-qp", "25",
                "-profile:v", "high",
                "-c:a", "aac",
                "-b:a", "192k",
                "-movflags", "+faststart"
            });
        }
        else if (request.Profile.TargetBitrateMbps > 0)
        {
            var target = request.Profile.TargetBitrateMbps;
            var max = Math.Max(target + 4, (int)Math.Round(target * 1.3));
            var buffer = target * 2;
            args.AddRange(new[]
            {
                "-c:v", "h264_nvenc",
                "-preset", "p7",
                "-tune", "hq",
                "-rc", "vbr",
                "-cq", "16",
                "-b:v", $"{target}M",
                "-maxrate", $"{max}M",
                "-bufsize", $"{buffer}M",
                "-profile:v", "high",
                "-spatial_aq", "1",
                "-temporal_aq", "1",
                "-rc-lookahead", "32",
                "-c:a", "aac",
                "-b:a", "320k",
                "-movflags", "+faststart"
            });
        }
        else
        {
            args.AddRange(new[]
            {
                "-c:v", "h264_nvenc",
                "-preset", "p7",
                "-tune", "hq",
                "-rc", "constqp",
                "-qp", request.Profile.ConstantQp.ToString(CultureInfo.InvariantCulture),
                "-profile:v", "high",
                "-spatial_aq", "1",
                "-temporal_aq", "1",
                "-rc-lookahead", "32",
                "-c:a", "aac",
                "-b:a", "320k"
            });
        }

        args.AddRange(new[]
        {
            "-progress", "pipe:1",
            "-nostats",
            request.OutputPath
        });
        return args;
    }

    private static string BuildFilter(
        CompositionRequest request,
        int width,
        int height,
        double duration,
        double secondaryPtsScale)
    {
        var filter = new StringBuilder();
        var scaleText = secondaryPtsScale.ToString("0.########", CultureInfo.InvariantCulture);

        string composedLabel;
        if (request.Orientation == "portrait")
        {
            filter.Append("[0:v]");
            AppendGameplayCrop(filter, request);
            if (request.PortraitLayout == "focus")
            {
                var focus = Math.Clamp(request.GameplayFocusXPercent, 0, 100) / 100d;
                filter.Append($"setpts=PTS-STARTPTS,crop=ih*9/16:ih:(iw-ow)*{focus.ToString("0.###", CultureInfo.InvariantCulture)}:0,scale={width}:{height}:flags=lanczos,setsar=1[base];");
            }
            else if (request.PortraitLayout == "stack")
            {
                filter.Append($"setpts=PTS-STARTPTS,scale={width}:-2:flags=lanczos[game];color=c=0x08101f:s={width}x{height}:d={FormatSeconds(duration)}[portraitbg];[portraitbg][game]overlay=x=0:y={height / 18}:shortest=1[base];");
            }
            else
            {
                filter.Append($"setpts=PTS-STARTPTS,split=2[bgsource][fgsource];[bgsource]scale={width}:{height}:force_original_aspect_ratio=increase:flags=lanczos,crop={width}:{height},boxblur=24:2[blurred];[fgsource]scale={width}:-2:flags=lanczos[foreground];[blurred][foreground]overlay=x=(W-w)/2:y=(H-h)/2:shortest=1[base];");
            }

            var webcamWidth = Math.Max(120, width * request.WebcamWidthPercent / 100);
            var x = (width * request.WebcamXPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            var y = (height * request.WebcamYPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            filter.Append("[1:v]");
            var cropWidth = 100 - request.WebcamCropLeftPercent - request.WebcamCropRightPercent;
            var cropHeight = 100 - request.WebcamCropTopPercent - request.WebcamCropBottomPercent;
            if (cropWidth < 100 || cropHeight < 100)
                filter.Append($"crop=iw*{cropWidth}/100:ih*{cropHeight}/100:iw*{request.WebcamCropLeftPercent}/100:ih*{request.WebcamCropTopPercent}/100,");
            filter.Append($"setpts=(PTS-STARTPTS)*{scaleText}");
            if (request.FlipWebcamHorizontally) filter.Append(",hflip");
            filter.Append($",scale={webcamWidth}:-2:flags=lanczos,format=rgba");
            var keyColor = request.WebcamBackgroundRemoval switch { "black" => "0x000000", "green" => "0x00FF00", _ => string.Empty };
            if (!string.IsNullOrEmpty(keyColor))
            {
                var similarity = Math.Clamp(request.WebcamKeySimilarity, 0.01, 1).ToString("0.00", CultureInfo.InvariantCulture);
                filter.Append($",colorkey={keyColor}:{similarity}:0.08");
            }
            filter.Append($"[camera];[base][camera]overlay=x={x}:y={y}:shortest=1:format=auto[composed]");
            composedLabel = "composed";
        }
        else if (request.Layout == "sidebyside")
        {
            var halfWidth = width / 2;
            filter.Append("[0:v]");
            AppendGameplayCrop(filter, request);
            filter.Append($"setpts=PTS-STARTPTS,scale={halfWidth}:{height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={halfWidth}:{height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1[left];");
            filter.Append($"[1:v]setpts=(PTS-STARTPTS)*{scaleText}");
            if (request.FlipWebcamHorizontally)
            {
                filter.Append(",hflip");
            }
            filter.Append($",scale={halfWidth}:{height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={halfWidth}:{height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1[right];");
            filter.Append("[left][right]hstack=inputs=2[composed]");
            composedLabel = "composed";
        }
        else
        {
            var webcamWidth = Math.Max(160, width * request.WebcamWidthPercent / 100);
            var x = (width * request.WebcamXPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            var y = (height * request.WebcamYPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            filter.Append("[0:v]");
            AppendGameplayCrop(filter, request);
            filter.Append($"setpts=PTS-STARTPTS,scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1[base];");
            filter.Append("[1:v]");
            var cropWidth = 100 - request.WebcamCropLeftPercent - request.WebcamCropRightPercent;
            var cropHeight = 100 - request.WebcamCropTopPercent - request.WebcamCropBottomPercent;
            if (cropWidth < 100 || cropHeight < 100)
            {
                filter.Append($"crop=iw*{cropWidth}/100:ih*{cropHeight}/100:iw*{request.WebcamCropLeftPercent}/100:ih*{request.WebcamCropTopPercent}/100,");
            }
            filter.Append($"setpts=(PTS-STARTPTS)*{scaleText}");
            if (request.FlipWebcamHorizontally)
            {
                filter.Append(",hflip");
            }
            filter.Append($",scale={webcamWidth}:-2:flags=lanczos,format=rgba");
            var keyColor = request.WebcamBackgroundRemoval switch
            {
                "black" => "0x000000",
                "green" => "0x00FF00",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(keyColor))
            {
                var similarity = Math.Clamp(request.WebcamKeySimilarity, 0.01, 1)
                    .ToString("0.00", CultureInfo.InvariantCulture);
                filter.Append($",colorkey={keyColor}:{similarity}:0.08");
            }
            filter.Append("[camera];");
            filter.Append($"[base][camera]overlay=x={x}:y={y}:shortest=1:format=auto[composed]");
            composedLabel = "composed";
        }

        var nextInputIndex = 2;
        if (!string.IsNullOrWhiteSpace(request.WatermarkPath))
        {
            var watermarkWidth = Math.Max(64, width * request.WatermarkWidthPercent / 100);
            var watermarkX = (width * request.WatermarkXPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            var watermarkY = (height * request.WatermarkYPercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            var opacity = (request.WatermarkOpacityPercent / 100d).ToString("0.00", CultureInfo.InvariantCulture);
            filter.Append($";[{nextInputIndex}:v]scale={watermarkWidth}:-2:flags=lanczos,format=rgba");
            var watermarkKeyColor = request.WatermarkBackgroundRemoval switch
            {
                "black" => "0x000000",
                "green" => "0x00FF00",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(watermarkKeyColor))
            {
                var similarity = Math.Clamp(request.WatermarkKeySimilarity, 0.01, 1)
                    .ToString("0.00", CultureInfo.InvariantCulture);
                filter.Append($",colorkey={watermarkKeyColor}:{similarity}:0.08");
            }
            filter.Append($",colorchannelmixer=aa={opacity}[watermark]");
            filter.Append($";[{composedLabel}][watermark]overlay=x={watermarkX}:y={watermarkY}:shortest=1:format=auto[videoDone]");
            nextInputIndex++;
        }
        else
        {
            filter.Append($";[{composedLabel}]null[videoDone]");
        }
        filter.Append(";[videoDone]scale=in_range=auto:out_range=tv,format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709[outv]");

        var audioInputs = new List<string>();
        var audioNumber = 0;
        foreach (var mix in request.PrimaryAudioStreams)
        {
            var streamIndex = mix.StreamIndex;
            var label = $"audio{audioNumber++}";
            var volume = (mix.Muted ? 0d : mix.VolumePercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            filter.Append($";[0:{streamIndex}]atrim=duration={FormatSeconds(duration)},asetpts=PTS-STARTPTS,volume={volume}[{label}]");
            audioInputs.Add($"[{label}]");
        }

        var tempo = 1d / secondaryPtsScale;
        var tempoText = tempo.ToString("0.########", CultureInfo.InvariantCulture);
        foreach (var mix in request.SecondaryAudioStreams)
        {
            var streamIndex = mix.StreamIndex;
            var label = $"audio{audioNumber++}";
            var volume = (mix.Muted ? 0d : mix.VolumePercent / 100d).ToString("0.###", CultureInfo.InvariantCulture);
            filter.Append($";[1:{streamIndex}]atrim=duration={FormatSeconds(duration / secondaryPtsScale)},asetpts=PTS-STARTPTS,atempo={tempoText},volume={volume}[{label}]");
            audioInputs.Add($"[{label}]");
        }

        foreach (var audio in request.AudioOverlays)
        {
            var label = $"audio{audioNumber++}";
            filter.Append($";[{nextInputIndex++}:a:0]aresample=48000");
            if (audio.StartMilliseconds < 0)
            {
                filter.Append($",atrim=start={FormatSeconds(-audio.StartMilliseconds / 1000d)},asetpts=PTS-STARTPTS");
            }
            else
            {
                filter.Append($",asetpts=PTS-STARTPTS,adelay={audio.StartMilliseconds}:all=1");
            }
            var volume = (audio.VolumePercent / 100d).ToString("0.00", CultureInfo.InvariantCulture);
            filter.Append($",atrim=duration={FormatSeconds(duration)},volume={volume}[{label}]");
            audioInputs.Add($"[{label}]");
        }

        if (audioInputs.Count == 1)
        {
            filter.Append($";{audioInputs[0]}alimiter=limit=0.85[outa]");
        }
        else if (audioInputs.Count > 1)
        {
            filter.Append($";{string.Concat(audioInputs)}amix=inputs={audioInputs.Count}:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.85[outa]");
        }

        return filter.ToString();
    }

    private static void AppendGameplayCrop(StringBuilder filter, CompositionRequest request)
    {
        var cropWidth = 100 - request.GameplayCropLeftPercent - request.GameplayCropRightPercent;
        var cropHeight = 100 - request.GameplayCropTopPercent - request.GameplayCropBottomPercent;
        if (cropWidth < 100 || cropHeight < 100)
        {
            filter.Append($"crop=iw*{cropWidth}/100:ih*{cropHeight}/100:iw*{request.GameplayCropLeftPercent}/100:ih*{request.GameplayCropTopPercent}/100,");
        }
    }

    private static string FormatSeconds(double seconds) =>
        Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture);
}
