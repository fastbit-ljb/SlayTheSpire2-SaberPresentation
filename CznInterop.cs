using System.Collections.Concurrent;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;

namespace SaberPresentation;

internal static class CznInterop
{
    private const string RegistryKey = "CznModConfig.SharedSettingsExtensions.v1";
    internal static void Register()
    {
        var registry = AppDomain.CurrentDomain.GetData(RegistryKey) as ConcurrentDictionary<string, object>;
        if (registry == null) { registry = new(); AppDomain.CurrentDomain.SetData(RegistryKey, registry); }
        registry["SaberPresentation"] = new Dictionary<string, object> {
            ["api_version"] = 1, ["category_id"] = "saber_presentation", ["category_order"] = 30,
            ["category_label"] = "Saber 演出", ["group_label"] = "阿尔托莉雅 · 战斗与交互", ["group_order"] = 0,
            ["controls"] = new object[] {
                Toggle("enabled", "启用 Saber 演出", () => Settings.Current.Enabled, v => Settings.Current.Enabled = v),
                Toggle("portrait", "卡牌悬停动态立绘", () => Settings.Current.CardHoverPresentation, v => { Settings.Current.CardHoverPresentation = v; if (!v) ReadyPresentation.Cancel(null); }),
                Toggle("ready", "卡牌预备动作", () => Settings.Current.ReadyBodyMotion, v => Settings.Current.ReadyBodyMotion = v),
                Toggle("card_art", "Saber 专属卡面", () => Settings.Current.SaberCardArt, v => Settings.Current.SaberCardArt = v),
                Toggle("native_energy", "原版能量球（清晰显示）", () => Settings.Current.NativeEnergyDisplay, EnergyPresentation.SetNativeDisplay),
                Toggle("hover_voice", "选牌日语回应", () => Settings.Current.HoverVoice, v => Settings.Current.HoverVoice = v),
                Toggle("jp_voice", "Saber 日配", () => Settings.Current.JapaneseVoice, v => Settings.Current.JapaneseVoice = v),
                Toggle("czn_effects", "使用 CZN 原生特效", () => Settings.Current.UseCznEffects, v => Settings.Current.UseCznEffects = v),
                Toggle("dash", "近身移动", () => Settings.Current.Dash, v => Settings.Current.Dash = v),
                Toggle("rich_motion", "丰富动作编排", () => Settings.Current.RichMotion, v => Settings.Current.RichMotion = v),
                Toggle("cinematics", "宝具全屏演出", () => Settings.Current.Cinematics, v => Settings.Current.Cinematics = v),
                Toggle("killing_blow", "击杀时释放咖喱棒 CG", () => Settings.Current.KillingBlowExcalibur, v => Settings.Current.KillingBlowExcalibur = v),
                Toggle("flash", "宝具闪光", () => Settings.Current.ScreenFlash, v => Settings.Current.ScreenFlash = v),
                Toggle("shake", "震屏", () => Settings.Current.ScreenShake, v => Settings.Current.ScreenShake = v),
                Toggle("prefer_saber", "铁甲战士优先使用 Saber", () => Settings.Current.PreferSaber, v => Settings.Current.PreferSaber = v),
                Slider("portrait_scale", "立绘大小", () => Settings.Current.PortraitScale, v => Settings.Current.PortraitScale = (float)v, 0.5, 1.5, 0.05),
                Slider("portrait_opacity", "立绘不透明度", () => Settings.Current.PortraitOpacity, v => Settings.Current.PortraitOpacity = (float)v, 0.1, 1, 0.05),
                Slider("voice_volume", "日配音量", () => Settings.Current.VoiceVolume, v => Settings.Current.VoiceVolume = (float)v, 0, 1, 0.05),
                Slider("effects_volume", "特效音量", () => Settings.Current.EffectsVolume, v => Settings.Current.EffectsVolume = (float)v, 0, 1, 0.05),
            }
        };
        _ = TaskHelper.RunSafely(InstallSkinCompatibility());
    }
    private static Dictionary<string, object> Toggle(string id, string label, Func<bool> read, Action<bool> write) => new() {
        ["type"] = "toggle", ["id"] = id, ["label"] = label, ["get"] = read,
        ["set"] = (Action<bool>)(v => { write(v); Settings.Save(); })
    };
    private static Dictionary<string, object> Slider(string id, string label, Func<double> read, Action<double> write,
        double min, double max, double step) => new() {
        ["type"] = "slider", ["id"] = id, ["label"] = label, ["get"] = read,
        ["set"] = (Action<double>)(v => { write(v); Settings.Save(); }), ["min"] = min, ["max"] = max, ["step"] = step
    };
    private static async Task InstallSkinCompatibility()
    {
        if (NGame.Instance == null) return;
        await NGame.Instance.GameStartupComplete;
        SaberSkin.PreserveInstalledSkin();
        var profile = AccessTools.TypeByName("CznCore.Presentation.CharacterSkinProfile");
        if (profile == null) return;
        var harmony = new Harmony("local.saber.presentation.czn-skin-preference");
        foreach (var method in profile.GetMethods().Where(m => m.Name == "AppliesTo"))
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(CznInterop), nameof(PreserveSaber)));
    }
    private static void PreserveSaber(object __instance, object[] __args, ref bool __result)
    {
        if (!__result || !Settings.Current.Enabled || !Settings.Current.PreferSaber) return;
        var character = __args[0] as CharacterModel ?? (__args[0] as Player)?.Character;
        if (character is not Ironclad) return;
        // Other Ironclad replacement packs stay available in their own menus;
        // while this extension is selected, retain the installed Saber model.
        __result = false;
    }
}
