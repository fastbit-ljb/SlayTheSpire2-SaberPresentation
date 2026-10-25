"""Normalize the cached public EMIYA instrumental for offline Godot playback."""
import hashlib
import json
from pathlib import Path
import subprocess

import imageio_ffmpeg

ROOT = Path(__file__).resolve().parents[1]
MUSIC = ROOT / "assets/music"
source = MUSIC / "emiya_ubw_source.m4a"
output = MUSIC / "emiya_ubw.mp3"
assert source.exists(), "Keep the source cache recorded in assets/music/source.json"
subprocess.run([
    imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
    "-i", str(source), "-vn", "-af",
    "loudnorm=I=-18:TP=-1.5:LRA=11,afade=t=in:d=0.3,afade=t=out:st=1812:d=2",
    "-ar", "44100", "-ac", "2", "-codec:a", "libmp3lame", "-q:a", "3",
    "-metadata", "title=EMIYA_UBW Extended (instrumental loop)",
    "-metadata", "artist=James Harris / Hideyuki Fukasawa", str(output),
], check=True)
record = json.loads((MUSIC / "source.json").read_text(encoding="utf-8"))
record.update({
    "work": "EMIYA_UBW Extended — Fate/stay night [Unlimited Blade Works] Original Soundtrack",
    "composer": "James Harris", "arranger": "Hideyuki Fukasawa", "label": "Aniplex Inc.",
    "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "processing": "Complete public 30-minute instrumental loop; -18 LUFS / -1.5 dBTP; MP3 VBR q3; short end fades",
    "runtime": "Offline loop; Saber runs only; game master/BGM controls; voice ducking; paused during cinematics",
    "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
})
(MUSIC / "source.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Prepared {output.name}: {output.stat().st_size:,} bytes")
