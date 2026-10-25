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
    private static Label _caption = null!;
    private static string Marker => System.IO.Path.Combine(Standalone.Root, "demo-state.json");
    private static void State(string phase) => System.IO.File.WriteAllText(Marker,
        System.Text.Json.JsonSerializer.Serialize(new { take = _take, phase, fullscreen = DisplayServer.WindowGetMode().ToString() }));
    internal static async Task Run(NGame game)
    {
        try
        {
            if (!OS.GetUserDataDir().EndsWith("SlayTheSpire2-SaberPresentation-Demo", StringComparison.Ordinal))
                throw new InvalidOperationException("Recording mode requires its isolated demo profile.");
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
            await WaitForStart();
            for (int second = 15; second > 0; second--)
            {
                _caption.Text = $"Saber 演出展示将在 {second} 秒后开始";
                State("countdown_" + second);
                await Fx.Delay(1);
            }
            _caption.Hide();
            await Fx.Delay(3);
            var character = ModelDb.Character<Ironclad>();
            var player = Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
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
            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            while (GodotObject.IsInstanceValid(game) && game.IsInsideTree())
            {
                _take++;
                _caption.Hide();
                await Perform(combat, player);
                State("complete");
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
        catch (Exception ex) { State("error: " + ex.Message); Log.Error("[SaberDemo] " + ex); }
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
        foreach (var enemy in state.HittableEnemies) { enemy.SetMaxHpInternal(200); enemy.SetCurrentHpInternal(200); }
        if (state.HittableEnemies.Count < 3)
        {
            var extra = await CreatureCmd.Add<TwoTailedRat>(state, "third");
            extra.SetMaxHpInternal(200); extra.SetCurrentHpInternal(200);
        }
        player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
        await CreatureCmd.LoseBlock(new BlockingPlayerChoiceContext(), player.Creature, player.Creature.Block, null);
        await CardPileCmd.RemoveFromCombat(PileType.Hand.GetPile(player).Cards.ToArray(), true);
        var templates = new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(),
            ModelDb.Card<PommelStrike>(), ModelDb.Card<Uppercut>(), ModelDb.Card<Hemokinesis>(),
            ModelDb.Card<TwinStrike>(), ModelDb.Card<Bash>(), ModelDb.Card<Whirlwind>(), ModelDb.Card<Bludgeon>() };
        var cards = templates.Select(t => state.CreateCard(t, player)).ToArray();
        foreach (var card in cards) await CardPileCmd.Add(card, PileType.Hand);
        var target = state.HittableEnemies.First();
        await Fx.Delay(3);
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
            await Fx.Delay(2.5); play.CancelPlayCard(); holder.CancelDrag();
            holder.EmitSignal(Control.SignalName.FocusExited);
        }
        await Fx.Delay(2);
        foreach (int index in new[] { 0, 2, 3, 4, 5, 6, 7 })
        {
            State("attack_" + cards[index].Id.Entry);
            await Play(cards[index], target, index == 7 ? 3 : 1);
            await Fx.Delay(2.8);
        }
        State("shield");
        await Play(cards[1], null);
        await Fx.Delay(2);
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 2, ValueProp.Move, target);
        await Fx.Delay(2.5);
        State("hurt");
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), player.Creature, 8, ValueProp.Move, target);
        await Fx.Delay(3);
        State("skill_aura");
        player.Creature.GetCreatureNode()!.SetAnimationTrigger("Cast");
        await Fx.Delay(3);
        State("killing_blow");
        target.SetCurrentHpInternal(16);
        await Focus(cards[8], 3);
        await Play(cards[8], target);
        await Fx.Delay(5);
        ReadyPresentation.Cancel(null);
        await Fx.Delay(2);
        Log.Info("[SaberDemo] TAKE " + _take + " COMPLETE");
    }
    private static NHandCardHolder? Holder(CardModel card) => Runtime.Descendants(NCombatRoom.Instance!)
        .OfType<NHandCardHolder>().FirstOrDefault(h => h.CardModel == card);
    private static async Task Focus(CardModel card, double duration)
    {
        var holder = Holder(card);
        holder?.EmitSignal(Control.SignalName.FocusEntered);
        await Fx.Delay(duration);
        holder?.EmitSignal(Control.SignalName.FocusExited);
        await Fx.Delay(0.3);
    }
    private static async Task Play(CardModel card, Creature? target, int energy = 1)
    {
        var holder = Holder(card);
        holder?.EmitSignal(Control.SignalName.FocusEntered);
        await Fx.Delay(1.6);
        await card.OnPlayWrapper(new BlockingPlayerChoiceContext(), target, true,
            new ResourceInfo { EnergySpent = energy, EnergyValue = energy, StarsSpent = 0, StarValue = 0 }, true);
        if (holder != null && GodotObject.IsInstanceValid(holder)) holder.EmitSignal(Control.SignalName.FocusExited);
        await Fx.Delay(0.8);
    }
}
#endif
