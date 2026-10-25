using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SaberPresentation;

internal static class ContextPresentation
{
    internal static bool LastDefeatUsedVideo { get; private set; }
    internal static double LastDefeatVideoSeconds { get; private set; }
    internal static bool Applies(Player? player) => SaberSkin.Active && player?.Character is Ironclad && LocalContext.IsMe(player);
    internal static void Speak(Player? player, string category, int variants)
    {
        if (Applies(player) && NGame.Instance != null) Voice.PlayNext(NGame.Instance, category, variants);
    }
    internal static async Task Outcome(Node parent, bool victory)
    {
        using var music = AnimeMusic.PauseForCinematic();
        var layer = new CanvasLayer { Name = "SaberOutcome", Layer = 52 };
        parent.AddChild(layer);
#if SABER_TEST
        TestHarness.OutcomeCount++;
        _ = TaskHelper.RunSafely(TestHarness.CaptureAfter(victory ? "victory_cg" : "defeat_cg", 0.5));
#endif
        try
        {
            var size = parent.GetViewport().GetVisibleRect().Size;
            LastDefeatUsedVideo = false;
            LastDefeatVideoSeconds = 0;
            if (!victory && Art.DefeatVideo is { } defeatStream)
            {
                var backdrop = new ColorRect { Color = Colors.Black, Size = size,
                    MouseFilter = Control.MouseFilterEnum.Stop };
                layer.AddChild(backdrop);
                var video = new VideoStreamPlayer { Name = "SaberAnimatedDefeatCg", Stream = defeatStream,
                    Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
                video.Set("expand", true);
                layer.AddChild(video);
                var defeatTitle = new Label { Text = "DEFEAT", Position = size * new Vector2(0.075f, 0.76f),
                    Modulate = new Color(0.76f, 0.86f, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
                defeatTitle.AddThemeFontSizeOverride("font_size", 62);
                defeatTitle.AddThemeColorOverride("font_color", new Color(0.76f, 0.86f, 1));
                layer.AddChild(defeatTitle);
                video.Play();
                await Fx.Delay(0.08);
                if (video.IsPlaying())
                {
                    LastDefeatUsedVideo = true;
#if SABER_TEST
                    _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("defeat_cg_mid", 3.6));
                    _ = TaskHelper.RunSafely(TestHarness.CaptureAfter("defeat_cg_late", 6.6));
#endif
                    defeatTitle.CreateTween().TweenProperty(defeatTitle, "modulate:a", 0.92f, 0.35).SetDelay(0.35);
                    while (GodotObject.IsInstanceValid(video) && video.IsPlaying())
                    {
                        LastDefeatVideoSeconds = video.StreamPosition;
                        if (Input.IsActionJustPressed("ui_cancel")) video.Stop();
                        await Fx.Delay(0.05);
                    }
                    return;
                }
                video.QueueFree();
                backdrop.QueueFree();
                defeatTitle.QueueFree();
            }
            var picture = new TextureRect { Texture = Art.Texture(victory ? "camelot_stage" : "saber_defeat"), Size = size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
            layer.AddChild(picture);
            if (victory)
            {
                var portrait = new TextureRect { Texture = Art.Texture("saber_ready_cast"), Position = size * new Vector2(0.5f, 0),
                    Size = new Vector2(size.Y * 0.75f, size.Y * 1.1f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
                picture.AddChild(portrait);
            }
            var title = new Label { Text = victory ? "VICTORY" : "DEFEAT", Position = size * new Vector2(0.09f, 0.48f),
                MouseFilter = Control.MouseFilterEnum.Ignore };
            title.AddThemeFontSizeOverride("font_size", 64);
            title.AddThemeColorOverride("font_color", victory ? new Color(1, 0.85f, 0.5f) : new Color(0.84f, 0.9f, 1));
            picture.AddChild(title);
            picture.CreateTween().TweenProperty(picture, "modulate:a", 1f, 0.22);
            await Fx.Delay(1.35);
            if (GodotObject.IsInstanceValid(picture)) picture.CreateTween().TweenProperty(picture, "modulate:a", 0f, 0.25);
            await Fx.Delay(0.27);
        }
        finally { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
internal static class RoomContextVoicePatch
{
    private static void Postfix(IRunState runState, AbstractRoom room)
    {
        if (room is not RestSiteRoom and not MerchantRoom) return;
        foreach (var player in runState.Players) ContextPresentation.Speak(player, "entry", 4);
    }
}
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRestSiteHeal))]
internal static class RestContextVoicePatch
{
    private static void Postfix(Player player) => ContextPresentation.Speak(player, "skill", 4);
}
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRestSiteSmith))]
internal static class SmithContextVoicePatch
{
    private static void Postfix(Player player) => ContextPresentation.Speak(player, "victory", 4);
}
[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
internal static class GameOverPresentationPatch
{
    private static void Postfix(NGameOverScreen __instance)
    {
        var run = (RunState?)AccessTools.Field(typeof(NGameOverScreen), "_runState").GetValue(__instance);
        if (!Settings.Current.Cinematics || run == null || !run.Players.Any(ContextPresentation.Applies)) return;
        ReadyPresentation.Cleanup();
        _ = TaskHelper.RunSafely(ContextPresentation.Outcome(__instance, run.CurrentRoom?.IsVictoryRoom == true));
    }
}
