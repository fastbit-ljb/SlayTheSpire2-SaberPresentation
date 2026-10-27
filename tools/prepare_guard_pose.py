"""Append native sword guard/raise clips, preserving original animation bytes.

The imported .spskel is raw Spine 4.2 binary. All original native animation
blocks remain byte-identical; two authored clips are appended. The source
skeleton is SHA-256 locked by assets/poses/ready_guard.json.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from pck_tool import extract, read_pck, write_pck

ANIMATIONS = ["attack", "attackvfff", "attack_heavy", "cast", "die", "hurt",
              "idle_loop", "relaxed_loop", "relaxed_loop2"]
CLONE_NAME = "saber_ready_guard"
RAISE_NAME = "saber_raise_charge"
SWORD_BONE = 38
SWORD_SETUP_ROTATION = 79.2010498046875


def varint(value: int) -> bytes:
    out = bytearray()
    while value > 127:
        out.append((value & 127) | 128)
        value >>= 7
    out.append(value)
    return bytes(out)


def encoded_string(value: str) -> bytes:
    data = value.encode("utf-8")
    return varint(len(data) + 1) + data


class Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.pos = 0

    def u8(self) -> int:
        value = self.data[self.pos]
        self.pos += 1
        return value

    def vi(self) -> int:
        value = shift = 0
        while True:
            byte = self.u8()
            value |= (byte & 127) << shift
            if byte < 128:
                return value
            shift += 7

    def skip(self, count: int) -> None:
        self.pos += count
        assert self.pos <= len(self.data), "Animation read exceeded block"

    def string(self) -> str | None:
        length = self.vi()
        if length == 0:
            return None
        length -= 1
        result = self.data[self.pos:self.pos + length].decode("utf-8")
        self.pos += length
        return result

    def f32(self) -> float:
        value = struct.unpack_from(">f", self.data, self.pos)[0]
        self.skip(4)
        return value


def read_setup_bones(data: bytes, with_slots=False, with_slot_end=False):
    reader = Reader(data)
    reader.skip(8)  # Hash.
    assert reader.string().startswith("4.2.")
    reader.skip(20)  # Bounds and reference scale.
    nonessential = reader.u8()
    if nonessential:
        reader.skip(4)
        reader.string()
        reader.string()
    strings = [reader.string() for _ in range(reader.vi())]
    bones = []
    for index in range(reader.vi()):
        bone = {"index": index, "name": reader.string(),
                "parent": reader.vi() if index else -1}
        for field in ("rotation", "x", "y", "scaleX", "scaleY", "shearX", "shearY", "length"):
            bone[field] = reader.f32()
        bone["inherit"] = reader.vi()
        reader.u8()  # Skin required.
        if nonessential:
            reader.skip(4)
            reader.string()
            reader.u8()
        bones.append(bone)
    if not with_slots:
        return bones
    slots = []
    for index in range(reader.vi()):
        name, bone = reader.string(), reader.vi()
        reader.skip(8)  # Light and dark colors.
        attachment = reader.vi()
        reader.vi()  # Blend mode.
        if nonessential:
            reader.u8()
        slots.append({"index": index, "name": name, "bone": bone,
                      "boneName": bones[bone]["name"], "setupAttachmentIndex": attachment})
    if with_slot_end:
        return bones, slots, strings, reader.pos, nonessential
    return bones, slots, strings


def read_cast_bone_tracks(cast: bytes, with_sections=False):
    reader = Reader(cast)
    assert reader.string() == "cast"
    name_end = reader.pos
    timeline_capacity = reader.vi()
    duration = 0
    attachment_tracks = []
    slot_timeline_count = 0

    def curve(frame_count, value_count, byte_values=False):
        frames = []
        time = reader.f32()
        values = [(reader.u8() / 255 if byte_values else reader.f32()) for _ in range(value_count)]
        frames.append({"time": time, "values": values})
        for _ in range(frame_count - 1):
            time = reader.f32()
            values = [(reader.u8() / 255 if byte_values else reader.f32()) for _ in range(value_count)]
            kind = reader.u8()
            frames[-1]["curve"] = kind
            if kind == 2:
                frames[-1]["bezier"] = [[reader.f32() for _ in range(4)] for _ in range(value_count)]
            frames.append({"time": time, "values": values})
        return frames

    for _ in range(reader.vi()):
        slot = reader.vi()
        for _ in range(reader.vi()):
            slot_timeline_count += 1
            kind, frame_count = reader.u8(), reader.vi()
            if kind == 0:
                for _ in range(frame_count):
                    time = reader.f32()
                    duration = max(duration, time)
                    attachment_tracks.append({"slot": slot, "time": time,
                                              "attachmentIndex": reader.vi()})
            else:
                reader.vi()
                frames = curve(frame_count, {1: 4, 2: 3, 3: 7, 4: 6, 5: 1}[kind], True)
                duration = max(duration, frames[-1]["time"])
    bone_start = reader.pos
    tracks = []
    bone_timeline_count = 0
    for _ in range(reader.vi()):
        bone = reader.vi()
        for _ in range(reader.vi()):
            bone_timeline_count += 1
            kind, frame_count = reader.u8(), reader.vi()
            if kind == 10:
                for _ in range(frame_count):
                    duration = max(duration, reader.f32())
                    reader.u8()
                continue
            reader.vi()
            frames = curve(frame_count, 2 if kind in (1, 4, 7) else 1)
            duration = max(duration, frames[-1]["time"])
            tracks.append({"bone": bone, "type": kind, "frames": frames})
    result = (tracks, duration, attachment_tracks)
    if with_sections:
        return (*result, {"nameEnd": name_end, "boneStart": bone_start,
                          "boneEnd": reader.pos, "timelineCapacity": timeline_capacity,
                          "slotTimelineCount": slot_timeline_count,
                          "boneTimelineCount": bone_timeline_count})
    return result


def interpolate(frames, time):
    first = frames[0]
    if time < first["time"]:
        return None
    for second in frames[1:]:
        if time <= second["time"]:
            if first.get("curve", 0) == 1:
                return first["values"]
            fraction = (time - first["time"]) / (second["time"] - first["time"])
            result = []
            for index, (value1, value2) in enumerate(zip(first["values"], second["values"])):
                if first.get("curve", 0) != 2:
                    result.append(value1 + (value2 - value1) * fraction)
                    continue
                cx1, cy1, cx2, cy2 = first["bezier"][index]
                low, high = 0.0, 1.0
                for _ in range(32):
                    u = (low + high) / 2
                    v = 1 - u
                    x = v**3 * first["time"] + 3*v*v*u*cx1 + 3*v*u*u*cx2 + u**3*second["time"]
                    if x < time:
                        low = u
                    else:
                        high = u
                u = (low + high) / 2
                v = 1 - u
                result.append(v**3*value1 + 3*v*v*u*cy1 + 3*v*u*u*cy2 + u**3*value2)
            return result
        first = second
    return first["values"]


def evaluate_pose(setup_bones, tracks, time):
    bones = copy.deepcopy(setup_bones)
    properties = {0: ["rotation"], 1: ["x", "y"], 2: ["x"], 3: ["y"],
                  4: ["scaleX", "scaleY"], 5: ["scaleX"], 6: ["scaleY"],
                  7: ["shearX", "shearY"], 8: ["shearX"], 9: ["shearY"]}
    for track in tracks:
        values = interpolate(track["frames"], time)
        if values is None:
            continue
        bone = bones[track["bone"]]
        for field, value in zip(properties[track["type"]], values):
            bone[field] = bone[field] * value if field.startswith("scale") else bone[field] + value
    return update_world_transforms(bones)


def update_world_transforms(bones):
    """Recompute normal-inheritance Spine matrices after offline local edits."""
    for bone in bones:
        assert bone["inherit"] == 0, "Offline evaluator expects normal bone inheritance"
        rotation_x = math.radians(bone["rotation"] + bone["shearX"])
        rotation_y = math.radians(bone["rotation"] + 90 + bone["shearY"])
        a, c = math.cos(rotation_x) * bone["scaleX"], math.sin(rotation_x) * bone["scaleX"]
        b, d = math.cos(rotation_y) * bone["scaleY"], math.sin(rotation_y) * bone["scaleY"]
        if bone["parent"] < 0:
            bone.update(wx=bone["x"], wy=bone["y"], a=a, b=b, c=c, d=d)
        else:
            parent = bones[bone["parent"]]
            x, y = bone["x"], bone["y"]
            bone.update(wx=parent["a"]*x + parent["b"]*y + parent["wx"],
                        wy=parent["c"]*x + parent["d"]*y + parent["wy"],
                        a=parent["a"]*a + parent["b"]*c,
                        b=parent["a"]*b + parent["b"]*d,
                        c=parent["c"]*a + parent["d"]*c,
                        d=parent["c"]*b + parent["d"]*d)
    return bones


def point(bone, x=0.0, y=0.0):
    return [bone["wx"] + bone["a"]*x + bone["b"]*y,
            bone["wy"] + bone["c"]*x + bone["d"]*y]


def phase_target(data, cast, phase, anchor_x, sword_bone=None, rotation_override=None):
    setup_bones = read_setup_bones(data)
    tracks, duration, attachments = read_cast_bone_tracks(cast)
    bones = evaluate_pose(setup_bones, tracks, phase * duration)
    if sword_bone is None:
        spare_slot_visible = False
        for attachment in attachments:
            if attachment["slot"] == 9 and attachment["time"] <= phase * duration:
                spare_slot_visible = attachment["attachmentIndex"] != 0
        sword_bone = 111 if spare_slot_visible else SWORD_BONE
    sword = bones[sword_bone]
    parent = bones[sword["parent"]]
    a, b, c, d = (parent[key] for key in ("a", "b", "c", "d"))
    determinant = a*d - b*c
    rotation = math.degrees(math.atan2(a / determinant, -b / determinant))
    upright_rotation = rotation
    if rotation_override is not None:
        rotation = rotation_override
    translation = None
    if anchor_x is not None:
        old_rotation = math.radians(sword["rotation"] + sword["shearX"])
        new_rotation = math.radians(rotation)
        translation = [
            sword["x"] + anchor_x * (math.cos(old_rotation) * sword["scaleX"] - math.cos(new_rotation)),
            sword["y"] + anchor_x * (math.sin(old_rotation) * sword["scaleX"] - math.sin(new_rotation)),
        ]
    left_palm = point(bones[37], 8.82, 1.72)
    right_palm = point(bones[172], 10.39, -3.41)
    report = {"phase": phase, "seconds": phase * duration, "duration": duration,
              "swordBone": sword_bone, "swordBoneName": sword["name"],
              "computedSwordLocalRotation": rotation, "anchorX": anchor_x,
              "uprightSwordLocalRotation": upright_rotation,
              "targetSwordLocalXY": translation,
              "originalSwordLocalXY": [sword["x"], sword["y"]],
              "originalSwordLocalRotation": sword["rotation"],
              "originalSwordGrip15World": point(sword, 15, 0),
              "leftHandBoneWorld": point(bones[37]), "rightHandBoneWorld": point(bones[172]),
              "leftPalmApproxWorld": left_palm, "rightPalmApproxWorld": right_palm,
              "approxPalmDistance": math.dist(left_palm, right_palm),
              "poseNote": "Analytic cubic evaluation; excludes mesh deform and Spine's sampled-Bezier approximation."}
    return rotation, translation, report, sword_bone


def find_blocks(data: bytes):
    starts = []
    for name in ANIMATIONS:
        token = encoded_string(name)
        index = data.find(token)
        assert index >= 0, f"Missing animation {name}"
        assert data.find(token, index + 1) < 0, f"Ambiguous animation {name}"
        starts.append(index)
    assert starts == sorted(starts), "Unexpected animation order"
    count_pos = starts[0] - 1
    reader = Reader(data)
    reader.pos = count_pos
    assert reader.vi() == len(ANIMATIONS), "Unexpected native animation count"
    assert reader.pos == starts[0], "Expected one-byte animation count"
    return count_pos, {name: (start, end) for name, start, end in zip(
        ANIMATIONS, starts, starts[1:] + [len(data)])}


def patch_cast(cast: bytes, rotation: float, translation=None, sword_bone=SWORD_BONE,
               setup_sword=None):
    clone = bytearray(cast)
    reader = Reader(cast)
    assert reader.string() == "cast"
    original_name_end = reader.pos
    reader.vi()  # Total timeline count; no timelines are added or removed.
    patched = []
    timeline_summary = []

    def patch_float(value: float, kind: str):
        pos = reader.pos
        old = struct.unpack_from(">f", cast, pos)[0]
        struct.pack_into(">f", clone, pos, value)
        reader.skip(4)
        patched.append({"castOffset": pos, "kind": kind, "before": old,
                        "after": value})

    def curve_timeline(frame_count: int, value_count: int, byte_values: bool,
                       targets=None, kind=""):
        reader.skip(4)  # First frame time.
        for value in range(value_count):
            if targets is None:
                reader.skip(1 if byte_values else 4)
            else:
                patch_float(targets[value], kind + ".value")
        for _ in range(frame_count - 1):
            reader.skip(4)  # Next frame time.
            for value in range(value_count):
                if targets is None:
                    reader.skip(1 if byte_values else 4)
                else:
                    patch_float(targets[value], kind + ".value")
            curve = reader.u8()
            assert curve in (0, 1, 2)
            if curve == 2:
                for value in range(value_count):
                    reader.skip(4)  # cx1: time coordinate.
                    if targets is None:
                        reader.skip(4)
                    else:
                        patch_float(targets[value], kind + ".bezierY")
                    reader.skip(4)  # cx2: time coordinate.
                    if targets is None:
                        reader.skip(4)
                    else:
                        patch_float(targets[value], kind + ".bezierY")

    # Slot timelines are kept byte-for-byte.
    for _ in range(reader.vi()):
        reader.vi()  # Slot index.
        for _ in range(reader.vi()):
            timeline_type, frame_count = reader.u8(), reader.vi()
            if timeline_type == 0:
                for _ in range(frame_count):
                    reader.skip(4)
                    reader.vi()  # Attachment string ref.
            else:
                reader.vi()  # Bezier count.
                curve_timeline(frame_count, {1: 4, 2: 3, 3: 7, 4: 6, 5: 1}[
                    timeline_type], True)

    # Replace sword rotation, scale and shear values, including every Bezier Y.
    # Translation and all other bone tracks retain the original cast data.
    targets_by_type = {
        0: [rotation - (setup_sword["rotation"] if setup_sword else SWORD_SETUP_ROTATION)],
        4: [1.0, 1.0], 5: [1.0], 6: [1.0],
        7: [0.0, 0.0], 8: [0.0], 9: [0.0],
    }
    if translation is not None:
        targets_by_type[1] = [translation[0] - setup_sword["x"],
                              translation[1] - setup_sword["y"]]
    for _ in range(reader.vi()):
        bone = reader.vi()
        for _ in range(reader.vi()):
            timeline_type, frame_count = reader.u8(), reader.vi()
            if timeline_type == 10:
                reader.skip(frame_count * 5)
                continue
            reader.vi()  # Bezier count.
            targets = targets_by_type.get(timeline_type) if bone == sword_bone else None
            curve_timeline(frame_count, 2 if timeline_type in (1, 4, 7) else 1,
                           False, targets, str(timeline_type))
            if targets is not None:
                timeline_summary.append({"bone": bone, "type": timeline_type,
                                         "frames": frame_count, "values": targets})
    found_types = {t["type"] for t in timeline_summary}
    assert {0, 4} <= found_types
    assert translation is None or 1 in found_types
    assert patched

    # Verify all constraints, deform, draw-order, events and unmodified track
    # bytes remain identical, not merely semantically equivalent.
    allowed = {i for patch in patched for i in range(patch["castOffset"],
                                                    patch["castOffset"] + 4)}
    changed = {i for i, (left, right) in enumerate(zip(cast, clone)) if left != right}
    assert changed <= allowed
    assert len(cast) == len(clone)
    clone = encoded_string(CLONE_NAME) + bytes(clone[original_name_end:])
    return clone, patched, timeline_summary, reader.pos, len(changed)


def override_guard_slots(clone: bytes, slots, strings, config):
    """Use one hand-carried sword and disable every combat effect from t=0."""
    effects = [slot for slot in slots if
               slot["name"].startswith(tuple(config["effectSlotNamePrefixes"])) or
               slot["boneName"].startswith(tuple(config["effectBoneNamePrefixes"]))]
    assert len(effects) == config["expectedEffectSlotCount"], "Source effect-slot inventory changed"
    by_name = {slot["name"]: slot["index"] for slot in slots}
    targets = {slot["index"]: 0 for slot in effects}
    targets[by_name["wuqi2"]] = 0
    targets[by_name["wuqi3"]] = 0
    targets[by_name["nv27"]] = strings.index("nv27") + 1
    reader = Reader(clone)
    assert reader.string() in (CLONE_NAME, RAISE_NAME)
    name_end = reader.pos
    capacity = reader.vi()
    groups, existing_slots = [], set()
    added_tracks = 0
    replaced_tracks = 0

    def null_or_sword_track(slot):
        return bytes([0, 1]) + struct.pack(">f", 0.0) + varint(targets[slot])

    def skip_curve(frame_count, value_count):
        reader.skip(4 + value_count)
        for _ in range(frame_count - 1):
            reader.skip(4 + value_count)
            if reader.u8() == 2:
                reader.skip(value_count * 16)

    for _ in range(reader.vi()):
        slot, timeline_count = reader.vi(), reader.vi()
        existing_slots.add(slot)
        timelines, saw_attachment = [], False
        for _ in range(timeline_count):
            start = reader.pos
            kind, frame_count = reader.u8(), reader.vi()
            if kind == 0:
                saw_attachment = True
                for _ in range(frame_count):
                    reader.skip(4)
                    reader.vi()
                if slot in targets:
                    timelines.append(null_or_sword_track(slot))
                    replaced_tracks += 1
                else:
                    timelines.append(clone[start:reader.pos])
            else:
                reader.vi()
                skip_curve(frame_count, {1: 4, 2: 3, 3: 7, 4: 6, 5: 1}[kind])
                timelines.append(clone[start:reader.pos])
        if slot in targets and not saw_attachment:
            timelines.append(null_or_sword_track(slot))
            added_tracks += 1
        groups.append(varint(slot) + varint(len(timelines)) + b"".join(timelines))
    old_slots_end = reader.pos
    for slot in sorted(targets.keys() - existing_slots):
        groups.append(varint(slot) + varint(1) + null_or_sword_track(slot))
        added_tracks += 1
    result = (clone[:name_end] + varint(capacity + added_tracks) + varint(len(groups))
              + b"".join(groups) + clone[old_slots_end:])
    return result, {"heldSwordSlot": by_name["nv27"],
                    "heldSwordAttachmentIndex": targets[by_name["nv27"]],
                    "effectSlots": effects, "effectSlotCount": len(effects),
                    "attachmentOverridesFromTimeZero": {str(key): value for key, value in sorted(targets.items())},
                    "addedAttachmentTimelineCount": added_tracks,
                    "replacedAttachmentTimelineCount": replaced_tracks,
                    "originalTimelineCapacity": capacity,
                    "outputTimelineCapacity": capacity + added_tracks,
                    "outputSlotGroupCount": len(groups),
                    "sizeDelta": len(result) - len(clone)}


def handheld_target(original, cast, phase, anchor_x, rotation_override, world_offset=(0.0, 0.0)):
    # Obtain the visible sword's intended world origin, including optional grip
    # anchoring, then attach that origin to the animated left hand instead.
    _, visible_xy, visible_report, visible_bone = phase_target(
        original, cast, phase, anchor_x)
    setup = read_setup_bones(original)
    tracks, duration, _ = read_cast_bone_tracks(cast)
    bones = evaluate_pose(setup, tracks, phase * duration)
    source = bones[visible_bone]
    if visible_xy is None:
        target_world = point(source)
    else:
        target_world = point(bones[source["parent"]], *visible_xy)
    target_world = [value + offset for value, offset in zip(target_world, world_offset)]
    sword = bones[SWORD_BONE]
    parent = bones[sword["parent"]]
    a, b, c, d = (parent[key] for key in ("a", "b", "c", "d"))
    determinant = a*d - b*c
    dx, dy = target_world[0] - parent["wx"], target_world[1] - parent["wy"]
    translation = [(d*dx - b*dy) / determinant, (-c*dx + a*dy) / determinant]
    rotation = math.degrees(math.atan2(a / determinant, -b / determinant))
    if rotation_override is not None:
        rotation = rotation_override
    report = dict(visible_report)
    report.update(swordBone=SWORD_BONE, swordBoneName=sword["name"],
                  sourceVisibleSwordBone=visible_bone,
                  targetSwordWorldOrigin=target_world,
                  swordOriginWorldOffset=list(world_offset),
                  computedSwordLocalRotation=rotation,
                  targetSwordLocalXY=translation,
                  handheld=True)
    return rotation, translation, report, SWORD_BONE


CONFIG = ROOT / "assets/poses/ready_guard.json"
REPORT = ROOT / "build/guard-pose-build.json"


def clone_as_cast(clone):
    reader = Reader(clone)
    assert reader.string() in (CLONE_NAME, RAISE_NAME)
    return encoded_string("cast") + clone[reader.pos:]


def animation_as_cast(animation):
    """Rename only the binary string header for the shared timeline reader."""
    reader = Reader(animation)
    reader.string()
    return encoded_string("cast") + animation[reader.pos:]


def solve_palm_target(bones, upper_arm, forearm, hand, palm_local, target):
    """Author two local arm rotations with damped finite-difference IK.

    This is a build-time solver, not a Spine/runtime constraint. Translation,
    scale, shear, and the inherited cast wrist pose remain untouched.
    """
    indices = (upper_arm, forearm)
    original_rotations = [bones[index]["rotation"] for index in indices]
    epsilon = 0.01
    damping = 1e-5
    initial_palm = point(bones[hand], *palm_local)
    for iteration in range(80):
        update_world_transforms(bones)
        palm = point(bones[hand], *palm_local)
        error = [target[axis] - palm[axis] for axis in range(2)]
        distance = math.hypot(*error)
        if distance < 1e-6:
            break
        columns = []
        for index in indices:
            bones[index]["rotation"] += epsilon
            update_world_transforms(bones)
            shifted = point(bones[hand], *palm_local)
            columns.append([(shifted[axis] - palm[axis]) / epsilon for axis in range(2)])
            bones[index]["rotation"] -= epsilon
        a, c = columns[0]
        b, d = columns[1]
        # Solve (J^T J + lambda I) delta = J^T error. A line search
        # keeps the solution close to the authored cast elbow orientation.
        aa, bb, dd = a*a + c*c + damping, a*b + c*d, b*b + d*d + damping
        ex, ey = a*error[0] + c*error[1], b*error[0] + d*error[1]
        determinant = aa*dd - bb*bb
        assert determinant > 0, "Degenerate guard arm solve"
        delta = [(dd*ex - bb*ey) / determinant, (aa*ey - bb*ex) / determinant]
        limit = max(1.0, max(abs(value) for value in delta) / 25.0)
        delta = [value / limit for value in delta]
        before = [bones[index]["rotation"] for index in indices]
        accepted = False
        for step in (1.0, 0.5, 0.25, 0.125, 0.0625):
            for index, rotation, change in zip(indices, before, delta):
                bones[index]["rotation"] = rotation + change * step
            update_world_transforms(bones)
            if math.dist(point(bones[hand], *palm_local), target) < distance:
                accepted = True
                damping = max(1e-8, damping / 2)
                break
        if not accepted:
            for index, rotation in zip(indices, before):
                bones[index]["rotation"] = rotation
            damping *= 10
            assert damping < 1e8, "Guard palm target is unreachable"
    update_world_transforms(bones)
    palm = point(bones[hand], *palm_local)
    error = math.dist(palm, target)
    assert error < 0.001, f"Guard palm solve failed for {bones[hand]['name']}: {error}"
    return {"upperArm": bones[upper_arm]["name"], "forearm": bones[forearm]["name"],
            "hand": bones[hand]["name"], "palmLocal": list(palm_local),
            "originalLocalRotations": original_rotations,
            "authoredLocalRotations": [bones[index]["rotation"] for index in indices],
            "initialPalmWorld": initial_palm, "targetPalmWorld": target,
            "authoredPalmWorld": palm, "palmError": error, "iterations": iteration + 1}


def upright_guard_pose(original, cast, upright, config):
    """Use idle's upright body and deform, with a solved two-hand cast grip."""
    setup = read_setup_bones(original)
    by_name = {bone["name"]: bone["index"] for bone in setup}
    cast_tracks, cast_duration, _ = read_cast_bone_tracks(animation_as_cast(cast))
    upright_tracks, upright_duration, _ = read_cast_bone_tracks(animation_as_cast(upright))
    reference_time = config["uprightReferencePhase"] * upright_duration
    body = evaluate_pose(setup, upright_tracks, reference_time)
    cast_time = config["phase"] * cast_duration
    arms = evaluate_pose(setup, cast_tracks, cast_time)
    bones = copy.deepcopy(body)
    arm_roots = {by_name[name] for name in config["armRootBones"]}
    arm_subtrees = set(arm_roots)
    for bone in setup:
        if bone["parent"] in arm_subtrees:
            arm_subtrees.add(bone["index"])
    for index in arm_subtrees:
        bones[index] = copy.deepcopy(arms[index])
    # Spine uses different scale-sign rules while mixing in versus out. A
    # reflected cast forearm would therefore snap when hover is interrupted.
    # Preserve the upright scale for sign-changing axes before solving the
    # guard, so every guard/idle scale pair has the same handedness.
    scale_replacements = []
    assert config["preserveUprightArmScaleSigns"]
    for index in sorted(arm_subtrees):
        for field in ("scaleX", "scaleY"):
            source, upright_scale = bones[index][field], body[index][field]
            assert source != 0 and upright_scale != 0
            if math.copysign(1, source) != math.copysign(1, upright_scale):
                bones[index][field] = upright_scale
                # A removed X reflection reverses the bone's primary axis.
                # Turn that axis back before IK, keeping the lowered-elbow
                # branch instead of letting the solver cross the upper chest.
                if field == "scaleX":
                    delta = bones[index]["rotation"] + 180 - setup[index]["rotation"]
                    bones[index]["rotation"] = setup[index]["rotation"] + (delta + 180) % 360 - 180
                scale_replacements.append({"bone": bones[index]["name"], "axis": field,
                                           "castValue": source, "uprightValue": upright_scale,
                                           "initialSolveRotation": bones[index]["rotation"]})
    update_world_transforms(bones)
    assert bones[34]["name"] == "Ys_xiongjia2" and bones[124]["name"] == "Ys_face"
    face, chest = point(bones[124]), point(bones[34])
    offset = config.get("swordOriginWorldOffset", [0.0, 0.0])
    origin = [face[0] + offset[0],
              face[1] - config["swordOriginFaceChestRatio"] * math.dist(face, chest) + offset[1]]
    sword = bones[SWORD_BONE]
    parent = bones[sword["parent"]]
    # This source rig uses uniform scale. Preserve its authored size while
    # expressing grip positions on the sword.
    world_scale = math.sqrt(abs(parent["a"]*parent["d"] - parent["b"]*parent["c"]))
    # The two native glove textures are wider than their palm markers. Close
    # markers can satisfy the grip solve while one glove completely masks the
    # other. Keep distinct upper/lower grips within the native hilt (3.5..40).
    grip_separation = config["leftGripSwordX"] - config["rightGripSwordX"]
    assert 3.5 < config["rightGripSwordX"] < config["leftGripSwordX"] < 40.0
    assert grip_separation >= config["minimumGripSeparationSwordUnits"]
    arm_reports = []
    for names, palm_local, grip_x in (
        (("Ys_larm2", "Ys_larm1", "Ys_lhand2"), config["leftPalmLocal"], config["leftGripSwordX"]),
        (("Ys_rarm2", "Ys_rarm1", "Ys_rhand"), config["rightPalmLocal"], config["rightGripSwordX"]),
    ):
        target = [origin[0], origin[1] + grip_x * world_scale]
        arm_reports.append(solve_palm_target(bones, *(by_name[name] for name in names),
                                             palm_local, target))
    parent = bones[sword["parent"]]
    a, b, c, d = (parent[key] for key in ("a", "b", "c", "d"))
    determinant = a*d - b*c
    dx, dy = origin[0] - parent["wx"], origin[1] - parent["wy"]
    sword["x"], sword["y"] = (d*dx - b*dy) / determinant, (-c*dx + a*dy) / determinant
    sword["rotation"] = math.degrees(math.atan2(a / determinant, -b / determinant))
    delta = sword["rotation"] - setup[SWORD_BONE]["rotation"]
    sword["rotation"] = setup[SWORD_BONE]["rotation"] + (delta + 180) % 360 - 180
    sword["scaleX"] = sword["scaleY"] = 1.0
    sword["shearX"] = sword["shearY"] = 0.0
    update_world_transforms(bones)
    assert all(bone[field] * reference[field] > 0
               for bone, reference in zip(bones, body) for field in ("scaleX", "scaleY"))
    # The original upright body and head are reproduced exactly; only the two
    # arm subtrees and hand-carried sword have any authored local changes.
    for index in set(range(len(bones))) - arm_subtrees:
        for field in ("rotation", "x", "y", "scaleX", "scaleY", "shearX", "shearY"):
            assert bones[index][field] == body[index][field]
    axes = {name: {"bone": bones[index]["name"], "worldOrigin": point(bones[index]),
                   "axisDegrees": math.degrees(math.atan2(bones[index]["c"], bones[index]["a"])),
                   "uprightReferenceAxisDegrees": math.degrees(math.atan2(body[index]["c"], body[index]["a"]))}
            for name, index in (("torso", 34), ("head", 124))}
    return bones, {"phase": config["phase"], "seconds": cast_time, "duration": cast_duration,
                   "armSourceAnimation": "cast", "uprightReferenceAnimation": config["uprightReferenceAnimation"],
                   "uprightReferencePhase": config["uprightReferencePhase"], "uprightReferenceSeconds": reference_time,
                   "heldProgress": config["heldProgress"], "swordBone": SWORD_BONE,
                   "swordBoneName": sword["name"], "handheld": True,
                   "targetSwordWorldOrigin": origin, "swordOriginWorldOffset": list(offset),
                   "computedSwordLocalRotation": sword["rotation"], "targetSwordLocalXY": [sword["x"], sword["y"]],
                   "swordWorldScale": world_scale, "bodyAxes": axes, "armSolves": arm_reports,
                   "gripSeparationSwordUnits": grip_separation,
                   "gripSeparationWorld": grip_separation * world_scale,
                   "minimumGripSeparationSwordUnits": config["minimumGripSeparationSwordUnits"],
                   "gripOrderFromCrossguard": ["Ys_lhand2", "Ys_rhand"],
                   "armScaleSignReplacements": scale_replacements,
                   "allScaleSignsMatchUprightReference": True,
                   "armSubtreeBones": [bones[index]["name"] for index in sorted(arm_subtrees)],
                   "poseNote": "Static idle body/head and cast wrist pose; build-time arm IK only. Idle slot/deform retained; foreground sword/hand draw order authored at t0."}


