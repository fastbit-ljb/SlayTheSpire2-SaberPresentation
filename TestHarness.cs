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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace SaberPresentation;

internal static class TestHarness
{
    private const string Output = "D:/L/Study/mod/SaberPresentation/build/verification";
    private static readonly List<object> Results = [];
    private static readonly HashSet<string> Captured = [];
    private static bool _running;
    private static readonly List<bool> MotionChecks = [];

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
            node.SetAnimationTrigger("Cast");
            await CaptureAfter("skill", 0.35);
            await Fx.Delay(1.4);
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 7, ValueProp.Move, target);
            await CaptureAfter("hurt", 0.08);
            await Fx.Delay(0.8);
            Assert("no_leftover_cinematic", !Runtime.Descendants(game).Any(n => n.Name == "SaberExcaliburCutIn"));
            Assert("final_actor_home", node.Visuals.Body.Position.DistanceTo(home) < 0.5f);
            Assert("melee_motion_reaches_resolved_targets", MotionChecks.Count >= 8 && MotionChecks.All(v => v));
            await CaptureAfter("combat_final", 0.1);
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
