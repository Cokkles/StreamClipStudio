using System.Diagnostics;
using System.Globalization;

namespace StreamClipStudio;

internal sealed record VisualSyncAnalysis(double OffsetSeconds, double Confidence, string Metric);

internal static class SilentWebcamSynchronizer
{
    private const int FramesPerSecond = 30;
    private const int SampleRate = 12_000;
    private const int FrameWidth = 140;
    private const int FrameHeight = 120;

    public static async Task<VisualSyncAnalysis> AnalyzeAsync(
        string ffmpegPath,
        string primaryPath,
        MediaInfo primaryInfo,
        int primaryAudioStream,
        string secondaryPath,
        MediaInfo secondaryInfo,
        TimeSpan primaryAnchor,
        int searchSeconds,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var primaryStart = Math.Clamp(primaryAnchor.TotalSeconds, 0, primaryInfo.Duration.TotalSeconds);
        var duration = Math.Min(120, primaryInfo.Duration.TotalSeconds - primaryStart);
        if (duration < 8)
        {
            throw new InvalidOperationException("Choose a Preview from time with at least eight seconds remaining.");
        }

        var secondaryStart = Math.Max(0, primaryStart - searchSeconds);
        var secondaryDuration = Math.Min(
            duration + searchSeconds * 2,
            secondaryInfo.Duration.TotalSeconds - secondaryStart);
        if (secondaryDuration < 8)
        {
            throw new InvalidOperationException("The webcam recording does not contain enough video around the preview timestamp.");
        }

        status?.Report("Reading the microphone envelope and webcam mouth motion...");
        var audioTask = ExtractAudioEnvelopeAsync(
            ffmpegPath, primaryPath, primaryAudioStream, primaryStart, duration, cancellationToken);
        var visualTask = ExtractVisualMetricsAsync(
            ffmpegPath, secondaryPath, secondaryStart, secondaryDuration, cancellationToken);
        await Task.WhenAll(audioTask, visualTask);

        var audio = await audioTask;
        var visual = await visualTask;
        var candidates = new[]
        {
            FindBestOffset(audio, visual.MouthOpening, primaryStart, secondaryStart, searchSeconds, "mouth opening"),
            FindBestOffset(audio, visual.LipDarkness, primaryStart, secondaryStart, searchSeconds, "lip shading")
        };
        var best = candidates.OrderByDescending(candidate => candidate.Confidence).First();
        if (best.Confidence < 0.12)
        {
            throw new InvalidOperationException(
                "Visual lip-sync analysis could not find a useful match. Choose a Preview from timestamp where you are clearly talking, increase the search range, or adjust manually.");
        }

        status?.Report("Visual rough alignment complete.");
        return best;
    }

    private static async Task<double[]> ExtractAudioEnvelopeAsync(
        string ffmpegPath,
        string inputPath,
        int streamIndex,
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var bytes = await RunFfmpegToBytesAsync(
            ffmpegPath,
            new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-ss", Format(startSeconds), "-i", inputPath,
                "-t", Format(durationSeconds), "-map", $"0:{streamIndex}",
                "-vn", "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                "-f", "s16le", "pipe:1"
            },
            cancellationToken);

