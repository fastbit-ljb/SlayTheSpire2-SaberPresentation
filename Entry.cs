using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace SaberPresentation;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public static void Initialize()
    {
        Settings.Load();
        Standalone.Initialize();
        CznInterop.Register();
        new Harmony("local.saber.presentation").PatchAll(typeof(Entry).Assembly);
        Log.Info("[SaberPresentation] JP combat presentation 0.3.0 initialized.");
#if SABER_TEST
        if (MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("saber-test"))
            _ = MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(TestHarness.Run(MegaCrit.Sts2.Core.Nodes.NGame.Instance!));
#endif
    }
}

// Check the actual loaded skeleton, so another Ironclad skin cannot receive
// Saber animation names, dashes or voice even if the Saber mod is installed.
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.GenerateAnimator))]
internal static class AnimatorPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CharacterModel __instance, MegaSprite controller, ref CreatureAnimator __result)
    {
        if (Settings.Current.Enabled && __instance is Ironclad) SaberSkin.Apply(controller);
        if (!Settings.Current.Enabled || __instance is not Ironclad || !controller.HasAnimation("attackvfff")) return true;
        var idle = new AnimState("idle_loop", true);
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Idle", idle);
        animator.AddAnyState("Relaxed", new AnimState("relaxed_loop", true));
        animator.AddAnyState("SaberVictory", new AnimState("relaxed_loop2", true));
        animator.AddAnyState("Revive", idle);
        animator.AddAnyState("SaberReadyAttack", new AnimState("attack_heavy"));
        animator.AddAnyState("SaberReadyCast", new AnimState("cast"));
        foreach (var (trigger, clip) in new (string, string)[] {
            ("Attack", "attack"), ("heavyAttack", "attack_heavy"),
            ("SaberAttack", "attack"), ("SaberSlashA", "attack"),
            ("SaberSlashB", "attackvfff"), ("SaberHeavy", "attack_heavy"),
            ("SaberExcalibur", "attack_heavy"), ("SaberCombo", "attackvfff"),
            ("SaberThrust", "attack"), ("SaberRising", "attack_heavy"),
            ("SaberAerial", "attack_heavy"), ("SaberSweep", "attackvfff"),
            ("Cast", "cast"), ("PowerUp", "cast"), ("Hit", "hurt") })
        {
            var state = new AnimState(controller.HasAnimation(clip) ? clip : "attack");
            state.AddNextState(idle);
            animator.AddAnyState(trigger, state);
        }
        // The skin has no low_health_loop; keep its supported idle at low HP.
        animator.AddAnyState("Dead", new AnimState("die"));
        __result = animator;
        Log.Info("[SaberPresentation] Saber skeleton detected; animation routes registered.");
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeAttack))]
internal static class AttackPatch
{
    private static void Postfix(AttackCommand command, ref Task __result)
    {
        if (Runtime.IsSaber(command.Attacker)) __result = Begin(__result, command);
    }
    private static async Task Begin(Task original, AttackCommand command)
    {
        await original; // Do not swallow gameplay hook exceptions.
        try { await Runtime.Begin(command); }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Attack presentation skipped: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterAttack))]
internal static class FinishAttackPatch
{
    private static void Postfix(AttackCommand command, ref Task __result)
    {
        if (Runtime.IsSaber(command.Attacker)) __result = Finish(__result, command);
    }
    private static async Task Finish(Task original, AttackCommand command)
    {
        try { await original; }
        finally { Runtime.Finish(command); }
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
internal static class TriggerPatch
{
    private static bool Prefix(NCreature __instance, ref string trigger)
    {
        if (!Runtime.IsSaber(__instance.Entity)) return true;
        try
        {
            if (trigger == "SaberExcalibur" && Runtime.HasExcaliburHit(__instance.Entity)) return false;
            if (trigger.StartsWith("Saber", StringComparison.Ordinal)) Runtime.AttackMotion(__instance.Entity, ref trigger);
            else if (trigger == "Hit") Runtime.Hurt(__instance.Entity);
            else if (trigger == "Cast" || trigger == "PowerUp") Runtime.Skill(__instance.Entity);
            else if (trigger == "Dead") Runtime.Dead(__instance.Entity);
        }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Feedback skipped: " + ex.Message); }
        return true;
    }
    private static void Postfix(NCreature __instance, string trigger)
    {
        if (trigger is "SaberCombo" or "SaberSweep" && Runtime.IsSaber(__instance.Entity))
        {
            using var track = __instance.Visuals.SpineBody?.GetAnimationState().GetCurrent(0);
            track?.SetTimeScale(1.65f);
        }
    }
}

[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
internal static class AttackCleanupPatch
{
    private static void Postfix(AttackCommand __instance, ref Task<AttackCommand> __result)
    {
        if (Runtime.IsSaber(__instance.Attacker)) __result = Complete(__result, __instance);
    }
    private static async Task<AttackCommand> Complete(Task<AttackCommand> original, AttackCommand command)
    {
        try { return await original; }
        finally { Runtime.Finish(command); }
    }
}

// Exactly one, innermost Damage overload. Random attacks have already selected
// their target here; never sample game RNG or decorate all candidate targets.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "Damage", new Type[] {
    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
    typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
    typeof(Creature), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay) })]
internal static class DamagePatch
{
    private static void Prefix(IEnumerable<Creature>? targets, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!Runtime.IsSaber(dealer) || (props & ValueProp.Move) == 0) return;
        try {
            Runtime.Impact(dealer!, targets, cardSource);
#if SABER_TEST
            TestHarness.OnImpact(cardSource?.Id.Entry ?? "unknown", dealer!);
#endif
        }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Hit VFX skipped: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
internal static class BlockPatch
{
    private static void Postfix(Creature creature, decimal amount)
    {
        if (amount > 0 && Runtime.IsSaber(creature)) Runtime.Shield(creature);
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
internal static class ShieldImpactPatch
{
    private static void Postfix(Creature target, DamageResult result)
    {
        if (result.BlockedDamage > 0 && Runtime.IsSaber(target)) Runtime.Blocked(target, result.WasBlockBroken);
    }
}

// Damage is final here, including block, powers and redirection, but the native
// death/removal phase has not started. Await only the presentation, never replay
// a damage calculation or advance targeting RNG to predict a kill.
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
internal static class KillingBlowPatch
{
    private static void Postfix(Creature? dealer, DamageResult results, ValueProp props, CardModel? cardSource, ref Task __result)
    {
        if (cardSource != null && Runtime.IsSaber(dealer) && (props & ValueProp.Move) != 0
            && results.WasTargetKilled && results.Receiver.IsDead && results.Receiver.Side != dealer!.Side)
            __result = Present(__result, dealer!, results, cardSource);
    }
    private static async Task Present(Task original, Creature dealer, DamageResult result, CardModel card)
    {
        await original;
        try { await Runtime.KillingBlow(dealer, result, card); }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Finisher presentation: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
internal static class VictoryPatch
{
    private static void Postfix(MegaCrit.Sts2.Core.Combat.ICombatState? combatState)
    {
        if (combatState == null) return;
        foreach (var player in combatState.Players)
            if (Runtime.IsSaber(player.Creature)) Runtime.Victory(player.Creature);
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
internal static class CombatReadyPatch
{
    private static void Postfix(NCombatRoom __instance) =>
        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Runtime.Enter(__instance));
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._ExitTree))]
internal static class CombatExitPatch
{
    private static void Prefix() { Runtime.Clear(); Voice.Clear(); }
}