def serialize_static_guard(upright, setup, bones):
    """Replace idle bone tracks, preserving its slot and trailing sections."""
    upright = animation_as_cast(upright)
    _, _, _, sections = read_cast_bone_tracks(upright, with_sections=True)
    groups = []
    for reference, bone in zip(setup, bones):
        timelines = []
        for kind, values in (
            (0, [bone["rotation"] - reference["rotation"]]),
            (1, [bone["x"] - reference["x"], bone["y"] - reference["y"]]),
            (4, [bone["scaleX"] / reference["scaleX"], bone["scaleY"] / reference["scaleY"]]),
            (7, [bone["shearX"] - reference["shearX"], bone["shearY"] - reference["shearY"]]),
        ):
            # A duplicate root rotation establishes >=1 second of duration
            # without moving any authored bone or relying on retained deform.
            frames = 2 if bone["index"] == 0 and kind == 0 else 1
            timeline = bytes([kind]) + varint(frames) + varint(0)
            timeline += struct.pack(">" + "f" * (1 + len(values)), 0.0, *values)
            if frames == 2:
                timeline += struct.pack(">" + "f" * (1 + len(values)), 1.0, *values) + bytes([0])
            timelines.append(timeline)
        groups.append(varint(bone["index"]) + varint(len(timelines)) + b"".join(timelines))
    bone_section = varint(len(groups)) + b"".join(groups)
    old_capacity = sections["timelineCapacity"]
    new_bone_count = len(bones) * 4
    capacity = old_capacity - sections["boneTimelineCount"] + new_bone_count
    header = Reader(upright)
    header.string()
    header.vi()
    clone = (encoded_string(CLONE_NAME) + varint(capacity)
             + upright[header.pos:sections["boneStart"]] + bone_section
             + upright[sections["boneEnd"]:])
    _, _, _, authored_sections = read_cast_bone_tracks(clone_as_cast(clone), with_sections=True)
    assert authored_sections["boneTimelineCount"] == new_bone_count
    assert authored_sections["slotTimelineCount"] == sections["slotTimelineCount"]
    assert clone_as_cast(clone)[authored_sections["boneEnd"]:] == upright[sections["boneEnd"]:]
    return clone, {"uprightBoneTrackStart": sections["boneStart"],
                   "uprightBoneTrackEnd": sections["boneEnd"],
                   "originalTimelineCapacity": old_capacity, "outputTimelineCapacity": capacity,
                   "originalBoneTimelineCount": sections["boneTimelineCount"],
                   "outputBoneTimelineCount": new_bone_count, "outputBoneGroupCount": len(groups),
                   "staticBoneKeyTime": 0.0, "durationKeyTime": 1.0,
                   "retainedIdleTrailingSectionsBytes": len(upright) - sections["boneEnd"],
                   "retainedIdleSlotTimelineCount": sections["slotTimelineCount"]}


