using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace SaberPresentation;

internal static class Fx
{
    private static readonly Color Gold = new(1f, 0.72f, 0.18f);
    private static readonly Color White = new(1f, 0.97f, 0.74f);
    private static readonly Color Blue = new(0.3f, 0.75f, 1f);
    private static GradientTexture2D? _glow;
    private static CanvasItemMaterial? _add;
    private static Font? _font;
    private static Curve? _bladeCurve;
    private static Shader? _beamShader;
    private const string BeamShader = """
        shader_type canvas_item;
        render_mode blend_add;
        void fragment() {
            vec4 modulation = COLOR;
            vec2 uv = UV;
            float tip = smoothstep(0.0, 0.035, uv.x) * (1.0 - smoothstep(0.88, 1.0, uv.x));
            float ripple = sin(uv.x * 48.0 - TIME * 62.0) * 0.012 + sin(uv.x * 97.0 + TIME * 41.0) * 0.007;
            float d = abs(uv.y - 0.5 + ripple);
            float width = 0.20 * (0.50 + 0.50 * tip);
            float halo = exp(-d * d * 24.0) * 0.30;
            float outer = 1.0 - smoothstep(width - 0.06, width + 0.025, d);
            float core = 1.0 - smoothstep(width * 0.22, width * 0.55, d);
            float strands = pow(max(0.0, sin(uv.y * 240.0 + sin(uv.x * 20.0 - TIME * 50.0))), 10.0);
            vec3 gold = vec3(1.0, 0.62, 0.075);
            vec3 white = vec3(1.0, 0.99, 0.79);
            vec3 light = gold * (halo + outer * 0.62) + white * core * 0.78;
            light += vec3(0.6, 0.76, 1.0) * strands * outer * 0.16;
            float fade = smoothstep(0.0, 0.02, uv.x) * (1.0 - smoothstep(0.95, 1.0, uv.x));
            COLOR = vec4(light, (halo + outer * 0.9) * fade) * modulation;
        }
        """;
    public static Node Container(Node owner) => (Node?)NCombatRoom.Instance?.CombatVfxContainer ?? owner.GetParent();
    private static CanvasItemMaterial Add => _add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private static Vector2 Local(Node parent, Vector2 global) => parent is CanvasItem item ? item.GetGlobalTransform().AffineInverse() * global : global;
    private static bool Valid(Node node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree();
    private static Node2D Root(Node parent, Vector2 position)
    {
        var root = new Node2D { Name = "SaberEffect", Position = Local(parent, position) };
        parent.AddChild(root);
        return root;
    }
    private static void Fade(Node2D node, double time, double hold = 0)
    {
        var tween = node.CreateTween();
        if (hold > 0) tween.TweenInterval(hold);
        tween.TweenProperty(node, "modulate:a", 0f, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(node.QueueFree));
    }
    private static Line2D Line(Node2D parent, Vector2[] points, float width, Color color, bool additive = true)
    {
        var line = new Line2D { Points = points, Width = width, DefaultColor = color,
            Antialiased = true, BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round,
            Material = additive ? Add : null };
        parent.AddChild(line);
        return line;
    }
    private static Sprite2D Glow(Node2D parent, float radius, Color color)
    {
        _glow ??= new GradientTexture2D {
            Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1, 0.5f),
            Gradient = new Gradient { Colors = [Colors.White, new Color(1, 1, 1, 0)], Offsets = [0, 1] }
        };
        var sprite = new Sprite2D { Texture = _glow, Material = Add, Modulate = color, Scale = Vector2.One * (radius / 64) };
        parent.AddChild(sprite);
        return sprite;
    }
    private static void Sparks(Node2D root, int count, float distance, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.399963f;
            var direction = Vector2.FromAngle(angle);
            float length = distance * (0.5f + 0.5f * ((i * 17 % 11) / 10f));
            var spark = Line(root, [direction * 8, direction * (16 + i % 4 * 3)], i % 3 == 0 ? 3.5f : 1.6f, color);
            root.CreateTween().TweenProperty(spark, "position", direction * length, 0.2 + i % 4 * 0.04).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
    }