        var samplesPerPoint = SampleRate / FramesPerSecond;
        var pointCount = bytes.Length / 2 / samplesPerPoint;
        var result = new double[pointCount];
        for (var point = 0; point < pointCount; point++)
        {
            double squares = 0;
            var sampleStart = point * samplesPerPoint;
            for (var index = 0; index < samplesPerPoint; index++)
            {
                var byteIndex = (sampleStart + index) * 2;
                var sample = (short)(bytes[byteIndex] | bytes[byteIndex + 1] << 8);
                var value = sample / 32768d;
                squares += value * value;
            }
            result[point] = Math.Sqrt(squares / samplesPerPoint);
        }
        return SmoothAndNormalize(result);
    }

    private static async Task<(double[] MouthOpening, double[] LipDarkness)> ExtractVisualMetricsAsync(
        string ffmpegPath,
        string inputPath,
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var bytes = await RunFfmpegToBytesAsync(
            ffmpegPath,
            new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-ss", Format(startSeconds), "-i", inputPath,
                "-t", Format(durationSeconds),
                "-vf", $"crop=iw*0.1823:ih*0.2778:iw*0.3906:ih*0.2083,scale={FrameWidth}:{FrameHeight},fps={FramesPerSecond}",
                "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"
            },
            cancellationToken);

        var frameSize = FrameWidth * FrameHeight * 3;
        var frameCount = bytes.Length / frameSize;
        var mouthOpening = new double[frameCount];
        var lipDarkness = new double[frameCount];
        var red = new bool[65, 110];

        for (var frame = 0; frame < frameCount; frame++)
        {
            Array.Clear(red);
            var frameStart = frame * frameSize;
            for (var y = 40; y < 105; y++)
            {
                for (var x = 0; x < 110; x++)
                {
                    var index = frameStart + (y * FrameWidth + x) * 3;
                    var r = bytes[index];
                    var g = bytes[index + 1];
                    var b = bytes[index + 2];
                    var isLip = r > 28 && r > g * 1.16 + 5 && r > b * 1.08 + 3;
                    red[y - 40, x] = isLip;
                    if (isLip)
                    {
                        var luminance = (r + g + b) / 3d;
                        lipDarkness[frame] += Math.Max(0, 120 - luminance);
                    }
                }
            }

            for (var y = 0; y < 65; y++)
            {
                for (var x = 0; x < 110; x++)
                {
                    var index = frameStart + ((y + 40) * FrameWidth + x) * 3;
                    var luminance = (bytes[index] + bytes[index + 1] + bytes[index + 2]) / 3d;
                    if (luminance >= 52 || !HasNearbyLip(red, y, x))
                    {
                        continue;
                    }
                    mouthOpening[frame]++;
                }
            }
        }

        return (SmoothAndNormalize(mouthOpening), SmoothAndNormalize(lipDarkness));
    }

    private static bool HasNearbyLip(bool[,] red, int y, int x)
    {
        for (var amount = -5; amount <= 5; amount++)
        {
            var horizontal = x + amount;
            if (horizontal >= 0 && horizontal < 110 && red[y, horizontal])
            {
                return true;
            }
            var vertical = y + amount;
            if (vertical >= 0 && vertical < 65 && red[vertical, x])
            {
                return true;
            }
        }
        return false;
    }

    private static VisualSyncAnalysis FindBestOffset(
        double[] audio,
        double[] visual,
        double primaryStart,
        double secondaryStart,
        int searchSeconds,
        string metric)
    {
        var bestOffset = 0d;
        var bestCorrelation = double.NegativeInfinity;
        for (var offsetFrames = -searchSeconds * FramesPerSecond;
             offsetFrames <= searchSeconds * FramesPerSecond;
             offsetFrames++)
        {
            var offset = offsetFrames / (double)FramesPerSecond;
            double dot = 0;
            double audioEnergy = 0;
            double visualEnergy = 0;
            var count = 0;
            for (var audioIndex = 0; audioIndex < audio.Length; audioIndex++)
            {
                var primaryTime = primaryStart + audioIndex / (double)FramesPerSecond;
                var visualIndex = (int)Math.Round(
                    (primaryTime - offset - secondaryStart) * FramesPerSecond,
                    MidpointRounding.AwayFromZero);
                if (visualIndex < 0 || visualIndex >= visual.Length)
                {
                    continue;
                }
                var a = audio[audioIndex];
                var v = visual[visualIndex];
                dot += a * v;
                audioEnergy += a * a;
                visualEnergy += v * v;
                count++;
            }

            if (count < FramesPerSecond * 6)
            {
                continue;
            }
            var denominator = Math.Sqrt(audioEnergy * visualEnergy);
            var correlation = denominator <= 0 ? -1 : dot / denominator;
            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestOffset = offset;
            }
        }

        return new VisualSyncAnalysis(bestOffset, bestCorrelation, metric);
    }

    private static double[] SmoothAndNormalize(double[] values)
    {
        if (values.Length == 0)
        {
            return values;
        }
        var smoothed = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            double sum = 0;
            var count = 0;
            for (var neighbor = Math.Max(0, index - 2); neighbor <= Math.Min(values.Length - 1, index + 2); neighbor++)
            {
                sum += values[neighbor];
                count++;
            }
            smoothed[index] = sum / count;
        }

        var mean = smoothed.Average();
        var deviation = Math.Sqrt(smoothed.Sum(value => (value - mean) * (value - mean)) / smoothed.Length);
        if (deviation < 1e-9)
        {
            throw new InvalidOperationException("The selected interval did not contain enough microphone or mouth activity for visual synchronization.");
        }
        for (var index = 0; index < smoothed.Length; index++)
        {
            smoothed[index] = (smoothed[index] - mean) / deviation;
        }
        return smoothed;
    }

    private static async Task<byte[]> RunFfmpegToBytesAsync(
        string ffmpegPath,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started for visual synchronization.");
        await using var output = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "FFmpeg could not read the webcam for visual synchronization."
                : error.Trim());
        }
        return output.ToArray();
    }

    private static string Format(double value) =>
        Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture);
}