def read_idle_tail_sections(animation, bone_end, slot_count):
    """Locate idle's deform and draw-order sections without modifying either.

    The SHA-locked source has no constraint timelines. Attachment deforms and
    sequence tracks are variable-sized, so walk their actual keys rather than
    searching for a byte pattern or assuming a fixed suffix length.
    """
    reader = Reader(animation)
    reader.pos = bone_end
    for kind in ("IK", "transform", "path", "physics"):
        assert reader.vi() == 0, f"Unexpected {kind} timelines in upright reference"
    deform_start = reader.pos
    attachment_timeline_count = 0
    for _ in range(reader.vi()):
        reader.vi()  # Skin index.
        for _ in range(reader.vi()):
            reader.vi()  # Slot index.
            for _ in range(reader.vi()):
                reader.vi()  # Attachment string reference.
                kind, frame_count = reader.u8(), reader.vi()
                attachment_timeline_count += 1
                if kind == 1:
                    reader.skip(frame_count * 12)  # Time, mode/index, delay.
                    continue
                assert kind == 0 and frame_count > 0, "Unexpected attachment timeline"
                reader.vi()  # Bezier count.
                reader.skip(4)  # First key time.
                for frame in range(frame_count):
                    value_count = reader.vi()
                    if value_count:
                        reader.vi()  # First deformed vertex coordinate.
                        reader.skip(value_count * 4)
                    if frame + 1 < frame_count:
                        reader.skip(4)  # Next key time.
                        curve = reader.u8()
                        assert curve in (0, 1, 2)
                        if curve == 2:
                            reader.skip(16)
    draw_start = reader.pos
    draw_keys = []
    for _ in range(reader.vi()):
        time = reader.f32()
        offsets = []
        for _ in range(reader.vi()):
            slot, offset = reader.vi(), reader.vi()
            if offset >= 1 << 31:
                offset -= 1 << 32
            offsets.append((slot, offset))
        order = [None] * slot_count
        selected = set()
        for slot, offset in offsets:
            assert slot not in selected and 0 <= slot + offset < slot_count
            assert order[slot + offset] is None
            selected.add(slot)
            order[slot + offset] = slot
        unchanged = iter(index for index in range(slot_count) if index not in selected)
        order = [next(unchanged) if index is None else index for index in order]
        assert sorted(order) == list(range(slot_count))
        draw_keys.append({"time": time, "offsets": offsets, "order": order})
    draw_end = reader.pos
    assert reader.vi() == 0, "Unexpected events in upright reference"
    assert reader.pos == len(animation), "Unparsed upright animation suffix"
    return {"deformStart": deform_start, "drawStart": draw_start, "drawEnd": draw_end,
            "drawKeys": draw_keys, "attachmentTimelineCount": attachment_timeline_count}


