using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SaberPresentation;

// Card focus holds one continuous character. Card use runs to completion before
// the overlay leaves, matching Nine's idle -> card animation -> out lifecycle.
internal static class ReadyPresentation
{
    private static NCombatRoom? _room;
    private static NCreature? _actor;
    private static Node2D? _screen, _portrait;
    private static PortraitRig? _rig;
    private static SceneTree? _tree;
    private static CardModel? _card, _actionCard;
    private static bool _targeting, _readyBody, _acting;
    private static int _generation, _pose;
    private static ulong _lastVoice;
    private static Tween? _transition;
    private static Vector2 _home;
    internal static bool AnimePortraitReady => _rig?.IsReady == true;
    internal static string? PortraitSource => _rig?.Source;
    internal static Vector2 PortraitResolution => _rig?.Resolution ?? Vector2.Zero;
    internal static int RigBones => _rig?.BoneCount ?? 0;
    internal static string? RigClip => _rig?.Clip;
    internal static string? TextureSignature => _rig?.TextureSignature;
    internal static Vector2 MeshSample => _rig?.MotionSample ?? Vector2.Zero;
    internal static int ActionFramesPlayed => _rig?.RenderSamples ?? 0;
    internal static bool ActionSwordTipsSafe { get; private set; } = true;
    internal static bool Visible => Valid(_portrait) && _portrait!.Visible && _portrait.Modulate.A > 0.5f;
    internal static bool Targeting => _targeting;
    internal static bool BodyReady => _readyBody;
    internal static CardModel? Card => _card;
    internal static int Pose => _pose;
    internal static int FocusCount { get; private set; }
    internal static int PlayCount { get; private set; }
#if SABER_TEST
    internal static void ExportPose(string clip, double seconds) => _rig?.ExportPose(clip, seconds);
#endif
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
        _portrait = new Node2D { Name = "SaberAnimatedPortrait", TopLevel = true, Modulate = new Color(1, 1, 1, 0) };
        _screen.AddChild(_portrait);
        _rig = new PortraitRig(_portrait);
        _tree = _room.GetTree();
        _tree.ProcessFrame += Tick;
        _room.TreeExiting += Cleanup;
        ApplyLayout();
        _portrait.Position = _home;
    }
    private static void ApplyLayout()
    {
        if (!Valid(_portrait)) return;
        var size = _portrait!.GetViewport().GetVisibleRect().Size;
        _portrait.GlobalScale = Vector2.One * (size.Y * 1.17f / 1536 * Settings.Current.PortraitScale);
        _home = new Vector2(size.X * 0.135f + Settings.Current.PortraitOffsetX, size.Y * 1.29f + Settings.Current.PortraitOffsetY);
    }
    private static void Tick()
    {
        if (!Visible || _rig == null) return;
        ApplyLayout();
        _rig.Sample();
        if (_acting)
        {
            Rect2 safe = _portrait!.GetViewport().GetVisibleRect().Grow(-25);
            ActionSwordTipsSafe &= safe.HasPoint(_rig.SwordTip);
        }
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
        _pose = card!.Type == CardType.Attack ? 0 : 1;
        _rig!.IdlePose();
        _transition?.Kill();
        _portrait!.Show();
        if (_portrait.Modulate.A < 0.03f) _portrait.Position = _home + Vector2.Left * 180;
        _transition = _portrait.CreateTween().SetParallel();
        _transition.TweenProperty(_portrait, "position", _home, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _transition.TweenProperty(_portrait, "modulate:a", Settings.Current.PortraitOpacity, 0.16);
        ReadyBody(card);
        if (changed && Time.GetTicksMsec() - _lastVoice > 3500 && Settings.Current.HoverVoice)
        {
            _lastVoice = Time.GetTicksMsec();
            Voice.PlayNext(_actor!, _pose == 0 ? "attack" : "skill", _pose == 0 ? 7 : 4);
        }
    }
    private static void ReadyBody(CardModel card)
    {
        if (!Settings.Current.ReadyBodyMotion || !Valid(_actor) || _acting || Runtime.IsAttacking(card.Owner.Creature)) return;
        _readyBody = true;
        _actor!.SetAnimationTrigger(_pose == 0 ? "SaberReadyAttack" : "SaberReadyCast");
        using var track = _actor.Visuals.SpineBody?.GetAnimationState().GetCurrent(0);
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
        if (_acting) return;
        _targeting = false;
        _generation++;
        Hide();
    }
    internal static void Play(CardModel? card)
    {
        if (!Accept(card) || (_acting && _actionCard == card)) return;
        Ensure(card!);
        PlayCount++;
        _targeting = false;
        _acting = true;
        _actionCard = card;
        int token = ++_generation;
        _transition?.Kill();
        _portrait!.Show();
        _portrait.Position = _home;
        _portrait.Modulate = new Color(1, 1, 1, Settings.Current.PortraitOpacity);
        double duration = _rig!.Play(card!.Type);
        _ = TaskHelper.RunSafely(EndAction(token, duration));
    }
    private static async Task EndAction(int token, double duration)
    {
        await Fx.Delay(duration + 0.06);
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
        _room = null; _actor = null; _card = _actionCard = null; _tree = null; _rig = null;
    }
}
