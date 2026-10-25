using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SaberPresentation;

// One player belongs to the run, so room transitions do not restart the song.
// Native ambience, SFX, menu music and other characters retain their own audio.
internal static class AnimeMusic
{
    private const string Path = "res://SaberPresentation/music/emiya_ubw.mp3";
    private static AudioStreamMP3? _clip;
    private static AudioStreamPlayer? _player;
    private static NRunMusicController? _owner;
    private static SceneTree? _tree;
    private static ulong _voiceUntil;
    private static int _cinematics;
    private static bool _missingLogged;
    internal static bool Playing => Valid(_player) && _player!.Playing;
    internal static double Length => _clip?.GetLength() ?? 0;
    internal static double Position => Valid(_player) ? _player!.GetPlaybackPosition() : 0;
    internal static float Gain => Valid(_player) ? _player!.VolumeLinear : 0;
    internal static bool PausedForCg => Valid(_player) && _player!.StreamPaused;
    internal static bool Owns(NRunMusicController owner) => _owner == owner && Valid(_player);
    private static bool Valid(Node? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();

    internal static bool TryPlay(NRunMusicController owner, IRunState state)
    {
        if (!Settings.Current.AnimeBackgroundMusic || !SaberSkin.Active
            || !state.Players.Any(p => p.Character is Ironclad && LocalContext.IsMe(p)))
        {
            Stop(owner);
            return false;
        }
        if (Owns(owner)) return true;
        if (!Godot.FileAccess.FileExists(Path))
        {
            if (!_missingLogged) Log.Warn("[SaberPresentation] Anime BGM unavailable; retaining native music.");
            _missingLogged = true;
            return false;
        }
        Clear();
        _clip ??= new AudioStreamMP3 { Data = Godot.FileAccess.GetFileAsBytes(Path), Loop = true };
        _owner = owner;
        owner.GetNode<Node>("Proxy").Call("stop_music");
        _player = new AudioStreamPlayer { Name = "SaberAnimeMusic", Stream = _clip, Bus = "Master", VolumeLinear = 0 };
        owner.AddChild(_player);
        _tree = owner.GetTree();
        _tree.ProcessFrame += Tick;
        owner.TreeExiting += Clear;
        _player.Play();
        Log.Info("[SaberPresentation] EMIYA_UBW instrumental started; native BGM replaced for this Saber run.");
        return true;
    }

    private static void Tick()
    {
        if (!Valid(_player)) return;
        var settings = SaveManager.Instance.SettingsSave;
        float gameGain = MathF.Pow(settings.VolumeMaster * settings.VolumeBgm, 2);
        float duck = Time.GetTicksMsec() < _voiceUntil ? 0.22f : 1f;
        float target = gameGain * Settings.Current.MusicVolume * duck;
        if (!Settings.Current.Enabled || !Settings.Current.AnimeBackgroundMusic) target = 0;
        float dt = (float)_player!.GetProcessDeltaTime();
        _player.VolumeLinear = Mathf.Lerp(_player.VolumeLinear, target, 1 - MathF.Exp(-dt * (target < _player.VolumeLinear ? 24 : 4)));
        _player.StreamPaused = _cinematics > 0;
    }

    internal static void DuckVoice(double seconds) =>
        _voiceUntil = Math.Max(_voiceUntil, Time.GetTicksMsec() + (ulong)(Math.Max(0, seconds) * 1000));

    internal static IDisposable PauseForCinematic()
    {
        _cinematics++;
        if (Valid(_player)) _player!.StreamPaused = true;
        return new CinematicLease();
    }
    private sealed class CinematicLease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cinematics = Math.Max(0, _cinematics - 1);
            if (Valid(_player)) _player!.StreamPaused = _cinematics > 0;
        }
    }
    internal static void Stop(NRunMusicController owner)
    {
        if (_owner == owner) Clear();
    }
    private static void Clear()
    {
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= Tick;
        if (_owner != null && GodotObject.IsInstanceValid(_owner)) _owner.TreeExiting -= Clear;
        if (_player != null && GodotObject.IsInstanceValid(_player)) { _player.Stop(); _player.QueueFree(); }
        _player = null;
        _owner = null;
        _tree = null;
        _voiceUntil = 0;
        // Active cinematic leases may outlive the run during a death transition.
        // Their Dispose calls still balance this counter after scene teardown.
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic))]
internal static class AnimeRunMusicPatch
{
    private static bool Prefix(NRunMusicController __instance, IRunState ____runState)
    {
        try
        {
            if (!AnimeMusic.TryPlay(__instance, ____runState)) return true;
            __instance.UpdateAmbience();
            return false;
        }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Anime music fallback: " + ex.Message); return true; }
    }
}
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
internal static class AnimeCustomMusicPatch
{
    private static bool Prefix(NRunMusicController __instance) => !AnimeMusic.Owns(__instance);
}
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
internal static class AnimeRestoreCustomMusicPatch
{
    private static bool Prefix(NRunMusicController __instance) => !AnimeMusic.Owns(__instance);
}
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateCustomTrack))]
internal static class AnimeCustomTrackPatch
{
    private static bool Prefix(NRunMusicController __instance) => !AnimeMusic.Owns(__instance);
}
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
internal static class AnimeStopMusicPatch
{
    private static void Postfix(NRunMusicController __instance) => AnimeMusic.Stop(__instance);
}
