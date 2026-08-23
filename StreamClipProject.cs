using System.Text.Json;

namespace StreamClipStudio;

internal sealed class StreamClipProject
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled project";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ProjectSourcePair> SourcePairs { get; set; } = new();
    public List<ProjectSegment> Segments { get; set; } = new();
    public ProjectComposition Composition { get; set; } = new();
    public List<ProjectAudioLayer> AudioLayers { get; set; } = new();

    public void Save(string path)
    {
        UpdatedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static StreamClipProject Load(string path) => JsonSerializer.Deserialize<StreamClipProject>(File.ReadAllText(path))
        ?? throw new InvalidDataException("The project file is empty or invalid.");
}

internal sealed class ProjectSourcePair
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameplayPath { get; set; } = string.Empty;
    public string WebcamPath { get; set; } = string.Empty;
    public double SecondaryOffsetSeconds { get; set; }
    public double SecondaryPtsScale { get; set; } = 1;
}

internal sealed class ProjectSegment
{
    public string SourcePairId { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public int Order { get; set; }
}

internal sealed class ProjectComposition
{
    public string Orientation { get; set; } = "landscape";
    public string Layout { get; set; } = "overlay";
    public string PortraitLayout { get; set; } = "focus";
    public int GameplayFocusXPercent { get; set; } = 50;
    public string OutputProfileId { get; set; } = "youtube4k";
}

internal sealed class ProjectAudioLayer
{
    public string Path { get; set; } = string.Empty;
    public int StartMilliseconds { get; set; }
    public int VolumePercent { get; set; } = 100;
    public string Label { get; set; } = string.Empty;
}
