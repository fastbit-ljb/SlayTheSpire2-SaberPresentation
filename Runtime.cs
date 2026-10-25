using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SaberPresentation;

internal enum AttackRoute { Slash, Thrust, Rising, Aerial, Sweep, Heavy, Excalibur }

internal static class Runtime
{
    private sealed class Actor
    {
        public int SlashIndex;
        public int Attacks;
        public ulong LastHurt, LastShield, LastSkill;
        public Session? Attack;
        public bool Entered;
        public Node2D? HomeBody;
        public Vector2 HomePosition, HomeScale;
        public float HomeRotation;
    }
    private sealed class Session
    {
        public required AttackCommand Command;
        public required NCreature Node;
        public required Node2D Body;
        public required Vector2 Home;
        public required Vector2 HomeScale;
        public float HomeRotation;
        public Vector2? Destination;
        public AttackRoute Route;
        public Tween? Motion;
        public int Hit;
        public ulong Started;
        public bool Finished;
        public bool FinisherPlayed;
    }
    private static ConditionalWeakTable<Creature, Actor> _actors = new();
    private static readonly HashSet<Session> Active = [];
    private static ConditionalWeakTable<CardPlay, object> _finishedCards = new();
    internal static int KillingBlowCount { get; private set; }
    internal static double LastChargeDuration { get; private set; }
    private static readonly AccessTools.FieldRef<AttackCommand, string?> Animation =
        AccessTools.FieldRefAccess<AttackCommand, string?>("_attackerAnimName");
    private static readonly AccessTools.FieldRef<AttackCommand, Creature?> SingleTarget =
        AccessTools.FieldRefAccess<AttackCommand, Creature?>("_singleTarget");
    private static readonly AccessTools.FieldRef<AttackCommand, Func<Task>?> BeforeDamage =
        AccessTools.FieldRefAccess<AttackCommand, Func<Task>?>("_beforeDamage");
    private static readonly AccessTools.FieldRef<AttackCommand, int> HitCount =
        AccessTools.FieldRefAccess<AttackCommand, int>("_hitCount");

    public static bool IsSaber(Creature? creature)
    {
        if (!Settings.Current.Enabled || creature?.Player?.Character is not Ironclad) return false;
        var node = creature.GetCreatureNode();
        return node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree()
            && node.Visuals?.SpineBody?.HasAnimation("attackvfff") == true;
    }

