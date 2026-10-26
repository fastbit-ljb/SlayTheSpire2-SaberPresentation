using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace SaberPresentation;

// Painted flipbooks and installed native Spine VFX replace the old line shapes.
internal static class Fx
{
    private static CanvasItemMaterial? _add;
    private static Font? _font;
    internal static Vector2 LastExcaliburOrigin { get; private set; }
    internal static Vector2 LastExcaliburOriginSize { get; private set; }
    internal static bool LastCutInUsedVideo { get; private set; }
    internal static bool LastCutInIntegrated { get; private set; }
    public static Node Container(Node owner) => (Node?)NCombatRoom.Instance?.CombatVfxContainer ?? owner.GetParent();
    private static CanvasItemMaterial Add => _add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private static Vector2 Local(Node parent, Vector2 global) => parent is CanvasItem canvas
        ? canvas.GetGlobalTransform().AffineInverse() * global : global;
    private static bool Valid(Node? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();
    internal static Sprite2D Painted(Node parent, Vector2 point, int row, Vector2 size,
        double duration = 0.4, float rotation = 0, bool additive = true, int repeat = 1, int[]? frameOrder = null)
    {
        var texture = Art.Texture("saber_vfx_atlas");
        var sprite = new Sprite2D { Name = "SaberPaintedEffect", Texture = texture, Hframes = 4, Vframes = 4,
            Frame = row * 4, Position = Local(parent, point), Scale = size / (texture.GetSize() / 4),
            Rotation = rotation, Material = additive ? Add : null };
        parent.AddChild(sprite);
        _ = TaskHelper.RunSafely(Animate(sprite, row, duration, repeat, frameOrder));
        return sprite;
    }
    private static async Task Animate(Sprite2D sprite, int row, double duration, int repeat, int[]? frameOrder)
    {
        try
        {
            var frames = frameOrder ?? [0, 1, 2, 3];
            for (int loop = 0; loop < repeat; loop++)
                foreach (int frame in frames)
                {
                    if (!Valid(sprite)) return;
                    sprite.Frame = row * 4 + frame;
                    await Delay(duration / frames.Length);
                }
        }
        finally { if (GodotObject.IsInstanceValid(sprite)) sprite.QueueFree(); }
    }
    public static void Slash(Node parent, Vector2 point, bool heavy, int hit, AttackRoute route = AttackRoute.Slash)
    {
        CznAssets.Impact(parent, point, heavy, hit, route);
        float rotation = route == AttackRoute.Rising ? -1.25f : route == AttackRoute.Aerial ? 0.9f :
            route == AttackRoute.Sweep ? 0.08f : (hit % 2 == 0 ? -0.42f : 0.52f);
        Painted(parent, point, 0, Vector2.One * (heavy ? 315 : 245), 0.29, rotation);
        Painted(parent, point, 1, Vector2.One * (heavy ? 230 : 135), 0.30);
        if (heavy && Settings.Current.ScreenShake) NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
    }
    public static void WindBlade(Node parent, Vector2 from, Vector2 to)
    {
        var effect = Painted(parent, from, 3, new Vector2(240, 180), 0.28, (to - from).Angle());
        effect.CreateTween().TweenProperty(effect, "position", Local(parent, to), 0.18).SetTrans(Tween.TransitionType.Cubic);
    }
    public static void Dash(Node parent, Vector2 from, Vector2 to)
    {
        if (from.DistanceTo(to) < 25) return;
        CznAssets.Step(parent, from, to);
        var effect = Painted(parent, from, 3, new Vector2(310, 160), 0.24);
        effect.Modulate = new Color(0.8f, 0.93f, 1, 0.65f);
        effect.CreateTween().TweenProperty(effect, "position", Local(parent, to), 0.13);
    }
    public static void Hurt(Node parent, Vector2 center) => Painted(parent, center, 1, new Vector2(170, 170), 0.22);
    public static void Shield(Node parent, Vector2 center)
    {
        if (!CznAssets.Shield(parent, center)) Painted(parent, center, 3, new Vector2(260, 310), 0.62);
    }
    public static void Buff(Node parent, Vector2 center)
    {
        CznAssets.Buff(parent, center);
        Painted(parent, center + Vector2.Up * 20, 3, new Vector2(235, 325), 0.55);
    }
    public static void Charge(Node parent, Vector2 center)
    {
        var effect = Painted(parent, center + Vector2.Up * 90, 1, new Vector2(300, 400), 0.65);
        effect.Modulate = new Color(1, 0.93f, 0.68f, 0.6f);
        effect.Scale *= 0.5f;
        effect.CreateTween().TweenProperty(effect, "scale", effect.Scale * 2, 0.62).SetTrans(Tween.TransitionType.Cubic);
        CznAssets.Charge(parent, center);
    }
    public static void Excalibur(Node parent, Vector2 from, Vector2[] targets, double hold = 0.13, Func<Vector2>? followOrigin = null)
    {
        LastExcaliburOrigin = from;
        LastExcaliburOriginSize = new Vector2(128, 156);
        Vector2 aim = targets.OrderByDescending(p => p.X).FirstOrDefault(from + Vector2.Right * 700);
        Vector2 end = new(aim.X + 280, aim.Y);
        Vector2 axis = Local(parent, end) - Local(parent, from);
        Vector2 mid = from + (end - from) * 0.5f;
        var beam = Painted(parent, mid, 2, new Vector2(axis.Length() * 1.12f, 245), 0.48, axis.Angle(), repeat: Math.Max(1, (int)Math.Ceiling(hold / 0.48)));
        beam.Name = "SaberExcaliburBeam";
        if (followOrigin != null) _ = TaskHelper.RunSafely(FollowBeam(beam, parent, end, followOrigin));
        var muzzle = Painted(parent, from, 1, LastExcaliburOriginSize, 0.34);
        muzzle.Modulate = new Color(1, 0.94f, 0.72f, 0.82f);
        foreach (var target in targets)
        {
            Painted(parent, target, 1, new Vector2(400, 400), 0.52);
            CznAssets.FinisherImpact(parent, target);
        }
        Voice.Beam(parent);
        if (Settings.Current.ScreenShake) NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Normal);
        if (Settings.Current.ScreenFlash) Flash(parent);
    }
    private static async Task FollowBeam(Sprite2D beam, Node parent, Vector2 end, Func<Vector2> origin)
    {
        while (Valid(beam) && Valid(parent))
        {
            Vector2 from = origin();
            Vector2 axis = Local(parent, end) - Local(parent, from);
            beam.Position = Local(parent, (from + end) * 0.5f);
            beam.Rotation = axis.Angle();
            beam.Scale = new Vector2(axis.Length() * 1.12f, 245) / (beam.Texture.GetSize() / 4);
            await Delay(0.016);
        }
    }
    private static void Flash(Node parent)
    {
        var layer = new CanvasLayer { Name = "SaberFlash", Layer = FinisherStage.Active ? 75 : 65 };
        parent.AddChild(layer);
        var flash = new ColorRect { Color = new Color(1, 0.94f, 0.7f, 0.16f),
            Size = parent.GetViewport().GetVisibleRect().Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(flash);
        var tween = flash.CreateTween();
        tween.TweenProperty(flash, "color:a", 0f, 0.18);
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }
    internal static bool HasExcaliburVideo => Art.ExcaliburVideo != null;
    public static async Task<bool> CutIn(NCreature owner, NCreature? target = null, bool holdForPreview = false, double duration = 1.24)
    {
        if (!Valid(owner)) return false;
        if (!holdForPreview)
        {
            LastCutInUsedVideo = await FinisherStage.Play(owner, target, duration);
            LastCutInIntegrated = FinisherStage.LastStageStarted;
            return LastCutInUsedVideo;
        }
        using var music = AnimeMusic.PauseForCinematic();
        ReadyPresentation.Hide();
        var layer = new CanvasLayer { Name = "SaberExcaliburCutIn", Layer = 70 };
        owner.AddChild(layer);
        try
        {
            var size = owner.GetViewport().GetVisibleRect().Size;
            LastCutInUsedVideo = false;
            LastCutInIntegrated = false;
            if (Art.ExcaliburVideo is { } stream)
            {
                var backdrop = new ColorRect { Color = Colors.Black, Size = size,
                    MouseFilter = Control.MouseFilterEnum.Ignore };
                layer.AddChild(backdrop);
                var video = new VideoStreamPlayer { Name = "SaberOfficialExcaliburAnimation", Stream = stream,
                    Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
                video.Set("expand", true);
                layer.AddChild(video);
                video.Play();
                await Delay(0.08);
                if (video.IsPlaying())
                {
                    LastCutInUsedVideo = true;
#if SABER_TEST
                    _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("excalibur_video", 0.55));
#endif
                    while (Valid(video) && video.IsPlaying())
                    {
                        if (!holdForPreview && Input.IsActionJustPressed("ui_cancel")) video.Stop();
                        await Delay(0.05);
                    }
                    return true;
                }
                video.QueueFree();
                backdrop.QueueFree();
            }
            var stage = new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
            layer.AddChild(stage);
            var bg = new TextureRect { Texture = Art.Texture("camelot_stage"), Size = size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(0.28f, 0.39f, 0.58f), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(bg);
            var portrait = new TextureRect { Texture = Art.Texture("saber_ready_cast"),
                Position = new Vector2(size.X * 0.48f + 130, size.Y * 0.02f), Size = new Vector2(size.Y * 0.9f, size.Y * 1.4f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(portrait);
            var lighting = new Node2D(); stage.AddChild(lighting);
            Painted(lighting, size * new Vector2(0.63f, 0.45f), 1, new Vector2(size.Y, size.Y), 0.65,
                repeat: (int)Math.Ceiling(duration / 0.65)).Modulate = new Color(1, 0.9f, 0.6f, 0.32f);
            var top = new ColorRect { Size = new Vector2(size.X, size.Y * 0.085f), Color = new Color(0.005f, 0.01f, 0.025f), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(top);
            var bottom = new ColorRect { Position = new Vector2(0, size.Y * 0.875f), Size = new Vector2(size.X, size.Y * 0.125f), Color = top.Color, MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(bottom);
            var text = new Control { Position = new Vector2(size.X * 0.09f, size.Y * 0.38f), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(text);
            stage.CreateTween().TweenProperty(stage, "modulate:a", 1f, 0.12);
            portrait.CreateTween().TweenProperty(portrait, "position:x", size.X * 0.46f, 0.35).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            if (holdForPreview) return false;
#if SABER_TEST
            _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("excalibur_cutin", 0.5));
#endif
            await Delay(Math.Max(0.12, duration - 0.19));
            if (Valid(stage)) { stage.CreateTween().TweenProperty(stage, "modulate:a", 0f, 0.18); await Delay(0.19); }
            return false;
        }
        finally { if (!holdForPreview && GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
    }
    private static void LabelAt(Control parent, string content, int size, Color color, Vector2 position)
    {
        _font ??= new SystemFont { FontNames = ["Microsoft YaHei", "Yu Gothic", "Arial"] };
        var label = new Label { Text = content, Position = position, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", _font); label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color); parent.AddChild(label);
    }
    public static async Task Delay(double seconds)
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return;
        await game.ToSignal(game.GetTree().CreateTimer(Math.Max(0.01, seconds)), SceneTreeTimer.SignalName.Timeout);
    }
}
