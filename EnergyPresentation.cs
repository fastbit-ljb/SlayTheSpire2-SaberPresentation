using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

internal static class EnergyPresentation
{
    private static PropertyInfo? _style;
    private static object? _nativeStyle;
    internal static void SetNativeDisplay(bool enabled)
    {
        Settings.Current.NativeEnergyDisplay = enabled;
        if (_style != null) _style.SetValue(null, Enum.Parse(_style.PropertyType, enabled ? "Sts2Native" : "Czn"));
        AccessTools.Method(AccessTools.TypeByName("CznStyleUI.CombatHud.CombatResourceHudRuntime"), "ApplyCurrentSetting")?.Invoke(null, null);
    }
    internal static void Install()
    {
        var settings = AccessTools.TypeByName("CznStyleUI.Configuration.UiModuleSettings");
        _style = settings?.GetProperty("CombatEnergyStyle", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (_style == null) return;
        _nativeStyle = Enum.Parse(_style.PropertyType, "Sts2Native");
        var state = AccessTools.TypeByName("CznStyleUI.CombatHud.CombatResourceHudRuntime+CombatResourceHudState");
        var method = AccessTools.Method(state, "ApplyEnergyStyle");
        if (method == null) return;
        new Harmony("local.saber.presentation.energy-visibility").Patch(method,
            prefix: new HarmonyMethod(typeof(EnergyPresentation), nameof(UseNative)),
            postfix: new HarmonyMethod(typeof(EnergyPresentation), nameof(RaiseEnergy)));
        UseNative();
    }
    private static void UseNative()
    {
        if (Settings.Current.NativeEnergyDisplay) _style?.SetValue(null, _nativeStyle);
    }
    private static void RaiseEnergy(object __instance)
    {
        if (!Settings.Current.NativeEnergyDisplay) return;
        if (AccessTools.Field(__instance.GetType(), "_combatUi").GetValue(__instance) is NCombatUi ui)
            ui.EnergyCounterContainer.ZIndex = 200;
    }
}