def override_guard_draw_order(clone, slots, config):
    """Foreground the grip/forearms while retaining upper arms behind the head."""
    _, _, _, sections = read_cast_bone_tracks(clone_as_cast(clone), with_sections=True)
    # Renaming changes the header size, so obtain the native clone bone offset.
    header = Reader(clone)
    animation_name = header.string()
    assert animation_name in (CLONE_NAME, RAISE_NAME)
    name_delta = len(encoded_string(animation_name)) - len(encoded_string("cast"))
    tail = read_idle_tail_sections(clone, sections["boneEnd"] + name_delta, len(slots))
    by_name = {slot["name"]: slot["index"] for slot in slots}
    arm_names = config["raiseAnimation"]["foregroundArmSlots"] if animation_name == RAISE_NAME else []
    foreground_names = [*arm_names, config["foregroundSwordSlot"], *config["foregroundGripSlots"]]
    foreground = [by_name[name] for name in foreground_names]
    assert len(foreground) == len(set(foreground))
    desired = [index for index in range(len(slots)) if index not in foreground] + foreground
    if arm_names:
        arm_slots = [slot for slot in slots if slot["name"] in arm_names]
        assert len(arm_slots) == 2
        assert {slot["boneName"] for slot in arm_slots} == {"Ys_larm1", "Ys_rarm1"}
        head_positions = [desired.index(by_name[name]) for name in config["raiseAnimation"]["visibleHeadSlots"]]
        sword_position = desired.index(by_name[config["foregroundSwordSlot"]])
        assert all(max(head_positions) < desired.index(slot["index"]) < sword_position for slot in arm_slots)
        upper_arm_names = config["raiseAnimation"]["backgroundUpperArmSlots"]
        upper_arm_slots = [slot for slot in slots if slot["name"] in upper_arm_names]
        assert len(upper_arm_slots) == 2
        assert {slot["boneName"] for slot in upper_arm_slots} == {"Ys_larm2", "Ys_rarm2"}
        assert all(desired.index(slot["index"]) < min(head_positions) for slot in upper_arm_slots)
    destinations = {slot: desired.index(slot) for slot in foreground}
    offsets = [(slot, destinations[slot] - slot) for slot in sorted(foreground)]
    assert all(offset >= 0 for _, offset in offsets), "Foreground slots must move forward"
    draw_section = varint(1) + struct.pack(">f", 0.0) + varint(len(offsets))
    draw_section += b"".join(varint(slot) + varint(offset) for slot, offset in offsets)
    header = Reader(clone)
    assert header.string() == animation_name
    name_end = header.pos
    capacity = header.vi()
    new_capacity = capacity + (0 if tail["drawKeys"] else 1)
    result = (clone[:name_end] + varint(new_capacity) + clone[header.pos:tail["drawStart"]]
              + draw_section + clone[tail["drawEnd"]:])
    _, _, _, authored = read_cast_bone_tracks(clone_as_cast(result), with_sections=True)
    checked = read_idle_tail_sections(result, authored["boneEnd"] + name_delta, len(slots))
    assert len(checked["drawKeys"]) == 1 and checked["drawKeys"][0]["time"] == 0.0
    assert checked["drawKeys"][0]["order"] == desired
    original_deform = clone[tail["deformStart"]:tail["drawStart"]]
    assert result[checked["deformStart"]:checked["drawStart"]] == original_deform
    assert new_capacity == (authored["slotTimelineCount"] + authored["boneTimelineCount"]
                            + checked["attachmentTimelineCount"] + 1)
    return result, {"time": 0.0, "foregroundSlotNames": foreground_names,
                    "foregroundArmSlotNames": arm_names,
                    "forearmsDrawAboveHeadBeforeSword": bool(arm_names),
                    "backgroundUpperArmSlotNames": upper_arm_names if arm_names else [],
                    "upperArmsDrawBehindHead": bool(arm_names),
                    "foregroundSlotIndices": foreground, "offsets": offsets,
                    "outputDrawOrder": desired, "originalDrawKeyCount": len(tail["drawKeys"]),
                    "outputDrawKeyCount": 1, "outputTimelineCapacity": new_capacity,
                    "retainedIdleDeformBytes": len(original_deform),
                    "retainedIdleDeformSha256": hashlib.sha256(original_deform).hexdigest(),
                    "retainedIdleDeformByteIdentical": True}


