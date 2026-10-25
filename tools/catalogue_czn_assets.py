"""Record the installed CZN resources used at runtime; copy no source assets."""
import hashlib
import json
from pathlib import Path

from pck_tool import extract, read_pck

ROOT = Path(__file__).resolve().parents[1]
WORKSHOP = Path(r"D:\L\Game\Steam\steamapps\workshop\content\2868840")

PACKS = [
    ("CznCore", "3826011578", [
        ("animations/common/", "_skel_data.tres", {
            "system_buff_shield_b": "shield rear layer",
            "system_buff_shield_f1": "shield sphere",
            "system_buff_shield_f2": "shield front particles",
            "system_hit_shield_player_f1": "blocked hit",
            "system_hit_shield_player_f2": "blocked hit particles",
            "system_break_shield_player": "shield breaking",
            "system_eff_buff": "skill aura",
        }),
        ("audio/sfx/", ".ogg", {
            "system_buff_shield": "shield sound",
            "system_eff_buff": "skill sound",
        }),
    ]),
    ("CznNineCharacter", "3815657213", [
        ("animations/basic_effects/", "_skel_data.tres", {
            "close_combat_step_player_move": "departure dust",
            "close_combat_step_player_arrive": "arrival dust",
            "nine_30047_attack_play1_self_f": "swing particles",
            "nine_30047_attack_play2_self_f_1": "combo swing particles",
            "nine_30047_u2_attack_play_f_1": "rising attack particles",
            "nine_30047_attack_play1_target_b": "normal impact",
            "nine_30047_attack_play2_target_b": "alternate impact",
            "nine_30047_u1_attack_play_target_b": "heavy impact",
            "nine_30047_u3_attack_play_target_f_1": "aerial landing impact",
        }),
        ("audio/sfx/", ".ogg", {
            "se_30047_attack_play1": "sword sound",
            "se_30047_u1_attack_play": "heavy sword sound",
            "se_30047_ug_attack": "beam sound layer",
        }),
        ("animations/cinematic/", "_skel_data.tres", {
            "nine_30047_ug_intro_sword": "noble phantasm charge",
            "nine_30047_ug_target_b": "noble phantasm rear impact",
            "nine_30047_ug_target_f": "noble phantasm front impact",
        }),
    ]),
]


def main():
    sources = []
    for name, workshop_id, groups in PACKS:
        folder = WORKSHOP / workshop_id
        manifest = json.loads((folder / f"{name}.json").read_text(encoding="utf-8-sig"))
        pack = read_pck(folder / f"{name}.pck")
        resources = []
        for prefix, suffix, names in groups:
            for filename, purpose in names.items():
                path = f"res://{name}/{prefix}{filename}{suffix}"
                packed = path if path in pack["files"] else path + ".import"
                data = extract(pack, packed)  # Fails if the installed source changed.
                resources.append({
                    "resource": path, "purpose": purpose, "packedEntry": packed,
                    "sha256": hashlib.sha256(data).hexdigest(),
                })
        sources.append({
            "id": name, "version": manifest["version"], "author": manifest["author"],
            "workshop": f"https://steamcommunity.com/sharedfiles/filedetails/?id={workshop_id}",
            "pckSha256": hashlib.sha256(pack["data"]).hexdigest(),
            "resources": resources,
        })
    output = {
        "integration": "Optional resource lookup from the user's installed, enabled PCKs",
        "bundledCznAssets": False, "directCznAssemblyReferences": False,
        "adaptation": "Route-specific layers, adjusted placement/scale/playback speed and warm tint; Saber JP voices retained",
        "sources": sources,
    }
    output["runtimeModules"] = []
    for name, workshop_id in [("CznStyleUI", "3771060891"), ("CznMainMenu", "3774940754"),
                              ("CznEnemySkin", "3783041069"), ("ChizuruIroncladSkin", "3826011833")]:
        folder = WORKSHOP / workshop_id
        manifest = json.loads((folder / f"{name}.json").read_text(encoding="utf-8-sig"))
        output["runtimeModules"].append({
            "id": name, "version": manifest["version"], "author": manifest["author"],
            "workshop": f"https://steamcommunity.com/sharedfiles/filedetails/?id={workshop_id}",
            "dllSha256": hashlib.sha256((folder / f"{name}.dll").read_bytes()).hexdigest(),
            "pckBytes": (folder / f"{name}.pck").stat().st_size,
            "integration": "Compatibility verification" if name == "ChizuruIroncladSkin" else "Original installed module; no duplication",
        })
    (ROOT / "CZN_ASSETS.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Recorded {sum(len(s['resources']) for s in sources)} runtime resources from {len(sources)} installed packs")


if __name__ == "__main__":
    main()
