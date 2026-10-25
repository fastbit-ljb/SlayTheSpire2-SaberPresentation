"""Record the original CZN resources adapted and bundled by the standalone build."""
import hashlib
import json
from pathlib import Path

from pck_tool import extract, read_pck

ROOT = Path(__file__).resolve().parents[1]

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
    lock = json.loads((ROOT / "STANDALONE_COMPONENTS.json").read_text(encoding="utf-8"))
    pack = read_pck(ROOT / "build/SaberPresentation.pck")
    assert hashlib.sha256(pack["data"]).hexdigest() == lock["ownPack"]["sha256"]
    sources = []
    for name, workshop_id, groups in PACKS:
        original = next(source for source in lock["effectSourcePacks"] if source["id"] == name)
        resources = []
        for prefix, suffix, names in groups:
            for filename, purpose in names.items():
                logical = f"{name}/{prefix}{filename}{suffix}"
                packed = next((logical + suffix for suffix in ("", ".import", ".remap") if logical + suffix in pack["files"]), None)
                assert packed, logical
                resources.append({"resource": "res://" + logical, "purpose": purpose, "packedEntry": packed,
                    "sha256": hashlib.sha256(extract(pack, packed)).hexdigest()})
        sources.append({"id": name, "version": original["version"], "author": original["author"],
            "workshop": original["workshop"], "pckSha256": original["sourceSha256"], "resources": resources})
    output = {
        "integration": "Character-only resource dependency closure in our single PCK; native game UI and menus",
        "bundledCznAssets": True, "directCznAssemblyReferences": False,
        "adaptation": "Route-specific layers, adjusted placement/scale/playback speed and warm tint; Saber JP voices retained",
        "sources": sources, "runtimeModules": [], "foreignRuntimeAssemblies": [],
        "dependencyResourceCount": len(lock["characterEffects"]["resources"])
    }
    (ROOT / "CZN_ASSETS.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Recorded {sum(len(s['resources']) for s in sources)} character effect seeds; zero foreign runtime modules")


if __name__ == "__main__":
    main()