    public static Task Begin(AttackCommand command)
    {
        var creature = command.Attacker!;
        var node = creature.GetCreatureNode();
        if (node == null || command.ModelSource is not CardModel card || creature.IsDead) return Task.CompletedTask;
        ReadyPresentation.Play(card);
        var actor = _actors.GetOrCreateValue(creature);
        CaptureHome(actor, node.Visuals.Body);
        var previous = actor.Attack;
        var home = actor.HomePosition;
        var homeRotation = actor.HomeRotation;
        var homeScale = actor.HomeScale;
        if (previous != null) { previous.Motion?.Kill(); Active.Remove(previous); }
        string id = card.Id.Entry;
        var route = Settings.Current.NoblePhantasmCards.Contains(id, StringComparer.OrdinalIgnoreCase) ? AttackRoute.Excalibur
            : id is "UPPERCUT" or "THUNDERCLAP" ? AttackRoute.Rising
            : id is "HEMOKINESIS" or "STOMP" or "BREAKTHROUGH" ? AttackRoute.Aerial
            : id is "WHIRLWIND" or "TWIN_STRIKE" or "SWORD_BOOMERANG" or "THRASH" ? AttackRoute.Sweep
            : id is "POMMEL_STRIKE" or "HEADBUTT" ? AttackRoute.Thrust
            : Animation(command) == "heavyAttack" || id is "BASH" or "MANGLE" or "TEAR_ASUNDER" or "PERFECTED_STRIKE" ? AttackRoute.Heavy
            : (actor.Attacks++ % 3 == 1 ? AttackRoute.Thrust : AttackRoute.Slash);
        var session = new Session { Command = command, Node = node, Body = node.Visuals.Body,
            Home = home, HomeScale = homeScale, HomeRotation = homeRotation,
            Route = route, Started = Time.GetTicksMsec() };
        actor.Attack = session;
        Active.Add(session);
        if (command.IsRandomlyTargeted && Settings.Current.Dash)
        {
            // The native hit-VFX callback receives the actual resolved target.
            // Keep the native pre-damage callback, including any gameplay effects.
            var nativeBeforeDamage = BeforeDamage(command);
            command.WithHitVfxNode(target => {
                try { Approach(session, target); }
                catch (Exception ex) { Log.Warn("[SaberPresentation] Random-target approach skipped: " + ex.Message); }
                return null;
            });
            command.BeforeDamage(async () => {
                await Cmd.CustomScaledWait(0.13f, 0.13f);
                if (nativeBeforeDamage != null) await nativeBeforeDamage();
            });
        }
        // No damage, hit-count, targeting, card-cost or gameplay callbacks change.
        command.WithAttackerAnim(route switch {
            AttackRoute.Excalibur => "SaberExcalibur", AttackRoute.Heavy => "SaberHeavy",
            AttackRoute.Thrust => "SaberThrust", AttackRoute.Rising => "SaberRising",
            AttackRoute.Aerial => "SaberAerial", AttackRoute.Sweep => "SaberSweep", _ => "SaberAttack"
        }, route is AttackRoute.Slash or AttackRoute.Thrust ? 0.24f : 0.48f);

        if (route == AttackRoute.Excalibur)
        {
            Voice.PlayNext(node, "heavy", 3);
            node.SetAnimationTrigger("Cast");
            Fx.Charge(Fx.Container(node), Center(node));
        }
        else
        {
            if (route is AttackRoute.Heavy or AttackRoute.Aerial or AttackRoute.Rising) Voice.PlayNext(node, "heavy", 3);
            else Voice.PlayNext(node, "attack", 7);
            if (Settings.Current.Dash)
            {
                // Use the known target, or the nearest living enemy for an AOE.
                // Randomly targeted attacks approach only after target resolution.
                var target = !command.IsRandomlyTargeted ? creature.CombatState?.HittableEnemies
                    .Where(t => t.IsAlive && t.GetCreatureNode() != null)
                    .OrderBy(t => Math.Abs(t.GetCreatureNode()!.GlobalPosition.X - node.GlobalPosition.X)).FirstOrDefault() : null;
                var selected = command.CardPlay?.Target ?? SingleTarget(command);
                Approach(session, selected ?? target);
            }
        }
        Log.Info($"[SaberPresentation] {id} -> {route}");
        return Task.CompletedTask;
    }

    private static void Approach(Session session, Creature? target)
    {
        if (!Settings.Current.Dash || target?.GetCreatureNode() is not { } enemy || !Valid(session.Body)) return;
        Vector2 global = session.Body.GlobalPosition;
        float scale = Math.Abs(session.Node.GetGlobalTransform().Scale.X);
        float gap = 175f * scale;
        var homeGlobal = ((Node2D)session.Body.GetParent()).ToGlobal(session.Home);
        float x = Math.Clamp(enemy.VfxSpawnPosition.X - gap, homeGlobal.X, homeGlobal.X + 1400f * scale);
        var local = ((Node2D)session.Body.GetParent()).ToLocal(new Vector2(x, homeGlobal.Y));
#if SABER_TEST
        Log.Info($"[SaberTest] approach actor={global} targetBody={enemy.Visuals.Body.GlobalPosition} targetCenter={enemy.VfxSpawnPosition} home={session.Home} destination={local}");
#endif
        session.Destination = local;
        session.Motion?.Kill();
        session.Motion = session.Body.CreateTween();
        session.Motion.TweenProperty(session.Body, "position", local, 0.13).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        Fx.Dash(Fx.Container(session.Node), Center(session.Node), new Vector2(x, Center(session.Node).Y));
    }

