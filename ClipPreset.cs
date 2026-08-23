namespace StreamClipStudio;

internal sealed record ClipPreset(
    string Id,
    string Name,
    string ShortName,
    string Description,
    string Extension,
    bool UsesNvenc,
    bool IsLosslessTrim)
{
    public override string ToString() => Name;

    public static IReadOnlyList<ClipPreset> All { get; } = new[]
    {
        new ClipPreset(
            "lossless",
            "Lossless trim — fastest",
            "Lossless",
            "Copies the original video and chosen audio tracks without re-encoding. Quality is untouched and the cut begins on the nearest keyframe.",
            ".mkv",
            false,
            true),
        new ClipPreset(
            "edit",
            "High-quality editing clip — CQP 18",
            "Edit-CQP18",
            "Precise H.264 NVENC clip for later webcam compositing. Very high quality with moderate compression; every chosen audio track is retained at 320 kbps.",
            ".mkv",
            true,
            false),
        new ClipPreset(
            "compact",
            "Compact sharing clip — CQP 23",
            "Compact-CQP23",
            "Smaller H.264 NVENC MP4 for previews and easy sharing. This is not the preferred master for another editing pass.",
            ".mp4",
            true,
            false),
        new ClipPreset(
            "youtube4k",
            "YouTube 4K60 master — 55 Mbps",
            "YouTube-4K60",
            "Upscales the finished 1440p clip to 4K and creates a high-bitrate H.264 master intended to receive YouTube's better 2160p60 delivery encode.",
            ".mp4",
            true,
            false)
    };
}
