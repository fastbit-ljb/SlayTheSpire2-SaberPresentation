#if SABER_TEST
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

internal static partial class TestHarness
{
    private sealed record HoverBone(string Name, float X, float Y, float AxisX, float AxisY);
    private sealed record HoverFrame(double Seconds, string? Animation, ulong EntryWrapperId,
        double? MixTime, double? MixDuration, double? TrackTime, double? TimeScale,
        bool Ready, string? ReadyClip, float ReadyProgress, float BodyX, float BodyY, HoverBone[] Bones, HoverGuard? Guard,
        float GoldStrength, bool GoldVisible, float? GoldAlignmentError);

    private static readonly string[] HoverBoneNames =
        ["wuqi3", "wuqi2o", "Ys_wuqi2", "Ys_wuqi1", "jian", "Ys_rjian4", "Ys_rjian3",
            "Ys_lhand2", "Ys_rhand", "Ys_face", "Ys_xiongjia2"];

    private static HoverFrame ReadHoverFrame(NCreature actor, ulong start)
    {
        var spine = actor.Visuals.SpineBody!;
        using var track = spine.GetAnimationState().GetCurrent(0);
        var native = track?.BoundObject;
        // Some game builds expose these on the native Spine entry without a C#
        // wrapper. Detect each method before calling it; bones remain the oracle.
        double? Scalar(string method) => native?.HasMethod(method) == true ? native.Call(method).AsDouble() : null;
        var bones = new List<HoverBone>();
        foreach (string name in HoverBoneNames)
            if (spine.GetGlobalBoneTransform(name) is { } transform)
            {
                Vector2 axis = transform.X.Normalized();
                bones.Add(new(name, transform.Origin.X, transform.Origin.Y, axis.X, axis.Y));
            }
        string? clip = spine.GetAnimationState().GetCurrentAnimationName();
        return new((Time.GetTicksUsec() - start) / 1_000_000d,
            clip, native?.GetInstanceId() ?? 0,
            Scalar("get_mix_time"), Scalar("get_mix_duration"), Scalar("get_track_time"), Scalar("get_time_scale"),
            ReadyPresentation.BodyReady, ReadyPresentation.BodyPoseClip, ReadyPresentation.BodyPoseProgress,
            actor.Visuals.Body.Position.X, actor.Visuals.Body.Position.Y, bones.ToArray(),
            clip == ReadyPresentation.AttackReadyClip ? ReadHoverGuard(spine) : null,
            ReadyPresentation.ChargeStrength, ReadyPresentation.ChargeGlowVisible,
            float.IsFinite(ReadyPresentation.ChargeAlignmentError) ? ReadyPresentation.ChargeAlignmentError : null);
    }

    private static double HoverDistance(HoverFrame a, HoverFrame b, string group = "all")
    {
        double squared = 0;
        int count = 0;
        foreach (var bone in a.Bones)
        {
            // Root/unused weapon bones are recorded but cannot hide a failure of
            // the animated sword and hand chains by diluting the distance.
            bool sword = bone.Name is "Ys_wuqi2" or "Ys_wuqi1";
            bool hand = bone.Name is "Ys_lhand2" or "Ys_rhand";
            if (!(group == "sword" ? sword : group == "hand" ? hand : sword || hand)) continue;
            var other = b.Bones.FirstOrDefault(v => v.Name == bone.Name);
            if (other == null) continue;
            double dx = bone.X - other.X, dy = bone.Y - other.Y;
            // A direction endpoint 80 px along the bone also detects a sword
            // rotating around a stationary grip. No Euler wrap discontinuity.
            double ax = (bone.AxisX - other.AxisX) * 80, ay = (bone.AxisY - other.AxisY) * 80;
            squared += dx * dx + dy * dy + ax * ax + ay * ay;
            count++;
        }
        return count == 0 ? double.PositiveInfinity : Math.Sqrt(squared / count);
    }