    public static void Slash(Node parent, Vector2 point, bool heavy, int hit, AttackRoute route = AttackRoute.Slash)
    {
        CznAssets.Impact(parent, point, heavy, hit, route);
        var root = Root(parent, point);
        root.Rotation = (hit % 2 == 0 ? -0.55f : 0.7f) + (heavy ? -0.3f : 0);
        if (route == AttackRoute.Rising) root.Rotation = -1.3f;
        if (route == AttackRoute.Aerial) root.Rotation = 1.05f;
        if (route == AttackRoute.Sweep) root.Rotation = hit % 2 == 0 ? 0.15f : -0.2f;
        float size = heavy ? 152 : 106;
        var points = Enumerable.Range(0, 25).Select(i => {
            float a = -2.5f + i / 24f * 4.15f;
            return new Vector2(MathF.Cos(a) * size, MathF.Sin(a) * size * 0.4f);
        }).ToArray();
        Line(root, points, heavy ? 28 : 19, new Color(Gold, 0.25f));
        Line(root, points, heavy ? 12 : 7, Gold);
        Line(root, points, heavy ? 4 : 2.5f, White);
        _bladeCurve ??= MakeBladeCurve();
        foreach (var line in root.GetChildren().OfType<Line2D>()) line.WidthCurve = _bladeCurve;
        Glow(root, heavy ? 120 : 68, new Color(Gold, 0.7f));
        Sparks(root, heavy ? 19 : 11, heavy ? 160 : 100, White);
        root.Scale = Vector2.One * 0.65f;
        root.CreateTween().TweenProperty(root, "scale", Vector2.One * 1.2f, 0.21).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        Fade(root, heavy ? 0.32 : 0.24, 0.04);
        if (heavy && Settings.Current.ScreenShake) NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
    }
    public static void WindBlade(Node parent, Vector2 from, Vector2 to)
    {
        var root = Root(parent, from);
        Line(root, [new Vector2(-35, -24), new Vector2(18, 0), new Vector2(-35, 24)], 12, new Color(Blue, 0.3f));
        Line(root, [new Vector2(-35, -24), new Vector2(18, 0), new Vector2(-35, 24)], 3, White);
        root.CreateTween().TweenProperty(root, "position", Local(parent, to), 0.12);
        Fade(root, 0.18, 0.03);
    }
    public static void Dash(Node parent, Vector2 from, Vector2 to)
    {
        if (from.DistanceTo(to) < 25) return;
        CznAssets.Step(parent, from, to);
        var root = Root(parent, from);
        var difference = Local(parent, to) - Local(parent, from);
        for (int i = 0; i < 4; i++)
        {
            float y = (i - 1.5f) * 20;
            Line(root, [new Vector2(-60, y), difference * 0.75f + new Vector2(0, y)], 2 + i % 2, new Color(Blue, 0.26f));
        }
        Fade(root, 0.17);
    }
    public static void Hurt(Node parent, Vector2 center)
    {
        var root = Root(parent, center);
        Sparks(root, 9, 70, new Color(1, 0.57f, 0.35f));
        Fade(root, 0.2);
    }
    public static void Shield(Node parent, Vector2 center)
    {
        if (CznAssets.Shield(parent, center)) return;
        var root = Root(parent, center);
        var points = Enumerable.Range(0, 7).Select(i => Vector2.FromAngle(i * MathF.Tau / 6 - MathF.PI / 6) * 90).ToArray();
        Line(root, points, 10, new Color(Blue, 0.18f));
        Line(root, points, 2.5f, new Color(Blue, 0.85f));
        var inner = points.Select(p => p * 0.85f).ToArray();
        Line(root, inner, 1, new Color(White, 0.7f));
        Glow(root, 88, new Color(Blue, 0.25f));
        root.Scale = Vector2.One * 0.6f;
        root.CreateTween().TweenProperty(root, "scale", Vector2.One, 0.17).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        Fade(root, 0.3, 0.13);
    }
    public static void Buff(Node parent, Vector2 center)
    {
        if (CznAssets.Buff(parent, center)) return;
        var root = Root(parent, center + Vector2.Down * 80);
        var ring = Enumerable.Range(0, 49).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / 48) * 95, MathF.Sin(i * MathF.Tau / 48) * 28)).ToArray();
        Line(root, ring, 2, Gold);
        Glow(root, 100, new Color(Blue, 0.4f));
        for (int i = 0; i < 12; i++)
        {
            var spark = Line(root, [Vector2.Zero, Vector2.Up * 18], 2, new Color(White, 0.8f));
            spark.Position = new Vector2(MathF.Cos(i * 2.4f) * 80, MathF.Sin(i * 2.4f) * 20);
            root.CreateTween().TweenProperty(spark, "position:y", spark.Position.Y - 150 - i % 3 * 30, 0.55);
        }
        Fade(root, 0.5, 0.1);
    }
    public static void Charge(Node parent, Vector2 center)
    {
        var root = Root(parent, center);
        var glow = Glow(root, 180, new Color(Gold, 0.6f));
        glow.Scale *= 0.3f;
        root.CreateTween().TweenProperty(glow, "scale", Vector2.One * 4, 0.8);
        Line(root, [new Vector2(30, 80), new Vector2(30, -280)], 32, new Color(Gold, 0.2f));
        Line(root, [new Vector2(30, 80), new Vector2(30, -280)], 5, White);
        for (int i = 0; i < 24; i++)
        {
            var mote = Glow(root, 4 + i % 3, White);
            mote.Position = Vector2.FromAngle(i * 2.4f) * (170 + i % 5 * 25);
            root.CreateTween().TweenProperty(mote, "position", new Vector2(30, -75), 0.5 + i % 5 * 0.1);
        }
        Fade(root, 0.2, 0.85);
    }
    public static void Excalibur(Node parent, Vector2 from, Vector2[] targets, double hold = 0.13)
    {
        var root = Root(parent, from);
        Vector2 end = targets.OrderByDescending(p => p.X).FirstOrDefault(from + Vector2.Right * 700) + Vector2.Right * 260;
        var direction = Local(parent, end) - Local(parent, from);
        direction.Y *= 0.3f;
        var axis = new Node2D { Rotation = direction.Angle() };
        root.AddChild(axis);
        _beamShader ??= new Shader { Code = BeamShader };
        var beam = new ColorRect { Position = new Vector2(0, -150), Size = new Vector2(direction.Length(), 300),
            Material = new ShaderMaterial { Shader = _beamShader }, MouseFilter = Control.MouseFilterEnum.Ignore };
        axis.AddChild(beam);
        for (int i = 0; i < 18; i++)
        {
            float x = direction.Length() * ((i * 13 % 19) / 19f);
            float y = MathF.Sin(i * 2.4f) * 70;
            var streak = Line(axis, [new Vector2(x, y), new Vector2(x + 35 + i % 4 * 14, y)], 1.5f, new Color(White, 0.8f));
            axis.CreateTween().TweenProperty(streak, "position:x", 150f, 0.24);
        }
        Glow(root, 135, new Color(Gold, 0.7f));
        Sparks(root, 35, 310, White);
        root.Scale = new Vector2(0.15f, 0.4f);
        root.CreateTween().TweenProperty(root, "scale", Vector2.One, 0.1).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        Fade(root, 0.4, hold);
        foreach (var target in targets)
        {
            var burst = Root(parent, target);
            Glow(burst, 200, new Color(Gold, 0.7f));
            Sparks(burst, 24, 230, White);
            var ring = Enumerable.Range(0, 49).Select(i => Vector2.FromAngle(i * MathF.Tau / 48) * 70).ToArray();
            var shock = Line(burst, ring, 2.5f, new Color(White, 0.8f));
            burst.CreateTween().TweenProperty(shock, "scale", Vector2.One * 2.2f, 0.3).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            Fade(burst, 0.5, 0.08);
        }
        Voice.Beam(parent);
        if (Settings.Current.ScreenShake) NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Normal);
        if (Settings.Current.ScreenFlash) Flash(parent);
    }
    private static Curve MakeBladeCurve()
    {
        var curve = new Curve();
        foreach (var p in new[] { new Vector2(0, 0), new Vector2(0.23f, 0.7f), new Vector2(0.5f, 1), new Vector2(0.8f, 0.6f), new Vector2(1, 0) }) curve.AddPoint(p);
        return curve;
    }
    private static void Flash(Node parent)
    {
        var layer = new CanvasLayer { Layer = 65 };
        parent.AddChild(layer);
        var flash = new ColorRect { Color = new Color(1, 0.94f, 0.7f, 0.18f),
            Size = parent.GetViewport().GetVisibleRect().Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(flash);
        var tween = flash.CreateTween();
        tween.TweenProperty(flash, "color:a", 0f, 0.18);
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }

    public static async Task CutIn(Node owner, bool holdForPreview = false)
    {
        if (!Valid(owner)) return;
        var layer = new CanvasLayer { Name = "SaberExcaliburCutIn", Layer = 70 };
        owner.AddChild(layer);
        try
        {
            Vector2 size = owner.GetViewport().GetVisibleRect().Size;
            var stage = new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(stage);
            var bg = new ColorRect { Size = size, Color = new Color(0.008f, 0.025f, 0.065f, 0.92f), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(bg);
            var shapes = new Node2D(); stage.AddChild(shapes);
            var diagonal = new Polygon2D { Polygon = [new Vector2(size.X * 0.55f, 0), new Vector2(size.X, 0),
                size, new Vector2(size.X * 0.31f, size.Y)], Color = new Color(0.04f, 0.14f, 0.29f) };
            shapes.AddChild(diagonal);
            for (int i = 0; i < 14; i++)
            {
                float y = size.Y * (0.1f + i * 0.061f);
                var line = Line(shapes, [new Vector2(-100, y), new Vector2(size.X + 100, y - 35)], i % 4 == 0 ? 2 : 1,
                    new Color(i % 3 == 0 ? Gold : Blue, i % 3 == 0 ? 0.3f : 0.13f));
                stage.CreateTween().TweenProperty(line, "position:x", -240f, 1.1);
            }
            var texture = ResourceLoader.Load<Texture2D>("res://SaberPresentation/images/artoria.png");
            var portrait = new TextureRect { Texture = new AtlasTexture { Atlas = texture, Region = new Rect2(0, 0, 1024, 768) },
                Position = new Vector2(size.X * 0.55f + 180, size.Y * 0.02f), Size = new Vector2(size.Y * 1.25f, size.Y * 0.94f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(portrait);
            stage.CreateTween().TweenProperty(portrait, "position:x", size.X * 0.44f, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            var text = new Control { Position = new Vector2(size.X * 0.09f, size.Y * 0.38f), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(text);
            LabelAt(text, "S A B E R   /   A R T O R I A", 21, Blue, Vector2.Zero);
            LabelAt(text, "EXCALIBUR", 78, White, new Vector2(-5, 38));
            LabelAt(text, "誓约胜利之剑", 36, Gold, new Vector2(0, 133));
            LabelAt(text, "対城宝具  ·  A++", 19, new Color(0.7f, 0.8f, 0.9f), new Vector2(0, 190));
            Line(shapes, [new Vector2(size.X * 0.09f, size.Y * 0.69f), new Vector2(size.X * 0.45f, size.Y * 0.69f)], 2, Gold);
            if (holdForPreview) return;
#if SABER_TEST
            _ = MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(TestHarness.CaptureAfter("excalibur_cutin", 0.5));
#endif
            await Delay(0.95);
            if (Valid(stage))
            {
                stage.CreateTween().TweenProperty(stage, "modulate:a", 0f, 0.16);
                await Delay(0.17);
            }
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
