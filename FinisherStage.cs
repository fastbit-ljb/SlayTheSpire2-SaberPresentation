using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SaberPresentation;

/// <summary>
/// A battle-connected Excalibur timeline.  The live combat bodies are staged on
/// the same canvas as the animated CG, then restored before native death cleanup.
/// This follows the actor/target/stage split used by the Chizuru presentation.
/// </summary>
internal static class FinisherStage
{
    private sealed class VisualSnapshot
    {
        public required Node2D Node;
        public required Node Parent;
        public int Index;
        public Transform2D Transform;
        public Transform2D ScreenTransform;
        public int ZIndex;
        public bool ZAsRelative;
        public Color Modulate;
        public bool Visible;
        public Vector2 HitLocal;
        public readonly List<Tween> Motions = [];
    }

    private static Action? _cancel;
    internal static bool Active { get; private set; }
    internal static float LastActorScreenDrift { get; private set; }
    internal static float LastTargetScreenDrift { get; private set; }
    internal static double LastVisibleBeamSeconds { get; private set; }
    internal static double LastVisibleStageSeconds { get; private set; }
    internal static int LastVisibleReleaseSamples { get; private set; }
    internal static bool LastTargetHit { get; private set; }
    internal static bool LastEnvironmentChanged { get; private set; }
    internal static float LastActorScaleMultiplier { get; private set; }
    internal static int LastReleaseCount { get; private set; }
    internal static double LastVideoSeconds { get; private set; }
    internal static bool LastStageStarted { get; private set; }
    internal static bool LastEmitterOnForwardSword { get; private set; }
    internal static bool LastEmitterUsesSwordBone { get; private set; }
    internal static void Cancel() => _cancel?.Invoke();

