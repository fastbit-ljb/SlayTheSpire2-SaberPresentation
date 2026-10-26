#if SABER_TEST
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace SaberPresentation;

internal static partial class TestHarness
{
    private sealed record HoverSlot(string Name, string? Attachment, float? Alpha);
    private sealed record HoverGuardSlots(bool Readable, HoverSlot[] Slots);
    private sealed record HoverGuard(float BodySpan, float SwordAxisX, float SwordAxisY,
        float LeftGripDistance, float RightGripDistance, float PalmDistance, float BladeFaceDistance,
        float CrossguardBelowFace, float TipAboveFace, float TorsoAxisX, float TorsoAxisY,
        float HeadAxisX, float HeadAxisY);

    private static HoverGuard? ReadHoverGuard(MegaSprite spine)
    {
        if (spine.GetGlobalBoneTransform("Ys_wuqi1") is not { } sword
            || spine.GetGlobalBoneTransform("Ys_lhand2") is not { } left
            || spine.GetGlobalBoneTransform("Ys_rhand") is not { } right
            || spine.GetGlobalBoneTransform("Ys_face") is not { } face
            || spine.GetGlobalBoneTransform("Ys_xiongjia2") is not { } chest) return null;

        // The unweighted nv27 mesh runs along this bone's +X axis. Use the
        // rendered transform, including reflection and scale, rather than a
        // local Euler angle or a hidden alternate sword bone.
        Vector2 axis = sword.X.Normalized();
        Vector2 pommel = sword * new Vector2(3.5f, 0);
        Vector2 crossguard = sword * new Vector2(40, 0);
        Vector2 tip = sword * new Vector2(206.7f, 0);
        Vector2 leftPalm = left * new Vector2(8.82f, 1.72f);
        Vector2 rightPalm = right * new Vector2(10.39f, -3.41f);
        float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float t = Math.Clamp((point - a).Dot(segment) / Math.Max(segment.LengthSquared(), 0.0001f), 0, 1);
            return point.DistanceTo(a + segment * t);
        }

        return new(face.Origin.DistanceTo(chest.Origin), axis.X, axis.Y,
            SegmentDistance(leftPalm, pommel, crossguard), SegmentDistance(rightPalm, pommel, crossguard),
            leftPalm.DistanceTo(rightPalm), SegmentDistance(face.Origin, crossguard, tip),
            crossguard.Y - face.Origin.Y, face.Origin.Y - tip.Y,
            chest.X.Normalized().X, chest.X.Normalized().Y,
            face.X.Normalized().X, face.X.Normalized().Y);
    }

    private static HoverGuardSlots ReadHoverGuardSlots(MegaSprite spine)
    {
        var slots = new List<HoverSlot>();
        bool readable = false;
        // Spine returns new wrappers on each read. Dispose them on this engine
        // thread; no wrapper identity is used as the identity of native data.
        using (var skeleton = spine.GetSkeleton())
        {
            var native = skeleton?.BoundObject;
            if (native?.HasMethod("get_slots") == true)
            {
                readable = true;
                using var nativeSlots = native.Call("get_slots").AsGodotArray();
                foreach (var value in nativeSlots)
                {
                    using var slot = value.AsGodotObject();
                    if (slot?.HasMethod("get_data") != true) { readable = false; continue; }
                    using var data = slot.Call("get_data").AsGodotObject();
                    if (data?.HasMethod("get_name") != true) { readable = false; continue; }
                    string name = data.Call("get_name").AsString();
                    if (name is not ("nv27" or "wuqi2" or "wuqi3") && !IsGuardEffectSlot(name)) continue;
                    if (!slot.HasMethod("get_attachment") || !slot.HasMethod("get_color"))
                    { readable = false; continue; }
                    using var attachment = slot.Call("get_attachment").AsGodotObject();
                    if (attachment != null && !attachment.HasMethod("get_attachment_name"))
                    { readable = false; continue; }
                    slots.Add(new(name, attachment?.Call("get_attachment_name").AsString(), slot.Call("get_color").AsColor().A));
                }
            }
        }

        return new(readable, slots.ToArray());
    }

    private static bool IsGuardEffectSlot(string name) => name.StartsWith("Eff", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("skill", StringComparison.OrdinalIgnoreCase)
        || name is "1" or "2" or "3" or "4" or "5";

    private static void CheckHoverGuard(string name, IReadOnlyList<HoverFrame> frames)
    {
        Assert(name + "_front_guard_native_geometry_readable", frames.Count > 0
            && frames.All(f => f.Guard is { BodySpan: > 1 }));
        var poses = frames.Select(f => f.Guard!).ToArray();
        // All distances scale with the native head-to-chest joint spacing.
        // Palm centers are approximate because the source art also has deform
        // timelines; these bounds still reject a floating sword or one hand
        // left at the shoulder, independent of viewport/sprite scale.
        Assert(name + "_front_guard_blade_points_up", poses.All(p => p.SwordAxisY < -0.985f && Math.Abs(p.SwordAxisX) < 0.17f));
        Assert(name + "_front_guard_both_palms_hold_handle", poses.All(p => p.LeftGripDistance < p.BodySpan * 0.25f
            && p.RightGripDistance < p.BodySpan * 0.25f && p.PalmDistance < p.BodySpan * 0.4f));
        Assert(name + "_front_guard_blade_crosses_face", poses.All(p => p.BladeFaceDistance < p.BodySpan * 0.35f
            && p.CrossguardBelowFace > p.BodySpan * 0.2f && p.TipAboveFace > p.BodySpan * 0.25f));
        Assert(name + "_front_guard_stands_upright", poses.All(p => Math.Abs(p.TorsoAxisX) < 0.12f
            && p.TorsoAxisY < -0.98f));
        Assert(name + "_front_guard_head_looks_forward", poses.All(p => Math.Abs(p.HeadAxisX) < 0.35f
            && p.HeadAxisY < -0.93f));
    }

    private static void CheckHoverGuardSlots(string name, HoverGuardSlots pose)
    {
        Assert(name + "_native_slots_readable", pose.Readable);
        Assert(name + "_one_visible_native_sword", pose.Slots.Count(s => s.Name == "nv27"
            && s.Attachment == "nv27" && s.Alpha > 0.95f) == 1
            && pose.Slots.Count(s => s.Name is "wuqi2" or "wuqi3") == 2
            && pose.Slots.Where(s => s.Name is "wuqi2" or "wuqi3").All(s => s.Attachment == null));
        Assert(name + "_has_no_cast_effect_attachments", pose.Slots.Count(s => IsGuardEffectSlot(s.Name)) == 66
            && pose.Slots.Any(s => s.Name.StartsWith("skill", StringComparison.OrdinalIgnoreCase))
            && pose.Slots.Where(s => IsGuardEffectSlot(s.Name)).All(s => s.Attachment == null));
    }
}
#endif