    public static void AttackMotion(Creature creature, ref string trigger)
    {
        var actor = _actors.GetOrCreateValue(creature);
        var session = actor.Attack;
#if SABER_TEST
        Log.Info($"[SaberTest] motion trigger={trigger} session={session != null}");
#endif
        if (session == null || session.Finished || !Valid(session.Body)) return;
        if (trigger == "SaberAttack") trigger = (actor.SlashIndex++ % 3 == 2) ? "SaberCombo" : "SaberSlashA";
        if (!Settings.Current.RichMotion || session.Route == AttackRoute.Excalibur) return;
        var body = session.Body;
        CznAssets.Swing(body, session.Route, session.Hit);
        var origin = session.Destination ?? session.Home;
        session.Motion?.Kill();
        var tween = session.Motion = body.CreateTween();
        switch (session.Route)
        {
            case AttackRoute.Rising:
                tween.TweenProperty(body, "position", origin + new Vector2(18, -65), 0.25).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation - 0.1f, 0.2);
                tween.TweenProperty(body, "position", origin, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation, 0.18);
                break;
            case AttackRoute.Aerial:
                tween.TweenProperty(body, "position", origin + new Vector2(-35, -95), 0.23).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation - 0.12f, 0.23);
                tween.TweenProperty(body, "position", origin + new Vector2(18, 0), 0.19).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation + 0.05f, 0.19);
                break;
            case AttackRoute.Thrust:
                tween.TweenProperty(body, "position", origin + Vector2.Right * 38, 0.18).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation + 0.055f, 0.18);
                break;
            case AttackRoute.Sweep:
                tween.TweenProperty(body, "position", origin + Vector2.Right * ((session.Hit & 1) == 0 ? 24 : -18), 0.2);
                tween.Parallel().TweenProperty(body, "rotation", session.HomeRotation + ((session.Hit & 1) == 0 ? -0.07f : 0.07f), 0.2);
                break;
            default:
                tween.TweenProperty(body, "position", origin, 0.14).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                break;
        }
    }

    public static void Impact(Creature creature, IEnumerable<Creature>? targets, CardModel? source)
    {
        var actor = _actors.GetOrCreateValue(creature);
        var session = actor.Attack;
        if (session == null || session.Finished || session.Command.ModelSource != source || !Valid(session.Body)) return;
        var nodes = targets?.Where(t => t.IsAlive).Select(t => t.GetCreatureNode())
            .Where(n => n != null && GodotObject.IsInstanceValid(n) && n.IsInsideTree()).Cast<NCreature>().ToArray() ?? [];
        if (nodes.Length == 0) return;
#if SABER_TEST
        Log.Info($"[SaberTest] impact route={session.Route} body={session.Body.Position} home={session.Home}");
#endif
        var container = Fx.Container(session.Node);
        if (session.Route == AttackRoute.Excalibur)
        {
            if (session.Hit == 0)
            {
                Fx.Excalibur(container, WeaponPoint(session.Node), nodes.Select(Center).ToArray(), Math.Clamp(HitCount(session.Command) * 0.43, 0.13, 1.8));
            }
            else foreach (var target in nodes) Fx.Slash(container, Center(target), true, session.Hit);
        }
        else
        {
            bool heavy = session.Route is AttackRoute.Heavy or AttackRoute.Aerial or AttackRoute.Rising;
            foreach (var target in nodes) Fx.Slash(container, Center(target), heavy, session.Hit, session.Route);
            if (session.Route == AttackRoute.Thrust) Fx.WindBlade(container, Center(session.Node), Center(nodes[0]));
            Voice.Sword(container, heavy);
        }
        session.Hit++;
    }

    internal static async Task KillingBlow(Creature creature, DamageResult result, CardModel card)
    {
        if (!Settings.Current.KillingBlowExcalibur || !result.WasTargetKilled || !result.Receiver.IsDead
            || !_actors.TryGetValue(creature, out var actor) || actor.Attack is not { } session
            || session.Command.ModelSource != card || session.FinisherPlayed || !Valid(session.Body)) return;
        var play = session.Command.CardPlay;
        if (play != null)
        {
            if (_finishedCards.TryGetValue(play, out _)) return;
            _finishedCards.Add(play, new object());
        }
        session.FinisherPlayed = true;
        KillingBlowCount++;
        var targetNode = result.Receiver.GetCreatureNode();
        Vector2 target = targetNode != null ? Center(targetNode) : Center(session.Node) + Vector2.Right * 600;
        var container = Fx.Container(session.Node);
        session.Motion?.Kill();
        session.Node.SetAnimationTrigger("Cast");
        Fx.Charge(container, Center(session.Node));
        if (Settings.Current.Cinematics)
        {
#if SABER_TEST
            if (KillingBlowCount == 1) _ = TaskHelper.RunSafely(TestHarness.CaptureFinisherSequence());
#endif
            bool usesVideo = Fx.HasExcaliburVideo;
            if (!usesVideo) Voice.Play(session.Node, "np_charge", priority: true);
            double duration = usesVideo ? 17.0 : Math.Max(1.24, Voice.Length("np_charge"));
            ulong started = Time.GetTicksMsec();
            usesVideo = await Fx.CutIn(session.Node, duration: duration);
            LastChargeDuration = (Time.GetTicksMsec() - started) / 1000.0;
            if (!usesVideo) Voice.PlaySequence(session.Node, ["np_release", "np_end"], priority: true);
        }
        if (!Valid(session.Node)) return;
        session.Node.SetAnimationTrigger("SaberHeavy");
        if (!Settings.Current.Cinematics) Voice.PlaySequence(session.Node, ["np_release", "np_end"], priority: true);
        Fx.Excalibur(container, WeaponPoint(session.Node), [target], 0.65);
#if SABER_TEST
        _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("killing_blow_excalibur", 0.08));
