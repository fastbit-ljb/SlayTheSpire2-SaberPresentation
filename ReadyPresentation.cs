using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SaberPresentation;

// One owner per combat room. Card focus, targeting, cancellation and execution
// share this state so a late unfocus cannot hide the next card's portrait.
internal static class ReadyPresentation
{
    private static NCombatRoom? _room;
    private static NCreature? _actor;
    private static Node2D? _screen;
    private static Node2D? _portrait;
    private static Polygon2D[] _planes = [];
    private static Sprite2D? _actionSprite;
    private static ShaderMaterial? _actionFade;
    private static CardModel? _actionCard;
    private static Vector2[] _base = [], _uv = [];
    private static SceneTree? _tree;
    private static CardModel? _card;
    private static bool _targeting, _readyBody, _acting;
    private static int _generation, _pose;
    private static ulong _lastVoice;
    private static Tween? _transition;
    private static Vector2 _home;
    private const int Columns = 32, Rows = 48;
    private static ShaderMaterial? _swordLight;
    internal static bool AnimePortraitReady => _planes.Length == 2 && _planes.All(Valid);
    internal static string? PortraitSource => _planes.Length > 0 ? _planes[0].Texture.ResourcePath : null;
    internal static Vector2 PortraitResolution => _planes.Length > 0 ? _planes[0].Texture.GetSize() : Vector2.Zero;
    internal static bool Visible => _portrait is { } portrait && GodotObject.IsInstanceValid(portrait) && portrait.Visible && portrait.Modulate.A > 0.5f;
    internal static bool Targeting => _targeting;
    internal static bool BodyReady => _readyBody;
    internal static CardModel? Card => _card;
    internal static int Pose => _pose;
    internal static int FocusCount { get; private set; }
    internal static int PlayCount { get; private set; }
    internal static int ActionFramesPlayed { get; private set; }
    internal static bool ActionSwordTipsSafe { get; private set; } = true;

