"""Acquire JP Artoria audio and build our art/audio pack.

prepare_standalone.py then adds the complete skin and effect dependency closure.
"""
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).parent))
from pck_tool import write_pck

API = "https://api.atlasacademy.io/nice/JP/servant/2?lore=true"
VOICE_IDS = {
    "attack_1": "0_B010", "attack_2": "0_B020", "attack_3": "0_B030",
    "attack_4": "0_B410", "attack_5": "0_B420", "attack_6": "0_B430", "attack_7": "0_B910",
    "heavy": "0_B040", "skill_1": "0_B080", "skill_2": "0_B090",
    "heavy_2": "0_B440", "heavy_3": "0_B840", "skill_3": "0_B480", "skill_4": "0_B490",
    "entry_1": "0_B100", "entry_2": "0_B110",
    "entry_3": "0_B500", "entry_4": "0_B510",
    "victory_1": "0_B120", "victory_2": "0_B130",
    "victory_3": "0_B520", "victory_4": "0_B530",
    "hurt_1": "0_B140", "hurt_2": "0_B150", "death": "0_B160",
    "np_charge": "0_B050", "np_release": "0_B051", "np_end": "0_B052",
}


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "SaberPresentation-local-fan-mod/0.1"})
    with urllib.request.urlopen(request, timeout=45) as response:
        return response.read()


def main():
    assets = ROOT / "assets"
    assets.mkdir(exist_ok=True)
    cache = assets / "artoria_jp.json"
    if not cache.exists():
        cache.write_bytes(fetch(API))
    data = json.loads(cache.read_text(encoding="utf-8"))
    assert data["id"] == 100100, "Must be Artoria Saber, JP"
    all_audio = {identifier: url for group in data["profile"]["voices"]
                 for line in group["voiceLines"]
                 for identifier, url in zip(line["id"], line["audioAssets"])}
    sources = {f"audio/{name}.mp3": all_audio[identifier] for name, identifier in VOICE_IDS.items()}
    sources["images/artoria.png"] = data["extraAssets"]["charaFigure"]["ascension"]["2"]

    def acquire(item):
        relative, url = item
        dest = assets / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        if not dest.exists():
            dest.write_bytes(fetch(url))
        return {"file": relative, "url": url, "sha256": hashlib.sha256(dest.read_bytes()).hexdigest()}

    with ThreadPoolExecutor(max_workers=5) as pool:
        records = list(pool.map(acquire, sources.items()))
    (assets / "sources.json").write_text(json.dumps({"api": API, "locale": "JP", "servantId": 100100,
        "referenceVideo": "https://www.bilibili.com/video/BV1wA4m1A7ZD/", "assets": records}, indent=2), encoding="utf-8")
    files = {f"SaberPresentation/{rel}": (assets / rel).read_bytes() for rel in sources if rel.endswith(".mp3")}
    # Keep the raster artwork and alpha intact; slice frames with AtlasTexture.
    image_paths = [assets / "images/artoria.png"]
    image_paths += [assets / "generated" / (name + ".png") for name in
        ["saber_card_atlas", "saber_vfx_atlas", "saber_ready_attack", "saber_ready_cast", "camelot_stage", "saber_defeat", "saber_attack_sequence"]
        if (assets / "generated" / (name + ".png")).exists()]
    for image_path in image_paths:
        im = Image.open(image_path).convert("RGBA")
        buff = io.BytesIO()
        im.save(buff, format="WEBP", lossless=True)
        webp = buff.getvalue()
        res_path = "SaberPresentation/" + image_path.relative_to(assets).as_posix()
        ctex = "saber_" + image_path.stem + "-" + hashlib.md5(res_path.encode()).hexdigest() + ".ctex"
        header = b"GST2" + struct.pack("<IIIIIIIIIHHIII", 1, *im.size, 0, 0, 0, 0, 0, 2, *im.size, 0, 5, len(webp))
        files[".godot/imported/" + ctex] = header + webp
        files[res_path + ".import"] = ('[remap]\nimporter="texture"\ntype="CompressedTexture2D"\n'
            f'path="res://.godot/imported/{ctex}"\n\n[deps]\nsource_file="res://{res_path}"\n'
            f'dest_files=["res://.godot/imported/{ctex}"]\n\n[params]\n').encode()
    for scene_path in (assets / "scenes").glob("*"):
        if scene_path.suffix in (".gd", ".tscn"):
            files["SaberPresentation/scenes/" + scene_path.name] = scene_path.read_bytes()
    out = ROOT / "build/SaberPresentation.pck"
    out.parent.mkdir(exist_ok=True)
    write_pck(out, files)
    print(f"Packed {len(files)} resources; {len(image_paths)} raster textures; {out.stat().st_size:,} bytes")
    print(f"JP voices: {len(VOICE_IDS)}; provenance: {assets / 'sources.json'}")


if __name__ == "__main__":
    main()
