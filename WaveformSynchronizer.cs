using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StreamClipStudio;

internal sealed record SyncAnalysis(
    double RoughOffsetSeconds,
    double StartOffsetSeconds,
    double? EndOffsetSeconds,
    double StartConfidence,
    double? EndConfidence,
    TimeSpan FirstPrimaryPosition,
    TimeSpan? LastPrimaryPosition,
    double SecondaryPtsScale)
{
    public bool HasDriftMeasurement => EndOffsetSeconds.HasValue && LastPrimaryPosition.HasValue;

    public double DriftMillisecondsPerHour
    {
        get
        {
            if (!HasDriftMeasurement)
            {
                return 0;
            }

            var elapsed = LastPrimaryPosition!.Value.TotalSeconds - FirstPrimaryPosition.TotalSeconds;
            return elapsed <= 0
                ? 0
                : (EndOffsetSeconds!.Value - StartOffsetSeconds) / elapsed * 3_600_000d;
        }
    }

    public double OffsetAt(TimeSpan primaryPosition)
    {
        if (!HasDriftMeasurement)
        {
            return StartOffsetSeconds;
        }

        var total = LastPrimaryPosition!.Value.TotalSeconds - FirstPrimaryPosition.TotalSeconds;
        if (total <= 0)
        {
            return StartOffsetSeconds;
        }

        var fraction = (primaryPosition.TotalSeconds - FirstPrimaryPosition.TotalSeconds) / total;
        return StartOffsetSeconds + (EndOffsetSeconds!.Value - StartOffsetSeconds) * fraction;
    }
}

internal static partial class WaveformSynchronizer
{
    private const int SampleRate = 8_000;
    private const int EnvelopeRate = 200;
    private const int SamplesPerEnvelopePoint = SampleRate / EnvelopeRate;

    public static async Task<SyncAnalysis> AnalyzeAsync(
        string ffmpegPath,
        string primaryPath,
        MediaInfo primaryInfo,
        int primaryAudioStream,
        string secondaryPath,
        MediaInfo secondaryInfo,
        int secondaryAudioStream,
        int searchSeconds,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var roughOffset = EstimateFilenameOffset(primaryPath, secondaryPath);
        var overlapStart = Math.Max(0, roughOffset);
        var overlapEnd = Math.Min(primaryInfo.Duration.TotalSeconds, roughOffset + secondaryInfo.Duration.TotalSeconds);
        var overlapDuration = overlapEnd - overlapStart;
        if (overlapDuration < 20)
        {
            throw new InvalidOperationException("The two recordings do not have enough overlapping time for waveform synchronization.");
        }

        var windowDuration = Math.Min(40, overlapDuration - 6);
        var firstAbsolute = overlapStart + 3;
        var first = await AnalyzeWindowAsync(
            ffmpegPath,
            primaryPath,
            primaryAudioStream,
            firstAbsolute,
            secondaryPath,
            secondaryAudioStream,
            firstAbsolute - roughOffset,
            windowDuration,
            roughOffset,
            searchSeconds,
            "Analyzing the first shared-audio window...",
            status,
            cancellationToken);

        double? endOffset = null;
        double? endConfidence = null;
        TimeSpan? lastPrimaryPosition = null;
        var ptsScale = 1d;

        if (overlapDuration >= 150)
        {
            var lastAbsolute = overlapEnd - windowDuration - 3;
            var last = await AnalyzeWindowAsync(
                ffmpegPath,
                primaryPath,
                primaryAudioStream,
                lastAbsolute,
                secondaryPath,
                secondaryAudioStream,
                lastAbsolute - roughOffset,
                windowDuration,
                roughOffset,
                searchSeconds,
                "Checking clock drift near the end of the overlap...",
                status,
                cancellationToken);

            endOffset = last.OffsetSeconds;
            endConfidence = last.Confidence;
            lastPrimaryPosition = TimeSpan.FromSeconds(lastAbsolute + windowDuration / 2);
            var firstPosition = firstAbsolute + windowDuration / 2;
            var elapsed = lastPrimaryPosition.Value.TotalSeconds - firstPosition;
            if (elapsed > 0)
            {
                var offsetSlope = (endOffset.Value - first.OffsetSeconds) / elapsed;
                ptsScale = 1d / (1d - offsetSlope);
                if (ptsScale is < 0.98 or > 1.02)
                {
                    throw new InvalidOperationException(
                        "The measured clock drift is unusually large. Confirm that the same audio event is present on both selected tracks or use manual synchronization.");
                }
            }
        }

        status?.Report("Synchronization analysis complete.");
        return new SyncAnalysis(
            roughOffset,
            first.OffsetSeconds,
            endOffset,
            first.Confidence,
            endConfidence,
            TimeSpan.FromSeconds(firstAbsolute + windowDuration / 2),
            lastPrimaryPosition,
            ptsScale);
    }

    public static double EstimateFilenameOffset(string primaryPath, string secondaryPath)
    {
        if (TryReadRecordingTimestamp(primaryPath, out var primaryTimestamp) &&
            TryReadRecordingTimestamp(secondaryPath, out var secondaryTimestamp))
        {
            return (secondaryTimestamp - primaryTimestamp).TotalSeconds;
        }

        return 0;
    }

