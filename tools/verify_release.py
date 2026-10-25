"""Audit the shipped ZIP/installation and preserve native game test evidence."""
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
INSTALL = Path(r"D:\L\Game\Steam\steamapps\common\Slay the Spire 2\mods\SaberPresentation")


def sha_stream(stream):
    h = hashlib.sha256()
    for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
        h.update(block)
    return h.hexdigest()


def sha(path):
    with path.open("rb") as f:
        return sha_stream(f)


def main():
    lock = json.loads((ROOT / "STANDALONE_COMPONENTS.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "SaberPresentation.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    archive = ROOT / f"dist/SaberPresentation-{version}.zip"
    expected = {item["file"]: item["sha256"] for lib in lock["libraries"] for item in lib["files"]}
    for relative in ("SaberPresentation.dll", "SaberPresentation.pck", "SaberPresentation.json"):
        expected[relative] = sha(ROOT / "dist/SaberPresentation" / relative)
    artifacts = []
    with zipfile.ZipFile(archive) as z:
        assert json.loads(z.read("SaberPresentation/SaberPresentation.json"))["dependencies"] == []
        for relative, digest in expected.items():
            with z.open("SaberPresentation/" + relative) as entry:
                assert sha_stream(entry) == digest, "ZIP mismatch: " + relative
            assert sha(INSTALL / relative) == digest, "Installed mismatch: " + relative
            artifacts.append({"file": relative, "sha256": digest, "zipMatches": True, "installedMatches": True})
        assert not any(n.endswith("CznNineCharacter.dll") or n.endswith("saber.json") for n in z.namelist())
    classes = subprocess.check_output([r"D:\L\Study\modbulid\tools\ilspycmd.exe", "-l", "c",
                                      str(INSTALL / "SaberPresentation.dll")], text=True, encoding="utf-8")
    assert "SaberPresentation.TestHarness" not in classes
    suites = []
    for folder in ("verification-standalone", "verification-menu", "verification-all"):
        source = ROOT / "build" / folder / "results.json"
        results = json.loads(source.read_text(encoding="utf-8"))
        assert results and all(r["passed"] for r in results), folder + " failed"
        required = "native_main_menu_has_saber_lobby" if folder == "verification-menu" else "aoe_two_kills_one_cg"
        assert any(r["check"] == required for r in results), "Stale suite evidence: " + folder
        suites.append({"suite": folder, "passed": len(results), "failed": 0, "checks": results})
    report = {"version": version, "gameVersion": "0.111.0", "nativeModEntries": 1,
              "manifestDependencies": manifest["dependencies"], "releaseContainsTestHarness": False,
              "packageBytes": archive.stat().st_size, "packageSha256": sha(archive),
              "verifiedArtifacts": artifacts, "tests": suites,
              "totalPassed": sum(s["passed"] for s in suites),
              "warnings": ["Original Saber exported resources contain stale UIDs; Godot resolves their included text paths.",
                           "Instrumented game exits report Godot renderer/resource allocations, also present in the baseline.",
                           "Full subscription compatibility run may retain original third-party startup warnings; standalone startup audio attachment is guarded."],
              "presentationLimits": "Original nine Saber battle Spine clips are choreographed into seven routes; large portrait uses new painted frames and animated mesh, not Nine's character skeleton."}
    dest = ROOT / f"dist/verification-{version}.json"
    dest.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (ROOT / f"dist/SaberPresentation-{version}.zip.sha256").write_text(report["packageSha256"] + "  " + archive.name + "\n", encoding="ascii")
    print(f"Release verified: {len(artifacts)} payload files match ZIP and installation; {report['totalPassed']} native checks passed; no test harness in production DLL")


if __name__ == "__main__":
    main()
