#if SABER_TEST
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

internal static partial class TestHarness
{
    private sealed record ChargePose(float BodySpan, float FaceX, float FaceY, float CrownY,
        float LeftPalmX, float LeftPalmY, float RightPalmX, float RightPalmY, float TipX, float TipY);
    private sealed record ChargeFrame(double Seconds, float TrackSeconds, float Progress, float Strength,
        bool GlowVisible, float? AlignmentError, string? Clip, float BodyScaleX, float BodyScaleY,
        HoverGuard? Guard, ChargePose? Pose);
    private sealed record ChargePixels(float RequestedProgress, float ActualProgress, float Strength,
        int RegionPixels, int WarmChangedPixels, double PositiveWarmDifference, double PositiveRedDifference,
        double PositiveLuminanceDifference, float MeanLocalX, float LocalSpan, int ImageWidth, int ImageHeight);
    private sealed record ChargeCheck(string Check, bool Passed);

    // This entire staging path is compiled only into the verification DLL. The
    // production hover never changes the actor's parent, transform, or scale.
    private static async Task VerifyChargePreview(NGame game, NCreature actor, CombatState state, Player player)
    {
        _ = state;
        var body = actor.Visuals.Body;
        var originalParent = body.GetParent();
        int originalIndex = body.GetIndex();
        Transform2D originalTransform = body.Transform;
        Transform2D originalGlobalTransform = body.GlobalTransform;
        Vector2 originalGlobalScale = body.GlobalScale;
        int originalZ = body.ZIndex;
        bool originalVisible = body.Visible;
        var viewportSize = game.GetViewport().GetVisibleRect().Size;
        var layer = new CanvasLayer { Name = "SaberChargePreviewLayer", Layer = 90 };
        var stage = new Node2D { Name = "SaberChargePreviewStage" };
        Node2D? portraitScreen = null;
        bool portraitScreenWasVisible = true;
        bool portraitScreenSnapshotTaken = false;
        var frames = new List<ChargeFrame>();
        var pixels = new List<ChargePixels>();
        var checks = new List<ChargeCheck>();
        var pictures = new List<(string Name, Image Image)>();
        bool normalSizeRestored = false, normalParentRestored = false, normalTransformRestored = false;
        int referenceEyePixels = 0, finalEyePixels = 0;
        var attack = Runtime.Descendants(game).OfType<NHandCardHolder>()
            .First(holder => holder.CardModel?.Owner == player && holder.CardModel.Type == CardType.Attack).CardModel!;
        void Check(string name, bool passed) => checks.Add(new(name, passed));

        async Task FrameDraw()
        {
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            var drawn = game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            RenderingServer.ForceDraw();
            await drawn;
        }
        Node2D? MoveGlowToPreview()
        {
            var glow = Runtime.Descendants(game).OfType<Node2D>().FirstOrDefault(n => n.Name == "SaberSwordChargeGlow"
                && !n.IsQueuedForDeletion());
            if (glow != null && glow.GetParent() != stage) glow.Reparent(stage, true);
            return glow;
        }
        async Task StartRaise()
        {
            ReadyPresentation.Cancel(null);
            actor.Visuals.SpineBody!.GetAnimationState().SetAnimation("idle_loop", true);
            using (var idle = actor.Visuals.SpineBody.GetAnimationState().GetCurrent(0))
            {
                idle!.SetMixDuration(0);
                idle.SetTimeScale(1);
                idle.SetTrackTime(0);
            }
            await Fx.Delay(0.20);
            ReadyPresentation.Focus(attack);
            await FrameDraw();
            portraitScreen ??= Runtime.Descendants(game).OfType<Node2D>()
                .FirstOrDefault(n => n.Name == "SaberBattleReadyScreen");
            if (portraitScreen != null)
            {
                if (!portraitScreenSnapshotTaken)
                {
                    portraitScreenWasVisible = portraitScreen.Visible;
                    portraitScreenSnapshotTaken = true;
                }
                portraitScreen.Hide();
            }
            MoveGlowToPreview();
        }
        try
        {
            ReadyPresentation.Cancel(null);
            game.AddChild(layer);
            layer.AddChild(new ColorRect
            {
                Name = "SaberChargePreviewBackdrop", Size = viewportSize,
                Color = new Color(0.025f, 0.035f, 0.05f), MouseFilter = Control.MouseFilterEnum.Ignore
            });
            layer.AddChild(stage);
            body.Reparent(stage, true);
            body.Position = new Vector2(viewportSize.X * 0.5f, viewportSize.Y * 0.95f);
            body.GlobalScale = originalGlobalScale * 2.5f;
            body.ZIndex = 1;
            body.Show();
            Check("charge_preview_only_test_body_is_enlarged_2_5x", body.GlobalScale.DistanceTo(originalGlobalScale * 2.5f) < 0.001f);
            Check("charge_preview_fullscreen_native_resolution", (DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen)
                && game.GetViewport().GetTexture().GetSize() == DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen()));

            actor.Visuals.SpineBody!.GetAnimationState().SetAnimation("idle_loop", true);
            using (var reference = actor.Visuals.SpineBody.GetAnimationState().GetCurrent(0))
            {
                reference!.SetMixDuration(0);
                reference.SetTrackTime(0);
                reference.SetTimeScale(0);
            }
            await FrameDraw();
            using (var reference = game.GetViewport().GetTexture().GetImage())
                referenceEyePixels = CountChargeEyePixels(reference, ReadChargePose(actor)!);

            // Record uninterrupted motion before pausing any track for pixel
            // comparisons. PNG encoding is deferred until the motion finishes.
            await StartRaise();
            ulong started = Time.GetTicksUsec();
            double nextPicture = 0;
            for (int i = 0; i < 300; i++)
            {
                MoveGlowToPreview();
                var frame = ReadChargeFrame(actor, started);
                frames.Add(frame);
                bool complete = frame.Progress >= 0.999f;
                if (frame.Seconds >= nextPicture || complete)
                {
                    pictures.Add(($"charge_motion_{pictures.Count:D2}", game.GetViewport().GetTexture().GetImage()));
                    nextPicture = frame.Seconds + 0.05;
                }
                if (complete || frame.Seconds > 3.0) break;
                await FrameDraw();
            }
            Check("charge_preview_native_raise_completes", frames.Count > 0 && frames[^1].Progress >= 0.999f
                && frames[^1].Clip == ReadyPresentation.AttackReadyClip);
            Check("charge_preview_records_multiple_actual_motion_frames", pictures.Count >= 15 && frames.Count >= 15
                && frames.Count(f => f.Progress is > 0.08f and < 0.93f) >= 10);
            var settled = frames.Where(f => f.TrackSeconds >= 0.34f && f.Guard != null).ToArray();
            Check("charge_preview_both_palms_stay_on_handle", settled.Length >= 5 && settled.All(f =>
                f.Guard!.LeftGripDistance < f.Guard.BodySpan * 0.12f
                && f.Guard.RightGripDistance < f.Guard.BodySpan * 0.12f));
            Check("charge_preview_body_stays_upright", settled.Length > 0 && settled.All(f =>
                Math.Abs(f.Guard!.TorsoAxisX) < 0.12f && f.Guard.TorsoAxisY < -0.98f
                && Math.Abs(f.Guard.HeadAxisX) < 0.35f && f.Guard.HeadAxisY < -0.93f));
            Check("charge_preview_glow_follows_actual_sword", settled.Length > 0 && settled.All(f =>
                f.GlowVisible && f.AlignmentError is <= 0.5f));
            Check("charge_preview_progress_has_no_restart", frames.Zip(frames.Skip(1)).All(pair => pair.Second.Progress + 0.001f >= pair.First.Progress));
            var finalPose = frames.LastOrDefault()?.Pose;
            Check("charge_preview_final_both_hands_above_crown", finalPose != null
                && finalPose.LeftPalmY < finalPose.CrownY && finalPose.RightPalmY < finalPose.CrownY);
            Check("charge_preview_final_blade_is_inside_fullscreen", finalPose != null && finalPose.TipY >= 2
                && finalPose.TipY < viewportSize.Y && finalPose.TipX > 2 && finalPose.TipX < viewportSize.X - 2);
            using (var final = game.GetViewport().GetTexture().GetImage())
                finalEyePixels = CountChargeEyePixels(final, ReadChargePose(actor)!);
            Check("charge_preview_face_and_eyes_stay_visible", referenceEyePixels >= 10
                && finalEyePixels >= referenceEyePixels * 0.75f);

            // Re-run the real hover. At each threshold, freeze its clock and
            // render identical geometry with/without only the glow's child.
            // This proves visible gold rather than trusting the strength field.
            await StartRaise();
            foreach (float milestone in new[] { 0.25f, 0.5f, 0.75f, 1f })
            {
                ulong waiting = Time.GetTicksMsec();
                while (ReadyPresentation.BodyPoseProgress + 0.0001f < milestone && Time.GetTicksMsec() - waiting < 5000)
                    await FrameDraw();
                using var track = actor.Visuals.SpineBody!.GetAnimationState().GetCurrent(0);
                track!.SetTimeScale(0);
                // In particular let the original 0.28s entry mix settle before
                // the first pair; pausing clip time does not pause mix time.
                await Fx.Delay(0.06);
                var glow = MoveGlowToPreview() ?? throw new Exception("Native sword glow missing from charge preview");
                var blade = Runtime.Descendants(glow).OfType<Sprite2D>().Single(n => n.Name == "SaberGoldenBlade");
                blade.Show();
                await FrameDraw();
                using var on = game.GetViewport().GetTexture().GetImage();
                var sword = actor.Visuals.SpineBody.GetGlobalBoneTransform("Ys_wuqi1")
                    ?? throw new Exception("Native sword bone missing from charge preview");
                float progress = ReadyPresentation.BodyPoseProgress;
                float strength = ReadyPresentation.ChargeStrength;
                try
                {
                    blade.Hide();
                    await FrameDraw();
                    using var off = game.GetViewport().GetTexture().GetImage();
                    pixels.Add(CompareChargePixels(on, off, sword, milestone, progress, strength));
                    string stem = $"charge_gold_{(int)Math.Round(milestone * 100):D3}";
                    await Task.Run(() => { on.SavePng(Output + "/" + stem + "_on.png"); off.SavePng(Output + "/" + stem + "_off.png"); });
                }
                finally
                {
                    if (GodotObject.IsInstanceValid(blade)) blade.Show();
                    if (track.GetTrackTime() < ReadyPresentation.AttackRaiseSeconds) track.SetTimeScale(1);
                }
            }
            Check("charge_preview_real_gold_changes_rendered_blade", pixels.Count == 4 && pixels.All(p =>
                p.RegionPixels >= 50 && p.WarmChangedPixels >= 5 && p.PositiveWarmDifference > 0.10));
            Check("charge_preview_gold_coverage_grows_during_raise", pixels.Count == 4
                && pixels[^1].WarmChangedPixels > pixels[0].WarmChangedPixels * 1.5
                && pixels[^1].LocalSpan > pixels[0].LocalSpan + 25f);
            Check("charge_preview_gold_energy_gradually_increases", pixels.Count == 4
                && pixels[^1].PositiveWarmDifference > pixels[0].PositiveWarmDifference * 2
                && pixels.Zip(pixels.Skip(1)).All(pair => pair.Second.PositiveWarmDifference >= pair.First.PositiveWarmDifference * 0.75));
        }
        finally
        {
            ReadyPresentation.Cancel(null);
            if (portraitScreen != null && GodotObject.IsInstanceValid(portraitScreen)) portraitScreen.Visible = portraitScreenWasVisible;
            if (GodotObject.IsInstanceValid(body) && GodotObject.IsInstanceValid(originalParent))
            {
                if (body.GetParent() != originalParent) body.Reparent(originalParent, false);
                originalParent.MoveChild(body, Math.Min(originalIndex, originalParent.GetChildCount() - 1));
                body.Transform = originalTransform;
                body.ZIndex = originalZ;
                body.Visible = originalVisible;
                normalParentRestored = body.GetParent() == originalParent && body.GetIndex() == originalIndex;
                normalTransformRestored = ChargeTransformsNear(body.Transform, originalTransform)
                    && ChargeTransformsNear(body.GlobalTransform, originalGlobalTransform);
                normalSizeRestored = body.GlobalScale.DistanceTo(originalGlobalScale) < 0.001f;
            }
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
            foreach (var (name, image) in pictures)
            {
                try { await Task.Run(() => image.SavePng(Output + "/" + name + ".png")); }
                finally { image.Dispose(); }
            }
            await CaptureAfter("charge_restored_normal_size_fullscreen", 0.25);
            Check("charge_preview_original_actor_parent_restored", normalParentRestored);
            Check("charge_preview_original_actor_transform_restored", normalTransformRestored);
            Check("charge_preview_normal_actor_size_restored", normalSizeRestored);
            Check("charge_preview_temporary_stage_and_glow_cleaned", !Runtime.Descendants(game)
                .Any(n => n.Name == "SaberChargePreviewLayer" || n.Name == "SaberSwordChargeGlow"));
            System.IO.File.WriteAllText(Output + "/charge-preview-samples.json", JsonSerializer.Serialize(new
            {
                clock = "Godot.Time.GetTicksUsec; uninterrupted first motion pass, then a separate frozen on/off pixel pass",
                screenshotResolution = game.GetViewport().GetTexture().GetSize().ToString(),
                previewOnlyScaleMultiplier = 2.5f,
                estimatedCrownHeightAboveFaceInBodySpans = 1.34f,
                originalTransform = ChargeMatrix(originalTransform), originalGlobalTransform = ChargeMatrix(originalGlobalTransform),
                originalGlobalScale = new { originalGlobalScale.X, originalGlobalScale.Y },
                normalParentRestored, normalTransformRestored, normalSizeRestored,
                referenceEyePixels, finalEyePixels,
                motionScreenshotCount = pictures.Count,
                frames, pixels, checks
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var check in checks) Assert(check.Check, check.Passed);
    }

    private static ChargeFrame ReadChargeFrame(NCreature actor, ulong started)
    {
        var spine = actor.Visuals.SpineBody!;
        using var track = spine.GetAnimationState().GetCurrent(0);
        float alignment = ReadyPresentation.ChargeAlignmentError;
        return new((Time.GetTicksUsec() - started) / 1_000_000d, track?.GetTrackTime() ?? 0,
            ReadyPresentation.BodyPoseProgress, ReadyPresentation.ChargeStrength, ReadyPresentation.ChargeGlowVisible,
            float.IsFinite(alignment) ? alignment : null, spine.GetAnimationState().GetCurrentAnimationName(),
            actor.Visuals.Body.GlobalScale.X, actor.Visuals.Body.GlobalScale.Y, ReadHoverGuard(spine), ReadChargePose(actor));
    }

    private static ChargePose? ReadChargePose(NCreature actor)
    {
        var spine = actor.Visuals.SpineBody!;
        if (spine.GetGlobalBoneTransform("Ys_face") is not { } face
            || spine.GetGlobalBoneTransform("Ys_xiongjia2") is not { } chest
            || spine.GetGlobalBoneTransform("Ys_lhand2") is not { } left
            || spine.GetGlobalBoneTransform("Ys_rhand") is not { } right
            || spine.GetGlobalBoneTransform("Ys_wuqi1") is not { } sword) return null;
        float span = face.Origin.DistanceTo(chest.Origin);
        Vector2 lp = left * new Vector2(8.82f, -1.72f), rp = right * new Vector2(10.39f, 3.41f);
        Vector2 tip = sword * new Vector2(206.7f, 0);
        return new(span, face.Origin.X, face.Origin.Y, face.Origin.Y - span * 1.34f,
            lp.X, lp.Y, rp.X, rp.Y, tip.X, tip.Y);
    }

    private static ChargePixels CompareChargePixels(Image on, Image off, Transform2D sword,
        float requested, float progress, float strength)
    {
        Vector2[] corners = [sword * new Vector2(45, -9), sword * new Vector2(203, -9),
            sword * new Vector2(45, 9), sword * new Vector2(203, 9)];
        int x0 = Math.Max(0, (int)Math.Floor(corners.Min(p => p.X))), y0 = Math.Max(0, (int)Math.Floor(corners.Min(p => p.Y)));
        int x1 = Math.Min(on.GetWidth() - 1, (int)Math.Ceiling(corners.Max(p => p.X)));
        int y1 = Math.Min(on.GetHeight() - 1, (int)Math.Ceiling(corners.Max(p => p.Y)));
        Transform2D inverse = sword.AffineInverse();
        int count = 0, changed = 0;
        double warm = 0, red = 0, light = 0, totalLocalX = 0;
        float minX = 203, maxX = 45;
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            Vector2 local = inverse * new Vector2(x + 0.5f, y + 0.5f);
            if (local.X < 45 || local.X > 203 || Math.Abs(local.Y) > 9) continue;
            count++;
            Color a = on.GetPixel(x, y), b = off.GetPixel(x, y);
            float dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            warm += Math.Max(0, dr - db);
            red += Math.Max(0, dr);
            light += Math.Max(0, dr * 0.2126f + dg * 0.7152f + db * 0.0722f);
            if (dr > 0.012f && dr - db > 0.006f)
            {
                changed++;
                totalLocalX += local.X;
                minX = Math.Min(minX, local.X);
                maxX = Math.Max(maxX, local.X);
            }
        }
        return new(requested, progress, strength, count, changed, warm, red, light,
            changed == 0 ? 0 : (float)(totalLocalX / changed), changed == 0 ? 0 : maxX - minX,
            on.GetWidth(), on.GetHeight());
    }

    private static int CountChargeEyePixels(Image image, ChargePose pose)
    {
        int count = 0;
        int x0 = Math.Max(0, (int)(pose.FaceX - pose.BodySpan * 0.25f));
        int x1 = Math.Min(image.GetWidth(), (int)(pose.FaceX + pose.BodySpan * 0.75f));
        int y0 = Math.Max(0, (int)(pose.FaceY - pose.BodySpan * 1.25f));
        int y1 = Math.Min(image.GetHeight(), (int)(pose.FaceY + pose.BodySpan * 0.05f));
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        {
            Color color = image.GetPixel(x, y);
            if (color.G > 0.32f && color.G > color.R * 1.25f && color.B > color.R * 1.2f) count++;
        }
        return count;
    }

    private static bool ChargeTransformsNear(Transform2D a, Transform2D b) => a.Origin.DistanceTo(b.Origin) < 0.01f
        && a.X.DistanceTo(b.X) < 0.001f && a.Y.DistanceTo(b.Y) < 0.001f;
    private static object ChargeMatrix(Transform2D t) => new
    { axisX = new { t.X.X, t.X.Y }, axisY = new { t.Y.X, t.Y.Y }, origin = new { t.Origin.X, t.Origin.Y } };
}
#endif
