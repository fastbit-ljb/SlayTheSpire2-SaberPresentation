#if SABER_DEMO
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
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace SaberPresentation;

// Recording-only build, never included in the installed/released mod DLL.
internal static class DemoHarness
{
    private static int _take;
    private static int _maxHandCards, _maxHandHolders;
    private static int _finisherCountBefore;
    private static bool _finisherVerified;
    private static Player? _player;
    private static bool Smoke => CommandLineHelper.HasArg("saber-demo-smoke");
    private static Label _caption = null!;
    private static string Marker => System.IO.Path.Combine(Standalone.Root, "demo-state.json");
    private static void State(string phase)
    {
        int handCards = _player?.PlayerCombatState?.Hand.Cards.Count ?? 0;
        int handHolders = NPlayerHand.Instance?.CardHolderContainer?.GetChildren().OfType<NHandCardHolder>().Count() ?? 0;
        _maxHandCards = Math.Max(_maxHandCards, handCards);
        _maxHandHolders = Math.Max(_maxHandHolders, handHolders);
        var evidence = new
        {
            take = _take, phase, fullscreen = DisplayServer.WindowGetMode().ToString(),
            smoke = Smoke, handCards, handHolders, maxHandCards = _maxHandCards, maxHandHolders = _maxHandHolders,
            finisherVerified = _finisherVerified, killingBlowsThisTake = Runtime.KillingBlowCount - _finisherCountBefore,
            integratedStageStarted = FinisherStage.LastStageStarted, integratedStageActive = FinisherStage.Active,
            videoSeconds = FinisherStage.LastVideoSeconds, releaseCount = FinisherStage.LastReleaseCount,
            targetHit = FinisherStage.LastTargetHit, emitterUsesSwordBone = FinisherStage.LastEmitterUsesSwordBone
        };
        System.IO.File.WriteAllText(Marker, System.Text.Json.JsonSerializer.Serialize(evidence));
        Log.Info("[SaberDemo] " + System.Text.Json.JsonSerializer.Serialize(evidence));
    }
    private static Task Hold(double seconds) => Fx.Delay(Smoke ? Math.Min(seconds, 0.75) : seconds);
    internal static async Task StartWhenReady()
    {
        while (NGame.Instance == null) await Task.Delay(50);
        await Run(NGame.Instance);
    }
    internal static async Task Run(NGame game)
    {
        try
        {
            if (!OS.GetUserDataDir().EndsWith("SlayTheSpire2-SaberPresentation-Demo", StringComparison.Ordinal))
                throw new InvalidOperationException("Recording mode requires its isolated demo profile.");
            game.StartOnMainMenu = false;
            await game.GameStartupComplete;
            SaveManager.Instance.Progress.EnableFtues = false;
            SaveManager.Instance.SettingsSave.VolumeMaster = 0.85f;
            SaveManager.Instance.SettingsSave.VolumeSfx = 0.9f;
            Settings.Current.Enabled = Settings.Current.Cinematics = Settings.Current.JapaneseVoice = true;
            Settings.Current.KillingBlowExcalibur = Settings.Current.UseCznEffects = true;
            Settings.Current.CardHoverPresentation = Settings.Current.ReadyBodyMotion = true;
            Settings.Current.HoverVoice = true;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            DisplayServer.WindowMoveToForeground();
            var overlay = new CanvasLayer { Layer = 95, Name = "SaberRecordingControls" };
            game.AddChild(overlay);
            _caption = new Label { Position = new Vector2(45, 100), MouseFilter = Control.MouseFilterEnum.Ignore };
            _caption.AddThemeFontSizeOverride("font_size", 28);
            _caption.AddThemeColorOverride("font_shadow_color", Colors.Black);
            _caption.AddThemeConstantOverride("shadow_offset_x", 2);
            _caption.AddThemeConstantOverride("shadow_offset_y", 2);
            overlay.AddChild(_caption);
            await PreloadManager.LoadMainMenuEssentials();
            await (Task)AccessTools.Method(typeof(NGame), "LoadMainMenu").Invoke(game, [false])!;
            _caption.Text = "Saber 全屏演示已就绪\nF8 开始／重播  ·  F9 隐藏提示\n开始后留 15 秒准备录制";
            State("ready");
            if (!Smoke) await WaitForStart();
            for (int second = Smoke ? 0 : 15; second > 0; second--)
            {
                _caption.Text = $"Saber 演出展示将在 {second} 秒后开始";
                State("countdown_" + second);
                await Fx.Delay(1);
            }
            _caption.Hide();
            await Hold(3);
            var character = ModelDb.Character<Ironclad>();
            var player = Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
            _player = player;
            var acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
            acts[0] = ModelDb.Act<Overgrowth>().ToMutable();
            var run = RunState.CreateForNewRun([player], acts, [], GameMode.Standard, 0, "SABERRECORDING");
            RunManager.Instance.SetUpNewSingleplayer(run, false);
            await PreloadManager.LoadRunAssets([character]);
            RunManager.Instance.Launch();
            game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
            await RunManager.Instance.SetActInternal(0);
            RunManager.Instance.RunLocationTargetedBuffer.OnLocationChanged(run.RunLocation);
            RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(run.MapLocation);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, ModelDb.Encounter<TwoTailedRatsNormal>().ToMutable());
            await Fx.Delay(4);
            // Run creation reapplies saved display settings. Force fullscreen
            // after it finishes, so the recording stays at the intended size.
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            DisplayServer.WindowMoveToForeground();
            await Fx.Delay(0.35);
            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            while (GodotObject.IsInstanceValid(game) && game.IsInsideTree())
            {
                _take++;
                _finisherVerified = false;
                _finisherCountBefore = Runtime.KillingBlowCount;
                _caption.Hide();
                await Perform(combat, player);
                State("complete");
                if (Smoke)
                {
                    if (_take < 2) continue;
                    Log.Info("[SaberDemo] TWO-TAKE SMOKE CHECKS PASSED");
                    game.GetTree().Quit();
                    return;
                }
                await Fx.Delay(5);
                _caption.Text = "演示完成  ·  F8 重播  ·  F9 隐藏提示";
                _caption.Show();
                await WaitForStart();
                for (int second = 5; second > 0; second--)
                {
                    _caption.Text = $"{second} 秒后重播";
                    await Fx.Delay(1);
                }
            }
        }
        catch (Exception ex)
        {
            State("error: " + ex.Message);
            Log.Error("[SaberDemo] " + ex);
            if (Smoke) game.GetTree().Quit(1);
        }
    }
    private static async Task WaitForStart()
    {
        while (Input.IsPhysicalKeyPressed(Key.F8)) await Fx.Delay(0.05);
        while (!Input.IsPhysicalKeyPressed(Key.F8))
        {
            if (Input.IsPhysicalKeyPressed(Key.F9)) _caption.Hide();
            await Fx.Delay(0.05);
        }
    }
    private static async Task Perform(CombatState state, Player player)
    {
        ReadyPresentation.Cancel(null);
        // The recording profile reuses one battle across takes. Clear the native
        // combat piles with their visual transitions, so discarded cards and
        // hand holders cannot accumulate on replay. The live mod never does this.
        await CardPileCmd.RemoveFromCombat(player.PlayerCombatState!.AllCards.ToArray());
        await Fx.Delay(0.4);
        if (NPlayerHand.Instance!.ActiveHolders.Count != 0)
            throw new InvalidOperationException("Recording reset left stale hand holders.");
        foreach (var creature in state.HittableEnemies.Append(player.Creature))
        {
            foreach (var power in creature.Powers.ToArray()) await PowerCmd.Remove(power);
            await CreatureCmd.LoseBlock(new BlockingPlayerChoiceContext(), creature, creature.Block, null);
        }
        foreach (var enemy in state.HittableEnemies) { enemy.SetMaxHpInternal(200); enemy.SetCurrentHpInternal(200); }
        while (state.HittableEnemies.Count < 3)
        {
            string slot = new[] { "third", "fourth", "fifth" }
                .First(name => !state.HittableEnemies.Any(enemy => enemy.SlotName == name));
            var extra = await CreatureCmd.Add<TwoTailedRat>(state, slot);
            extra.SetMaxHpInternal(200); extra.SetCurrentHpInternal(200);
        }
        player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
        await CreatureCmd.LoseBlock(new BlockingPlayerChoiceContext(), player.Creature, player.Creature.Block, null);
        var templates = new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(),
            ModelDb.Card<PommelStrike>(), ModelDb.Card<Uppercut>(), ModelDb.Card<Hemokinesis>(),
            ModelDb.Card<TwinStrike>(), ModelDb.Card<Bash>(), ModelDb.Card<Whirlwind>(), ModelDb.Card<Bludgeon>() };
        var cards = templates.Select(t => state.CreateCard(t, player)).ToArray();
        foreach (var card in cards) await CardPileCmd.Add(card, PileType.Hand);
        var target = state.HittableEnemies.First();
        State("take_reset");
        await Hold(3);
        State("hover_attack");
        await Focus(cards[0], 4);
        State("hover_skill");
        await Focus(cards[1], 4);
        State("target_and_cancel");
        var holder = Holder(cards[0]);
        if (holder != null)
        {
            holder.EmitSignal(Control.SignalName.FocusEntered);
            var play = NMouseCardPlay.Create(holder, "saber_demo", false);
            NCombatRoom.Instance!.AddChild(play); play.Start();
            await Hold(2.5); play.CancelPlayCard(); holder.CancelDrag();
            holder.EmitSignal(Control.SignalName.FocusExited);
        }
        await Hold(2);
        foreach (int index in new[] { 0, 2, 3, 4, 5, 6, 7 })
        {
            State("attack_" + cards[index].Id.Entry);
            await Play(cards[index], target, index == 7 ? 3 : 1);
            await Hold(2.8);
        }
        State("shield");
        await Play(cards[1], null);
        await Hold(2);
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 2, ValueProp.Move, target);
        await Hold(2.5);
        State("hurt");
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 8, ValueProp.Move, target);
        await Hold(3);
        State("skill_aura");
        player.Creature.GetCreatureNode()!.SetAnimationTrigger("Cast");
        await Hold(3);
        State("killing_blow");
        foreach (var power in target.Powers.ToArray()) await PowerCmd.Remove(power);
        await CreatureCmd.LoseBlock(new BlockingPlayerChoiceContext(), target, target.Block, player.Creature);
        target.SetCurrentHpInternal(1);
        await Focus(cards[8], 3);
        await Play(cards[8], target);
        _finisherVerified = target.IsDead && Runtime.KillingBlowCount == _finisherCountBefore + 1
            && Fx.LastCutInIntegrated && FinisherStage.LastStageStarted && FinisherStage.LastTargetHit
            && FinisherStage.LastVideoSeconds > 7.45 && FinisherStage.LastReleaseCount == 1 && !FinisherStage.Active;
        State("finisher_verified");
        if (!_finisherVerified)
            throw new InvalidOperationException("Recording did not complete one integrated Excalibur finisher; inspect demo-state.json.");
        await Hold(5);
        ReadyPresentation.Cancel(null);
        await Hold(2);
        Log.Info("[SaberDemo] TAKE " + _take + " COMPLETE");
    }
    private static NHandCardHolder? Holder(CardModel card) => Runtime.Descendants(NCombatRoom.Instance!)
        .OfType<NHandCardHolder>().FirstOrDefault(h => h.CardModel == card);
    private static async Task Focus(CardModel card, double duration)
    {
        var holder = Holder(card);
        holder?.EmitSignal(Control.SignalName.FocusEntered);
        await Hold(duration);
        holder?.EmitSignal(Control.SignalName.FocusExited);
        await Fx.Delay(0.3);
    }
    private static async Task Play(CardModel card, Creature? target, int energy = 1)
    {
        var holder = Holder(card);
        holder?.EmitSignal(Control.SignalName.FocusEntered);
        await Hold(1.6);
        await card.OnPlayWrapper(new BlockingPlayerChoiceContext(), target, true,
            new ResourceInfo { EnergySpent = energy, EnergyValue = energy, StarsSpent = 0, StarValue = 0 });
        if (holder != null && GodotObject.IsInstanceValid(holder)) holder.EmitSignal(Control.SignalName.FocusExited);
        await Fx.Delay(0.8);
        State("played_" + card.Id.Entry);
        if (_maxHandCards > 10 || _maxHandHolders > 10)
            throw new InvalidOperationException("Recording exceeded the native hand limit.");
    }
}
#endif
