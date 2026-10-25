#if SABER_TEST
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace SaberPresentation;

internal static class TestHarness
{
    private static bool WithAllMods => CommandLineHelper.HasArg("saber-all-test");
    private static bool WithTheme => WithAllMods || CommandLineHelper.HasArg("saber-theme-test");
    private static bool WithCzn => WithTheme || CommandLineHelper.HasArg("saber-czn-test");
    private static string Output => "D:/L/Study/mod/SaberPresentation/build/" + (WithAllMods ? "verification-all" : WithTheme ? "verification-theme" : WithCzn ? "verification-czn" : "verification");
    private static readonly List<object> Results = [];
    private static readonly HashSet<string> Captured = [];
    private static bool _running;
    private static readonly List<bool> MotionChecks = [];
    internal static int OutcomeCount;

    internal static async Task Run(NGame game)
    {
        if (_running) return;
        _running = true;
        System.IO.Directory.CreateDirectory(Output);
        try
        {
            if (!OS.GetUserDataDir().EndsWith("SlayTheSpire2-SaberPresentation-Test", StringComparison.Ordinal))
                throw new Exception("Refusing to test outside the isolated user directory.");
            await game.GameStartupComplete;
            await Fx.Delay(0.5);
            Log.Info("[SaberTest] Startup passed. Isolated user data: " + OS.GetUserDataDir());
            SaveManager.Instance.Progress.EnableFtues = false;
            Settings.Current.NoblePhantasmCards = ["BLUDGEON", "FIEND_FIRE", "PACTS_END"];
            Settings.Current.UseCznEffects = true;
            Assert("czn_pack_detection", CznAssets.Available == WithCzn);
            var character = ModelDb.Character<Ironclad>();
            var player = Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
            var acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
            acts[0] = ModelDb.Act<Overgrowth>().ToMutable();
            var run = RunState.CreateForNewRun([player], acts, [], GameMode.Standard, 0, "SABERPRESENTATIONTEST");
            RunManager.Instance.SetUpNewSingleplayer(run, false);
            await PreloadManager.LoadRunAssets([character]);
            RunManager.Instance.Launch();
            game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
            await RunManager.Instance.SetActInternal(0);
            RunManager.Instance.RunLocationTargetedBuffer.OnLocationChanged(run.RunLocation);
            RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(run.MapLocation);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, ModelDb.Encounter<TwoTailedRatsNormal>().ToMutable());
            await Fx.Delay(4);
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            var node = player.Creature.GetCreatureNode()!;
            Assert("saber_detected", Runtime.IsSaber(player.Creature));
            Assert("saber_identity_icon_preserved", player.Character.IconTexture is AtlasTexture);
            foreach (string raster in new[] { "saber_card_atlas", "saber_vfx_atlas", "saber_ready_attack", "saber_ready_cast", "camelot_stage", "saber_defeat", "saber_attack_sequence" })
                Assert("raster_decodes_" + raster, Art.Texture(raster).GetWidth() >= 1000);
            await VerifyInteractions(game, node);
            await VerifyCznSurfaces(game);
            var home = node.Visuals.Body.Position;
            Log.Info("[SaberTest] Loaded animations: " + string.Join(", ", node.Visuals.SpineBody!.GetSkeleton()!.GetData().GetAnimationNames()));
            Assert("voice_attack_decodes", Voice.Length("attack_1") > 0);
            Assert("voice_np_decodes", Voice.Length("np_release") > 0);
            foreach (string voice in new[] { "attack_1", "attack_2", "attack_3", "heavy", "skill_1", "skill_2", "entry_1", "entry_2",
                "victory_1", "victory_2", "hurt_1", "hurt_2", "death", "np_charge", "np_release", "np_end" })
                Assert("jp_audio_" + voice, Voice.Length(voice) > 0);
            foreach (string voice in new[] { "attack_4", "attack_5", "attack_6", "attack_7", "heavy_2", "heavy_3",
                "skill_3", "skill_4", "entry_3", "entry_4", "victory_3", "victory_4" }) Assert("jp_audio_" + voice, Voice.Length(voice) > 0);
            foreach (var enemy in state.HittableEnemies) { enemy.SetMaxHpInternal(500); enemy.SetCurrentHpInternal(500); }
            var target = state.HittableEnemies.First();
            var models = new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<PommelStrike>(),
                ModelDb.Card<Uppercut>(), ModelDb.Card<Hemokinesis>(), ModelDb.Card<Whirlwind>(), ModelDb.Card<Bash>(), ModelDb.Card<Bludgeon>() };
            foreach (var model in models)
            {
                var card = state.CreateCard(model, player);
                int before = target.CurrentHp;
                await DamageCmd.Attack(2).FromCard(card, null).Targeting(target).Execute(new BlockingPlayerChoiceContext());
                Assert("damage_unchanged_" + model.Id.Entry, before - target.CurrentHp == 2);
                await Fx.Delay(1.0);
                Assert("actor_returns_" + model.Id.Entry, node.Visuals.Body.Position.DistanceTo(home) < 0.5f);
            }
            var multi = state.CreateCard(ModelDb.Card<TwinStrike>(), player);
            int hp = target.CurrentHp;
            await DamageCmd.Attack(2).FromCard(multi, null).Targeting(target).WithHitCount(3).Execute(new BlockingPlayerChoiceContext());
            Assert("multi_hit_count_unchanged", hp - target.CurrentHp == 6);
            await Fx.Delay(1);
            var random = state.CreateCard(ModelDb.Card<SwordBoomerang>(), player);
            int totalHp = state.HittableEnemies.Sum(e => e.CurrentHp);
            int nativeCallbacks = 0;
            await DamageCmd.Attack(2).FromCard(random, null).TargetingRandomOpponents(state).WithHitCount(3)
                .BeforeDamage(() => { nativeCallbacks++; return Task.CompletedTask; }).Execute(new BlockingPlayerChoiceContext());
            Assert("random_damage_unchanged", totalHp - state.HittableEnemies.Sum(e => e.CurrentHp) == 6);
            Assert("native_callback_preserved", nativeCallbacks == 3);
            await Fx.Delay(1);
            var aoe = state.CreateCard(ModelDb.Card<PactsEnd>(), player);
            totalHp = state.HittableEnemies.Sum(e => e.CurrentHp);
            int targets = state.HittableEnemies.Count;
            await DamageCmd.Attack(2).FromCard(aoe, null).TargetingAllOpponents(state).Execute(new BlockingPlayerChoiceContext());
            Assert("aoe_damage_unchanged", totalHp - state.HittableEnemies.Sum(e => e.CurrentHp) == 2 * targets);
            await Fx.Delay(1.2);
            var finisher = state.CreateCard(ModelDb.Card<FiendFire>(), player);
            hp = target.CurrentHp;
            await DamageCmd.Attack(2).FromCard(finisher, null).Targeting(target).WithHitCount(3).Execute(new BlockingPlayerChoiceContext());
            Assert("np_multihit_damage_unchanged", hp - target.CurrentHp == 6);
            await Fx.Delay(2);
            var real = state.CreateCard(ModelDb.Card<Bludgeon>(), player);
            await CardPileCmd.Add(real, PileType.Hand);
            hp = target.CurrentHp;
            await real.OnPlayWrapper(new BlockingPlayerChoiceContext(), target, true,
                new ResourceInfo { EnergySpent = 0, EnergyValue = 3, StarsSpent = 0, StarValue = 0 }, true);
            Assert("real_bludgeon_card_damage_unchanged", hp - target.CurrentHp == 32);
            await Fx.Delay(1);
            await CreatureCmd.GainBlock(player.Creature, 5, ValueProp.Move, null);
            await CaptureAfter("shield", 0.08);
            int playerHp = player.Creature.CurrentHp;
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 2, ValueProp.Move, target);
            Assert("fully_blocked_damage_unchanged", player.Creature.CurrentHp == playerHp);
            await CaptureAfter("block_hit", 0.08);
            node.SetAnimationTrigger("Cast");
            await CaptureAfter("skill", 0.35);
            await Fx.Delay(1.4);
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 7, ValueProp.Move, target);
            await CaptureAfter("hurt", 0.08);
            await Fx.Delay(0.8);
            Assert("no_leftover_cinematic", !Runtime.Descendants(game).Any(n => n.Name == "SaberExcaliburCutIn"));
            Assert("final_actor_home", node.Visuals.Body.Position.DistanceTo(home) < 0.5f);
            Assert("melee_motion_reaches_resolved_targets", MotionChecks.Count >= 8 && MotionChecks.All(v => v));
            await Fx.Delay(1.2);
            Assert("czn_effects_no_errors", CznAssets.Failures == 0);
            Assert("czn_effects_used_when_available", WithCzn ? CznAssets.Spawned > 15 : CznAssets.Spawned == 0);
            Assert("czn_sfx_used_when_available", WithCzn ? CznAssets.SoundsPlayed > 0 : CznAssets.SoundsPlayed == 0);
            Assert("czn_effects_cleaned_up", !Runtime.Descendants(game).Any(n => n.Name == "SaberCznEffect"));
            Assert("painted_effects_cleaned_up", !Runtime.Descendants(game).Any(n => n.Name == "SaberPaintedEffect"));
            Assert("no_line_geometry", !Runtime.Descendants(game).OfType<Line2D>().Any(n => n.GetParent()?.Name.ToString().StartsWith("Saber") == true));
            if (WithCzn)
            {
                int spawned = CznAssets.Spawned;
                Settings.Current.UseCznEffects = false;
                Fx.Shield(Fx.Container(node), Runtime.Center(node));
                await CaptureAfter("fallback_shield", 0.08);
                Assert("czn_setting_disables_reuse", CznAssets.Spawned == spawned);
                Settings.Current.UseCznEffects = true;
            }
            await CaptureAfter("combat_final", 0.1);
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 9999, ValueProp.Move, target);
            Assert("native_player_death_resolves", player.Creature.IsDead);
            await Fx.Delay(4.5);
            Assert("native_game_over_triggers_saber_cg", OutcomeCount > 0);
            await Fx.Delay(1.8);
            Assert("game_over_cg_cleans_up", !Runtime.Descendants(game).Any(n => n.Name == "SaberOutcome"));
            Log.Info("[SaberTest] ALL CHECKS PASSED");
        }
        catch (Exception ex)
        {
            Results.Add(new { check = "exception", passed = false, detail = ex.ToString() });
            Log.Error("[SaberTest] " + ex);
        }
        finally
        {
            System.IO.File.WriteAllText(Output + "/results.json", JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            await Fx.Delay(0.3);
            game.GetTree().Quit();
        }
    }
    private static void Assert(string check, bool passed)
    {
        Results.Add(new { check, passed });
        Log.Info($"[SaberTest] {check}: {passed}");
        if (!passed) throw new Exception(check);
    }
    private static async Task VerifyInteractions(NGame game, NCreature actor)
    {
        var hand = Runtime.Descendants(game).OfType<NHandCardHolder>().Where(h => h.CardModel?.Owner == actor.Entity.Player).ToArray();
        var attack = hand.First(h => h.CardModel!.Type == CardType.Attack);
        var skill = hand.First(h => h.CardModel!.Type == CardType.Skill);
        int hp = actor.Entity.CurrentHp;
        attack.EmitSignal(Control.SignalName.FocusEntered);
        await CaptureAfter("hover_attack", 0.35);
        Assert("native_card_focus_shows_portrait", ReadyPresentation.Visible && ReadyPresentation.Card == attack.CardModel);
        Assert("attack_hover_changes_body_pose", ReadyPresentation.BodyReady && ReadyPresentation.Pose == 0);
        Vector2 sample = ReadyPresentation.MeshSample;
        await Fx.Delay(0.2);
        Assert("portrait_mesh_is_animated", sample.DistanceTo(ReadyPresentation.MeshSample) > 0.01f);
        Assert("saber_card_art_replaced", attack.CardNode!.GetNode<TextureRect>("%Portrait").Texture is AtlasTexture);
        if (WithTheme) for (int i = 0; i < 10; i++) await CaptureAfter("hover_frame_" + i.ToString("D2"), 0.055);
        attack.EmitSignal(Control.SignalName.FocusExited);
        skill.EmitSignal(Control.SignalName.FocusEntered);
        await CaptureAfter("hover_skill", 0.3);
        if (WithTheme) for (int i = 10; i < 20; i++) await CaptureAfter("hover_frame_" + i.ToString("D2"), 0.055);
        Assert("rapid_focus_switch_preserved", ReadyPresentation.Visible && ReadyPresentation.Card == skill.CardModel && ReadyPresentation.Pose == 1);
        skill.EmitSignal(Control.SignalName.FocusExited);
        attack.EmitSignal(Control.SignalName.FocusEntered);
        var play = NMouseCardPlay.Create(attack, "saber_test", false);
        NCombatRoom.Instance!.AddChild(play);
        play.Start();
        await Fx.Delay(0.16);
        attack.EmitSignal(Control.SignalName.FocusExited);
        await Fx.Delay(0.16);
        Assert("targeting_keeps_portrait", ReadyPresentation.Targeting && ReadyPresentation.Visible);
        play.CancelPlayCard();
        attack.CancelDrag();
        await CaptureAfter("hover_cancelled", 0.3);
        Assert("cancel_restores_portrait_and_body", !ReadyPresentation.Visible && !ReadyPresentation.BodyReady && !ReadyPresentation.Targeting);
        Assert("hover_does_not_damage_actor", actor.Entity.CurrentHp == hp);
        attack.EmitSignal(Control.SignalName.FocusEntered);
        await Fx.Delay(0.26);
        int framesBefore = ReadyPresentation.ActionFramesPlayed;
        var enemy = actor.Entity.CombatState!.HittableEnemies.First();
        int enemyHp = enemy.CurrentHp;
        _ = TaskHelper.RunSafely(CaptureAfter("portrait_attack_frame_0", 0.05));
        _ = TaskHelper.RunSafely(CaptureAfter("portrait_attack_frame_1", 0.17));
        _ = TaskHelper.RunSafely(CaptureAfter("portrait_attack_frame_2", 0.28));
        await attack.CardModel!.OnPlayWrapper(new BlockingPlayerChoiceContext(), enemy, true,
            new ResourceInfo { EnergySpent = 0, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, true);
        await Fx.Delay(0.5);
        Assert("native_card_play_animates_portrait", ReadyPresentation.ActionFramesPlayed - framesBefore >= 3);
        Assert("native_strike_damage_preserved", enemyHp - enemy.CurrentHp == 6);
        if (WithTheme)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToArray();
            Assert("czn_style_ui_loaded", assemblies.Contains("CznStyleUI"));
            Assert("czn_main_menu_loaded", assemblies.Contains("CznMainMenu"));
            Assert("czn_enemy_skin_loaded", assemblies.Contains("CznEnemySkin"));
        }
    }
    private static async Task VerifyCznSurfaces(NGame game)
    {
        if (WithCzn)
        {
            var registry = AccessTools.TypeByName("CznCore.Settings.SharedSettingsExtensionRegistry")!;
            var categories = (System.Collections.IEnumerable)AccessTools.Method(registry, "GetCategories").Invoke(null, null)!;
            Assert("czn_settings_accept_saber_controls", categories.Cast<object>().Any(c =>
                (string)c.GetType().GetProperty("Id")!.GetValue(c)! == "saber_presentation"));
        }
        var lobby = ResourceLoader.Load<PackedScene>("res://SaberPresentation/scenes/saber_lobby.tscn").Instantiate<Control>();
        var layer = new CanvasLayer { Layer = 90, Name = "SaberLobbyVerification" };
        game.AddChild(layer);
        layer.AddChild(lobby);
        await CaptureAfter("saber_live_lobby", 0.3);
        Assert("saber_live_lobby_loaded", lobby.GetChildCount() == 2 && lobby.GetChild(1) is Polygon2D);
        layer.QueueFree();
        await Fx.Delay(0.1);
        if (WithTheme)
        {
            var catalog = AccessTools.TypeByName("CznMainMenu.MainMenuCatalog")!;
            AccessTools.Method(catalog, "EnsureAvailableBackgroundsResolved").Invoke(null, null);
            object?[] args = ["skin_lobby_saber", null];
            Assert("czn_main_menu_discovers_saber_lobby", (bool)AccessTools.Method(catalog, "TryGetBackground").Invoke(null, args)!);
        }
    }
    internal static void OnImpact(string id, Creature attacker)
    {
        if (id is not "BLUDGEON" and not "FIEND_FIRE" and not "PACTS_END") MotionChecks.Add(Runtime.NearTargetAtImpact(attacker));
        if (Captured.Add(id)) TaskHelper.RunSafely(CaptureAfter("impact_" + id.ToLowerInvariant(), 0.045));
    }
    internal static async Task CaptureAfter(string name, double seconds)
    {
        await Fx.Delay(seconds);
        if (NGame.Instance == null) return;
        await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        image.SavePng(Output + "/" + name + ".png");
    }
}
#endif
