"""Estimate a rough silent-webcam offset from lip motion and a microphone track.

This is an experimental diagnostic helper for Stream Clip Studio. It deliberately
returns a rough starting point; the app's preview/nudge controls remain the final
authority for lip sync.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import numpy as np


FPS = 30
FRAME_WIDTH = 140
FRAME_HEIGHT = 120
SAMPLE_RATE = 12_000


def run_bytes(command: list[str]) -> bytes:
    process = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if process.returncode != 0:
        raise RuntimeError(process.stderr.decode("utf-8", errors="replace"))
    return process.stdout


def webcam_metrics(ffmpeg: str, path: str) -> dict[str, np.ndarray]:
    # The Snap Camera banana is centered in these 3840x2160 source recordings.
    # Crop the head/mouth region before downscaling to keep analysis inexpensive.
    raw = run_bytes([
        ffmpeg, "-v", "error", "-i", path,
        "-vf", f"crop=700:600:1500:450,scale={FRAME_WIDTH}:{FRAME_HEIGHT},fps={FPS}",
        "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1",
    ])
    frame_size = FRAME_WIDTH * FRAME_HEIGHT * 3
    frames = np.frombuffer(raw, dtype=np.uint8)
    frames = frames[: frames.size // frame_size * frame_size]
    frames = frames.reshape((-1, FRAME_HEIGHT, FRAME_WIDTH, 3)).astype(np.float32)

    # Mouth stays near the middle-left of this crop. Red/pink pixels isolate lips
    # and the dark mouth opening while excluding the black background.
    region = frames[:, 40:105, 0:110, :]
    red = region[..., 0]
    green = region[..., 1]
    blue = region[..., 2]
    lip_mask = (red > 28) & (red > green * 1.16 + 5) & (red > blue * 1.08 + 3)
    red_count = lip_mask.sum(axis=(1, 2)).astype(np.float64)

    luminance = region.mean(axis=3)
    lip_darkness = np.where(lip_mask, np.maximum(0, 120 - luminance), 0).sum(axis=(1, 2))
    lip_motion = np.abs(np.diff(red_count, prepend=red_count[0]))

    # A genuinely open mouth is dark and immediately surrounded by lip-colored
    # pixels. Dilation by a few pixels separates it from the black canvas.
    near_lip = lip_mask.copy()
    for amount in range(1, 6):
        near_lip[:, :, amount:] |= lip_mask[:, :, :-amount]
        near_lip[:, :, :-amount] |= lip_mask[:, :, amount:]
        near_lip[:, amount:, :] |= lip_mask[:, :-amount, :]
        near_lip[:, :-amount, :] |= lip_mask[:, amount:, :]
    mouth_opening = ((luminance < 52) & near_lip).sum(axis=(1, 2)).astype(np.float64)

    return {
        "lip area": red_count,
        "lip darkness": lip_darkness,
        "mouth opening": mouth_opening,
        "lip motion": lip_motion,
    }


def microphone_envelope(ffmpeg: str, path: str, audio_stream: int) -> np.ndarray:
    raw = run_bytes([
        ffmpeg, "-v", "error", "-i", path, "-map", f"0:a:{audio_stream}",
        "-vn", "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "s16le", "pipe:1",
    ])
    samples = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    samples_per_frame = SAMPLE_RATE // FPS
    samples = samples[: samples.size // samples_per_frame * samples_per_frame]
    samples = samples.reshape((-1, samples_per_frame))
    return np.sqrt(np.mean(samples * samples, axis=1) + 1e-12)


def smooth(values: np.ndarray, points: int) -> np.ndarray:
    kernel = np.ones(points, dtype=np.float64) / points
    return np.convolve(values, kernel, mode="same")


def normalized(values: np.ndarray) -> np.ndarray:
    values = values.astype(np.float64)
    deviation = values.std()
    return (values - values.mean()) / deviation if deviation > 1e-9 else values * 0


def score_offsets(audio: np.ndarray, visual: np.ndarray, search_seconds: int = 12) -> list[tuple[float, float]]:
    audio = normalized(smooth(audio, 5))
    visual = normalized(smooth(visual, 5))
    length = min(audio.size, visual.size)
    audio = audio[:length]
    visual = visual[:length]
    results: list[tuple[float, float]] = []
    for offset_frames in range(-search_seconds * FPS, search_seconds * FPS + 1):
        # App convention: webcam sample = gameplay time - offset.
        if offset_frames >= 0:
            a = audio[offset_frames:]
            v = visual[: length - offset_frames]
        else:
            a = audio[: length + offset_frames]
            v = visual[-offset_frames:]
        if a.size < FPS * 15:
            continue
        results.append((offset_frames / FPS, float(np.dot(a, v) / a.size)))
    return sorted(results, key=lambda item: item[1], reverse=True)


def main() -> int:
    if len(sys.argv) != 5:
        print("Usage: analyze_silent_webcam_sync.py <ffmpeg> <gameplay> <webcam> <zero-based-mic-track>")
        return 2

    ffmpeg, gameplay, webcam, audio_stream_text = sys.argv[1:]
    if not Path(gameplay).is_file() or not Path(webcam).is_file():
        print("Both source files must exist.", file=sys.stderr)
        return 2

    metrics = webcam_metrics(ffmpeg, webcam)
    audio = microphone_envelope(ffmpeg, gameplay, int(audio_stream_text))
    print(f"Analyzed {min(len(audio), len(next(iter(metrics.values())))) / FPS:.1f} seconds at {FPS} measurements/sec")
    best_offsets = []
    for name, values in metrics.items():
        ranked = score_offsets(audio, values)
        best_offsets.append(ranked[0][0])
        leaders = ", ".join(f"{offset:+.3f}s ({score:.3f})" for offset, score in ranked[:5])
        print(f"{name}: {leaders}")
    print(f"Median rough offset: {float(np.median(best_offsets)):+.3f} sec")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
