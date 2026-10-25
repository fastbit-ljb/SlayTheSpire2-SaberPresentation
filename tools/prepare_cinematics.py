"""Rebuild the two in-mod videos from the attributed, locally cached originals."""
from pathlib import Path
import subprocess

import imageio_ffmpeg

ROOT = Path(__file__).resolve().parents[1]
VIDEO = ROOT / "assets/video"


def encode(source, output, start, duration, filters, audio):
    command = [imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
               "-ss", str(start), "-t", str(duration), "-i", str(VIDEO / source),
               "-vf", filters, "-c:v", "libtheora", "-q:v", "8", "-pix_fmt", "yuv420p"]
    command += ["-c:a", "libvorbis", "-q:a", "5"] if audio else ["-an"]
    subprocess.run(command + [str(VIDEO / output)], check=True)


if __name__ == "__main__":
    encode("fgo_official_saber_720p.mp4", "excalibur_np.ogv", 23.15, 16.6,
           "crop=1138:456:71:80,scale=1280:512,pad=1280:720:0:104:black", True)
    encode("fgo_memorial_2023_official.mp4", "saber_defeat.ogv", 105.35, 9.3,
           "crop=1138:640:71:80,scale=1280:720,fade=t=in:st=0:d=0.16,fade=t=out:st=8.5:d=0.8", False)