def place_upright_sword(bones, origin, previous_rotation):
    """Express a world-vertical sword in its moving wrist's local space."""
    sword = bones[SWORD_BONE]
    parent = bones[sword["parent"]]
    a, b, c, d = (parent[key] for key in ("a", "b", "c", "d"))
    determinant = a*d - b*c
    assert abs(determinant) > 1e-6
    dx, dy = origin[0] - parent["wx"], origin[1] - parent["wy"]
    sword["x"], sword["y"] = (d*dx - b*dy) / determinant, (-c*dx + a*dy) / determinant
    rotation = math.degrees(math.atan2(a / determinant, -b / determinant))
    sword["rotation"] = previous_rotation + (rotation - previous_rotation + 180) % 360 - 180
    sword["scaleX"] = sword["scaleY"] = 1.0
    sword["shearX"] = sword["shearY"] = 0.0
    update_world_transforms(bones)


def native_visible_head_envelope(original, bones, head_slots):
    """Read the visible native head meshes, rather than guessing a bone top."""
    _, slots, strings, slot_end, nonessential = read_setup_bones(original, True, True)
    reader = Reader(original)
    reader.pos = slot_end
    assert reader.vi() == 0, "Unexpected source IK constraints"
    for _ in range(reader.vi()):
        reader.string()
        reader.vi()
        for _ in range(reader.vi()):
            reader.vi()
        reader.vi()
        flags = reader.u8()
        for mask in (8, 16, 32, 64, 128):
            if flags & mask:
                reader.skip(4)
        flags = reader.u8()
        for mask in (1, 2, 4, 8, 16, 32, 64):
            if flags & mask:
                reader.skip(4)
    assert reader.vi() == reader.vi() == 0, "Unexpected source path/physics constraints"

    def string_ref():
        index = reader.vi()
        return strings[index - 1] if index else None

    def vertices(weighted):
        count, result = reader.vi(), []
        if not weighted:
            return count, [[reader.f32(), reader.f32()] for _ in range(count)]
        for _ in range(count):
            result.append([[reader.vi(), reader.f32(), reader.f32(), reader.f32()]
                           for _ in range(reader.vi())])
        return count, result

    envelopes = []
    for _ in range(reader.vi()):  # Default skin slot groups.
        slot = slots[reader.vi()]
        for _ in range(reader.vi()):
            attachment, flags = string_ref(), reader.u8()
            name = string_ref() if flags & 8 else attachment
            kind, mesh_vertices, weighted = flags & 7, None, False
            if kind in (0, 2, 3):
                path = string_ref() if flags & 16 else name
                if flags & 32:
                    reader.skip(4)
                if flags & 64:
                    for _ in range(4):
                        reader.vi()
            if kind == 0:
                reader.skip(4 if flags & 128 else 0)
                reader.skip(24)
            elif kind in (1, 6):
                if kind == 6:
                    reader.vi()
                vertices(flags & 16)
                reader.skip(4 if nonessential else 0)
            elif kind == 2:
                hull = reader.vi()
                weighted = bool(flags & 128)
                count, mesh_vertices = vertices(weighted)
                reader.skip(count * 8)  # Mesh UVs.
                for _ in range((count * 2 - hull - 2) * 3):
                    reader.vi()
                if nonessential:
                    for _ in range(reader.vi()):
                        reader.vi()
                    reader.skip(8)
            elif kind == 3:
                reader.vi()
                string_ref()
                reader.skip(8 if nonessential else 0)
            elif kind == 4:
                count, _ = vertices(flags & 64)
                reader.skip((count * 2 // 6) * 4 + (4 if nonessential else 0))
            elif kind == 5:
                reader.skip(12 + (4 if nonessential else 0))
            else:
                raise AssertionError(f"Unknown native attachment type {kind}")
            setup_attachment = strings[slot["setupAttachmentIndex"] - 1] if slot["setupAttachmentIndex"] else None
            if slot["name"] not in head_slots or attachment != setup_attachment:
                continue
            assert kind == 2 and mesh_vertices, "Visible head geometry must be a native mesh"
            world_vertices = []
            for vertex in mesh_vertices:
                if weighted:
                    world_vertices.append([sum(point(bones[index], x, y)[axis] * weight
                                               for index, x, y, weight in vertex) for axis in range(2)])
                else:
                    world_vertices.append(point(bones[slot["bone"]], *vertex))
            top_index = max(range(len(world_vertices)), key=lambda index: world_vertices[index][1])
            envelopes.append({"slot": slot["name"], "attachment": attachment, "meshPath": path,
                              "weighted": weighted, "vertexCount": len(world_vertices),
                              "topVertexIndex": top_index, "topVertexWorld": world_vertices[top_index],
                              "sourceTopVertex": mesh_vertices[top_index]})
    assert {envelope["slot"] for envelope in envelopes} == set(head_slots)
    highest = max(envelopes, key=lambda envelope: envelope["topVertexWorld"][1])
    face, chest = point(bones[124]), point(bones[34])
    return {"source": "SHA-locked native default-skin mesh vertices transformed by idle t=0 bones",
            "headTopWorldY": highest["topVertexWorld"][1], "headTopSlot": highest["slot"],
            "headTopAttachment": highest["attachment"], "headTopVertexIndex": highest["topVertexIndex"],
            "headTopOffsetFromFaceRatio": (highest["topVertexWorld"][1] - face[1]) / math.dist(face, chest),
            "nativeHeadMeshes": envelopes}


def build_raise_poses(original, setup, guard_bones, guard_pose, config):
    """Solve a continuous chest-to-overhead motion without runtime IK."""
    raise_config = config["raiseAnimation"]
    assert raise_config["animation"] == RAISE_NAME
    raise_seconds, duration = raise_config["raiseSeconds"], raise_config["duration"]
    frame_count = raise_config["motionFrames"]
    assert 24 <= frame_count <= 40 and 0 < raise_seconds < duration
    face, chest = point(guard_bones[124]), point(guard_bones[34])
    body_span = math.dist(face, chest)
    start_origin = point(guard_bones[SWORD_BONE])
    end_origin = [face[0] + raise_config["endSwordRightWorldOffset"],
                  face[1] + raise_config["overheadOriginFaceChestRatio"] * body_span]
    assert end_origin[1] > start_origin[1]
    by_name = {bone["name"]: bone["index"] for bone in setup}
    arm_groups = [(("Ys_larm2", "Ys_larm1", "Ys_lhand2"), config["leftPalmLocal"], config["leftGripSwordX"]),
                  (("Ys_rarm2", "Ys_rarm1", "Ys_rhand"), config["rightPalmLocal"], config["rightGripSwordX"])]
    previous = copy.deepcopy(guard_bones)
    frames, reports = [], []
    scale = guard_pose["swordWorldScale"]
    shoulder_lift = raise_config["shoulderLiftFaceChestRatio"] * body_span
    head_envelope = native_visible_head_envelope(original, guard_bones, raise_config["visibleHeadSlots"])
    for index in range(frame_count):
        key_fraction = index / (frame_count - 1)
        # Spend more keys where the hands travel quickly. Uniform time keys
        # waste samples at the eased ends and allow the two palms to separate
        # during the middle of the lift.
        sampling_weight = raise_config["progressSamplingWeight"]
        assert 0.0 <= sampling_weight < 1.0
        low, high = 0.0, 1.0
        for _ in range(40):
            fraction = (low + high) / 2
            eased = fraction**3 * (10 + fraction * (-15 + 6*fraction))
            metric = (1 - sampling_weight) * fraction + sampling_weight * eased
            if metric < key_fraction:
                low = fraction
            else:
                high = fraction
        fraction = key_fraction if index in (0, frame_count - 1) else (low + high) / 2
        # Quintic easing has zero speed and acceleration at both ends. The
        # sampled linear Spine keys approximate it without a runtime tween.
        eased = fraction**3 * (10 + fraction * (-15 + 6*fraction))
        # Carry the blade sideways mainly after it clears the chest. Moving
        # sideways too early sends the upper grip through the left shoulder's
        # pivot, forcing a sharp folded-elbow turn during interpolation.
        x_progress = eased ** raise_config["rightwardProgressExponent"]
        origin = [start_origin[0] + (end_origin[0] - start_origin[0]) * x_progress,
                  start_origin[1] + (end_origin[1] - start_origin[1]) * eased]
        bones = copy.deepcopy(previous)
        arm_reports = []
        if index:
            for root_name in config["armRootBones"]:
                root_index = by_name[root_name]
                bone, reference = bones[root_index], guard_bones[root_index]
                parent = bones[bone["parent"]]
                determinant = parent["a"]*parent["d"] - parent["b"]*parent["c"]
                world_lift = shoulder_lift * eased
                bone["x"] = reference["x"] - parent["b"] * world_lift / determinant
                bone["y"] = reference["y"] + parent["a"] * world_lift / determinant
            update_world_transforms(bones)
            for names, palm_local, grip_x in arm_groups:
                target = [origin[0], origin[1] + grip_x * scale]
                arm_reports.append(solve_palm_target(bones, *(by_name[name] for name in names), palm_local, target))
            for bone, old in zip(bones, previous):
                bone["rotation"] = old["rotation"] + (bone["rotation"] - old["rotation"] + 180) % 360 - 180
            update_world_transforms(bones)
            place_upright_sword(bones, origin, previous[SWORD_BONE]["rotation"])
        else:
            assert math.dist(origin, start_origin) < 1e-9
        for bone, reference in zip(bones, guard_bones):
            for field in ("scaleX", "scaleY"):
                assert bone[field] == reference[field] and bone[field] != 0
            assert abs(bone["rotation"] - previous[bone["index"]]["rotation"]) < 30.0
        for body_index in (34, 124):
            assert math.dist(point(bones[body_index]), point(guard_bones[body_index])) < 1e-9
            assert abs(bones[body_index]["a"] - guard_bones[body_index]["a"]) < 1e-9
            assert abs(bones[body_index]["c"] - guard_bones[body_index]["c"]) < 1e-9
        frames.append({"time": raise_seconds * fraction, "bones": bones})
        reports.append({"time": raise_seconds * fraction, "easedProgress": eased,
                        "targetSwordWorldOrigin": origin, "armSolves": arm_reports})
        previous = bones
    frames.append({"time": duration, "bones": copy.deepcopy(previous)})
    assert all(point(previous[by_name[names[2]]], *palm)[1] > face[1]
               for names, palm, _ in arm_groups)
    return frames, {"animation": RAISE_NAME, "raiseSeconds": raise_seconds,
                    "duration": duration, "heldTime": raise_seconds,
                    "motionFrames": frame_count, "frames": len(frames),
                    "startSwordWorldOrigin": start_origin, "endSwordWorldOrigin": end_origin,
                    "bodySpan": body_span, "sourceGuardAnimation": CLONE_NAME,
                    "shoulderLiftWorld": shoulder_lift,
                    "shoulderLiftFaceChestRatio": raise_config["shoulderLiftFaceChestRatio"],
                    **head_envelope,
                    "easing": "quintic smoothstep sampled with linear Spine keys",
                    "progressSamplingWeight": raise_config["progressSamplingWeight"],
                    "swordOriginWorldOffset": config["swordOriginWorldOffset"],
                    "endSwordRightWorldOffset": raise_config["endSwordRightWorldOffset"],
                    "rightwardProgressExponent": raise_config["rightwardProgressExponent"],
                    "overheadOriginFaceChestRatio": raise_config["overheadOriginFaceChestRatio"],
                    "frameSolves": reports}


def frozen_idle_tail(animation, bone_end):
    """Freeze native idle mesh deforms at t=0 for the new raise clip only.

    Keeping idle's later deform keys would silently extend the clip beyond its
    explicit 1.30 s hold. The static guard keeps its original idle tail bytes.
    """
    reader = Reader(animation)
    reader.pos = bone_end
    constraints = bytes([0, 0, 0, 0])
    for kind in ("IK", "transform", "path", "physics"):
        assert reader.vi() == 0, f"Unexpected {kind} timelines in upright reference"
    skins = []
    timeline_count = 0
    for _ in range(reader.vi()):
        skin, slots = reader.vi(), []
        for _ in range(reader.vi()):
            slot, attachments = reader.vi(), []
            for _ in range(reader.vi()):
                attachment, kind, frame_count = reader.vi(), reader.u8(), reader.vi()
                assert frame_count > 0
                if kind == 1:
                    first = reader.data[reader.pos:reader.pos + 12]
                    first_time = struct.unpack_from(">f", first)[0]
                    assert first_time >= 0.0
                    reader.skip(frame_count * 12)
                    track = bytes([kind]) + varint(1) + first if first_time == 0.0 else None
                else:
                    assert kind == 0
                    reader.vi()  # Bezier count.
                    first_time = reader.f32()
                    assert first_time >= 0.0
                    first_payload = None
                    for frame in range(frame_count):
                        start = reader.pos
                        count = reader.vi()
                        if count:
                            reader.vi()
                            reader.skip(count * 4)
                        if first_payload is None:
                            first_payload = reader.data[start:reader.pos]
                        if frame + 1 < frame_count:
                            reader.skip(4)
                            curve = reader.u8()
                            assert curve in (0, 1, 2)
                            if curve == 2:
                                reader.skip(16)
                    track = (bytes([kind]) + varint(1) + varint(0) + struct.pack(">f", 0.0) + first_payload
                             if first_time == 0.0 else None)
                if track is not None:
                    timeline_count += 1
                    attachments.append(varint(attachment) + track)
            if attachments:
                slots.append(varint(slot) + varint(len(attachments)) + b"".join(attachments))
        if slots:
            skins.append(varint(skin) + varint(len(slots)) + b"".join(slots))
    # The upright reference has no native draw order or events. The authored
    # foreground hand/sword draw order is appended by the shared helper.
    assert reader.vi() == reader.vi() == 0
    assert reader.pos == len(animation)
    return constraints + varint(len(skins)) + b"".join(skins) + bytes([0, 0]), timeline_count


def serialize_raise_animation(upright, setup, frames):
    """Serialize authored local transforms; every animated key is linear."""
    upright = animation_as_cast(upright)
    _, _, _, sections = read_cast_bone_tracks(upright, with_sections=True)
    header = Reader(upright)
    header.string()
    header.vi()
    slots = upright[header.pos:sections["boneStart"]]
    # This source's idle attachment keys are all at zero. Assert that property
    # instead of inheriting a late blink or combat attachment switch.
    _, _, attachments = read_cast_bone_tracks(upright)
    assert all(key["time"] == 0.0 for key in attachments)
    groups, moving_timelines = [], []
    for reference in setup:
        bone_index, timelines = reference["index"], []
        for kind, fields in ((0, ("rotation",)), (1, ("x", "y")),
                             (4, ("scaleX", "scaleY")), (7, ("shearX", "shearY"))):
            values = []
            for frame in frames:
                bone = frame["bones"][bone_index]
                values.append([bone[field] / reference[field] if field.startswith("scale")
                               else bone[field] - reference[field] for field in fields])
            moving = any(values[index] != values[0] for index in range(1, len(values)))
            keys = list(zip(frames, values)) if moving else [(frames[0], values[0])]
            timeline = bytes([kind]) + varint(len(keys)) + varint(0)
            for index, (frame, key_values) in enumerate(keys):
                timeline += struct.pack(">" + "f" * (1 + len(key_values)), frame["time"], *key_values)
                if index:
                    timeline += bytes([0])  # Linear curve for preceding key.
            timelines.append(timeline)
            if moving:
                moving_timelines.append({"bone": reference["name"], "type": kind, "keys": len(keys)})
        groups.append(varint(bone_index) + varint(len(timelines)) + b"".join(timelines))
    tail, deform_count = frozen_idle_tail(upright, sections["boneEnd"])
    capacity = sections["slotTimelineCount"] + len(groups) * 4 + deform_count
    clone = (encoded_string(RAISE_NAME) + varint(capacity) + slots + varint(len(groups))
             + b"".join(groups) + tail)
    return clone, {"movingTimelines": moving_timelines, "boneTimelineCount": len(groups) * 4,
                   "frozenIdleMeshDeform": True, "frozenDeformTime": 0.0,
                   "frozenDeformTimelineCount": deform_count, "timelineCapacity": capacity,
                   "motionKeyCount": len(frames) - 1, "holdKeyTime": frames[-1]["time"]}


def validate_raise_animation(clone, setup, guard_bones, config, pose_report):
    """Densely check the serialized motion, including in-between frame grips."""
    tracks, duration, _ = read_cast_bone_tracks(animation_as_cast(clone))
    raise_config = config["raiseAnimation"]
    assert abs(duration - raise_config["duration"]) < 1e-6
    by_name = {bone["name"]: bone["index"] for bone in setup}
    reference_axes = {index: math.degrees(math.atan2(guard_bones[index]["c"], guard_bones[index]["a"]))
                      for index in (34, 124)}
    sample_count = (raise_config["motionFrames"] - 1) * raise_config["validationSubframes"] + 1
    max_palm_error = max_origin_error = max_axis_error = 0.0
    min_determinant = math.inf
    frames = []
    previous_y = -math.inf
    for sample in range(sample_count):
        fraction = sample / (sample_count - 1)
        time = raise_config["raiseSeconds"] * fraction
        bones = evaluate_pose(setup, tracks, time)
        sword = bones[SWORD_BONE]
        origin = point(sword)
        eased = fraction**3 * (10 + fraction * (-15 + 6*fraction))
        progress = (eased ** raise_config["rightwardProgressExponent"], eased)
        expected_origin = [pose_report["startSwordWorldOrigin"][axis] +
                           (pose_report["endSwordWorldOrigin"][axis] - pose_report["startSwordWorldOrigin"][axis]) * progress[axis]
                           for axis in range(2)]
        max_origin_error = max(max_origin_error, math.dist(origin, expected_origin))
        assert origin[1] + 0.001 >= previous_y, "Authored sword reverses during raise"
        previous_y = origin[1]
        axis_error = abs(math.degrees(math.atan2(sword["c"], sword["a"])) - 90.0)
        max_axis_error = max(max_axis_error, axis_error)
        assert axis_error < 0.001
        palm_errors = []
        for hand_name, palm, grip in (("Ys_lhand2", config["leftPalmLocal"], config["leftGripSwordX"]),
                                      ("Ys_rhand", config["rightPalmLocal"], config["rightGripSwordX"])):
            palm_errors.append(math.dist(point(bones[by_name[hand_name]], *palm), point(sword, grip, 0)))
        max_palm_error = max(max_palm_error, *palm_errors)
        for index, axis in reference_axes.items():
            assert math.dist(point(bones[index]), point(guard_bones[index])) < 0.001
            assert abs(math.degrees(math.atan2(bones[index]["c"], bones[index]["a"])) - axis) < 0.001
        for bone, reference in zip(bones, guard_bones):
            determinant = abs(bone["a"]*bone["d"] - bone["b"]*bone["c"])
            min_determinant = min(min_determinant, determinant)
            assert determinant > 1e-6
            assert bone["scaleX"] * reference["scaleX"] > 0 and bone["scaleY"] * reference["scaleY"] > 0
        frames.append({"time": time, "swordOriginWorld": origin, "palmErrors": palm_errors})
    sword_scale = math.sqrt(abs(guard_bones[SWORD_BONE]["a"] * guard_bones[SWORD_BONE]["d"]
                               - guard_bones[SWORD_BONE]["b"] * guard_bones[SWORD_BONE]["c"]))
    palm_error_sword_units = max_palm_error / sword_scale
    assert palm_error_sword_units < raise_config["interpolatedPalmErrorLimitSwordUnits"], palm_error_sword_units
    start = evaluate_pose(setup, tracks, 0.0)
    assert all(math.dist(point(a), point(b)) < 0.001 for a, b in zip(start, guard_bones))
    held = evaluate_pose(setup, tracks, raise_config["raiseSeconds"])
    for time in (raise_config["raiseSeconds"] + 0.05, duration):
        frozen = evaluate_pose(setup, tracks, time)
        assert all(math.dist(point(a), point(b)) < 0.001 for a, b in zip(frozen, held))
    final_face_y = point(held[124])[1]
    above_face = {name: point(held[by_name[name]], *palm)[1] - final_face_y
                  for name, palm in (("Ys_lhand2", config["leftPalmLocal"]), ("Ys_rhand", config["rightPalmLocal"]))}
    assert min(above_face.values()) > 0
    crown_clearance = {name: point(held[by_name[name]], *palm)[1] - pose_report["headTopWorldY"]
                       for name, palm in (("Ys_lhand2", config["leftPalmLocal"]), ("Ys_rhand", config["rightPalmLocal"]))}
    assert min(crown_clearance.values()) > 0, "Both palms must rise above the visible native crown"
    return {"sampleCount": sample_count, "maximumInterpolatedPalmError": max_palm_error,
            "maximumInterpolatedPalmErrorSwordUnits": palm_error_sword_units,
            "interpolatedPalmErrorLimitSwordUnits": raise_config["interpolatedPalmErrorLimitSwordUnits"],
            "maximumSwordOriginCurveError": max_origin_error, "maximumSwordAxisErrorDegrees": max_axis_error,
            "minimumWorldTransformDeterminant": min_determinant, "allScaleSignsMatchUprightReference": True,
            "torsoAndHeadMatchUprightEverySample": True, "startMatchesStaticGuard": True,
            "finalPoseHeldThroughDuration": True, "finalPalmsAboveFaceWorld": above_face,
            "finalPalmsAboveCrownWorld": crown_clearance,
            "finalLowerPalmClearanceWorld": min(crown_clearance.values()),
            "monotonicSwordRaise": True, "frameErrors": frames}


def build_guard_animation(original: bytes, config):
    """Return the authored skeleton plus a build report without writing files."""
    source_sha = hashlib.sha256(original).hexdigest()
    assert source_sha == config["sourceSkeletonSha256"], "Native Saber skeleton SHA-256 mismatch"
    assert config["format"] == 1
    assert config["sourceAnimation"] == "cast" and config["animation"] == CLONE_NAME
    assert config["handheld"] and config["originMode"] == "pivot"
    assert config["nativeAnimations"] == ANIMATIONS
    assert 0 <= config["phase"] <= 1
    assert config["uprightReferenceAnimation"] == "idle_loop"
    assert config["uprightReferencePhase"] == config["heldProgress"] == 0.0
    count_pos, blocks = find_blocks(original)
    cast_start, cast_end = blocks["cast"]
    cast = original[cast_start:cast_end]
    upright_start, upright_end = blocks[config["uprightReferenceAnimation"]]
    upright = original[upright_start:upright_end]
    setup, slots, strings = read_setup_bones(original, with_slots=True)
    assert setup[SWORD_BONE]["name"] == config["swordBone"]
    authored_bones, pose = upright_guard_pose(original, cast, upright, config)
    rotation, translation = pose["computedSwordLocalRotation"], pose["targetSwordLocalXY"]
    clone, static_report = serialize_static_guard(upright, setup, authored_bones)
    clone, slot_report = override_guard_slots(clone, slots, strings, config)
    clone, draw_report = override_guard_draw_order(clone, slots, config)

    # Reparse the serialized clone. Every effect and alternate sword must have
    # exactly one attachment key at time zero, including absent native tracks.
    guard_tracks, duration, attachments = read_cast_bone_tracks(clone_as_cast(clone))
    for slot, target in slot_report["attachmentOverridesFromTimeZero"].items():
        keys = [entry for entry in attachments if entry["slot"] == int(slot)]
        assert len(keys) == 1 and keys[0]["time"] == 0
        assert keys[0]["attachmentIndex"] == target
    assert duration >= 1.0
    pose_bones = evaluate_pose(setup, guard_tracks, 0.0)
    held_sword = pose_bones[SWORD_BONE]
    origin_error = math.dist(point(held_sword), pose["targetSwordWorldOrigin"])
    sword_angle = math.degrees(math.atan2(held_sword["c"], held_sword["a"]))
    assert origin_error < 0.001 and abs(sword_angle - 90) < 0.001
    palm_errors = []
    for arm, grip_x in zip(pose["armSolves"], (config["leftGripSwordX"], config["rightGripSwordX"])):
        hand = next(bone for bone in pose_bones if bone["name"] == arm["hand"])
        palm_error = math.dist(point(hand, *arm["palmLocal"]), point(held_sword, grip_x, 0))
        arm["serializedPalmError"] = palm_error
        palm_errors.append(palm_error)
        assert palm_error < 0.001, "Serialized guard grip separated from sword"
    for name, index in (("torso", 34), ("head", 124)):
        authored_axis = math.degrees(math.atan2(pose_bones[index]["c"], pose_bones[index]["a"]))
        pose["bodyAxes"][name]["serializedAxisDegrees"] = authored_axis
        assert abs(authored_axis - pose["bodyAxes"][name]["uprightReferenceAxisDegrees"]) < 0.001
    for time in (0.0, 0.5, 1.0, duration):
        frame = evaluate_pose(setup, guard_tracks, time)
        assert all(math.dist(point(a), point(b)) < 0.001 for a, b in zip(frame, pose_bones))

    raise_frames, raise_pose_report = build_raise_poses(original, setup, authored_bones, pose, config)
    raise_clone, raise_serialization = serialize_raise_animation(upright, setup, raise_frames)
    raise_clone, raise_slot_report = override_guard_slots(raise_clone, slots, strings, config)
    raise_clone, raise_draw_report = override_guard_draw_order(raise_clone, slots, config)
    raise_validation = validate_raise_animation(raise_clone, setup, pose_bones, config, raise_pose_report)
    _, _, raise_attachments = read_cast_bone_tracks(animation_as_cast(raise_clone))
    for slot, target in raise_slot_report["attachmentOverridesFromTimeZero"].items():
        keys = [entry for entry in raise_attachments if entry["slot"] == int(slot)]
        assert len(keys) == 1 and keys[0]["time"] == 0 and keys[0]["attachmentIndex"] == target

    output = bytearray(original)
    output[count_pos] = len(ANIMATIONS) + 2
    output += clone + raise_clone
    output = bytes(output)
    assert len(output) == len(original) + len(clone) + len(raise_clone)
    assert output[:count_pos] == original[:count_pos]
    assert output[count_pos + 1:len(original)] == original[count_pos + 1:]
    preserved = []
    for name, (start, end) in blocks.items():
        assert output[start:end] == original[start:end]
        preserved.append({"name": name, "start": start, "end": end,
                          "sha256": hashlib.sha256(original[start:end]).hexdigest()})
    output_sha = hashlib.sha256(output).hexdigest()
    raise_metadata = {
        "name": RAISE_NAME, "animation": RAISE_NAME,
        "bytes": len(raise_clone), "sha256": hashlib.sha256(raise_clone).hexdigest(),
        "duration": config["raiseAnimation"]["duration"],
        "raiseSeconds": config["raiseAnimation"]["raiseSeconds"],
        "heldTime": config["raiseAnimation"]["raiseSeconds"],
        "frames": len(raise_frames), "motionFrames": config["raiseAnimation"]["motionFrames"],
        "sourceGuardAnimation": CLONE_NAME,
        "startSwordWorldOrigin": raise_pose_report["startSwordWorldOrigin"],
        "endSwordWorldOrigin": raise_pose_report["endSwordWorldOrigin"],
        "overheadOriginFaceChestRatio": config["raiseAnimation"]["overheadOriginFaceChestRatio"],
        "endSwordRightWorldOffset": config["raiseAnimation"]["endSwordRightWorldOffset"],
        "shoulderLiftWorld": raise_pose_report["shoulderLiftWorld"],
        "shoulderLiftFaceChestRatio": raise_pose_report["shoulderLiftFaceChestRatio"],
        "headTopWorldY": raise_pose_report["headTopWorldY"],
        "headTopSlot": raise_pose_report["headTopSlot"],
        "headTopAttachment": raise_pose_report["headTopAttachment"],
        "headTopVertexIndex": raise_pose_report["headTopVertexIndex"],
        "headTopOffsetFromFaceRatio": raise_pose_report["headTopOffsetFromFaceRatio"],
        "headGeometrySource": raise_pose_report["source"],
        "nativeHeadMeshes": raise_pose_report["nativeHeadMeshes"],
        "swordOriginWorldOffset": config["swordOriginWorldOffset"],
        "gripSeparationSwordUnits": pose["gripSeparationSwordUnits"],
        "geometrySource": "Original native Saber bones, hand meshes, and sword; offline two-arm IK continuation",
        "frozenIdleMeshDeform": True, "frozenIdleMeshDeformTime": 0.0,
        "combatEffectSlotsSuppressed": raise_slot_report["effectSlotCount"],
        "effectAttachmentsClearedFromTimeZero": True,
        "foregroundGripSlotNames": raise_draw_report["foregroundSlotNames"],
        "foregroundArmSlotNames": raise_draw_report["foregroundArmSlotNames"],
        "forearmsDrawAboveHeadBeforeSword": raise_draw_report["forearmsDrawAboveHeadBeforeSword"],
        "backgroundUpperArmSlotNames": raise_draw_report["backgroundUpperArmSlotNames"],
        "upperArmsDrawBehindHead": raise_draw_report["upperArmsDrawBehindHead"],
        "retainedNativeAnimations": ANIMATIONS, "retainedNativeAnimationBytes": True,
        **{key: value for key, value in raise_validation.items() if key != "frameErrors"},
    }
    metadata = {
        "animation": CLONE_NAME, "sourceAnimation": "cast", "phase": config["phase"],
        "armSourceAnimation": "cast", "armSourcePhase": config["phase"],
        "uprightReferenceAnimation": config["uprightReferenceAnimation"],
        "uprightReferencePhase": config["uprightReferencePhase"],
        "heldProgress": config["heldProgress"], "staticBonePose": True,
        "retainedIdleMeshDeform": True, "worldTorsoAndHeadAxes": pose["bodyAxes"],
        "allScaleSignsMatchUprightReference": pose["allScaleSignsMatchUprightReference"],
        "swordDrawsInFrontOfFace": True, "palmsDrawOverSwordGrip": True,
        "maximumSerializedPalmError": max(palm_errors),
        "gripSeparationSwordUnits": pose["gripSeparationSwordUnits"],
        "gripSeparationWorld": pose["gripSeparationWorld"],
        "minimumGripSeparationSwordUnits": pose["minimumGripSeparationSwordUnits"],
        "gripOrderFromCrossguard": pose["gripOrderFromCrossguard"],
        "handheld": True, "originMode": "pivot", "swordBone": config["swordBone"],
        "resourcePath": config["resourcePath"], "sourceSkeletonSha256": source_sha,
        "authoredSkeletonSha256": output_sha, "sourceBytes": len(original), "authoredBytes": len(output),
        "retainedNativeAnimations": ANIMATIONS, "retainedNativeAnimationBytes": True,
        "nativeAnimationCount": len(ANIMATIONS), "authoredAnimationCount": 2,
        "raiseAnimation": raise_metadata,
        "combatEffectSlotsSuppressed": slot_report["effectSlotCount"],
        "effectAttachmentsClearedFromTimeZero": True,
        "targetSwordLocalRotation": rotation, "targetSwordLocalXY": translation,
        "swordOriginWorldOffset": pose["swordOriginWorldOffset"],
        "buildReport": "build/guard-pose-build.json",
    }
    report = {
        "authoredGuardAnimation": metadata, "animationCountOffset": count_pos,
        "castStart": cast_start, "castEnd": cast_end,
        "uprightStart": upright_start, "uprightEnd": upright_end,
        "cloneStart": len(original), "cloneBytes": len(clone),
        "raiseCloneStart": len(original) + len(clone), "raiseCloneBytes": len(raise_clone),
        "raisePose": raise_pose_report, "raiseBoneTimelines": raise_serialization,
        "raiseSlotOverrides": raise_slot_report, "raiseDrawOrderOverride": raise_draw_report,
        "raiseValidation": raise_validation,
        "phaseTarget": pose, "staticBoneTimelines": static_report,
        "slotOverrides": slot_report, "drawOrderOverride": draw_report,
        "preservedOriginalAnimations": preserved,
        "worldSwordAxisDegrees": sword_angle, "worldOriginError": origin_error,
        "preservedAllOriginalAnimationBytes": True,
        "configSha256": hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest(),
    }
    return output, report


def prepare_guard_pose(files, config_path=CONFIG, report_path=REPORT):
    """Replace only the battle skeleton in a prepared resource map."""
    config = json.loads(Path(config_path).read_text(encoding="utf-8"))
    resource = config["resourcePath"]
    assert resource in files, "Bundled Saber battle skeleton is missing"
    output, report = build_guard_animation(files[resource], config)
    files[resource] = output
    report_path = Path(report_path)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report["authoredGuardAnimation"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-pck", type=Path,
                        default=ROOT / "build/source-cache/standalone-source.pck")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "build/guard-pose-preview")
    args = parser.parse_args()
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    source = read_pck(args.source_pck)
    path = config["resourcePath"]
    original = extract(source, path)
    files = {path: original}
    metadata = prepare_guard_pose(files)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    skel_path, pck_path = args.output_dir / "guard.spskel", args.output_dir / "guard.pck"
    skel_path.write_bytes(files[path])
    write_pck(pck_path, files)
    verified = read_pck(pck_path)
    assert verified["count"] == 1 and extract(verified, path) == files[path]
    assert verified["files"][path][1] == len(files[path])
    assert verified["files"][path][2] == hashlib.md5(files[path]).digest()
    assert extract(read_pck(args.source_pck), path) == original
    print(json.dumps({"pack": str(pck_path), "report": str(REPORT),
                      "animations": [metadata["animation"], metadata["raiseAnimation"]["animation"]],
                      "bytes": metadata["authoredBytes"],
                      "effectSlotsSuppressed": metadata["combatEffectSlotsSuppressed"],
                      "nativeAnimationsPreserved": metadata["nativeAnimationCount"]}, indent=2))


if __name__ == "__main__":
    main()
