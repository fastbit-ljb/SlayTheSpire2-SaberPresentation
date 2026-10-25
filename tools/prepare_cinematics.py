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


def encode_hsr():
    # Two complete clean camera phases; omit the intervening demonstration
    # insert that carries the original game's skill-title panel.
    filters = (
        "[0:v]trim=start=185.0:end=189.5,setpts=PTS-STARTPTS[a];"
        "[0:v]trim=start=197.0:end=201.5,setpts=PTS-STARTPTS[b];"
        "[a][b]concat=n=2:v=1:a=0,crop=1152:648:384:100,scale=1920:1080,"
        "fade=t=in:st=0:d=0.12,fade=t=out:st=8.68:d=0.32[v]"
    )
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
                    "-i", str(VIDEO / "hsr_saber_official_1080.mp4"), "-filter_complex", filters,
                    "-map", "[v]", "-an", "-c:v", "libtheora", "-q:v", "9", "-pix_fmt", "yuv420p",
                    str(VIDEO / "excalibur_np.ogv")], check=True)


if __name__ == "__main__":
    # Official Honkai: Star Rail Saber showcase.  The source audio is omitted;
    # the runtime synchronizes the existing JP Artoria NP lines to the stage.
    encode_hsr()
    encode("fgo_memorial_2023_official.mp4", "saber_defeat.ogv", 105.35, 9.3,
           "crop=1138:640:71:80,scale=1280:720,fade=t=in:st=0:d=0.16,fade=t=out:st=8.5:d=0.8", False)
