# Stream Clip Studio

A Windows desktop frontend for the local FFmpeg installation, designed for OBS gameplay recordings with multiple audio tracks. Version 0.6 removes the space-heavy collage banner, introduces a subdued navy workspace with purple/cyan illumination and modern gradient actions, upgrades audio mixing with a usable hybrid slider plus editable percentage/dB gain, and adds a complete 9:16 portrait composition path for Shorts, TikTok, and Reels. It also introduces versioned `.scsproj` project files as the foundation for future multi-segment timelines.

## Version 0.6 highlights

- Landscape 16:9 and portrait 9:16 composition canvases.
- Portrait templates for focused gameplay crop, full gameplay with blurred fill, and gameplay-top/banana-lower stacking.
- Universal 1080x1920 18 Mbps, compact social 12 Mbps, and vertical CQP 18 output profiles.
- YouTube Shorts, TikTok, and Meta safe-area/interface-obstruction guides.
- A movable gameplay focus position for portrait crops.
- Mixer channels with a hybrid 0-200% slider: 70% of its travel covers 0-100%, while 30% covers 100-200%.
- Exact percentage and dB entry, with 100% clearly represented as 0 dB.
- Headroom guidance that no longer treats several normal 100% tracks as proof of clipping.
- Versioned project files preserving source pairs, synchronization, clip range, orientation, layout, output profile, and future timeline-ready segment data.

## Clip one recording

1. Drop a recording onto the window or choose **Browse**.
2. Enter the start and end timestamps.
3. Choose the required audio tracks.
4. Select a preset:
   - **Lossless trim** preserves the exact source streams and is the normal first step for selecting footage.
   - **High-quality editing clip** creates an exact H.264 NVENC CQP 18 clip and retains every chosen audio track at 320 kbps.
   - **Compact sharing clip** creates a smaller CQP 23 MP4 preview.
   - **YouTube 4K60 master** produces a 3840x2160, 55 Mbps upload master.
5. Select **Create clip**.

## Sync and compose two recordings

Use the second tab for a gameplay recording from the gaming PC and a dedicated webcam recording from the streaming PC.

1. Load the gameplay recording as the primary source and the webcam recording as the secondary source.
2. For each source, choose a synchronization track containing the same audible material. Shared microphone, game, or stream audio works best. If the webcam track is absent or digital silence, choose the isolated gameplay microphone track instead.
3. Enter clip times relative to the gameplay recording. For a silent webcam, set **Preview from** to the beginning of a section containing clear speech.
4. Select **Analyze waveform sync** or **Analyze visual lip sync**, depending on the available tracks.
5. Review the detected offset and drift. A positive offset means the secondary recording began later than the gameplay recording.
6. Choose the tracks included in the final audio mix, give them useful labels, and adjust each from 0–200% with its logarithmic slider or exact percentage. Each track can be muted or reset independently. Then choose webcam placement or side-by-side layout and output quality. Drag the webcam on the standalone 16:9 placement canvas or enter exact X/Y percentages. Positions may extend beyond the frame, making it possible to tuck the banana below an edge.
7. Select **Render 10-second preview**. If needed, use the one-frame, 100 ms, or one-second webcam earlier/later controls and preview again.
8. Select **Compose video**.

For a portrait export, choose **Portrait / Reel 9:16**, select a portrait template and platform guide, move the gameplay focus when using the crop template, then choose one of the 1080x1920 output profiles. Synchronization is unchanged: sources are aligned first and arranged on the selected canvas afterward.

Independent webcam and gameplay crop controls remove unwanted source edges before scaling; webcam crop is applied before keying. The optional watermark section accepts PNG, WebP, JPEG, or BMP images and provides independent X/Y, width, and opacity controls. PNG is recommended when the watermark needs transparency.

The placement canvas now reflects webcam crop proportions, gameplay crop boundaries, and watermark location. Composition controls remain fixed at the bottom of the window while the configuration area scrolls independently. Section headers can be collapsed to shorten the workspace. Mouse-wheel input is disabled on numeric fields to prevent accidental changes.

Use **Saved composition configurations** to store and reload layout, webcam placement/crop/keying, gameplay crop, output quality, and watermark settings. Recording paths and clip timestamps are not stored in a layout preset. The most recently used settings and window size are also restored automatically.

Before a full composition begins, the app presents a visual layout and settings summary for confirmation. After a clip or composition succeeds, a verification dialog reports the actual resolution, FPS, bitrate, duration, selected profile, and any profile mismatch, with shortcuts to the file and its folder.

Up to five extra audio files can be layered into the final mix. Each layer has a start offset in milliseconds and an independent volume. Positive offsets delay the sound relative to the final clip; negative offsets trim that amount from the beginning of the sound. MP3, WAV, M4A/AAC, FLAC, OGG/Opus, and WMA are accepted.

The waveform analyzer uses recording timestamps for an initial estimate and then cross-correlates compact audio envelopes. On overlaps of at least 150 seconds, it also measures clock drift near the end and corrects long-session timing. Silent-webcam mode compares the isolated microphone envelope with mouth-opening and lip-shading changes over up to two minutes. Visual matching is deliberately treated as a rough estimate; the preview and nudge controls are the final authority. No cloud service or AI is involved.

For the easiest future synchronization, make sure both recordings contain at least one shared audio source and create a sharp clap or click near the beginning. Matching Windows clocks helps the search but is not a replacement for waveform analysis.

### Webcam transparency

The app reports whether the secondary file actually stores an alpha channel. Most H.264 recordings have no alpha even when OBS or Snap Camera displayed a transparent source. Choose **Remove black background** for the tested Snap Camera banana recording, **Remove green background** for green-screen footage, or **None** for real alpha. Adjust key sensitivity in the preview if dark details are removed. **Flip webcam horizontally** mirrors the webcam before it is keyed and overlaid.

The app automatically discovers the Tarkov/PPE FFmpeg installation at:

`C:\pinokio\bin\miniconda\Library\bin`

Preferences are stored in `%LOCALAPPDATA%\StreamClipStudio\settings.json`.

## Verified configuration

The single-clip workflow was tested with the project's CQP 17/P7 OBS sample:

- H.264, 2560×1440, 60 FPS source
- Three separate AAC audio tracks
- Lossless MKV extraction
- Precise NVENC CQP 18 editing output
- NVENC CQP 23 compact MP4 output
- 3840×2160 60 FPS, 55 Mbps YouTube output

The two-source engine was tested with a controlled +1.250 second offset. It recovered +1.250 seconds at 88% correlation and rendered a synchronized 1280x720 NVENC preview with the expected duration. A separate 190-second clock-skew test measured the changing offset and calculated a 1.001080 correction scale for an injected 1.001000 skew. The supplied two-minute silent-banana/gameplay pair produced a -0.533 second visual rough match and successfully rendered a horizontally flipped, black-keyed NVENC preview. Version 0.3 feature validation additionally rendered custom off-canvas placement, four-sided webcam crop, a transparent image watermark, and an offset auxiliary WAV layer in one pass. The precise modes reset video and every selected audio track to timestamp zero. Lossless mode remains keyframe-aligned by design.

## Build

From PowerShell:

```powershell
.\build-release.ps1
```

To create a larger self-contained build that does not require the .NET desktop runtime:

```powershell
.\build-release.ps1 -Portable
```

Builds are written to versioned folders beneath `release`, so an existing v0.1 build is not overwritten.
