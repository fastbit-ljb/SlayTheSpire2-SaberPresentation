using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace SaberPresentation;

// Resource-only effect adapter. All required atlas/skeleton/audio dependencies
// ship in this mod's private payload; no installed Nine character is required.
internal static class CznAssets
{
    private const string Nine = "res://CznNineCharacter/animations/basic_effects/";
    private const string Core = "res://CznCore/animations/common/";
    private static readonly Dictionary<string, Resource> Cache = [];
    private static readonly HashSet<string> Missing = [];
    private static readonly HashSet<string> Warned = [];
    internal static int Spawned { get; private set; }
    internal static int Failures { get; private set; }
    internal static int SoundsPlayed { get; private set; }

    private static Resource? Load(string path)
    {
        if (!Settings.Current.UseCznEffects) return null;
        if (Cache.TryGetValue(path, out var resource) && GodotObject.IsInstanceValid(resource)) return resource;
        if (Missing.Contains(path)) return null;
        if (!ResourceLoader.Exists(path)) { Missing.Add(path); return null; }
        try
        {
            resource = ResourceLoader.Load(path);
            if (resource != null) Cache[path] = resource;
            return resource;
        }
        catch (Exception ex) { Warn(path, ex); return null; }
    }
    private static void Warn(string path, Exception ex)
    {
        Failures++;
        if (Warned.Add(path)) Log.Warn("[SaberPresentation] CZN asset skipped: " + path + ": " + ex.Message);
    }
    private static bool Valid(Node node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree();
    private static Vector2 Local(Node parent, Vector2 global) => parent is CanvasItem canvas
        ? canvas.GetGlobalTransform().AffineInverse() * global : global;

    // A SpineSprite's own atlas blend modes are retained; source colors are
    // multiplied only where a slight warm/cool adjustment suits Saber's palette.
    private static bool Spine(Node parent, string path, Vector2 position, float scale,
        float speed = 1.4f, string animation = "animation", bool follow = false,
        Color? tint = null, float rotation = 0, int z = 0)
    {
        if (!Valid(parent) || Load(path) is not { } data) return false;
        Node2D? sprite = null;
        try
        {
            sprite = (Node2D)ClassDB.Instantiate("SpineSprite").AsGodotObject();
            sprite.Name = "SaberCznEffect";
            sprite.Set("skeleton_data_res", data);
            sprite.Position = follow ? Vector2.Zero : Local(parent, position);
            float parentScale = parent is Node2D parent2D ? Math.Abs(parent2D.GlobalScale.X) :
                parent is CanvasItem canvas ? Math.Abs(canvas.GetGlobalTransform().Scale.X) : 1;
            sprite.Scale = Vector2.One * (scale / Math.Max(0.001f, parentScale));
            sprite.Rotation = rotation;
            sprite.Modulate = tint ?? Colors.White;
            sprite.ZIndex = z;
            parent.AddChild(sprite);
            _ = TaskHelper.RunSafely(Animate(sprite, path, animation, speed));
            return true;
        }
        catch (Exception ex)
        {
            if (sprite != null && GodotObject.IsInstanceValid(sprite)) sprite.QueueFree();
            Warn(path, ex);
            return false;
        }
    }
    private static async Task Animate(Node2D sprite, string path, string animation, float speed)
    {
        try
        {
            MegaAnimationState? state = null;
            var spine = new MegaSprite(sprite);
            // Spine can initialize after AddChild. Bound the wait and avoid a
            // fixed animation clip assumption for the movement effects ("b").
            for (int i = 0; i < 8 && Valid(sprite); i++)
            {
                state = spine.TryGetAnimationState();
                if (state != null) break;
                await Fx.Delay(0.016);
            }
            if (!Valid(sprite) || state == null) return;
            if (!spine.HasAnimation(animation))
                animation = spine.GetSkeleton()?.GetData().GetAnimationNames().FirstOrDefault() ?? animation;
            if (!spine.HasAnimation(animation)) throw new InvalidOperationException("No playable animation");
            state.SetAnimation(animation, false);
            using (var entry = state.GetCurrent(0)) entry?.SetTimeScale(speed);
            Spawned++;
            double duration = (state.GetCurrentAnimationDuration() ?? 0.6f) / speed;
            await Fx.Delay(Math.Clamp(duration, 0.08, 4));
        }
        catch (Exception ex) { Warn(path, ex); }
        finally { if (GodotObject.IsInstanceValid(sprite)) sprite.QueueFree(); }
    }

    internal static void Swing(Node2D body, AttackRoute route, int hit)
    {
        if (!Valid(body)) return;
        string name = route switch {
            AttackRoute.Rising => "nine_30047_u2_attack_play_f_1",
            AttackRoute.Aerial => "nine_30047_attack_play2_self_f_1",
            AttackRoute.Heavy => "nine_30047_attack_play1_self_f",
            AttackRoute.Sweep => "nine_30047_attack_play2_self_f_1",
            _ => (hit & 1) == 0 ? "nine_30047_attack_play1_self_f" : "nine_30047_attack_play2_self_f_1"
        };
        Spine(body, Nine + name + "_skel_data.tres", Vector2.Zero, route == AttackRoute.Rising ? 0.31f : 0.36f,
            speed: 1.65f, follow: true, tint: new Color(1, 0.96f, 0.84f));
    }
    internal static void Impact(Node parent, Vector2 center, bool heavy, int hit, AttackRoute route)
    {
        string name = heavy ? "nine_30047_u1_attack_play_target_b" :
            (hit & 1) == 0 ? "nine_30047_attack_play1_target_b" : "nine_30047_attack_play2_target_b";
        Spine(parent, Nine + name + "_skel_data.tres", center + Vector2.Down * 80, heavy ? 0.42f : 0.36f,
            speed: 1.8f, tint: new Color(1, 0.98f, 0.84f));
        if (route == AttackRoute.Aerial)
            Spine(parent, Nine + "nine_30047_u3_attack_play_target_f_1_skel_data.tres", center + Vector2.Down * 80, 0.28f, speed: 1.8f);
    }
    internal static void Step(Node parent, Vector2 from, Vector2 to)
    {
        if (from.DistanceTo(to) < 25 || !Settings.Current.UseCznEffects) return;
        Spine(parent, Nine + "close_combat_step_player_move_skel_data.tres", from + Vector2.Down * 85, 0.38f,
            speed: 1.8f, animation: "b");
        _ = TaskHelper.RunSafely(Arrive(parent, to + Vector2.Down * 85));
    }
    private static async Task Arrive(Node parent, Vector2 foot)
    {
        await Fx.Delay(0.13);
        if (Valid(parent)) Spine(parent, Nine + "close_combat_step_player_arrive_skel_data.tres", foot, 0.4f, speed: 1.7f, animation: "b");
    }
    internal static bool Shield(Node parent, Vector2 center)
    {
        Vector2 feet = center + Vector2.Down * 90;
        bool front = Spine(parent, Core + "system_buff_shield_f1_skel_data.tres", feet, 0.49f, speed: 2.2f);
        if (!front) return false;
        Spine(parent, Core + "system_buff_shield_b_skel_data.tres", feet, 0.49f, speed: 2.2f, z: -1);
        Spine(parent, Core + "system_buff_shield_f2_skel_data.tres", feet, 0.49f, speed: 1.5f, z: 1);
        Sound(parent, "res://CznCore/audio/sfx/system_buff_shield.ogg");
        return true;
    }
    internal static void Blocked(Node parent, Vector2 center, bool broken)
    {
        string name = broken ? "system_break_shield_player" : "system_hit_shield_player_f1";
        Spine(parent, Core + name + "_skel_data.tres", center + Vector2.Down * 90, 0.49f, speed: 2);
        Spine(parent, Core + "system_hit_shield_player_f2_skel_data.tres", center + Vector2.Down * 90, 0.49f, speed: 2, z: 1);
    }
    internal static bool Buff(Node parent, Vector2 center)
    {
        bool played = Spine(parent, Core + "system_eff_buff_skel_data.tres", center + Vector2.Down * 85, 0.44f, speed: 1.65f,
            tint: new Color(1, 0.98f, 0.83f));
        if (played) Sound(parent, "res://CznCore/audio/sfx/system_eff_buff.ogg");
        return played;
    }
    internal static void Charge(Node parent, Vector2 center)
    {
        Spine(parent, "res://CznNineCharacter/animations/cinematic/nine_30047_ug_intro_sword_skel_data.tres",
            center + Vector2.Down * 80, 0.34f, speed: 1.7f, tint: new Color(1, 0.96f, 0.8f));
    }
    internal static void FinisherImpact(Node parent, Vector2 center)
    {
        Spine(parent, "res://CznNineCharacter/animations/cinematic/nine_30047_ug_target_b_skel_data.tres",
            center + Vector2.Down * 90, 0.48f, speed: 1.9f);
        Spine(parent, "res://CznNineCharacter/animations/cinematic/nine_30047_ug_target_f_skel_data.tres",
            center + Vector2.Down * 90, 0.48f, speed: 1.9f, z: 1);
    }
    internal static bool Sound(Node parent, string path)
    {
        if (!Valid(parent) || Load(path) is not AudioStream stream) return false;
        Voice.PlayEffect(parent, stream);
        SoundsPlayed++;
        return true;
    }
    internal static bool Available => Load(Core + "system_buff_shield_f1_skel_data.tres") != null
        && Load(Nine + "close_combat_step_player_move_skel_data.tres") != null;
}