    private static async Task VerifyHoverEntry(NGame game, NCreature actor, CombatState state, Player player)
    {
        var spine = actor.Visuals.SpineBody!;
        var animation = spine.GetAnimationState();
        var sessions = new List<object>();
        var transfers = new List<object>();
        var hand = Runtime.Descendants(game).OfType<NHandCardHolder>().Where(h => h.CardModel?.Owner == player).ToArray();
        var attackHolder = hand.First(h => h.CardModel!.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack);
        var castHolder = hand.First(h => h.CardModel!.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill);
        var attack = attackHolder.CardModel!;
        var cast = castHolder.CardModel!;
        var otherAttack = state.CreateCard(ModelDb.Card<PommelStrike>(), player);
        var otherCast = state.CreateCard(ModelDb.Card<DefendIronclad>(), player);
        Vector2 home = actor.Visuals.Body.Position;

        async Task<List<HoverFrame>> Observe(string name, double seconds, Action<double>? whileSampling = null)
        {
            ulong start = Time.GetTicksUsec();
            var frames = new List<HoverFrame> { ReadHoverFrame(actor, start) };
            // One sample per actual engine frame, with a monotonic timestamp.
            // There is no PNG encoding, artificial FPS, or per-frame file IO.
            do
            {
                whileSampling?.Invoke((Time.GetTicksUsec() - start) / 1_000_000d);
                await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
                frames.Add(ReadHoverFrame(actor, start));
            } while (frames[^1].Seconds < seconds);
            sessions.Add(new { name, frames });
            return frames;
        }

        async Task ResetIdle()
        {
            ReadyPresentation.Cancel(null);
            animation.SetAnimation("idle_loop", true);
            using var track = animation.GetCurrent(0);
            track!.SetMixDuration(0);
            track.SetTimeScale(1);
            track.SetTrackTime(0);
            await Fx.Delay(0.18);
        }

        async Task<HoverFrame> Reference(string clip, float progress)
        {
            animation.SetAnimation(clip, false);
            using (var track = animation.GetCurrent(0))
            {
                track!.SetMixDuration(0);
                track.SetTrackTime(track.GetAnimationDuration() * progress);
                track.SetTimeScale(0);
            }
            await Fx.Delay(0.06);
            var reference = ReadHoverFrame(actor, Time.GetTicksUsec());
            sessions.Add(new { name = "reference_" + (clip == ReadyPresentation.AttackReadyClip ? "attack" : clip), frames = new[] { reference } });
            return reference;
        }

        void CheckBlend(string name, List<HoverFrame> frames, HoverFrame source, HoverFrame target, string clip, float progress)
        {
            var early = frames.Where(f => f.Seconds > 0.015 && f.Seconds < 0.27).ToArray();
            var tail = frames.Where(f => f.Seconds >= frames[^1].Seconds - 0.15).ToArray();
            Assert(name + "_sampling_resolves_transition", early.Length >= 3 && tail.Length >= 3);
            Assert(name + "_holds_requested_animation", frames[^1].Animation == clip && frames[^1].Ready
                && frames[^1].ReadyClip == clip && Math.Abs(frames[^1].ReadyProgress - progress) < 0.001);
            foreach (string group in new[] { "sword", "hand" })
            {
                double span = HoverDistance(source, target, group);
                double margin = Math.Max(1.0, span * 0.06);
                var intermediate = early.Where(f => HoverDistance(f, source, group) > margin && HoverDistance(f, target, group) > margin).ToArray();
                Assert(name + "_" + group + "_endpoint_differs_from_idle", span > 4);
                Assert(name + "_" + group + "_has_multiple_intermediate_poses", intermediate.Length >= 2
                    && intermediate.Skip(1).Any(f => HoverDistance(intermediate[0], f, group) > Math.Max(0.4, span * 0.015)));
                var entering = new[] { source }.Concat(frames).ToArray();
                Assert(name + "_" + group + "_has_no_entry_jump", entering.Zip(entering.Skip(1))
                    .All(pair => HoverDistance(pair.First, pair.Second, group) < Math.Max(3, span * 0.45)));
                Assert(name + "_" + group + "_reaches_held_reference", tail.All(f => HoverDistance(f, target, group) < 2.0));
                Assert(name + "_" + group + "_holds_stably", tail.All(f => HoverDistance(f, tail[^1], group) < 0.8));
            }
            var mix = frames.Where(f => f.MixTime.HasValue && f.MixDuration.HasValue).ToArray();
            if (mix.Length > 0)
            {
                Assert(name + "_native_mix_clock_advances", mix[^1].MixTime >= mix[^1].MixDuration - 0.02);
                Assert(name + "_native_mix_clock_never_rewinds", mix.Zip(mix.Skip(1)).All(pair => pair.Second.MixTime >= pair.First.MixTime - 0.002));
            }
            var clocks = tail.Where(f => f.TrackTime.HasValue).ToArray();
            if (clocks.Length > 1) Assert(name + "_held_clip_clock_does_not_drift", clocks.Max(f => f.TrackTime!.Value) - clocks.Min(f => f.TrackTime!.Value) < 0.002);
            Assert(name + "_body_stays_home", frames.All(f => new Vector2(f.BodyX, f.BodyY).DistanceTo(home) < 0.5f));
            if (clip == ReadyPresentation.AttackReadyClip) CheckHoverGuard(name, tail);
            else Assert(name + "_has_no_attack_charge_glow", tail.All(f => !f.GoldVisible && f.GoldStrength == 0f));
        }

        try
        {
            ReadyPresentation.Cancel(null);
            var attackReference = await Reference(ReadyPresentation.AttackReadyClip, ReadyPresentation.AttackReadyProgress);
            var castReference = await Reference("cast", 0.90f);
            Assert("hover_entry_samples_live_sword_and_hand_bones", attackReference.Bones.Count(b => b.Name.StartsWith("Ys_wuqi")) >= 2
                && attackReference.Bones.Count(b => b.Name is "Ys_lhand2" or "Ys_rhand") == 2);

            foreach (bool useAttack in new[] { true, false })
            {
                string clip = useAttack ? ReadyPresentation.AttackReadyClip : "cast";
                string name = "hover_idle_to_" + (useAttack ? "attack" : "cast");
                var holder = useAttack ? attackHolder : castHolder;
                var card = useAttack ? attack : cast;
                var alternate = useAttack ? otherAttack : otherCast;
                var target = useAttack ? attackReference : castReference;
                await ResetIdle();
                var source = ReadHoverFrame(actor, Time.GetTicksUsec());
                holder.EmitSignal(Control.SignalName.FocusEntered);
                bool retained = true;
                int notifications = 0;
                var earlyScreenshot = CaptureAfter(name + "_early", 0.11);
                var samples = await Observe(name, useAttack ? 1.5 : 0.62, seconds =>
                {
                    if (notifications >= 4 || seconds < 0.035 + notifications * 0.04) return;
                    // Both a duplicate native focus event and a different card
                    // of the same type arrive while the entry is still mixing.
                    var before = ReadHoverFrame(actor, Time.GetTicksUsec());
                    holder.EmitSignal(Control.SignalName.FocusEntered);
                    ReadyPresentation.Focus(alternate);
                    ReadyPresentation.Focus(card);
                    var after = ReadHoverFrame(actor, Time.GetTicksUsec());
                    transfers.Add(new { name, seconds, kind = "duplicate_and_direct_same_type", before, after });
                    // get_current creates a new native wrapper on each read;
                    // its instance ID is not the underlying Spine track ID.
                    retained &= before.Animation == after.Animation && HoverDistance(before, after) < 0.01
                        && (!before.MixTime.HasValue || Math.Abs(after.MixTime!.Value - before.MixTime.Value) < 0.002)
                        && (!before.TrackTime.HasValue || Math.Abs(after.TrackTime!.Value - before.TrackTime.Value) < 0.002);
                    notifications++;
                });
                Assert(name + "_duplicate_and_same_type_focus_keep_mix", notifications == 4 && retained);
                CheckBlend(name, samples, source, target, clip, useAttack ? ReadyPresentation.AttackReadyProgress : 0.90f);
                if (useAttack) CheckChargeTrajectory(samples);
                await earlyScreenshot;
                await CaptureAfter(name + "_held", 0.001);
                if (useAttack)
                {
                    var slots = ReadHoverGuardSlots(spine);
                    sessions.Add(new { name = "hover_front_guard_slots", frames = new[] { ReadHoverFrame(actor, Time.GetTicksUsec()) }, slots });
                    CheckHoverGuardSlots("hover_front_guard", slots);
                    await CaptureAfter("hover_front_guard", 0.001);
                }

                // Native card navigation commonly sends exit/enter in one
                // frame. Verify its visible result separately from direct Focus.
                var beforeTransfer = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Unfocus(card);
                ReadyPresentation.Focus(alternate);
                var nativeTransfer = await Observe(name + "_native_same_type_transfer", 0.37);
                transfers.Add(new { name, kind = "native_unfocus_then_focus", before = beforeTransfer, after = nativeTransfer[0] });
                Assert(name + "_native_same_type_transfer_has_no_visible_reset", nativeTransfer.All(f => HoverDistance(f, target) < 2.0));
                Assert(name + "_native_same_type_transfer_tracks_new_card", ReadyPresentation.Card == alternate);
            }

            foreach (bool useAttack in new[] { true, false })
            {
                string clip = useAttack ? "attack" : "cast";
                var card = useAttack ? attack : cast;
                var alternate = useAttack ? otherAttack : otherCast;
                var target = useAttack ? attackReference : castReference;
                await ResetIdle();
                var idle = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Focus(card);
                var entering = await Observe("hover_native_switch_" + clip + "_before", 0.08);
                var transferClock = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Unfocus(card);
                ReadyPresentation.Focus(alternate);
                var continuedClock = ReadHoverFrame(actor, Time.GetTicksUsec());
                Assert("hover_native_switch_" + clip + "_does_not_restart_clip_clock",
                    !transferClock.TrackTime.HasValue || continuedClock.TrackTime >= transferClock.TrackTime - 0.002);
                var switched = await Observe("hover_native_switch_" + clip + "_after", useAttack ? 1.35 : 0.45);
                var sequence = new[] { entering[^1] }.Concat(switched).ToArray();
                double span = HoverDistance(idle, target);
                Assert("hover_native_switch_during_" + clip + "_mix_has_no_jump",
                    sequence.Zip(sequence.Skip(1)).All(pair => HoverDistance(pair.First, pair.Second) < Math.Max(3, span * 0.45)));
                Assert("hover_native_switch_during_" + clip + "_mix_reaches_held_reference",
                    HoverDistance(switched[^1], target) < 2 && ReadyPresentation.Card == alternate);
            }

            // The preceding case ends in cast. Test each direction without an
            // artificial idle reset, using the actual held body as the source.
            foreach (bool toAttack in new[] { true, false })
            {
                string clip = toAttack ? ReadyPresentation.AttackReadyClip : "cast";
                string name = toAttack ? "hover_cast_to_attack" : "hover_attack_to_cast";
                var source = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Focus(toAttack ? attack : cast);
                var samples = await Observe(name, toAttack ? 1.5 : 0.62);
                Assert(name + "_selects_new_animation", samples[0].Animation == clip && samples[0].Animation != source.Animation);
                CheckBlend(name, samples, source, toAttack ? attackReference : castReference, clip,
                    toAttack ? ReadyPresentation.AttackReadyProgress : 0.90f);
            }

            foreach (bool cancelAttack in new[] { true, false })
            {
                string clip = cancelAttack ? "attack" : "cast";
                await ResetIdle();
                var idle = ReadHoverFrame(actor, Time.GetTicksUsec());
                var card = cancelAttack ? attack : cast;
                ReadyPresentation.Focus(card);
                await Observe("hover_cancel_" + clip + "_before", 0.085);
                var cancelSource = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Cancel(card);
                var cancelled = await Observe("hover_cancel_" + clip + "_after", 0.35);
                var leave = new[] { cancelSource }.Concat(cancelled).ToArray();
                double leaveSpan = HoverDistance(cancelSource, idle);
                Assert("hover_cancel_" + clip + "_mix_has_no_jump", leave.Zip(leave.Skip(1))
                    .All(pair => HoverDistance(pair.First, pair.Second) < Math.Max(3, leaveSpan * 0.45)));
                Assert("hover_cancel_" + clip + "_restores_idle_during_mix", cancelled[^1].Animation == "idle_loop"
                    && !cancelled[^1].Ready && cancelled[^1].ReadyClip == null && !ReadyPresentation.Visible && !ReadyPresentation.Targeting);
                // Idle breathes; compare to an idle envelope, not pixel identity.
                Assert("hover_cancel_" + clip + "_bones_return_to_idle", HoverDistance(cancelled[^1], idle) < 14);
                if (cancelled[^1].TimeScale.HasValue)
                    Assert("hover_cancel_" + clip + "_releases_clip_clock", cancelled[^1].TimeScale > 0.9);
            }

            foreach (double elapsed in new[] { 0.40, 0.78 })
            {
                string name = "hover_cancel_charge_" + (int)(elapsed * 1000);
                await ResetIdle();
                var idle = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Focus(attack);
                await Observe(name + "_before", elapsed);
                var source = ReadHoverFrame(actor, Time.GetTicksUsec());
                ReadyPresentation.Cancel(attack);
                var after = await Observe(name + "_after", 0.35);
                var sequence = new[] { source }.Concat(after).ToArray();
                double span = HoverDistance(source, idle);
                Assert(name + "_returns_without_jump", sequence.Zip(sequence.Skip(1)).All(p =>
                    HoverDistance(p.First, p.Second) < Math.Max(3, span * 0.45)));
                Assert(name + "_clears_gold_and_returns_idle", after[^1].Animation == "idle_loop"
                    && !ReadyPresentation.BodyReady && !ReadyPresentation.ChargeGlowVisible
                    && !Runtime.Descendants(game).Any(n => n.Name == "SaberSwordChargeGlow"));
            }

            await ResetIdle();
            ReadyPresentation.Focus(attack);
            await Observe("hover_cancel_rehover_before", 0.075);
            ReadyPresentation.Cancel(attack);
            var rehoverSource = ReadHoverFrame(actor, Time.GetTicksUsec());
            ReadyPresentation.Focus(cast);
            var rehover = await Observe("hover_cancel_immediate_rehover", 0.62);
            CheckBlend("hover_cancel_immediate_rehover", rehover, rehoverSource, castReference, "cast", 0.90f);
            Assert("hover_cancel_immediate_rehover_keeps_new_card", ReadyPresentation.Card == cast && ReadyPresentation.Visible);
            ReadyPresentation.Cancel(cast);
            await CaptureAfter("hover_entry_cancelled_idle", 0.25);
            Assert("hover_entry_no_actor_drift", actor.Visuals.Body.Position.DistanceTo(home) < 0.5f);
            // The caller immediately runs VerifyInteractions, including a real
            // Strike OnPlayWrapper and its damage/retraction/release assertions.
        }
        finally
        {
            // Keep partial measurements even when an assertion fails.
            System.IO.File.WriteAllText(Output + "/hover-entry-samples.json", JsonSerializer.Serialize(new
            {
                clock = "Godot.Time.GetTicksUsec",
                sampling = "one read per SceneTree.ProcessFrame; timestamps are elapsed wall time",
                distance = "RMS of active sword/hand origins and 80px direction vectors; global pixel units",
                sessions,
                transfers
            }, new JsonSerializerOptions { WriteIndented = true }));
            ReadyPresentation.Cancel(null);
        }
    }
}
#endif
