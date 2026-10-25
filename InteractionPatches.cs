using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

[HarmonyPatch(typeof(NHandCardHolder), "OnFocus")]
internal static class CardFocusPatch
{
    private static void Postfix(NHandCardHolder __instance)
    {
        ReadyPresentation.Focus(__instance.CardModel);
    }
}
[HarmonyPatch(typeof(NHandCardHolder), "OnUnfocus")]
internal static class CardUnfocusPatch
{
    private static void Postfix(NHandCardHolder __instance) => ReadyPresentation.Unfocus(__instance.CardModel);
}
[HarmonyPatch(typeof(NHandCardHolder), "DoCardHoverEffects")]
internal static class CardHoverPatch
{
    private static void Postfix(NHandCardHolder __instance, bool isHovered)
    {
        if (isHovered) ReadyPresentation.Focus(__instance.CardModel);
        else ReadyPresentation.Unfocus(__instance.CardModel);
    }
}
[HarmonyPatch]
internal static class CardTargetingPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(NCardPlay).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(NCardPlay)) && !t.IsAbstract)
        .Select(t => AccessTools.DeclaredMethod(t, "Start")).Where(m => m != null)!;
    private static void Postfix(NCardPlay __instance) => ReadyPresentation.Focus(__instance.Holder?.CardModel, targeting: true);
}
[HarmonyPatch(typeof(NCardPlay), nameof(NCardPlay.CancelPlayCard))]
internal static class CardCancelPatch
{
    private static void Prefix(NCardPlay __instance) => ReadyPresentation.Cancel(__instance.Holder?.CardModel);
}
[HarmonyPatch(typeof(NCardPlay), "Cleanup")]
internal static class CardCommitPatch
{
    private static void Prefix(NCardPlay __instance, bool isFinished)
    {
        if (isFinished) ReadyPresentation.Play(__instance.Holder?.CardModel);
        else ReadyPresentation.Cancel(__instance.Holder?.CardModel);
    }
}
[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class PlayedCardPresentationPatch
{
    private static void Prefix(CardModel __instance)
    {
        if (!Runtime.IsSaber(__instance.Owner?.Creature)) return;
        ReadyPresentation.Play(__instance);
        if (__instance.Type is CardType.Skill or CardType.Power)
            __instance.Owner!.Creature.GetCreatureNode()?.SetAnimationTrigger("Cast");
    }
}
