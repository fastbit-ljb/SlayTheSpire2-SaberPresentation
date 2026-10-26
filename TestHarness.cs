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
    private static bool StandaloneOnly => CommandLineHelper.HasArg("saber-standalone-test");
    private static bool WithTheme => StandaloneOnly || WithAllMods || CommandLineHelper.HasArg("saber-theme-test");
    private static bool WithCzn => WithTheme || CommandLineHelper.HasArg("saber-czn-test");
    private static string Output => "D:/L/Study/mod/SaberPresentation/build/" + (CommandLineHelper.HasArg("saber-finisher-smoke") ? "verification-finisher" : CommandLineHelper.HasArg("saber-nine-reference") ? "nine-inspection" : CommandLineHelper.HasArg("saber-rig-preview") ? "verification-rig" : CommandLineHelper.HasArg("saber-fix-smoke") ? "verification-fix-hd" : CommandLineHelper.HasArg("saber-menu-test") ? "verification-menu" : StandaloneOnly ? "verification-standalone" : WithAllMods ? "verification-all" : WithTheme ? "verification-theme" : WithCzn ? "verification-czn" : "verification");
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
            bool isolatedByEnvironment = System.Environment.GetEnvironmentVariable("SABER_TEST_ISOLATED") == "1"
                && OS.GetUserDataDir().Contains("SaberPresentation", StringComparison.OrdinalIgnoreCase);
            if (!OS.GetUserDataDir().EndsWith("SlayTheSpire2-SaberPresentation-Test", StringComparison.Ordinal)
                && !isolatedByEnvironment)
                throw new Exception("Refusing to test outside the isolated user directory.");
            if (!CommandLineHelper.HasArg("saber-menu-test")) game.StartOnMainMenu = false;
            await game.GameStartupComplete;
            if (CommandLineHelper.HasArg("saber-finisher-smoke")) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            else if (StandaloneOnly || CommandLineHelper.HasArg("saber-fix-smoke")) DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
            await Fx.Delay(0.5);
            Log.Info("[SaberTest] Startup passed. Isolated user data: " + OS.GetUserDataDir());
            SaveManager.Instance.Progress.EnableFtues = false;
            Settings.Current.NoblePhantasmCards = ["BLUDGEON", "FIEND_FIRE", "PACTS_END"];
            Settings.Current.UseCznEffects = true;
            Assert("czn_pack_detection", CznAssets.Available == WithCzn);
            var foreign = new[] { "CznCore", "CznStyleUI", "CznMainMenu", "CznEnemySkin", "CznNineCharacter" };
            Assert("no_foreign_runtime_assemblies", !AppDomain.CurrentDomain.GetAssemblies().Any(a => foreign.Contains(a.GetName().Name)));
            Assert("no_czn_shared_settings_registry", AppDomain.CurrentDomain.GetData("CznModConfig.SharedSettingsExtensions.v1") == null);
            if (StandaloneOnly)
            {
                var mods = MegaCrit.Sts2.Core.Modding.ModManager.GetLoadedMods().ToArray();
                Assert("only_one_native_mod_installed", mods.Length == 1 && mods[0].manifest?.id == "SaberPresentation");
                Assert("zero_manifest_dependencies", mods[0].manifest!.dependencies?.Count == 0);
            }
            if (CommandLineHelper.HasArg("saber-menu-test"))
            {
                await PreloadManager.LoadMainMenuEssentials();
                await (Task)AccessTools.Method(typeof(NGame), "LoadMainMenu").Invoke(game, [false])!;
                await Fx.Delay(2);
                await CaptureAfter("standalone_main_menu", 0.2);
                Assert("native_main_menu_created", game.MainMenu != null);
                Assert("native_main_menu_no_theme_host", !Runtime.Descendants(game.MainMenu!).Any(n => n.Name.ToString().Contains("Czn") || n.Name.ToString().Contains("SaberLobby")));
                Assert("no_custom_lobby_resource", !ResourceLoader.Exists("res://SaberPresentation/scenes/saber_lobby.tscn"));
                Assert("anime_music_absent_from_main_menu", !Runtime.Descendants(game).Any(n => n.Name == "SaberAnimeMusic"));
                Log.Info("[SaberTest] MENU CHECKS PASSED");
                return;
            }
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
            Assert("enemy_skin_module_absent", !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CznEnemySkin"));
            Assert("card_art_override_not_installed", AccessTools.TypeByName("SaberPresentation.SaberCardArtPatch") == null);
            Assert("saber_detected", Runtime.IsSaber(player.Creature));
            Assert("saber_identity_icon_preserved", player.Character.IconTexture is AtlasTexture);
            foreach (string raster in new[] { "saber_vfx_atlas", "saber_ready_attack", "saber_ready_cast", "camelot_stage", "saber_defeat", "saber_rig_body_v1", "saber_rig_head_v1", "saber_arm_parts_v1" })
                Assert("raster_decodes_" + raster, Art.Texture(raster).GetWidth() >= 1000);
            Assert("official_excalibur_video_decodes", Art.ExcaliburVideo != null);
            Assert("animated_defeat_video_decodes", Art.DefeatVideo != null);
            if (CommandLineHelper.HasArg("saber-finisher-smoke"))
            {
                // Run creation applies the saved display options. Select the
                // recording surface after that native setup has completed.
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                await Fx.Delay(0.35);
                Assert("finisher_test_window_is_fullscreen", DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
                await VerifyKillingBlow(state, player, state.HittableEnemies.First());
                Log.Info("[SaberTest] FINISHER SMOKE CHECKS PASSED");
                return;
            }
            await VerifyMusic();
            if (CommandLineHelper.HasArg("saber-nine-reference")) { await ExportNineReference(game); return; }
            if (CommandLineHelper.HasArg("saber-rig-preview")) { await ExportBattleReference(game, node); await ExportPortraitMotion(game, node); return; }
            await VerifyPortraitDeformation(game);
            await VerifyInteractions(game, node);
            await VerifyHoverDuringReturn(game, node, state, player);
            await VerifyNativeUi(game, player);
            if (CommandLineHelper.HasArg("saber-fix-smoke"))
            {
                Assert("actual_1920x1080_render", game.GetViewport().GetTexture().GetWidth() == 1920 && game.GetViewport().GetTexture().GetHeight() == 1080);
                Log.Info("[SaberTest] FIX SMOKE CHECKS PASSED");
                return;
            }
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
            Assert("nonlethal_cards_do_not_trigger_cg", Runtime.KillingBlowCount == 0);
            target = await VerifyKillingBlow(state, player, target);
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
            Assert("game_over_uses_animated_defeat_cg", ContextPresentation.LastDefeatUsedVideo);
            for (int i = 0; i < 120 && Runtime.Descendants(game).Any(n => n.Name == "SaberOutcome"); i++) await Fx.Delay(0.1);
            Assert("defeat_video_advances_beyond_single_frame", ContextPresentation.LastDefeatVideoSeconds > 8.5);
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

    private static async Task VerifyMusic()
    {
        var controller = MegaCrit.Sts2.Core.Nodes.Audio.NRunMusicController.Instance!;
        Assert("anime_bgm_instrumental_decodes", AnimeMusic.Length > 1800);
        Assert("anime_bgm_owned_by_saber_run", AnimeMusic.Owns(controller) && AnimeMusic.Playing);
        Assert("native_run_music_suppressed", controller.Get("_currentTrack").AsString() == "");
        double before = AnimeMusic.Position;
        controller.UpdateMusic();
        await Fx.Delay(0.3);
        Assert("anime_bgm_does_not_restart_on_update", AnimeMusic.Position > before);
        var audio = SaveManager.Instance.SettingsSave;
        float master = audio.VolumeMaster, bgm = audio.VolumeBgm;
        audio.VolumeMaster = 1; audio.VolumeBgm = 1;
        await Fx.Delay(1.7);
        float baseline = AnimeMusic.Gain;
        AnimeMusic.DuckVoice(0.5);
        await Fx.Delay(0.18);
        Assert("anime_bgm_ducks_for_japanese_voice", AnimeMusic.Gain < baseline * 0.35f);
        using (AnimeMusic.PauseForCinematic())
        {
            double paused = AnimeMusic.Position;
            await Fx.Delay(0.25);
            Assert("anime_bgm_pauses_for_cg", AnimeMusic.PausedForCg && Math.Abs(AnimeMusic.Position - paused) < 0.10);
        }
        await Fx.Delay(0.2);
        Assert("anime_bgm_resumes_after_cg", !AnimeMusic.PausedForCg && AnimeMusic.Playing);
        audio.VolumeBgm = 0;
        await Fx.Delay(0.5);
        Assert("anime_bgm_obeys_game_music_mute", AnimeMusic.Gain < 0.001f);
        audio.VolumeMaster = master; audio.VolumeBgm = bgm;
    }
    private static void Assert(string check, bool passed)
    {
        Results.Add(new { check, passed });
        Log.Info($"[SaberTest] {check}: {passed}");
        if (!passed) throw new Exception(check);
    }
    private static async Task<Creature> VerifyKillingBlow(CombatState state, Player player, Creature target)
    {
        await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.TwoTailedRat>(state, "first");
        await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.TwoTailedRat>(state, "second");
        await Fx.Delay(0.3);
        foreach (var enemy in state.HittableEnemies) { enemy.SetMaxHpInternal(500); enemy.SetCurrentHpInternal(500); }
        var strike = state.CreateCard(ModelDb.Card<StrikeIronclad>(), player);
        target.SetCurrentHpInternal(2);
        await CreatureCmd.GainBlock(target, 10, ValueProp.Unpowered, null);
        await DamageCmd.Attack(2).FromCard(strike, null).Targeting(target).Execute(new BlockingPlayerChoiceContext());
        Assert("blocked_low_hp_target_does_not_trigger_cg", Runtime.KillingBlowCount == 0 && target.CurrentHp == 2);
        await CreatureCmd.LoseBlock(new BlockingPlayerChoiceContext(), target, target.Block, player.Creature);
        await CardPileCmd.Add(strike, PileType.Hand);
        await strike.OnPlayWrapper(new BlockingPlayerChoiceContext(), target, true,
            new ResourceInfo { EnergySpent = 0, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, true);
        Assert("real_lethal_strike_triggers_excalibur_once", target.IsDead && Runtime.KillingBlowCount == 1);
        Assert("killing_blow_uses_animated_cg", Fx.LastCutInUsedVideo);
        Assert("killing_blow_uses_integrated_battle_stage", Fx.LastCutInIntegrated);
        Assert("finisher_actor_stays_on_original_battle_mark", FinisherStage.LastActorScreenDrift < 1f);
        Assert("finisher_does_not_independently_resize_actor", Math.Abs(FinisherStage.LastActorScaleMultiplier - 1f) < 0.01f);
        Assert("finisher_target_stays_on_original_mark_except_hit_recoil", FinisherStage.LastTargetScreenDrift <= 30f);
        Assert("finisher_warm_camelot_is_visibly_revealed", FinisherStage.LastVisibleStageSeconds >= 1.8);
        Assert("finisher_beam_is_visible_with_actor_and_target", FinisherStage.LastVisibleBeamSeconds >= 1.55
            && FinisherStage.LastVisibleReleaseSamples >= 20);
        Assert("finisher_environment_changes_with_timeline", FinisherStage.LastEnvironmentChanged);
        Assert("finisher_target_receives_stage_hit", FinisherStage.LastTargetHit);
        Assert("finisher_emitter_follows_forward_sword", FinisherStage.LastEmitterOnForwardSword);
        Assert("finisher_emitter_uses_live_sword_bone", FinisherStage.LastEmitterUsesSwordBone);
        Assert("finisher_stage_releases_live_bodies", !FinisherStage.Active);
        Assert("finisher_releases_only_one_beam", FinisherStage.LastReleaseCount == 1);
        Assert("hsr_cg_reaches_final_column_impact", FinisherStage.LastVideoSeconds > 7.45);
        Assert("finisher_actor_restores_original_parent", player.Creature.GetCreatureNode()!.Visuals.Body.GetParent().Name != "SaberFinisherActor");
        Assert("finisher_target_restores_original_parent", target.GetCreatureNode()?.Visuals.Body.GetParent().Name != "SaberFinisherTarget");
        Assert("excalibur_muzzle_does_not_cover_saber", Fx.LastExcaliburOriginSize.X <= 140 && Fx.LastExcaliburOriginSize.Y <= 170);
        Assert("full_jp_charge_finishes_before_release", Runtime.LastChargeDuration >= Voice.Length("np_charge") - 0.05);
        await Fx.Delay(0.6);
        var twin = state.CreateCard(ModelDb.Card<TwinStrike>(), player);
        var multiTarget = state.HittableEnemies.First();
        multiTarget.SetCurrentHpInternal(3);
        await DamageCmd.Attack(2).FromCard(twin, null).Targeting(multiTarget).WithHitCount(3).Execute(new BlockingPlayerChoiceContext());
        Assert("multihit_kill_triggers_once", multiTarget.IsDead && Runtime.KillingBlowCount == 2);
        await Fx.Delay(0.6);
        foreach (var enemy in state.HittableEnemies.Take(2)) enemy.SetCurrentHpInternal(2);
        var aoe = state.CreateCard(ModelDb.Card<Whirlwind>(), player);
        int living = state.HittableEnemies.Count;
        await DamageCmd.Attack(2).FromCard(aoe, null).TargetingAllOpponents(state).WithHitCount(2).Execute(new BlockingPlayerChoiceContext());
        Assert("aoe_two_kills_one_cg", state.HittableEnemies.Count == living - 2 && Runtime.KillingBlowCount == 3);
        await Fx.Delay(1.0);
        Assert("finisher_cg_cleans_up", !Runtime.Descendants(NGame.Instance!).Any(n => n.Name == "SaberExcaliburCutIn" || n.Name == "SaberIntegratedFinisher"));
        var actorBody = player.Creature.GetCreatureNode()!.Visuals.Body;
        var liveTargetBody = state.HittableEnemies.First().GetCreatureNode()!.Visuals.Body;
        var actorParent = actorBody.GetParent();
        var targetParent = liveTargetBody.GetParent();
        var actorTransform = actorBody.Transform;
        var targetTransform = liveTargetBody.Transform;
        var interrupted = FinisherStage.Play(player.Creature.GetCreatureNode()!, state.HittableEnemies.First().GetCreatureNode(), 1.0);
        await Fx.Delay(0.22);
        FinisherStage.Cancel();
        await interrupted;
        await Fx.Delay(0.15);
        Assert("finisher_cancel_restores_live_body_ownership", actorBody.GetParent() == actorParent && liveTargetBody.GetParent() == targetParent);
        Assert("finisher_cancel_restores_live_body_transforms", actorBody.Transform.IsEqualApprox(actorTransform) && liveTargetBody.Transform.IsEqualApprox(targetTransform));
        Assert("finisher_cancel_cleans_stage", !FinisherStage.Active && !Runtime.Descendants(NGame.Instance!).Any(n => n.Name == "SaberIntegratedFinisher"));
        return state.HittableEnemies.First();
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
        Assert("hover_uses_anime_portrait_art", ReadyPresentation.AnimePortraitReady
            && ReadyPresentation.PortraitSource?.EndsWith("/saber_rig_body_v1.png") == true);
        Assert("portrait_is_native_weighted_skeleton", ReadyPresentation.RigBones >= 24);
        Assert("hover_native_art_resolution", ReadyPresentation.PortraitResolution.X >= 1024 && ReadyPresentation.PortraitResolution.Y >= 1536);
        Assert("hover_does_not_clone_battle_or_selection_spine", !Runtime.Descendants(game).Any(n => n.Name.ToString().Contains("HoverSpine")));
        Assert("attack_hover_holds_charge_pose", ReadyPresentation.BodyReady
            && ReadyPresentation.BodyPoseClip == "attack"
            && Math.Abs(ReadyPresentation.BodyPoseProgress - 0.30f) < 0.001f);
        Vector2 portraitSample = ReadyPresentation.MeshSample;
        await Fx.Delay(0.28);
        Assert("anime_cloth_animates_while_hovered", portraitSample.DistanceTo(ReadyPresentation.MeshSample) > 0.01f);
        var art = attack.CardNode!.GetNode<TextureRect>("%Portrait").Texture;
        string artPath = art is AtlasTexture atlas ? atlas.Atlas.ResourcePath : art.ResourcePath;
        Assert("original_card_portrait_preserved", !artPath.Contains("saber_card_atlas", StringComparison.OrdinalIgnoreCase));
        var energy = Runtime.Descendants(game).OfType<NEnergyCounter>().Single();
        var energyLabel = energy.GetNode<Control>("Label");
        Assert("native_energy_visible_during_hover", energy.IsVisibleInTree() && energyLabel.IsVisibleInTree());
        Assert("native_energy_above_portrait", NCombatRoom.Instance!.Ui.EnergyCounterContainer.ZIndex >= 200);
        Assert("native_energy_in_viewport", game.GetViewport().GetVisibleRect().Intersects(energy.GetGlobalRect()));
        var owner = actor.Entity.Player!;
        var readout = energy.GetNode<Label>("Label");
        int originalEnergy = owner.PlayerCombatState!.Energy;
        foreach (int amount in new[] { 2, 0, originalEnergy })
        {
            await PlayerCmd.SetEnergy(amount, owner);
            await Fx.Delay(0.2);
            Assert("energy_readout_updates_" + amount, readout.Text == $"{amount}/{owner.PlayerCombatState.MaxEnergy}");
            Assert("energy_stays_visible_" + amount, energy.IsVisibleInTree() && readout.IsVisibleInTree());
        }
        if (WithTheme) for (int i = 0; i < 10; i++) await CaptureAfter("hover_frame_" + i.ToString("D2"), 0.055);
        ReadyPresentation.Unfocus(attack.CardModel);
        ReadyPresentation.Focus(skill.CardModel);
        await CaptureAfter("hover_skill", 0.3);
        Assert("rapid_focus_switch_preserved", ReadyPresentation.Visible && ReadyPresentation.Card == skill.CardModel && ReadyPresentation.Pose == 1);
        Assert("skill_hover_holds_sword_in_ground_pose", ReadyPresentation.BodyReady
            && ReadyPresentation.BodyPoseClip == "cast"
            && Math.Abs(ReadyPresentation.BodyPoseProgress - 0.90f) < 0.001f);
        Assert("hover_pose_has_no_painted_combat_vfx", !Runtime.Descendants(game).Any(n => n.Name == "SaberPaintedEffect"));
        ReadyPresentation.Unfocus(skill.CardModel);
        Assert("mouse_leave_retracts_hover_pose", !ReadyPresentation.BodyReady && ReadyPresentation.BodyPoseClip == null);
        ReadyPresentation.Cancel(skill.CardModel);
        await Fx.Delay(0.16);
        Assert("mouse_leave_hides_ready_portrait", !ReadyPresentation.Visible);
        ReadyPresentation.Focus(attack.CardModel, targeting: true);
        await Fx.Delay(0.16);
        ReadyPresentation.Unfocus(attack.CardModel);
        await Fx.Delay(0.16);
        Assert("targeting_keeps_portrait", ReadyPresentation.Targeting && ReadyPresentation.Visible);
        ReadyPresentation.Cancel(attack.CardModel);
        await CaptureAfter("hover_cancelled", 0.3);
        Assert("cancel_restores_portrait_and_body", !ReadyPresentation.Visible && !ReadyPresentation.BodyReady && !ReadyPresentation.Targeting);
        Assert("cancel_retracts_hover_pose", ReadyPresentation.BodyPoseClip == null);
        Assert("hover_does_not_damage_actor", actor.Entity.CurrentHp == hp);
        attack.EmitSignal(Control.SignalName.FocusEntered);
        await Fx.Delay(0.26);
        int retractionsBefore = ReadyPresentation.RetractionCount;
        string? portraitTextures = ReadyPresentation.TextureSignature;
        var enemy = actor.Entity.CombatState!.HittableEnemies.First();
        int enemyHp = enemy.CurrentHp;
        _ = TaskHelper.RunSafely(CaptureAfter("ready_portrait_exit_00", 0.025));
        _ = TaskHelper.RunSafely(CaptureAfter("ready_portrait_exit_01", 0.085));
        _ = TaskHelper.RunSafely(CaptureAfter("ready_portrait_exit_02", 0.16));
        _ = TaskHelper.RunSafely(CaptureAfter("battle_body_attack", 0.34));
        await attack.CardModel!.OnPlayWrapper(new BlockingPlayerChoiceContext(), enemy, true,
            new ResourceInfo { EnergySpent = 0, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, true);
        await Fx.Delay(1.3);
        Assert("ready_portrait_retracts_on_card_play", ReadyPresentation.RetractionCount > retractionsBefore
            && ReadyPresentation.ActionRetracted && !ReadyPresentation.Visible);
        Assert("portrait_textures_unchanged_through_ready_exit", portraitTextures == ReadyPresentation.TextureSignature);
        Assert("native_strike_damage_preserved", enemyHp - enemy.CurrentHp == 6);
    }

    private static async Task VerifyPortraitDeformation(NGame game)
    {
        var viewport = new SubViewport { Name = "PortraitRenderProof", Size = new Vector2I(512, 768), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        game.AddChild(viewport);
        var canvas = new Node2D { Position = new Vector2(256, 768), Scale = Vector2.One * .5f };
        viewport.AddChild(canvas);
        var rig = new PortraitRig(canvas);
        async Task<byte[]> Pose(string name, string clip, double time, double wind)
        {
            rig.ExportPose(clip, time, wind);
            await Fx.Delay(.06);
            var drawn = game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            RenderingServer.ForceDraw(); await drawn;
            using var frame = viewport.GetTexture().GetImage();
            frame.SavePng(Output + "/rig_pixels_" + name + ".png");
            return frame.GetData();
        }
        int Changed(byte[] a, byte[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i += 4)
                if ((a[i + 3] > 30 || b[i + 3] > 30) && Enumerable.Range(0, 4).Any(k => Math.Abs(a[i + k] - b[i + k]) > 25)) count++;
            return count;
        }
        rig.ExportHeadOnly(true);
        var hairRest = await Pose("hair_rest", "b_idle", 0, 0);
        var wind = await Pose("hair", "b_idle", 0, 1.68);
        rig.ExportHeadOnly(false);
        var idle = await Pose("idle", "b_idle", 0, 0);
        var idleMotion = await Pose("idle_motion", "b_idle", 1.12, 1.12);
        int hairPixels = Changed(hairRest, wind), bodyPixels = Changed(idle, idleMotion);
        Log.Info("[SaberTest] Actual portrait pixels changed: hair=" + hairPixels + ", idle=" + bodyPixels);
        Assert("portrait_hair_pixels_really_deform", hairPixels >= 200);
        Assert("portrait_idle_pixels_really_deform", bodyPixels >= 8000);
        viewport.QueueFree();
    }

    private static async Task ExportNineReference(NGame game)
    {
        const string pack = "D:/L/Game/Steam/steamapps/workshop/content/2868840/3815657213/CznNineCharacter.pck";
        if (!ProjectSettings.LoadResourcePack(pack, false)) throw new Exception("Nine reference pack missing");
        var node = ResourceLoader.Load<PackedScene>("res://CznNineCharacter/scenes/nine_battle_ready.tscn").Instantiate<Node2D>();
        game.AddChild(node);
        node.ZIndex = 100;
        var spine = new MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite(node);
        await Fx.Delay(.3);
        var skeleton = spine.GetSkeleton()!;
        var methods = skeleton.BoundObject.GetMethodList().Select(m => m["name"].AsString()).ToArray();
        Log.Info("[SaberTest] Nine skeleton methods: " + string.Join(",", methods));
        var bones = skeleton.BoundObject.Call("get_bones").AsGodotArray();
        var bd = bones[0].AsGodotObject().Call("get_data").AsGodotObject();
        Log.Info("[SaberTest] Nine bone data methods: " + string.Join(",", bd.GetMethodList().Select(m => m["name"].AsString())));
        var names = bones.Select(b => b.AsGodotObject().Call("get_data").AsGodotObject().Call("get_bone_name").AsString()).ToArray();
        Log.Info("[SaberTest] Nine bones: " + string.Join(",", names));
        var records = new List<object>();
        foreach (string clip in new[] { "b_idle", "card_attack", "card_casting" })
        {
            spine.GetAnimationState().SetAnimation(clip, false);
            using var entry = spine.GetAnimationState().GetCurrent(0)!;
            entry.SetTimeScale(0); entry.SetMixDuration(0);
            float length = entry.GetAnimationDuration();
            for (int frame = 0; frame <= 30; frame++)
            {
                entry.SetTrackTime(length * frame / 30f);
                await Fx.Delay(.025);
                foreach (string bone in names)
                    if (spine.GetGlobalBoneTransform(bone) is { } transform)
                        records.Add(new { clip, length, time = length * frame / 30f, bone,
                            x = transform.Origin.X, y = transform.Origin.Y, angle = Mathf.RadToDeg(transform.X.Angle()) });
                await CaptureAfter("nine_" + clip + "_" + frame.ToString("D2"), .001);
            }
        }
        System.IO.File.WriteAllText(Output + "/nine_motion_reference.json", JsonSerializer.Serialize(records));
        node.QueueFree();
        Log.Info("[SaberTest] NINE REFERENCE COMPLETE");
    }

    private static async Task ExportPortraitMotion(NGame game, NCreature actor)
    {
        var card = Runtime.Descendants(game).OfType<NHandCardHolder>().First(h => h.CardModel != null && h.CardModel.Owner == actor.Entity.Player && h.CardModel.Type == CardType.Attack).CardModel!;
        ReadyPresentation.Focus(card);
        await Fx.Delay(0.4);
        string folder = Output + "/frames";
        System.IO.Directory.CreateDirectory(folder);
        int index = 0;
        // Offline render of native GPU-skinned game poses at a fixed 30 fps;
        // this is an animation review export, not a performance measurement.
        foreach (var (clip, frames) in new[] { ("b_idle", 180) })
        {
            for (int frame = 0; frame < frames; frame++)
            {
                ReadyPresentation.ExportPose(clip, frame / 30.0);
                await Fx.Delay(0.02); // Flush native Bone2D/Skeleton2D transforms before offscreen draw.
                var drawn = game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                RenderingServer.ForceDraw();
                await drawn;
                using var image = game.GetViewport().GetTexture().GetImage();
                image.SaveJpg(folder + "/portrait_" + index.ToString("D4") + ".jpg", 0.9f);
                index++;
            }
        }
        Assert("continuous_portrait_preview_rendered", index == 180 && ReadyPresentation.RigBones >= 20);
        Log.Info("[SaberTest] RIG PREVIEW COMPLETE: " + folder);
    }

    private static async Task ExportBattleReference(NGame game, NCreature actor)
    {
        var spine = actor.Visuals.SpineBody!;
        var state = spine.GetAnimationState();
        var records = new List<object>();
        foreach (string clip in new[] { "attack", "attackvfff", "attack_heavy", "cast", "relaxed_loop", "relaxed_loop2" })
        {
            if (!spine.HasAnimation(clip)) continue;
            state.SetAnimation(clip, false);
            using var entry = state.GetCurrent(0)!;
            entry.SetTimeScale(0);
            entry.SetMixDuration(0);
            float length = entry.GetAnimationDuration();
            for (int step = 0; step <= 10; step++)
            {
                entry.SetTrackTime(length * step / 10f);
                await Fx.Delay(0.04);
                foreach (string bone in new[] { "wuqi3", "wuqi2o", "Ys_wuqi2", "Ys_wuqi1", "jian", "Ys_rjian4", "Ys_rjian3" })
                    if (spine.GetGlobalBoneTransform(bone) is { } transform)
                        records.Add(new { clip, progress = step / 10f, bone, x = transform.Origin.X,
                            y = transform.Origin.Y, angle = Mathf.RadToDeg(transform.X.Angle()) });
                await CaptureAfter("native_" + clip + "_" + step.ToString("D2"), 0.01);
            }
        }
        System.IO.File.WriteAllText(Output + "/native_sword_motion.json", JsonSerializer.Serialize(records));
        state.SetAnimation("idle_loop", true);
        using var idle = state.GetCurrent(0); idle?.SetTimeScale(1);
    }

    private static async Task VerifyHoverDuringReturn(NGame game, NCreature actor, CombatState state, Player player)
    {
        var home = actor.Visuals.Body.Position;
        var card = state.CreateCard(ModelDb.Card<StrikeIronclad>(), player);
        var target = state.HittableEnemies.First();
        target.SetMaxHpInternal(500); target.SetCurrentHpInternal(500);
        for (int i = 0; i < 4; i++)
        {
            await DamageCmd.Attack(1).FromCard(card, null).Targeting(target).Execute(new BlockingPlayerChoiceContext());
            await Fx.Delay(0.19);
            ReadyPresentation.Focus(card);
            await Fx.Delay(0.14);
            if (i % 2 == 1) actor.SetAnimationTrigger("SaberVictory");
            await Fx.Delay(0.35);
            Assert("hover_during_return_no_drift_" + i, actor.Visuals.Body.Position.DistanceTo(home) < 0.5f);
            ReadyPresentation.Cancel(card);
        }
        await CaptureAfter("fixed_energy_native_cards_and_actor_home", 0.3);
    }
    private static async Task VerifyNativeUi(NGame game, Player player)
    {
        var nodes = Runtime.Descendants(game).ToArray();
        Assert("no_czn_hud_menu_or_audio_nodes", !nodes.Any(n => n.Name.ToString().StartsWith("Czn")));
        var gold = nodes.Single(n => n.GetType().Name == "NTopBarGold");
        var label = gold.GetNode<Label>("%GoldLabel");
        Assert("native_gold_label_visible_and_accurate", label.IsVisibleInTree() && label.Text == player.Gold.ToString());
        var textures = Runtime.Descendants(gold).OfType<TextureRect>().Where(n => n.Texture != null).ToArray();
        Assert("native_gold_icon_preserved", textures.Length > 0 && textures.All(n => !n.Texture.ResourcePath.Contains("Czn")));
        var ui = NCombatRoom.Instance!.Ui;
        foreach (var holder in Runtime.Descendants(ui.Hand).OfType<NHandCardHolder>().Where(h => h.CardNode != null))
        {
            var card = holder.CardNode!;
            Assert("native_card_frame_" + holder.CardModel!.Id.Entry, card.GetNode<TextureRect>("%Frame").Texture == holder.CardModel.Frame);
        }
        Assert("native_energy_counter_retained", Runtime.Descendants(ui.EnergyCounterContainer).OfType<NEnergyCounter>().Single().IsVisibleInTree());
        Assert("native_end_turn_button_retained", ui.EndTurnButton.IsVisibleInTree());
        Assert("no_ui_assets_in_character_payload", !ResourceLoader.Exists("res://CznStyleUI/CznStyleUI.cs") && !ResourceLoader.Exists("res://SaberPresentation/scenes/saber_lobby.tscn"));
        await CaptureAfter("native_ui_character_presentation", 0.15);
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
        var drawn = NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        RenderingServer.ForceDraw();
        await drawn;
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        // Encoding screenshots must not block the animation being measured.
        await Task.Run(() => image.SavePng(Output + "/" + name + ".png"));
    }
    internal static async Task CaptureFinisherSequence()
    {
        for (int frame = 0; frame < 60; frame++) await CaptureAfter($"finisher_frame_{frame:D2}", 0.075);
    }
    internal static async Task CaptureHandoffSequence()
    {
        for (int frame = 0; frame < 12; frame++) await CaptureAfter($"finisher_handoff_{frame:D2}", 0.045);
    }
}
#endif