    private static bool Valid(Node? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();
    private static bool Accept(CardModel? card) => card?.Owner != null && LocalContext.IsMe(card.Owner)
        && Runtime.IsSaber(card.Owner.Creature) && !card.Owner.Creature.IsDead
        && !CombatManager.Instance.IsOverOrEnding;

    private static void Ensure(CardModel card)
    {
        if (Valid(_screen)) return;
        _room = NCombatRoom.Instance!;
        _actor = card.Owner.Creature.GetCreatureNode()!;
        _screen = new Node2D { Name = "SaberBattleReadyScreen", ZIndex = 20 };
        _room.CombatVfxContainer.AddChild(_screen);
        _portrait = new Node2D { Name = "SaberAnimatedPortrait", Modulate = new Color(1, 1, 1, 0), TopLevel = true };
        _screen.AddChild(_portrait);
        _base = new Vector2[(Columns + 1) * (Rows + 1)];
        _uv = new Vector2[_base.Length];
        for (int y = 0; y <= Rows; y++)
            for (int x = 0; x <= Columns; x++)
            {
                int i = y * (Columns + 1) + x;
                _base[i] = new Vector2((float)x / Columns * 1024 - 512, (float)y / Rows * 1536 - 1536);
                _uv[i] = new Vector2((float)x / Columns * 1024, (float)y / Rows * 1536);
            }
        var faces = new Godot.Collections.Array<int[]>();
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
            {
                int i = y * (Columns + 1) + x;
                faces.Add([i, i + 1, i + Columns + 2, i + Columns + 1]);
            }
        _planes = Enumerable.Range(0, 2).Select(i => {
            var plane = new Polygon2D { Name = "PortraitMesh" + i, Polygon = _base, UV = _uv,
                Polygons = (Godot.Collections.Array)faces, Texture = Art.Texture(i == 0 ? "saber_ready_attack" : "saber_ready_cast"),
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                Modulate = new Color(1, 1, 1, i == 0 ? 1 : 0) };
            _portrait.AddChild(plane);
            return plane;
        }).ToArray();
        _swordLight = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            uniform float phase = 0.0;
            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                float warm = smoothstep(0.1, 0.32, tex.r - tex.b);
                float blade = 1.0 - smoothstep(0.18, 0.25, UV.y);
                float sweep = pow(max(0.0, cos((UV.x + phase) * 11.0)), 16.0);
                tex.rgb += vec3(0.12, 0.095, 0.035) * warm * blade * sweep;
                COLOR = tex * COLOR;
            }
            """ } };
        _planes[0].Material = _swordLight;
        _actionFade = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                float body = 1.0 - smoothstep(0.72, 0.90, UV.x);
                tex.a *= 1.0 - smoothstep(0.92, 1.0, UV.y) * body;
                COLOR = tex * COLOR;
            }
            """ } };
        _actionSprite = new Sprite2D { Name = "SaberPortraitAttackAnimation", Texture = Art.Texture("saber_ready_attack"),
            Centered = false, Position = new Vector2(-512, -1536), Visible = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        _portrait.AddChild(_actionSprite);
        _tree = _room.GetTree();
        _tree.ProcessFrame += Tick;
        _room.TreeExiting += Cleanup;
        ApplyLayout();
    }

    private static void ApplyLayout()
    {
        if (!Valid(_portrait)) return;
        var size = _portrait!.GetViewport().GetVisibleRect().Size;
        float scale = size.Y * 1.17f / 1536 * Settings.Current.PortraitScale;
        _portrait.GlobalScale = Vector2.One * scale;
        _home = new Vector2(size.X * 0.135f + Settings.Current.PortraitOffsetX, size.Y * 1.29f + Settings.Current.PortraitOffsetY);
    }
    private static void Tick()
    {
        if (!Valid(_portrait) || !_portrait!.Visible || _portrait.Modulate.A < 0.001f) return;
        ApplyLayout();
        float t = Time.GetTicksMsec() / 1000f;
        for (int pose = 0; pose < _planes.Length; pose++)
        {
            var plane = _planes[pose];
            if (!Valid(plane) || plane.Modulate.A < 0.002f) continue;
            var vertices = new Vector2[_base.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = AnimatePortrait(_base[i], pose, t);
            plane.Polygon = vertices;
        }
        _swordLight?.SetShaderParameter("phase", t * -0.24f);
    }

    private static float Ramp(float low, float high, float value)
    {
        float x = Math.Clamp((value - low) / (high - low), 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static Vector2 AnimatePortrait(Vector2 point, int pose, float time)
    {
        // The approved anime artwork is the actual visible character. Dense,
        // smoothly weighted vertices animate separate cloth and ribbon regions;
        // the face, fingers, armor and blade keep their shape.
        float x = point.X + 512, y = point.Y + 1536;
        float waist = pose == 0 ? 700 : 500;
        float upper = 1 - Ramp(waist - 120, waist + 60, y);
        float breath = MathF.Sin(time * 1.65f);
        Vector2 pivot = new(0, waist - 1536);
        point = pivot + (point - pivot).Rotated(breath * 0.0018f * upper);
        point += new Vector2(breath * 1.6f, breath * -3.1f) * upper;

        float dress = Ramp(waist + 30, waist + 210, y) * (1 - Ramp(1280, 1490, y));
        float dressEdge = Ramp(120, 430, Math.Abs(x - 480));
        float wave = MathF.Sin(time * 2.25f - y * 0.005f + x * 0.003f);
        point.X += wave * 11f * dress * dressEdge;
        point.Y += MathF.Sin(time * 2.25f - y * 0.005f + x * 0.003f + 0.6f) * 4.5f * dress * dressEdge;

        float ribbon = pose == 0
            ? Ramp(600, 780, x) * Ramp(330, 460, y) * (1 - Ramp(580, 670, y))
            : (1 - Ramp(360, 440, x)) * Ramp(160, 225, y) * (1 - Ramp(285, 335, y));
        point += new Vector2(MathF.Sin(time * 2.8f + x * 0.013f) * 8,
            MathF.Cos(time * 2.8f + x * 0.013f) * 4) * ribbon;
        return point;
    }

    internal static void Focus(CardModel? card, bool targeting = false)
    {
        if (!Settings.Current.CardHoverPresentation || !Accept(card) || _acting) return;
        Ensure(card!);
        ApplyLayout();
        bool changed = _card != card;
        _card = card;
        _targeting |= targeting;
        _generation++;
        if (changed) FocusCount++;
        int pose = card!.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack ? 0 : 1;
        _pose = pose;
        _transition?.Kill();
        _portrait!.Rotation = 0;
        _portrait.Show();
        if (Valid(_actionSprite)) _actionSprite!.Hide();
        _transition = _portrait.CreateTween().SetParallel();
        if (_portrait.Modulate.A < 0.03f) _portrait.Position = _home + Vector2.Left * 180;
        _transition.TweenProperty(_portrait, "position", _home, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _transition.TweenProperty(_portrait, "modulate:a", Settings.Current.PortraitOpacity, 0.16);
        for (int i = 0; i < _planes.Length; i++)
            _transition.TweenProperty(_planes[i], "modulate:a", i == pose ? 1f : 0f, 0.13);
        ReadyBody(card, pose);
        if (changed && Time.GetTicksMsec() - _lastVoice > 3500 && Settings.Current.HoverVoice)
        {
            _lastVoice = Time.GetTicksMsec();
            Voice.PlayNext(_actor!, pose == 0 ? "attack" : "skill", pose == 0 ? 7 : 4);
        }
    }
    private static void ReadyBody(CardModel card, int pose)
    {
        if (!Settings.Current.ReadyBodyMotion || !Valid(_actor) || _acting || Runtime.IsAttacking(card.Owner.Creature)) return;
        _readyBody = true;
        _actor!.SetAnimationTrigger(pose == 0 ? "SaberReadyAttack" : "SaberReadyCast");
        using var track = _actor.Visuals.SpineBody?.GetAnimationState().GetCurrent(0);
        // Card focus must never freeze an attack halfway through its swing.
        // The battle actor keeps its supported natural idle while the large
        // portrait supplies the attack/skill selection feedback.
        track?.SetTimeScale(1);
    }
    internal static void Unfocus(CardModel? card)
    {
        if (card != _card || _targeting || _acting) return;
        int token = ++_generation;
        _ = TaskHelper.RunSafely(HideDelayed(token, 0.10));
    }
    private static async Task HideDelayed(int token, double delay)
    {
        await Fx.Delay(delay);
        if (token == _generation) Hide();
    }
    internal static void Cancel(CardModel? card)
    {
        if (card != null && _card != null && card != _card) return;
        _targeting = false;
        _acting = false;
        _actionCard = null;
        _generation++;
        Hide();
    }
    internal static void Play(CardModel? card)
    {
        if (!Accept(card)) return;
        if (_acting && _actionCard == card) return;
        PlayCount++;
        _targeting = false;
        _acting = true;
        _actionCard = card;
        RestoreBody();
        int token = ++_generation;
        _card = null;
        if (Valid(_portrait) && _portrait!.Modulate.A > 0.01f)
        {
            _transition?.Kill();
            _transition = _portrait.CreateTween().SetParallel();
            _transition.TweenProperty(_portrait, "position:x", _home.X + 70, 0.18).SetTrans(Tween.TransitionType.Cubic);
            _transition.TweenProperty(_portrait, "modulate:a", 0f, 0.18).SetDelay(0.25);
            if (card!.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack && !Art.IsNoblePhantasm(card))
                _ = TaskHelper.RunSafely(PlayAttackFrames(token));
        }
        _ = TaskHelper.RunSafely(EndAction(token));
    }
    private static async Task PlayAttackFrames(int token)
    {
        if (!Valid(_actionSprite)) return;
        foreach (var plane in _planes) plane.Modulate = new Color(1, 1, 1, 0);
        _actionSprite!.Show();
        // Each action is a full-resolution source, rather than an enlarged cell
        // from a sprite sheet. Wide action canvases include the complete blade.
        for (int frame = 0; frame < 4 && token == _generation && Valid(_actionSprite); frame++)
        {
            string texture = frame == 1 ? "saber_slash_anime_v4" : frame == 2 ? "saber_recover_anime_v4" : "saber_ready_attack";
            _actionSprite.Texture = Art.Texture(texture);
            _actionSprite.Position = frame == 1 ? new Vector2(-512, -1390) : frame == 2 ? new Vector2(-500, -1360) : new Vector2(-512, -1536);
            _actionSprite.Scale = Vector2.One * (frame == 2 ? 0.95f : 1f);
            _actionSprite.Material = frame is 1 or 2 ? _actionFade : null;
            if (frame is 1 or 2)
            {
                Vector2 tip = _actionSprite.ToGlobal(frame == 1 ? new Vector2(1518, 378) : new Vector2(1510, 955));
                Rect2 safe = _portrait!.GetViewport().GetVisibleRect().Grow(-25);
                ActionSwordTipsSafe &= safe.HasPoint(tip);
            }
            ActionFramesPlayed++;
            await Fx.Delay(frame == 1 ? 0.08 : 0.09);
        }
    }
    private static async Task EndAction(int token)
    {
        await Fx.Delay(0.47);
        if (token != _generation) return;
        _acting = false;
        _actionCard = null;
        Hide();
    }
    internal static void Hide()
    {
        _card = null;
        RestoreBody();
        if (!Valid(_portrait)) return;
        _transition?.Kill();
        _transition = _portrait!.CreateTween().SetParallel();
        _transition.TweenProperty(_portrait, "position:x", _home.X - 180, 0.21).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _transition.TweenProperty(_portrait, "modulate:a", 0f, 0.16);
    }
    private static void RestoreBody()
    {
        if (!_readyBody) return;
        _readyBody = false;
        if (!Valid(_actor)) return;
        using var track = _actor!.Visuals.SpineBody?.GetAnimationState().GetCurrent(0);
        track?.SetTimeScale(1);
        if (!_actor.Entity.IsDead && !Runtime.IsAttacking(_actor.Entity)) _actor.SetAnimationTrigger("Idle");
    }
    internal static Vector2 MeshSample => _planes.Length > 0 && Valid(_planes[0]) ? _planes[0].Polygon[36 * (Columns + 1) + 29] : Vector2.Zero;
    internal static void Cleanup()
    {
        _generation++;
        _targeting = _acting = false;
        RestoreBody();
        if (GodotObject.IsInstanceValid(_tree)) _tree!.ProcessFrame -= Tick;
        if (GodotObject.IsInstanceValid(_room)) _room!.TreeExiting -= Cleanup;
        _transition?.Kill();
        if (GodotObject.IsInstanceValid(_screen)) _screen!.QueueFree();
        _screen = _portrait = null;
        _room = null; _actor = null; _card = null; _tree = null;
        _planes = [];
        _swordLight = null;
        _actionFade = null;
        _actionSprite = null; _actionCard = null;
    }
}
