using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StreamClipStudio;

internal sealed record ClipRequest(
    string FfmpegPath,
    string InputPath,
    string OutputPath,
    TimeSpan Start,
    TimeSpan Duration,
    ClipPreset Preset,
    IReadOnlyList<int> AudioStreamIndices);

internal sealed record ClipRunResult(bool Success, string Log, TimeSpan Elapsed);

internal static class FfmpegRunner
{
    public static async Task<ClipRunResult> RunAsync(
        ClipRequest request,
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
                // Process may have completed while cancellation was requested.
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

        status("Verifying the completed file…");
        if (!File.Exists(request.OutputPath) || new FileInfo(request.OutputPath).Length == 0)
        {
            return new ClipRunResult(false, log + Environment.NewLine + "The output file was not created.", stopwatch.Elapsed);
        }

        return new ClipRunResult(true, log.ToString(), stopwatch.Elapsed);
    }

    private static IReadOnlyList<string> BuildArguments(ClipRequest request)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-y"
        };

        if (request.Preset.IsLosslessTrim)
        {
            args.AddRange(new[] { "-ss", FormatTime(request.Start), "-i", request.InputPath });
        }
        else
        {
            // Seek close to the requested point first, then decode a short lead-in for an exact cut.
            // This avoids decoding hours of a long OBS session while keeping synchronization at zero.
            var coarseSeek = request.Start > TimeSpan.FromSeconds(5)
                ? request.Start - TimeSpan.FromSeconds(5)
                : TimeSpan.Zero;
            var preciseSeek = request.Start - coarseSeek;
            args.AddRange(new[]
            {
                "-ss", FormatTime(coarseSeek),
                "-i", request.InputPath,
                "-ss", FormatTime(preciseSeek)
            });
        }

        args.AddRange(new[]
        {
            "-t", FormatTime(request.Duration),
            "-map", "0:v:0"
        });

        foreach (var audioIndex in request.AudioStreamIndices)
        {
            args.Add("-map");
            args.Add($"0:{audioIndex}");
        }

        args.AddRange(new[] { "-map_metadata", "0" });

        switch (request.Preset.Id)
        {
            case "lossless":
                args.AddRange(new[]
                {
                    "-c", "copy",
                    "-avoid_negative_ts", "make_zero"
                });
                break;

            case "edit":
                args.AddRange(new[]
                {
                    "-c:v", "h264_nvenc",
                    "-preset", "p7",
                    "-tune", "hq",
                    "-rc", "constqp",
                    "-qp", "18",
                    "-profile:v", "high",
                    "-spatial_aq", "1",
                    "-temporal_aq", "1",
                    "-rc-lookahead", "32",
                    "-c:a", "aac",
                    "-b:a", "320k"
                });
                break;

            case "compact":
                args.AddRange(new[]
                {
                    "-c:v", "h264_nvenc",
                    "-preset", "p6",
                    "-tune", "hq",
                    "-rc", "constqp",
                    "-qp", "23",
                    "-profile:v", "high",
                    "-spatial_aq", "1",
                    "-temporal_aq", "1",
                    "-c:a", "aac",
                    "-b:a", "192k",
                    "-movflags", "+faststart"
                });
                break;

            case "youtube4k":
                args.AddRange(new[]
                {
                    "-vf", "scale=3840:2160:flags=lanczos",
                    "-c:v", "h264_nvenc",
                    "-preset", "p7",
                    "-tune", "hq",
                    "-rc", "vbr",
                    "-cq", "16",
                    "-b:v", "55M",
                    "-maxrate", "70M",
                    "-bufsize", "110M",
                    "-profile:v", "high",
                    "-spatial_aq", "1",
                    "-temporal_aq", "1",
                    "-rc-lookahead", "32",
                    "-c:a", "aac",
                    "-b:a", "320k",
                    "-movflags", "+faststart"
                });
                break;

            default:
                throw new InvalidOperationException($"Unknown preset: {request.Preset.Id}");
        }

        args.AddRange(new[]
        {
            "-progress", "pipe:1",
            "-nostats",
            request.OutputPath
        });

        return args;
    }

    private static string FormatTime(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
