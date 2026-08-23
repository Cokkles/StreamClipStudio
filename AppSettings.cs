using System.Text.Json;

namespace StreamClipStudio;

internal sealed class AppSettings
{
    public string FfmpegPath { get; set; } = string.Empty;
    public string FfprobePath { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string LastInputFolder { get; set; } = string.Empty;
    public string PresetId { get; set; } = "lossless";
    public string LastSecondaryFolder { get; set; } = string.Empty;
    public string ComposeOutputFolder { get; set; } = string.Empty;
    public string ComposeLayout { get; set; } = "overlay";
    public string ComposeQuality { get; set; } = "youtube4k";
    public string ComposeOrientation { get; set; } = "landscape";
    public string PortraitLayout { get; set; } = "focus";
    public int GameplayFocusXPercent { get; set; } = 50;
    public string SafeAreaPlatform { get; set; } = "youtube";
    public string WebcamPosition { get; set; } = "Bottom center";
    public int WebcamWidthPercent { get; set; } = 28;
    public int WebcamXPercent { get; set; } = 2;
    public int WebcamYPercent { get; set; } = 70;
    public bool WebcamUseCustomPosition { get; set; }
    public int WebcamCropLeftPercent { get; set; }
    public int WebcamCropTopPercent { get; set; }
    public int WebcamCropRightPercent { get; set; }
    public int WebcamCropBottomPercent { get; set; }
    public int GameplayCropLeftPercent { get; set; }
    public int GameplayCropTopPercent { get; set; }
    public int GameplayCropRightPercent { get; set; }
    public int GameplayCropBottomPercent { get; set; }
    public bool ApplyGreenScreen { get; set; }
    public string WebcamBackgroundRemoval { get; set; } = "none";
    public int WebcamKeySensitivityPercent { get; set; } = 18;
    public bool FlipWebcamHorizontally { get; set; }
    public string WatermarkPath { get; set; } = string.Empty;
    public int WatermarkXPercent { get; set; } = 82;
    public int WatermarkYPercent { get; set; } = 92;
    public int WatermarkWidthPercent { get; set; } = 15;
    public int WatermarkOpacityPercent { get; set; } = 75;
    public string WatermarkBackgroundRemoval { get; set; } = "none";
    public int WatermarkKeySensitivityPercent { get; set; } = 18;
    public int SyncSearchSeconds { get; set; } = 8;
    public bool ShowYouTubeSafeAreas { get; set; } = true;
    public List<SavedAudioMix> AudioMixes { get; set; } = new();
    public List<CompositionPreset> CompositionPresets { get; set; } = new();
    public string LastProjectPath { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    private static string SettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamClipStudio");

    private static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                    ?? new AppSettings();
                settings.CompositionPresets ??= new List<CompositionPreset>();
                settings.AudioMixes ??= new List<SavedAudioMix>();
                return settings;
            }
        }
        catch
        {
            // A corrupt preference file should never prevent the app from opening.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}

internal sealed class CompositionPreset
{
    public string Name { get; set; } = string.Empty;
    public string Layout { get; set; } = "overlay";
    public string Quality { get; set; } = "youtube4k";
    public string Orientation { get; set; } = "landscape";
    public string PortraitLayout { get; set; } = "focus";
    public int GameplayFocusXPercent { get; set; } = 50;
    public string SafeAreaPlatform { get; set; } = "youtube";
    public string Position { get; set; } = "Custom / drag on canvas";
    public int WebcamWidthPercent { get; set; } = 28;
    public int WebcamXPercent { get; set; }
    public int WebcamYPercent { get; set; }
    public int WebcamCropLeftPercent { get; set; }
    public int WebcamCropTopPercent { get; set; }
    public int WebcamCropRightPercent { get; set; }
    public int WebcamCropBottomPercent { get; set; }
    public int GameplayCropLeftPercent { get; set; }
    public int GameplayCropTopPercent { get; set; }
    public int GameplayCropRightPercent { get; set; }
    public int GameplayCropBottomPercent { get; set; }
    public string BackgroundRemoval { get; set; } = "none";
    public int KeySensitivityPercent { get; set; } = 18;
    public bool FlipWebcamHorizontally { get; set; }
    public string WatermarkPath { get; set; } = string.Empty;
    public int WatermarkXPercent { get; set; } = 82;
    public int WatermarkYPercent { get; set; } = 92;
    public int WatermarkWidthPercent { get; set; } = 15;
    public int WatermarkOpacityPercent { get; set; } = 75;
    public string WatermarkBackgroundRemoval { get; set; } = "none";
    public int WatermarkKeySensitivityPercent { get; set; } = 18;
    public bool ShowYouTubeSafeAreas { get; set; } = true;
    public List<SavedAudioMix> AudioMixes { get; set; } = new();

    public override string ToString() => Name;
}

internal sealed class SavedAudioMix
{
    public string Source { get; set; } = string.Empty;
    public int StreamIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public int VolumePercent { get; set; } = 100;
    public bool Muted { get; set; }
    public bool Selected { get; set; }
}
