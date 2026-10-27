"""Audit the shipped ZIP/installation and preserve native game test evidence."""
import hashlib
import json
import math
from pathlib import Path
import subprocess
import zipfile

from pck_tool import read_pck

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


def hover_distance(a, b):
    other = {bone["Name"]: bone for bone in b["Bones"]}
    squared = []
    for bone in a["Bones"]:
        if bone["Name"] not in ("Ys_wuqi1", "Ys_wuqi2", "Ys_lhand2", "Ys_rhand"):
            continue
        target = other[bone["Name"]]
        squared.append(sum((bone[k] - target[k]) ** 2 for k in ("X", "Y"))
                       + 80 ** 2 * sum((bone[k] - target[k]) ** 2 for k in ("AxisX", "AxisY")))
    assert len(squared) == 4, "Missing live sword/hand measurements"
    return math.sqrt(sum(squared) / len(squared))


def main():
    lock = json.loads((ROOT / "STANDALONE_COMPONENTS.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "SaberPresentation.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    archive = ROOT / f"dist/SaberPresentation-{version}.zip"
    expected = {item["file"]: item["sha256"] for lib in lock["libraries"] for item in lib["files"]}
    for relative in ("SaberPresentation.dll", "SaberPresentation.pck", "SaberPresentation.json"):
        expected[relative] = sha(ROOT / "dist" / version / "SaberPresentation" / relative)
    artifacts = []
    with zipfile.ZipFile(archive) as z:
        assert json.loads(z.read("SaberPresentation/SaberPresentation.json"))["dependencies"] == []
        for relative, digest in expected.items():
            with z.open("SaberPresentation/" + relative) as entry:
                assert sha_stream(entry) == digest, "ZIP mismatch: " + relative
            assert sha(INSTALL / relative) == digest, "Installed mismatch: " + relative
            artifacts.append({"file": relative, "sha256": digest, "zipMatches": True, "installedMatches": True})
        assert [n for n in z.namelist() if n.endswith(".dll")] == ["SaberPresentation/SaberPresentation.dll"]
        assert [n for n in z.namelist() if n.endswith(".pck")] == ["SaberPresentation/SaberPresentation.pck"]
    assert lock["libraries"] == [] and lock["nativeUiPreserved"]
    assert not list(INSTALL.glob("bundled/*.dll")) and not list(INSTALL.glob("bundled/*.pck"))
    pack = read_pck(INSTALL / "SaberPresentation.pck")
    from pck_tool import extract
    guard_animation = lock["authoredGuardAnimation"]
    assert guard_animation["animation"] == "saber_ready_guard"
    assert guard_animation["retainedNativeAnimationBytes"] and guard_animation["nativeAnimationCount"] == 9
    assert guard_animation["combatEffectSlotsSuppressed"] == 66
    assert guard_animation["effectAttachmentsClearedFromTimeZero"] and guard_animation["handheld"]
    assert guard_animation["uprightReferenceAnimation"] == "idle_loop" and guard_animation["heldProgress"] == 0
    assert guard_animation["staticBonePose"] and guard_animation["maximumSerializedPalmError"] < 0.01
    assert guard_animation["swordDrawsInFrontOfFace"] and guard_animation["palmsDrawOverSwordGrip"]
    assert guard_animation["allScaleSignsMatchUprightReference"]
    assert guard_animation["gripSeparationSwordUnits"] >= 20 and guard_animation["authoredAnimationCount"] == 2
    raise_animation = guard_animation["raiseAnimation"]
    assert raise_animation["animation"] == "saber_raise_charge" and raise_animation["raiseSeconds"] == 1.1
    assert raise_animation["duration"] > raise_animation["heldTime"] == 1.1
    assert raise_animation["maximumInterpolatedPalmErrorSwordUnits"] < 0.2
    assert raise_animation["finalLowerPalmClearanceWorld"] > 10
    assert raise_animation["allScaleSignsMatchUprightReference"] and raise_animation["torsoAndHeadMatchUprightEverySample"]
    assert raise_animation["startMatchesStaticGuard"] and raise_animation["finalPoseHeldThroughDuration"]
    for body_part in ("torso", "head"):
        axes = guard_animation["worldTorsoAndHeadAxes"][body_part]
        assert abs(axes["serializedAxisDegrees"] - axes["uprightReferenceAxisDegrees"]) < 0.01
    assert hashlib.sha256(extract(pack, guard_animation["resourcePath"])).hexdigest() == guard_animation["authoredSkeletonSha256"]
    assert not any(any(term in p for term in ("CznStyleUI", "CznMainMenu", "CznEnemySkin", "/localization/", "saber_card_atlas", "saber_lobby")) for p in pack["files"])
    assert len(lock["characterEffects"]["seedResources"]) == 24
    for resource in ("music/emiya_ubw.mp3", "video/excalibur_np.ogv", "video/saber_defeat.ogv",
                     "generated/saber_rig_body_v1.png.import", "generated/saber_rig_head_v1.png.import", "generated/saber_arm_parts_v1.png.import"):
        assert "SaberPresentation/" + resource in pack["files"], "Missing offline media: " + resource
    assert not any(any(term in p for term in ("anime_v2", "anime_v3", "anime_v4", "saber_attack_sequence")) for p in pack["files"])
    for item in lock["characterEffects"]["resources"]:
        from pck_tool import extract
        assert hashlib.sha256(extract(pack, item["path"])).hexdigest() == item["sha256"]
    classes = subprocess.check_output([r"D:\L\Study\modbulid\tools\ilspycmd.exe", "-l", "c",
                                      str(INSTALL / "SaberPresentation.dll")], text=True, encoding="utf-8")
    assert "SaberPresentation.TestHarness" not in classes
    assert "SaberPresentation.DemoHarness" not in classes
    suites = []
    for folder in ("verification-standalone", "verification-menu", "verification-fix-hd"):
        source = ROOT / "build" / folder / "results.json"
        results = json.loads(source.read_text(encoding="utf-8"))
        assert results and all(r["passed"] for r in results), folder + " failed"
        required = "native_main_menu_no_theme_host" if folder == "verification-menu" else (
            "actual_1920x1080_render" if folder == "verification-fix-hd" else "aoe_two_kills_one_cg")
        assert any(r["check"] == required for r in results), "Stale suite evidence: " + folder
        new_checks = ["anime_music_absent_from_main_menu"] if folder == "verification-menu" else [
            "anime_bgm_instrumental_decodes", "anime_bgm_ducks_for_japanese_voice",
            "anime_bgm_pauses_for_cg", "anime_bgm_resumes_after_cg", "anime_bgm_obeys_game_music_mute",
            "ready_portrait_retracts_on_card_play", "portrait_is_native_weighted_skeleton",
            "portrait_textures_unchanged_through_ready_exit",
            "portrait_hair_pixels_really_deform", "portrait_idle_pixels_really_deform",
            "attack_hover_holds_charge_pose", "skill_hover_holds_sword_in_ground_pose",
            "hover_pose_has_no_painted_combat_vfx", "mouse_leave_retracts_hover_pose"]
        if folder == "verification-standalone":
            new_checks += ["killing_blow_uses_animated_cg", "game_over_uses_animated_defeat_cg",
                           "defeat_video_advances_beyond_single_frame", "killing_blow_uses_integrated_battle_stage",
                           "finisher_actor_stays_on_original_battle_mark", "finisher_does_not_independently_resize_actor",
                           "finisher_target_stays_on_original_mark_except_hit_recoil", "finisher_warm_camelot_is_visibly_revealed",
                           "finisher_beam_is_visible_with_actor_and_target",
                           "finisher_environment_changes_with_timeline", "finisher_target_receives_stage_hit",
                           "finisher_emitter_follows_forward_sword", "finisher_emitter_uses_live_sword_bone",
                           "finisher_stage_releases_live_bodies", "finisher_releases_only_one_beam",
                           "hsr_cg_reaches_final_column_impact", "finisher_cancel_restores_live_body_ownership",
                           "finisher_cancel_restores_live_body_transforms", "finisher_cancel_cleans_stage"]
        for check in new_checks:
            assert any(r["check"] == check and r["passed"] for r in results), "Stale suite evidence: " + check
        metadata_path = ROOT / "build" / folder / "run-metadata.json"
        metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.exists() else {}
        if folder == "verification-standalone":
            assert metadata.get("version") == version, "Battle evidence is from another release"
            assert metadata.get("assemblySha256") == sha(ROOT / "build/test-dll/SaberPresentation.dll"), "Battle evidence is from an older test build"
            assert metadata.get("captureWindowMode") in ("Fullscreen", "ExclusiveFullscreen")
        suites.append({"suite": folder, "testedVersion": metadata.get("version"),
                       "passed": len(results), "failed": 0, "checks": results})
    hover_root = ROOT / "build/verification-hover"
    hover = json.loads((hover_root / "results.json").read_text(encoding="utf-8"))
    hover_metadata = json.loads((hover_root / "run-metadata.json").read_text(encoding="utf-8"))
    assert hover_metadata["version"] == version, "Hover evidence is from another release"
    assert hover_metadata["assemblySha256"] == sha(ROOT / "build/test-dll/SaberPresentation.dll"), "Hover evidence is from an older test build"
    assert hover_metadata["captureWindowMode"] in ("Fullscreen", "ExclusiveFullscreen")
    hover_samples_path = hover_root / "hover-entry-samples.json"
    hover_samples = json.loads(hover_samples_path.read_text(encoding="utf-8"))
    assert hover_samples["sessions"] and all(s["frames"] for s in hover_samples["sessions"])
    sampled = {s["name"]: s["frames"] for s in hover_samples["sessions"]}
    switch_motion = {}
    for clip in ("attack", "cast"):
        sequence = sampled[f"hover_native_switch_{clip}_before"][-1:] + sampled[f"hover_native_switch_{clip}_after"]
        target = sampled[f"reference_{clip}"][0]
        pairs = list(zip(sequence, sequence[1:]))
        reverse = max(0, max(hover_distance(b, target) - hover_distance(a, target) for a, b in pairs))
        assert reverse < 2, f"Native {clip} switch briefly jumps away from its held pose"
        switch_motion[clip] = {"maxAdjacentStepPixels": max(hover_distance(a, b) for a, b in pairs),
                               "maxReversePixels": reverse}
    assert hover and all(r["passed"] for r in hover), "Hover entry suite failed"
    for check in ("hover_entry_samples_live_sword_and_hand_bones",
                  "capture_window_is_fullscreen", "capture_uses_native_screen_resolution",
                  "hover_idle_to_attack_sword_has_multiple_intermediate_poses",
                  "hover_idle_to_attack_hand_has_multiple_intermediate_poses",
                  "hover_idle_to_attack_sword_has_no_entry_jump",
                  "hover_idle_to_attack_hand_has_no_entry_jump",
                  "hover_idle_to_cast_sword_has_multiple_intermediate_poses",
                  "hover_idle_to_cast_hand_has_multiple_intermediate_poses",
                  "hover_idle_to_attack_duplicate_and_same_type_focus_keep_mix",
                  "hover_idle_to_cast_duplicate_and_same_type_focus_keep_mix",
                  "hover_idle_to_attack_native_same_type_transfer_has_no_visible_reset",
                  "hover_idle_to_cast_native_same_type_transfer_has_no_visible_reset",
                  "hover_idle_to_attack_front_guard_stands_upright",
                  "hover_idle_to_attack_front_guard_head_looks_forward",
                  "hover_idle_to_attack_front_guard_both_palms_hold_handle",
                  "hover_idle_to_attack_both_hands_and_blade_above_crown",
                  "hover_charge_both_hands_rise_continuously", "hover_charge_palms_stay_on_handle_during_raise",
                  "hover_charge_gold_has_intermediate_stages", "hover_charge_gold_gathers_without_reset",
                  "hover_charge_gold_follows_live_blade", "hover_cancel_charge_400_clears_gold_and_returns_idle",
                  "hover_cancel_charge_780_clears_gold_and_returns_idle", "card_play_releases_overhead_charge_and_gold",
                  "hover_front_guard_one_visible_native_sword",
                  "hover_front_guard_has_no_cast_effect_attachments",
                  "hover_native_switch_during_attack_mix_has_no_jump",
                  "hover_native_switch_during_cast_mix_has_no_jump",
                  "hover_cast_to_attack_sword_reaches_held_reference",
                  "hover_attack_to_cast_sword_reaches_held_reference",
                  "hover_cancel_attack_restores_idle_during_mix",
                  "hover_cancel_cast_restores_idle_during_mix",
                  "hover_cancel_attack_mix_has_no_jump", "hover_cancel_cast_mix_has_no_jump",
                  "hover_cancel_immediate_rehover_sword_has_multiple_intermediate_poses",
                  "native_strike_damage_preserved"):
        assert any(r["check"] == check for r in hover), "Missing hover transition proof: " + check
    suites.append({"suite": "verification-hover", "testedVersion": hover_metadata["version"],
                   "passed": len(hover), "failed": 0, "checks": hover})
    charge_root = ROOT / "build/verification-charge-preview"
    charge_metadata = json.loads((charge_root / "run-metadata.json").read_text(encoding="utf-8"))
    charge_checks = json.loads((charge_root / "results.json").read_text(encoding="utf-8"))
    charge_samples = json.loads((charge_root / "charge-preview-samples.json").read_text(encoding="utf-8"))
    assert charge_metadata["version"] == version and charge_metadata["assemblySha256"] == sha(ROOT / "build/test-dll/SaberPresentation.dll")
    assert charge_checks and all(c["passed"] for c in charge_checks)
    assert charge_samples["normalParentRestored"] and charge_samples["normalTransformRestored"] and charge_samples["normalSizeRestored"]
    assert len(charge_samples["pixels"]) == 4 and charge_samples["pixels"][-1]["WarmChangedPixels"] > charge_samples["pixels"][0]["WarmChangedPixels"] * 1.5
    suites.append({"suite": "verification-charge-preview", "testedVersion": version,
                   "passed": len(charge_checks), "failed": 0, "checks": charge_checks})
    finisher = json.loads((ROOT / "build/verification-finisher/results.json").read_text(encoding="utf-8"))
    assert finisher and all(r["passed"] for r in finisher)
    for check in ("finisher_test_window_is_fullscreen", "finisher_emitter_uses_live_sword_bone",
                  "finisher_releases_only_one_beam", "finisher_cancel_cleans_stage",
                  "finisher_actor_stays_on_original_battle_mark", "finisher_warm_camelot_is_visibly_revealed",
                  "finisher_beam_is_visible_with_actor_and_target"):
        assert any(r["check"] == check and r["passed"] for r in finisher), "Missing fullscreen finisher proof: " + check
    finisher_metadata_path = ROOT / "build/verification-finisher/run-metadata.json"
    finisher_metadata = json.loads(finisher_metadata_path.read_text(encoding="utf-8")) if finisher_metadata_path.exists() else {}
    suites.append({"suite": "verification-finisher", "testedVersion": finisher_metadata.get("version"),
                   "passed": len(finisher), "failed": 0, "checks": finisher})
    demo = json.loads((ROOT / "build/verification-demo/two-take-evidence.json").read_text(encoding="utf-8"))
    assert demo["take"] == 2 and demo["phase"] == "complete" and demo["finisherVerified"]
    assert demo["maxHandCards"] <= 10 and demo["maxHandHolders"] <= 10
    assert demo["killingBlowsThisTake"] == 1 and demo["targetHit"] and demo["emitterUsesSwordBone"]
    media_qa = json.loads((ROOT / "build/hsr-fullscreen-media-qa.json").read_text(encoding="utf-8"))
    assert media_qa and all(r["blackBarsAbsent"] for r in media_qa)
    from pck_tool import extract
    assert hashlib.sha256(extract(pack, "SaberPresentation/video/excalibur_np.ogv")).hexdigest() == sha(ROOT / "assets/video/excalibur_np.ogv")
    report = {"version": version, "gameVersion": "0.111.0", "nativeModEntries": 1,
              "manifestDependencies": manifest["dependencies"], "releaseContainsTestHarness": False,
              "packageBytes": archive.stat().st_size, "packageSha256": sha(archive),
              "verifiedArtifacts": artifacts, "tests": suites,
              "totalPassed": sum(s["passed"] for s in suites),
              "currentVersionPassed": sum(s["passed"] for s in suites if s["testedVersion"] == version),
              "hoverRunEvidence": hover_metadata,
              "chargePreviewEvidence": {"metadata": charge_metadata,
                                        "previewOnlyScaleMultiplier": charge_samples["previewOnlyScaleMultiplier"],
                                        "normalSizeRestored": charge_samples["normalSizeRestored"],
                                        "normalTransformRestored": charge_samples["normalTransformRestored"],
                                        "renderedGoldPixelMeasurements": charge_samples["pixels"]},
              "authoredGuardAnimation": guard_animation,
              "hoverMotionEvidence": {"sha256": sha(hover_samples_path),
                                      "clock": hover_samples["clock"],
                                      "nativeSwitchMotion": switch_motion,
                                      "sessions": [{"name": s["name"], "frames": len(s["frames"])} for s in hover_samples["sessions"]]},
              "excludedModules": lock["excludedModules"],
              "foreignRuntimeAssemblies": [], "nativeUiPreserved": True,
              "bundledCharacterEffectDependencies": len(lock["characterEffects"]["resources"]),
              "cardArtDefault": "Original game portraits; custom card art paused",
              "energyDisplay": "Native current/max energy, raised above the hand and portrait",
              "hoverBattlePose": "Attacks blend in and raise the two-hand sword from chest to above the actual native crown over 1.10 seconds, then hold. Blade gold progressively gathers from guard to tip, following the actual blade surface. Torso/head stay upright; same-type focus preserves the clock; leave/cancel/skill/play clear gold. Native nine clips and static guard preserved. Skills retain planted guard.",
              "cinematics": "Official Honkai: Star Rail Saber Excalibur close-up with synchronized JP Artoria voice; planted original battle marks, fullscreen cut-in, warm Camelot reveal and sustained live sword-to-target beam. Defeat: official FGO Memorial Movie 9.3-second animated clip",
              "fullscreenCgEvidence": media_qa,
              "demoReplayEvidence": demo,
              "saberLinkedSequence": "Blade-anchored Invisible Air gathers blue-white wind; JP Excalibur call turns environment gold; downstroke emits from live sword bone into actual target with flash/shake; ambient light and actor recover after the beam",
              "backgroundMusic": "EMIYA_UBW Extended instrumental; offline 30-minute loop; Saber runs only; voice duck and CG pause",
              "warnings": ["Original Saber exported resources contain stale UIDs; Godot resolves their included text paths.",
                           "Instrumented game exits report Godot renderer/resource allocations, also present in the baseline."],
              "portraitAnimation": "Compact 24-bone battle-ready portrait, 26.5% smaller than 0.3.4; weighted body/head plus rigid overlapping arm cutouts and continuous hair/cloth idle. Card play retracts the ready portrait in 170 ms; the in-world Saber body and layered VFX perform the attack, matching the CZN presentation structure.",
              "presentationLimits": "Original nine Saber battle Spine clips are choreographed into seven routes. The HD portrait is an original idle cutout rig; it deliberately does not fake a large-portrait sword swing."}
    dest = ROOT / f"dist/verification-{version}.json"
    dest.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (ROOT / f"dist/SaberPresentation-{version}.zip.sha256").write_text(report["packageSha256"] + "  " + archive.name + "\n", encoding="ascii")
    print(f"Release verified: {len(artifacts)} payload files match ZIP and installation; {report['currentVersionPassed']} current-version native checks passed ({report['totalPassed']} across all evidence); no test harness in production DLL")


if __name__ == "__main__":
    main()
