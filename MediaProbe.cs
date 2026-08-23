using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace StreamClipStudio;

internal sealed record AudioStreamInfo(int Index, string Codec, int Channels, int SampleRate, long BitRate, string Title)
{
    public bool IsLikelySilent => BitRate is > 0 and < 8_000;

    public override string ToString()
    {
        var channelText = Channels == 2 ? "Stereo" : $"{Channels} channels";
        var titleText = string.IsNullOrWhiteSpace(Title) ? $"Audio track {Index}" : Title;
        return $"{titleText}  •  {Codec.ToUpperInvariant()}  •  {channelText}  •  {SampleRate / 1000d:0.#} kHz";
    }
}

internal sealed record MediaInfo(
    TimeSpan Duration,
    long SizeBytes,
    long BitRate,
    string VideoCodec,
    int Width,
    int Height,
    double FramesPerSecond,
    string PixelFormat,
    IReadOnlyList<AudioStreamInfo> AudioStreams)
{
    public bool HasAlpha
    {
        get
        {
            var format = PixelFormat.ToLowerInvariant();
            return format.StartsWith("yuva", StringComparison.Ordinal) ||
                   format.StartsWith("gbrap", StringComparison.Ordinal) ||
                   format is "rgba" or "bgra" or "argb" or "abgr" or "ya8" or "ya16be" or "ya16le";
        }
    }

    public string Summary =>
        $"{Width}×{Height}  •  {FramesPerSecond:0.##} FPS  •  {VideoCodec.ToUpperInvariant()}  •  " +
        $"{FormatDuration(Duration)}  •  {FormatBytes(SizeBytes)}  •  {BitRate / 1_000_000d:0.0} Mbps";

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private static string FormatBytes(long value)
    {
        var gb = value / 1_000_000_000d;
        return gb >= 1 ? $"{gb:0.00} GB" : $"{value / 1_000_000d:0} MB";
    }
}

internal static class MediaProbe
{
    public static async Task<MediaInfo> ReadAsync(string ffprobePath, string inputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
        {
            "-v", "error", "-show_format", "-show_streams", "-of", "json", inputPath
        })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFprobe could not be started.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "FFprobe could not read this file."
                : error.Trim());
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var format = root.GetProperty("format");
        var duration = ParseDouble(format, "duration");
        var size = ParseLong(format, "size");
        var bitRate = ParseLong(format, "bit_rate");

        string videoCodec = "unknown";
        int width = 0;
        int height = 0;
        double fps = 0;
        string pixelFormat = string.Empty;
        var audio = new List<AudioStreamInfo>();

        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var codecType = GetString(stream, "codec_type");
            if (codecType == "video" && width == 0)
            {
                videoCodec = GetString(stream, "codec_name");
                width = GetInt(stream, "width");
                height = GetInt(stream, "height");
                pixelFormat = GetString(stream, "pix_fmt");
                fps = ParseRate(GetString(stream, "avg_frame_rate"));
                if (fps <= 0)
                {
                    fps = ParseRate(GetString(stream, "r_frame_rate"));
                }
            }
            else if (codecType == "audio")
            {
                var title = string.Empty;
                if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("title", out var titleElement))
                {
                    title = titleElement.GetString() ?? string.Empty;
                }

                audio.Add(new AudioStreamInfo(
                    GetInt(stream, "index"),
                    GetString(stream, "codec_name"),
                    GetInt(stream, "channels"),
                    GetInt(stream, "sample_rate"),
                    ParseLong(stream, "bit_rate"),
                    title));
            }
        }

        return new MediaInfo(
            TimeSpan.FromSeconds(duration), size, bitRate, videoCodec, width, height, fps, pixelFormat, audio);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static int GetInt(JsonElement element, string property) =>
        int.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static long ParseLong(JsonElement element, string property) =>
        long.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static double ParseDouble(JsonElement element, string property) =>
        double.TryParse(GetString(element, property), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static double ParseRate(string rate)
    {
        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator != 0)
        {
            return numerator / denominator;
        }

        return 0;
    }
}