    internal static async Task<bool> Play(NCreature actor, NCreature? target, double fallbackDuration)
    {
        LastStageStarted = false;
        if (!Valid(actor) || actor.Visuals?.Body is not { } actorBody || !Valid(actorBody)) return false;
        var host = (Node?)NCombatRoom.Instance ?? actor;
        var viewport = host.GetViewport();
        if (!GodotObject.IsInstanceValid(viewport)) return false;

        ReadyPresentation.Hide();
        using var music = AnimeMusic.PauseForCinematic();
        Active = true;
        LastActorScreenDrift = 0;
        LastTargetScreenDrift = 0;
        LastVisibleBeamSeconds = 0;
        LastVisibleStageSeconds = 0;
        LastVisibleReleaseSamples = 0;
        LastTargetHit = false;
        LastEnvironmentChanged = false;
        LastActorScaleMultiplier = 1f;
        LastReleaseCount = 0;
        LastVideoSeconds = 0;
        LastEmitterOnForwardSword = false;
        LastEmitterUsesSwordBone = false;

        var layer = new CanvasLayer { Name = "SaberIntegratedFinisher", Layer = 70 };
        host.AddChild(layer);
        var size = viewport.GetVisibleRect().Size;
        var stage = new Control
        {
            Name = "SaberFinisherStage",
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true
        };
        layer.AddChild(stage);
        VisualSnapshot? actorSnapshot = null;
        VisualSnapshot? targetSnapshot = null;
        var hiddenDebris = new List<(GodotObject Slot, Color Color)>();
        bool cancelled = false;
        void Cleanup()
        {
            if (cancelled) return;
            cancelled = true;
            try { Restore(targetSnapshot); }
            finally
            {
                try { Restore(actorSnapshot); }
                finally
                {
                    try
                    {
                        try { BodyAnimation(actor, "idle_loop", loop: true); }
                        finally
                        {
                            foreach (var (slot, color) in hiddenDebris)
                            {
                                try { if (GodotObject.IsInstanceValid(slot)) slot.Call("set_color", color); }
                                catch (Exception ex) { Log.Warn("[SaberPresentation] Debris color restore: " + ex.Message); }
                                finally { slot.Dispose(); }
                            }
                        }
                    }
                    finally
                    {
                        if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
                        Active = false;
                    }
                }
            }
        }
        _cancel = Cleanup;
        try
        {
            var background = new TextureRect
            {
                Name = "SaberFinisherEnvironment",
                Texture = Art.Texture("camelot_stage"),
                Size = size,
                PivotOffset = size * 0.5f,
                Scale = Vector2.One * 1.075f,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(0.42f, 0.55f, 0.72f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 0
            };
            stage.AddChild(background);
            var atmosphere = new ColorRect
            {
                Name = "SaberFinisherAtmosphere",
                Size = size,
                Color = new Color(0.015f, 0.055f, 0.15f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 1
            };
            stage.AddChild(atmosphere);
            var effects = new Node2D { Name = "SaberFinisherEffects", ZIndex = 80 };
            stage.AddChild(effects);
            var targetAnchor = new Node2D { Name = "SaberFinisherTarget", ZIndex = 18 };
            stage.AddChild(targetAnchor);
            var actorAnchor = new Node2D { Name = "SaberFinisherActor", ZIndex = 24 };
            stage.AddChild(actorAnchor);

            actorSnapshot = Stage(actorBody, actorAnchor, actor.VfxSpawnPosition);
            // The original heavy includes rocks large enough to cover the face
            // at cinematic scale; retain the sword motion and our target VFX.
            using (var skeleton = actor.Visuals.SpineBody?.GetSkeleton())
            if (skeleton != null)
                foreach (string name in new[] { "Eff-stone0001", "Eff-stone1", "Eff-stone2", "Eff-stone3" })
                {
                    var slot = skeleton.BoundObject.Call("find_slot", name).AsGodotObject();
                    if (slot == null) continue;
                    var color = slot.Call("get_color").AsColor();
                    hiddenDebris.Add((slot, color));
                    slot.Call("set_color", new Color(color.R, color.G, color.B, 0));
                }
            if (target?.Visuals?.Body is { } targetBody && Valid(targetBody))
                targetSnapshot = Stage(targetBody, targetAnchor, target.VfxSpawnPosition);

            LastStageStarted = true;
            var envIn = stage.CreateTween().SetParallel(true);
            envIn.TweenProperty(background, "modulate:a", 1f, 0.20);
            envIn.TweenProperty(background, "scale", Vector2.One, 1.10)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            envIn.TweenProperty(atmosphere, "color:a", 0.16f, 0.28);

            Vector2 actorStart = actorSnapshot.Node.Position;
            Vector2 actorScale = actorSnapshot.Node.Scale;
            Vector2? targetStart = targetSnapshot?.Node.Position;
            // Excalibur is a planted, sustained release. The close-up comes
            // from the cut-in camera; neither combatant slides to a stage mark.

            BodyAnimation(actor, "attack", 0.30f);
            await Fx.Delay(0.76);
            if (cancelled || !Valid(stage) || !Valid(actorSnapshot.Node)) return false;
            LastActorScaleMultiplier = actorSnapshot.Node.Scale.Length() / actorScale.Length();
            LastEnvironmentChanged = background.Modulate.A > 0.95f && atmosphere.Color.A > 0.12f;
            // Invisible Air: collect blue-white wind along the raised blade,
            // rather than placing an explosion on Saber's chest.
            var raisedBlade = actor.Visuals.SpineBody?.GetGlobalBoneTransform("Ys_wuqi1");
            Vector2 grip = raisedBlade?.Origin ?? actorSnapshot.Node.ToGlobal(actorSnapshot.HitLocal);
            Vector2 bladeAxis = raisedBlade?.X.Normalized() ?? Vector2.Up;
            var wind = Fx.Painted(effects, grip + bladeAxis * 95f, 3, new Vector2(105f, 215f), 0.30,
                rotation: bladeAxis.Angle() + Mathf.Pi * 0.5f, frameOrder: [3, 2, 1]);
            wind.Modulate = new Color(0.60f, 0.86f, 1f, 0.55f);
            wind.ZIndex = -70;
            wind.CreateTween().TweenProperty(wind, "scale", wind.Scale * 0.48f, 0.28)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
#if SABER_TEST
            _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_stationary_charge", 0.01));
#endif
            await Fx.Delay(0.34);
            if (cancelled || !Valid(stage)) return false;
            Voice.Play(actor, "np_charge", priority: true);
            bool released = false, hitReached = false, strokePrepared = false;
            void PrepareStroke()
            {
                if (strokePrepared || cancelled || !Valid(actor)) return;
                strokePrepared = true;
                // Prepare the forward/downward release behind the full-screen
                // close-up. The actor is already in this pose when revealed.
                BodyAnimation(actor, "attack_heavy", holdProgress: 0.40f);
            }
            void Release()
            {
                if (released || cancelled || !Valid(actor)) return;
                released = true;
                LastReleaseCount++;
                var releaseLight = stage.CreateTween().SetParallel(true);
                releaseLight.TweenProperty(background, "modulate", new Color(0.90f, 0.80f, 0.64f, 1f), 0.35);
                releaseLight.TweenProperty(atmosphere, "color", new Color(0.18f, 0.10f, 0.015f, 0.04f), 0.35);
                Voice.PlaySequence(actor, ["np_release", "np_end"], priority: true);
            }
            async Task Hit()
            {
                if (hitReached || cancelled || !Valid(stage)) return;
                hitReached = true;
                if (!strokePrepared)
                {
                    PrepareStroke();
                    await Fx.Delay(0.14);
                }
                if (cancelled || !Valid(stage) || !Valid(actorSnapshot.Node)) return;
                using (var stroke = actor.Visuals.SpineBody?.GetAnimationState().GetCurrent(0))
                    stroke?.SetTimeScale(0.11f);
                actorSnapshot.Node.Modulate = Colors.White;
                Vector2 center = actorSnapshot.Node.ToGlobal(actorSnapshot.HitLocal);
                Vector2 weapon = SwordTip(actor, center);
                LastEmitterOnForwardSword = weapon.X > center.X + 30f;
                Vector2 impact = targetSnapshot != null && Valid(targetSnapshot.Node)
                    ? targetSnapshot.Node.ToGlobal(targetSnapshot.HitLocal) : new Vector2(size.X * 0.73f, size.Y * 0.59f);
                Fx.Excalibur(effects, weapon, [impact], 1.68,
                    followOrigin: () => Valid(actor) && Valid(actorSnapshot.Node)
                        ? SwordTip(actor, actorSnapshot.Node.ToGlobal(actorSnapshot.HitLocal)) : weapon);
                var impactLight = background.CreateTween();
                impactLight.TweenProperty(background, "modulate", new Color(1f, 0.91f, 0.75f, 1f), 0.08);
                impactLight.TweenInterval(1.85);
                impactLight.TweenProperty(background, "modulate", new Color(0.68f, 0.71f, 0.77f, 1f), 0.55);
                LastTargetHit = targetSnapshot != null && Valid(targetSnapshot.Node);
                if (LastTargetHit)
                {
                    var node = targetSnapshot!.Node;
                    float x = node.Position.X;
                    node.Modulate = Colors.White;
                    var hit = node.CreateTween();
                    targetSnapshot.Motions.Add(hit);
                    hit.TweenProperty(node, "position:x", x + 28f, 0.055);
                    hit.TweenProperty(node, "position:x", x - 20f, 0.065);
                    hit.TweenProperty(node, "position:x", x + 12f, 0.055);
                    hit.TweenProperty(node, "position:x", x, 0.065);
                    var flash = node.CreateTween();
                    targetSnapshot.Motions.Add(flash);
                    flash.TweenProperty(node, "modulate", new Color(2.1f, 2.1f, 2.1f, 1f), 0.05);
                    flash.TweenProperty(node, "modulate", Colors.White, 0.20);
                }
#if SABER_TEST
                _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_stage_hit", 0.08));
                _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_release_030", 0.30));
                _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_release_080", 0.80));
                _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_release_130", 1.30));
#endif
            }
            bool usedVideo = await PlayCg(stage, actorSnapshot, targetSnapshot, size, fallbackDuration, Release, PrepareStroke, Hit);
            if (cancelled || !Valid(stage)) return usedVideo;
            Release();
            if (!hitReached) { await Fx.Delay(0.13); await Hit(); }
            if (cancelled || !Valid(stage)) return usedVideo;
            // Measure the actual revealed stage and live beam, rather than
            // keeping a hidden movie running and hoping the hit is visible.
            ulong liveStart = Time.GetTicksMsec();
            ulong previous = liveStart;
            while (Time.GetTicksMsec() - liveStart < 2180)
            {
                await Fx.Delay(0.04);
                if (cancelled || !Valid(stage)) return usedVideo;
                ulong now = Time.GetTicksMsec();
                double elapsed = (now - previous) / 1000.0;
                previous = now;
                LastActorScreenDrift = Math.Max(LastActorScreenDrift, actorSnapshot.Node.Position.DistanceTo(actorStart));
                if (targetSnapshot != null && targetStart.HasValue)
                    LastTargetScreenDrift = Math.Max(LastTargetScreenDrift, targetSnapshot.Node.Position.DistanceTo(targetStart.Value));
                bool stageVisible = background.Modulate.A > 0.95f && background.Modulate.R > 0.75f
                    && actorSnapshot.Node.Modulate.A > 0.95f
                    && (targetSnapshot == null || targetSnapshot.Node.Modulate.A > 0.95f)
                    && !stage.GetChildren().OfType<VideoStreamPlayer>().Any(v => v.Modulate.A > 0.05f);
                if (stageVisible) LastVisibleStageSeconds += elapsed;
                bool beamVisible = effects.GetChildren().OfType<Sprite2D>()
                    .Any(s => s.Name == "SaberExcaliburBeam" && s.IsVisibleInTree());
                if (stageVisible && beamVisible)
                {
                    LastVisibleBeamSeconds += elapsed;
                    LastVisibleReleaseSamples++;
                }
            }
            if (cancelled || !Valid(stage)) return usedVideo;

            // Let the authored recovery raise the blade after the beam ends.
            using (var recovery = actor.Visuals.SpineBody?.GetAnimationState().GetCurrent(0))
            {
                if (recovery != null)
                {
                    recovery.SetTrackTime(recovery.GetAnimationDuration() * 0.72f);
                    recovery.SetTimeScale(1f);
                }
            }
            await Fx.Delay(0.45);
            if (cancelled || !Valid(stage)) return usedVideo;
            BodyAnimation(actor, "idle_loop", loop: true);

            var outro = stage.CreateTween().SetParallel(true);
            outro.TweenProperty(background, "modulate:a", 0f, 0.40);
            outro.TweenProperty(atmosphere, "color:a", 0f, 0.40);
            await Fx.Delay(0.42);
            return usedVideo;
        }
        finally
        {
            try { Cleanup(); }
            finally { if (_cancel == Cleanup) _cancel = null; }
        }
    }

    private static async Task<bool> PlayCg(Control stage, VisualSnapshot actorSnapshot, VisualSnapshot? targetSnapshot,
        Vector2 size, double fallbackDuration, Action release, Action prepareStroke, Func<Task> hit)
    {
        var actor = actorSnapshot.Node;
        var target = targetSnapshot?.Node;
        if (Art.ExcaliburVideo is not { } stream)
        {
            await Fx.Delay(Math.Max(0.45, fallbackDuration));
            return false;
        }

        var video = new VideoStreamPlayer
        {
            Name = "SaberOfficialExcaliburAnimation",
            Stream = stream,
            Size = size,
            Modulate = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 20
        };
        video.Set("expand", true);
        stage.AddChild(video);
        // World actors stay on their original battle marks. The movie provides
        // the close-up; it gives the whole stage back before the actual beam.
        if (target != null) target.ZIndex = 32;
        actor.ZIndex = 34;
        video.Play();
        await Fx.Delay(0.08);
        if (!Valid(stage) || !Valid(video)) return false;
        if (!video.IsPlaying())
        {
            video.QueueFree();
            return false;
        }

        var intro = video.CreateTween().SetParallel(true);
        intro.TweenProperty(video, "modulate:a", 1f, 0.16);
        intro.TweenProperty(actor, "modulate:a", 0f, 0.18);
        actorSnapshot.Motions.Add(intro);
        if (target != null) intro.TweenProperty(target, "modulate:a", 0f, 0.18);
#if SABER_TEST
        _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_integrated_cg", 0.38));
#endif
        bool released = false, hitReached = false;
        while (Valid(stage) && Valid(video) && video.IsPlaying())
        {
            LastVideoSeconds = video.StreamPosition;
            if (!released && LastVideoSeconds >= 4.55)
            {
                released = true;
                release();
            }
            if (LastVideoSeconds >= 7.10) prepareStroke();
            if (!hitReached && LastVideoSeconds >= 7.50)
            {
                hitReached = true;
#if SABER_TEST
                _ = TaskHelper.RunSafely(TestHarness.CaptureHandoffSequence());
#endif
                var handoff = video.CreateTween().SetParallel(true);
                handoff.TweenProperty(video, "modulate:a", 0f, 0.20);
                handoff.TweenProperty(actor, "modulate:a", 1f, 0.20);
                if (target != null) handoff.TweenProperty(target, "modulate:a", 1f, 0.20);
                await Fx.Delay(0.26);
                if (!Valid(stage) || !Valid(video)) return true;
                video.Stop();
                video.QueueFree();
#if SABER_TEST
                _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("finisher_camelot_establish", 0.01));
#endif
                await Fx.Delay(0.18);
                if (!Valid(stage)) return true;
                await hit();
                return true;
            }
            if (Input.IsActionJustPressed("ui_cancel")) video.Stop();
            await Fx.Delay(0.05);
        }
        if (Valid(video) && Valid(actor))
        {
            var outro = video.CreateTween().SetParallel(true);
            outro.TweenProperty(video, "modulate:a", 0f, 0.16);
            outro.TweenProperty(actor, "modulate:a", 1f, 0.16);
            if (target != null) outro.TweenProperty(target, "modulate:a", 1f, 0.16);
            await Fx.Delay(0.17);
            video.QueueFree();
        }
        return true;
    }

    private static VisualSnapshot Stage(Node2D node, Node stageParent, Vector2 hitGlobal)
    {
        var snapshot = new VisualSnapshot
        {
            Node = node,
            Parent = node.GetParent(),
            Index = node.GetIndex(),
            Transform = node.Transform,
            ZIndex = node.ZIndex,
            ZAsRelative = node.ZAsRelative,
            Modulate = node.Modulate,
            Visible = node.Visible,
            HitLocal = node.ToLocal(hitGlobal),
            ScreenTransform = node.GetGlobalTransformWithCanvas()
        };
        Transform2D screenTransform = node.GetGlobalTransformWithCanvas();
        node.Reparent(stageParent, false);
        node.Transform = screenTransform;
        node.ZAsRelative = true;
        node.Visible = true;
        return snapshot;
    }

    private static void Restore(VisualSnapshot? snapshot)
    {
        if (snapshot == null || !GodotObject.IsInstanceValid(snapshot.Node)) return;
        foreach (var tween in snapshot.Motions) if (GodotObject.IsInstanceValid(tween)) tween.Kill();
        if (GodotObject.IsInstanceValid(snapshot.Parent) && snapshot.Parent.IsInsideTree())
        {
            snapshot.Node.Reparent(snapshot.Parent, false);
            snapshot.Parent.MoveChild(snapshot.Node, Math.Clamp(snapshot.Index, 0, snapshot.Parent.GetChildCount() - 1));
            snapshot.Node.Transform = snapshot.Transform;
            snapshot.Node.ZIndex = snapshot.ZIndex;
            snapshot.Node.ZAsRelative = snapshot.ZAsRelative;
            snapshot.Node.Modulate = snapshot.Modulate;
            snapshot.Node.Visible = snapshot.Visible;
        }
    }

    private static Vector2 SwordTip(NCreature actor, Vector2 center)
    {
        // Ys_wuqi2 is the forward blade on the authored downward heavy stroke.
        // Read it only after the Spine update, in the staged body's coordinates.
        var spine = actor.Visuals.SpineBody;
        if (spine?.GetGlobalBoneTransform("Ys_wuqi2") is { } blade)
        {
            Vector2 tip = blade.Origin + blade.X.Normalized() * 50f;
            if (tip.X > center.X + 30f && tip.DistanceTo(center) < 600f)
            {
                LastEmitterUsesSwordBone = true;
                return tip;
            }
        }
        return center + new Vector2(145f, 75f);
    }

    private static void BodyAnimation(NCreature actor, string clip, float? holdProgress = null, bool loop = false, float startProgress = 0f)
    {
        if (!Valid(actor) || actor.Visuals?.SpineBody is not { } spine || !spine.HasAnimation(clip)) return;
        var animation = spine.GetAnimationState();
        animation.SetAnimation(clip, loop);
        using var track = animation.GetCurrent(0);
        if (track == null) return;
        track.SetMixDuration(0.12f);
        track.SetTimeScale(holdProgress.HasValue ? 0f : 1f);
        track.SetTrackTime(track.GetAnimationDuration() * (holdProgress ?? startProgress));
    }

    private static bool Valid(Node? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();
}
