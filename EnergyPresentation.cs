using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

// Retain the original counter, placement, texture and readout. Only its draw
// order is raised so the character cut-in cannot hide the current energy.
[HarmonyPatch(typeof(NCombatUi), "_Ready")]
internal static class EnergyPresentation
{
    private static void Postfix(NCombatUi __instance) => __instance.EnergyCounterContainer.ZIndex = 200;
}