    private static async Task<(double OffsetSeconds, double Confidence)> AnalyzeWindowAsync(
        string ffmpegPath,
        string primaryPath,
        int primaryAudioStream,
        double primaryStart,
        string secondaryPath,
        int secondaryAudioStream,
        double secondaryStart,
        double duration,
        double roughOffset,
        int searchSeconds,
        string statusText,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Report(statusText);
        var primaryTask = ExtractEnvelopeAsync(
            ffmpegPath, primaryPath, primaryAudioStream, primaryStart, duration, cancellationToken);
        var secondaryTask = ExtractEnvelopeAsync(
            ffmpegPath, secondaryPath, secondaryAudioStream, secondaryStart, duration, cancellationToken);
        await Task.WhenAll(primaryTask, secondaryTask);

        var primary = await primaryTask;
        var secondary = await secondaryTask;
        var (lagPoints, confidence) = FindBestLag(primary, secondary, searchSeconds * EnvelopeRate);
        var lagSeconds = lagPoints / (double)EnvelopeRate;

        // B events appear at roughOffset - actualOffset relative to A in the aligned windows.
        return (roughOffset - lagSeconds, confidence);
    }

    private static async Task<float[]> ExtractEnvelopeAsync(
        string ffmpegPath,
        string inputPath,
        int streamIndex,
        double startSeconds,
        double durationSeconds,
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

        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-ss", startSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", inputPath,
            "-t", durationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-map", $"0:{streamIndex}",
            "-vn", "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
            "-f", "s16le", "pipe:1"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started for waveform analysis.");
        await using var buffer = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "FFmpeg could not extract the selected synchronization audio."
                : error.Trim());
        }

        return BuildEnvelope(buffer.ToArray());
    }

    private static float[] BuildEnvelope(byte[] pcmBytes)
    {
        var sampleCount = pcmBytes.Length / 2;
        var pointCount = sampleCount / SamplesPerEnvelopePoint;
        if (pointCount < EnvelopeRate * 5)
        {
            throw new InvalidOperationException("The selected audio window is too short for synchronization.");
        }

        var envelope = new float[pointCount];
        for (var point = 0; point < pointCount; point++)
        {
            double sumSquares = 0;
            var sampleStart = point * SamplesPerEnvelopePoint;
            for (var index = 0; index < SamplesPerEnvelopePoint; index++)
            {
                var byteIndex = (sampleStart + index) * 2;
                var sample = (short)(pcmBytes[byteIndex] | (pcmBytes[byteIndex + 1] << 8));
                var normalized = sample / 32768d;
                sumSquares += normalized * normalized;
            }
            envelope[point] = (float)Math.Sqrt(sumSquares / SamplesPerEnvelopePoint);
        }

        Normalize(envelope);
        return envelope;
    }

    private static void Normalize(float[] values)
    {
        var mean = values.Average(value => (double)value);
        double variance = 0;
        foreach (var value in values)
        {
            var centered = value - mean;
            variance += centered * centered;
        }

        var standardDeviation = Math.Sqrt(variance / values.Length);
        if (standardDeviation < 0.00001)
        {
            throw new InvalidOperationException(
                "The selected synchronization track is nearly silent. Choose a track containing shared microphone or game audio.");
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (float)((values[index] - mean) / standardDeviation);
        }
    }

    private static (int Lag, double Confidence) FindBestLag(float[] primary, float[] secondary, int maxLag)
    {
        var bestLag = 0;
        var bestCorrelation = double.NegativeInfinity;
        var minimumOverlap = Math.Min(primary.Length, secondary.Length) / 2;

        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            var primaryStart = Math.Max(0, -lag);
            var secondaryStart = Math.Max(0, lag);
            var count = Math.Min(primary.Length - primaryStart, secondary.Length - secondaryStart);
            if (count < minimumOverlap)
            {
                continue;
            }

            double dot = 0;
            double primaryEnergy = 0;
            double secondaryEnergy = 0;
            for (var index = 0; index < count; index++)
            {
                var a = primary[primaryStart + index];
                var b = secondary[secondaryStart + index];
                dot += a * b;
                primaryEnergy += a * a;
                secondaryEnergy += b * b;
            }

            var denominator = Math.Sqrt(primaryEnergy * secondaryEnergy);
            var correlation = denominator <= 0 ? -1 : dot / denominator;
            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }

        if (bestCorrelation < 0.10)
        {
            throw new InvalidOperationException(
                "The selected audio tracks did not contain a confident shared waveform match. Try different tracks, increase the search range, or enter the offset manually.");
        }

        return (bestLag, bestCorrelation);
    }

    private static bool TryReadRecordingTimestamp(string path, out DateTime timestamp)
    {
        var match = RecordingTimestampRegex().Match(Path.GetFileNameWithoutExtension(path));
        if (match.Success)
        {
            var value = $"{match.Groups["date"].Value} {match.Groups["time"].Value}";
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH-mm-ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out timestamp);
        }

        timestamp = default;
        return false;
    }

    [GeneratedRegex(@"(?<date>\d{4}-\d{2}-\d{2})[ _](?<time>\d{2}-\d{2}-\d{2})")]
    private static partial Regex RecordingTimestampRegex();
}
