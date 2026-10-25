"""Build private, self-contained presentation payload from the verified sources.

Keep original compiled libraries intact and attribute/hash them. Nine contributes
only the referenced effect dependency closure, never its gameplay assembly.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil

from catalogue_czn_assets import PACKS, WORKSHOP
from pck_tool import read_pck, extract, write_pck

ROOT = Path(__file__).resolve().parents[1]
MODULES = [("CznCore", "3826011578"), ("CznStyleUI", "3771060891"),
           ("CznMainMenu", "3774940754")]


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(4 * 1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def normalized(path):
    return path.removeprefix("res://")


def main():
    out = ROOT / "build/bundled"
    out.mkdir(parents=True, exist_ok=True)
    cache = ROOT / "build/source-cache"
    previous = json.loads((cache / "component-lock.json").read_text(encoding="utf-8")) if (cache / "component-lock.json").exists() else None
    legacy = None
    if previous:
        source_pack = cache / "standalone-source.pck"
        assert sha(source_pack) == previous["ownPack"]["sha256"], "Source cache checksum mismatch"
        legacy = read_pck(source_pack)
    records = []
    localization = {}
    localization_overrides = []
    for name, workshop in MODULES:
        source = WORKSHOP / workshop
        source_manifest = source / (name + ".json")
        manifest = json.loads(source_manifest.read_text(encoding="utf-8-sig")) if source_manifest.exists() else next(
            lib for lib in previous["libraries"] if lib["id"] == name)
        item = {"id": name, "version": manifest["version"], "author": manifest["author"],
                "workshop": "https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshop, "files": []}
        for ext in ("dll", "pck"):
            src, dst = source / (name + "." + ext), out / (name + "." + ext)
            if not src.exists():
                src = dst
                expected = next(f["sha256"] for f in manifest["files"] if f["file"] == "bundled/" + dst.name)
                assert sha(src) == expected, "Bundled source cache mismatch: " + name
            digest = sha(src)
            if not dst.exists() or sha(dst) != digest:
                shutil.copyfile(src, dst)
            item["files"].append({"file": "bundled/" + dst.name, "bytes": dst.stat().st_size, "sha256": digest})
        records.append(item)
        packed = read_pck(out / (name + ".pck"))
        for path in packed["files"]:
            if "/localization/" not in path or not path.endswith(".json"):
                continue
            relative = path.split("/localization/", 1)[1]
            table = json.loads(extract(packed, path).decode("utf-8-sig"))
            target = localization.setdefault(relative, {})
            for key, value in table.items():
                if key in target and target[key] != value:
                    localization_overrides.append({"table": relative, "key": key, "provider": name})
                target[key] = value
        del packed

    own_pack = read_pck(ROOT / "build/SaberPresentation.pck")
    files = {normalized(p): extract(own_pack, p) for p in own_pack["files"]}
    for path, table in localization.items():
        files["SaberPresentation/localization/" + path] = json.dumps(table, ensure_ascii=False).encode("utf-8")
    original = WORKSHOP / "3747659940/saber.pck"
    skin = read_pck(original) if original.exists() else legacy
    nine_cache_paths = {r["path"] for r in previous["nineEffects"]["resources"]} if previous else set()
    skin_files = [p for p in skin["files"] if normalized(p).startswith(
        ("animations/", "images/", ".godot/exported/", ".godot/imported/"))
        and normalized(p) not in nine_cache_paths and not normalized(p).startswith(".godot/imported/saber_")]
    for path in skin_files:
        files[normalized(path)] = extract(skin, path)

    nine_path = WORKSHOP / "3815657213/CznNineCharacter.pck"
    nine = read_pck(nine_path) if nine_path.exists() else legacy
    core = read_pck(out / "CznCore.pck")
    core_index = {normalized(p) for p in core["files"]}
    index = {normalized(p): p for p in nine["files"]}
    wanted = []
    for name, _, groups in PACKS:
        if name != "CznNineCharacter":
            continue
        for prefix, suffix, names in groups:
            wanted += [name + "/" + prefix + filename + suffix for filename in names]
    copied, missing = set(), set()

    def include(path):
        path = normalized(path)
        if path in copied:
            return
        if path not in index:
            if path in core_index or path + ".import" in core_index or path + ".remap" in core_index:
                return
            mapped = next((path + s for s in (".import", ".remap") if path + s in index), None)
            if mapped:
                include(mapped)
            else:
                missing.add(path)
            return
        copied.add(path)
        data = extract(nine, index[path])
        files[path] = data
        if path.endswith(".spatlas"):
            atlas = json.loads(data)
            folder = PurePosixPath(normalized(atlas["source_path"])).parent
            for page in re.findall(r"(?m)^([^\r\n]+\.png)\r?$", atlas["atlas_data"]):
                include(str(folder / page))
        # Both text resources/import maps and exported binary Spine atlases
        # contain the exact UTF-8 res:// paths of their dependencies.
        if not path.endswith((".ctex", ".spskel", ".oggvorbisstr")):
            for ref in re.findall(rb'res://[^\x00-\x1f"\x27]+', data):
                include(ref.decode("utf-8"))

    if nine_path.exists():
        for path in wanted:
            include(path)
    else:
        for item in previous["nineEffects"]["resources"]:
            path = item["path"]
            data = extract(nine, index[path])
            assert hashlib.sha256(data).hexdigest() == item["sha256"], "Effect cache mismatch: " + path
            files[path] = data
            copied.add(path)
    # Import files also list their non-packed source filename; a matching import
    # entry is its resolution, not an absent dependency.
    missing = {p for p in missing if p not in copied and p + ".import" not in copied and p + ".remap" not in copied}
    if missing:
        raise RuntimeError("Unresolved Nine effect dependencies: " + repr(sorted(missing)))
    write_pck(ROOT / "build/SaberPresentation.pck", files)
    lock = {"format": 1, "externalModDependencies": [], "nativeModEntries": 1,
            "localizationTablesMerged": len(localization),
            "localizationOverridesInModuleOrder": localization_overrides,
            "libraries": records,
            "saberSkin": {"author": "会画画的Small_day", "workshop": "https://steamcommunity.com/sharedfiles/filedetails/?id=3747659940",
                          "sourceSha256": sha(original) if original.exists() else previous["saberSkin"]["sourceSha256"], "includedResources": len(skin_files)},
            "nineEffects": {"author": "Pelleas", "workshop": "https://steamcommunity.com/sharedfiles/filedetails/?id=3815657213",
                           "sourceSha256": sha(nine_path) if nine_path.exists() else previous["nineEffects"]["sourceSha256"], "includesGameplayAssembly": False,
                           "resources": [{"path": p, "sha256": hashlib.sha256(files[p]).hexdigest()} for p in sorted(copied)]},
            "ownPack": {"file": "SaberPresentation.pck", "resources": len(files), "bytes": (ROOT / "build/SaberPresentation.pck").stat().st_size,
                        "sha256": sha(ROOT / "build/SaberPresentation.pck")}}
    (ROOT / "STANDALONE_COMPONENTS.json").write_text(json.dumps(lock, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Standalone: {len(skin_files)} Saber skin files + {len(copied)} Nine effect dependencies; {len(records)} bundled presentation libraries")
    print(f"Own pack: {len(files)} resources, {lock['ownPack']['bytes']:,} bytes. No external mod dependencies.")


if __name__ == "__main__":
    main()