#endif
        await Fx.Delay(0.72);
        Log.Info($"[SaberPresentation] Confirmed killing blow: {card.Id.Entry} -> Excalibur CG");
    }

    public static void Finish(AttackCommand command)
    {
        if (command.Attacker == null || !_actors.TryGetValue(command.Attacker, out var actor) || actor.Attack?.Command != command) return;
        var session = actor.Attack;
        if (session.Finished) return;
        session.Finished = true;
        TaskHelper.RunSafely(Return(session));
    }

    private static async Task Return(Session session)
    {
        try
        {
            // Keep the impact pose visible before returning; a new attack cancels this motion.
            await Fx.Delay(session.Route == AttackRoute.Excalibur ? 0.34 : 0.16);
            if (!Active.Contains(session)) return;
            if (!GodotObject.IsInstanceValid(session.Node)) return;
            if (!_actors.TryGetValue(session.Node.Entity, out var actor) || actor.Attack != session) return;
            session.Motion?.Kill();
            if (!Valid(session.Body)) return;
            session.Motion = session.Body.CreateTween();
            session.Motion.TweenProperty(session.Body, "position", session.Home, 0.22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            session.Motion.Parallel().TweenProperty(session.Body, "rotation", session.HomeRotation, 0.22);
            session.Motion.Parallel().TweenProperty(session.Body, "scale", session.HomeScale, 0.22);
            await Fx.Delay(0.23);
            if (actor.Attack == session && Valid(session.Body))
            {
                session.Motion?.Kill();
                session.Body.Position = session.Home;
                session.Body.Rotation = session.HomeRotation;
                session.Body.Scale = session.HomeScale;
                actor.Attack = null;
            }
        }
        finally { Active.Remove(session); }
    }

    private static void Restore(Session session, bool immediate)
    {
        session.Motion?.Kill();
        if (!Valid(session.Body)) { Active.Remove(session); return; }
        if (immediate) { session.Body.Position = session.Home; session.Body.Rotation = session.HomeRotation; session.Body.Scale = session.HomeScale; }
        else
        {
            var tween = session.Body.CreateTween();
            tween.TweenProperty(session.Body, "position", session.Home, 0.22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        Active.Remove(session);
    }

    public static void Hurt(Creature creature)
    {
        var actor = _actors.GetOrCreateValue(creature);
        if (Time.GetTicksMsec() - actor.LastHurt < 500) return;
        actor.LastHurt = Time.GetTicksMsec();
        var node = creature.GetCreatureNode()!;
        Voice.PlayNext(node, "hurt", 2);
        Fx.Hurt(Fx.Container(node), Center(node));
    }

    public static void Shield(Creature creature)
    {
        var actor = _actors.GetOrCreateValue(creature);
        if (Time.GetTicksMsec() - actor.LastShield < 220) return;
        actor.LastShield = Time.GetTicksMsec();
        var node = creature.GetCreatureNode()!;
        Fx.Shield(Fx.Container(node), Center(node));
    }
    internal static void Blocked(Creature creature, bool broken)
    {
        var node = creature.GetCreatureNode();
        if (node != null) CznAssets.Blocked(Fx.Container(node), Center(node), broken);
    }

    public static void Skill(Creature creature)
    {
        var actor = _actors.GetOrCreateValue(creature);
        if (Time.GetTicksMsec() - actor.LastSkill < 700) return;
        actor.LastSkill = Time.GetTicksMsec();
        var node = creature.GetCreatureNode()!;
        Voice.PlayNext(node, "skill", 4);
        Fx.Buff(Fx.Container(node), Center(node));
    }
    public static void Dead(Creature creature) { Voice.Play(creature.GetCreatureNode()!, "death", true); }
    public static void Victory(Creature creature)
    {
        ReadyPresentation.Cancel(null);
        var node = creature.GetCreatureNode()!;
        node.SetAnimationTrigger("SaberVictory");
        TaskHelper.RunSafely(Voice.VictoryAfterCurrent(node));
        Fx.Buff(Fx.Container(node), Center(node));
    }
    public static async Task Enter(NCombatRoom room)
    {
        await Fx.Delay(0.5);
        if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
        foreach (var node in Descendants(room).OfType<NCreature>())
        {
            if (!IsSaber(node.Entity)) continue;
            var actor = _actors.GetOrCreateValue(node.Entity);
            CaptureHome(actor, node.Visuals.Body);
            if (actor.Entered) continue;
            actor.Entered = true;
            Voice.PlayNext(node, "entry", 4);
            Fx.Buff(Fx.Container(node), Center(node));
        }
    }
    internal static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    public static Vector2 Center(NCreature node)
    {
        var center = node.VfxSpawnPosition;
        if (_actors.TryGetValue(node.Entity, out var actor) && actor.Attack is { } session && Valid(session.Body))
            center += session.Body.GlobalPosition - ((Node2D)session.Body.GetParent()).ToGlobal(session.Home);
        return center;
    }
    internal static Vector2 WeaponPoint(NCreature node)
    {
        Vector2 center = Center(node);
        var spine = node.Visuals.SpineBody;
        if (spine != null)
        {
            Vector2 best = Vector2.Zero;
            bool found = false;
            foreach (string name in new[] { "wuqi3", "wuqi2o", "Ys_wuqi2", "Ys_wuqi1", "jian", "Ys_rjian4", "Ys_rjian3" })
            {
                Transform2D? bone = spine.GetGlobalBoneTransform(name);
                if (bone is not { } transform) continue;
                Vector2 candidate = transform.Origin;
                float distance = candidate.DistanceTo(center);
                if (distance < 15 || distance > 520 || candidate.X < center.X - 45) continue;
                Vector2 direction = transform.X.LengthSquared() > 0.001f ? transform.X.Normalized() : Vector2.Right;
                candidate += direction * Math.Clamp(Math.Abs(node.Visuals.Body.GlobalScale.X) * 65f, 35f, 115f);
                if (!found || candidate.X > best.X) { best = candidate; found = true; }
            }
            if (found) return best;
        }
        float scale = Math.Max(0.75f, Math.Abs(node.GetGlobalTransform().Scale.X));
        return center + new Vector2(145f * scale, -48f * scale);
    }
    private static bool Valid(Node node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree();
    private static void CaptureHome(Actor actor, Node2D body)
    {
        if (actor.HomeBody == body) return;
        actor.HomeBody = body;
        actor.HomePosition = body.Position;
        actor.HomeScale = body.Scale;
        actor.HomeRotation = body.Rotation;
    }
    internal static bool HasExcaliburHit(Creature creature) => _actors.TryGetValue(creature, out var actor)
        && actor.Attack is { Route: AttackRoute.Excalibur, Hit: > 0 };
    internal static bool IsAttacking(Creature creature) => _actors.TryGetValue(creature, out var actor) && actor.Attack is { Finished: false };
#if SABER_TEST
    internal static bool NearTargetAtImpact(Creature creature)
    {
        if (!_actors.TryGetValue(creature, out var actor) || actor.Attack is not { Destination: { } dest } session) return false;
        return dest.DistanceTo(session.Home) > 50 && session.Body.Position.DistanceTo(dest) < 60;
    }
#endif
    public static void Clear()
    {
        ReadyPresentation.Cleanup();
        foreach (var session in Active.ToArray()) Restore(session, true);
        Active.Clear();
        _actors = new();
        _finishedCards = new();
    }
}
