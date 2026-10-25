using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace SaberPresentation;

internal static class Voice
{
    private sealed class Channel
    {
        public required AudioStreamPlayer Player;
        public ulong ProtectedUntil;
        public int Generation;
    }
    private static readonly Dictionary<string, AudioStreamMP3> Clips = [];
    private static ConditionalWeakTable<Node, Channel> _channels = new();
    private static readonly HashSet<string> Missing = [];
    private static readonly Dictionary<string, int> Variants = [];
    private static AudioStreamWav? _sword, _heavy, _beam;

    private static Channel Get(Node host)
    {
        return _channels.GetValue(host, node => {
            var player = new AudioStreamPlayer { Name = "SaberJapaneseVoice", Bus = "Master" };
            node.AddChild(player);
            return new Channel { Player = player };
        });
    }
    private static Node Host(Node owner) => (Node?)MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance ?? owner;
    private static float GameVolume => SaveManager.Instance.SettingsSave.VolumeMaster * SaveManager.Instance.SettingsSave.VolumeSfx;
    private static AudioStreamMP3? Clip(string name)
    {
        if (Clips.TryGetValue(name, out var clip)) return clip;
        string path = $"res://SaberPresentation/audio/{name}.mp3";
        if (!Godot.FileAccess.FileExists(path))
        {
            if (Missing.Add(name)) Log.Warn("[SaberPresentation] Missing JP voice " + path);
            return null;
        }
        clip = new AudioStreamMP3 { Data = Godot.FileAccess.GetFileAsBytes(path) };
        Clips.Add(name, clip);
        return clip;
    }

    public static void Play(Node owner, string name, bool priority = false)
    {
        if (!Settings.Current.JapaneseVoice || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()) return;
        var clip = Clip(name);
        if (clip == null) return;
        var channel = Get(Host(owner));
        if (!priority && Time.GetTicksMsec() < channel.ProtectedUntil) return;
        if (!priority && channel.Player.Playing) return;
        channel.Generation++;
        channel.Player.Stop();
        channel.Player.Stream = clip;
        channel.Player.VolumeLinear = GameVolume * Settings.Current.VoiceVolume * 1.6f;
        channel.ProtectedUntil = priority ? Time.GetTicksMsec() + (ulong)(clip.GetLength() * 1000) : 0;
        channel.Player.Play();
    }

    public static void PlaySequence(Node owner, string[] names, bool priority = false) =>
        TaskHelper.RunSafely(Sequence(owner, names, priority));

    public static void PlayNext(Node owner, string category, int count)
    {
        Variants.TryGetValue(category, out int index);
        Variants[category] = index + 1;
        Play(owner, category == "heavy" && index % count == 0 ? "heavy" : $"{category}_{index % count + 1}");
    }

    private static async Task Sequence(Node owner, string[] names, bool priority)
    {
        var host = Host(owner);
        if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree()) return;
        var channel = Get(host);
        foreach (string name in names)
        {
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()) return;
            Play(owner, name, priority);
            int generation = channel.Generation;
            await Fx.Delay((Clip(name)?.GetLength() ?? 0.1) + 0.025);
            if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree() || channel.Generation != generation) return;
        }
    }

    public static async Task VictoryAfterCurrent(Node owner)
    {
        var host = Host(owner);
        if (!GodotObject.IsInstanceValid(host)) return;
        var channel = Get(host);
        int generation = channel.Generation;
        double wait = Math.Max(0, ((double)channel.ProtectedUntil - Time.GetTicksMsec()) / 1000) + 0.12;
        await Fx.Delay(Math.Min(wait, 4));
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() || generation != channel.Generation) return;
        PlayNext(owner, "victory", 4);
    }

    public static void Sword(Node host, bool heavy)
    {
        string name = heavy ? "se_30047_u1_attack_play" : "se_30047_attack_play1";
        if (!CznAssets.Sound(host, $"res://CznNineCharacter/audio/sfx/{name}.ogg"))
            PlayEffect(host, heavy ? _heavy ??= MakeSound(0.4, 1) : _sword ??= MakeSound(0.24, 0));
    }
    public static void Beam(Node host)
    {
        if (!CznAssets.Sound(host, "res://CznNineCharacter/audio/sfx/se_30047_ug_attack.ogg"))
            PlayEffect(host, _beam ??= MakeSound(0.95, 2));
    }

    internal static void PlayEffect(Node host, AudioStream stream)
    {
        if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree() || Settings.Current.EffectsVolume <= 0) return;
        var player = new AudioStreamPlayer { Stream = stream, Bus = "Master",
            VolumeLinear = GameVolume * Settings.Current.EffectsVolume };
        host.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    // Short generated sword/wind/beam layers. Own deterministic noise generator;
    // never consumes the game's seeded combat RNG. All speech is authentic JP.
    private static AudioStreamWav MakeSound(double duration, int kind)
    {
        const int rate = 22050;
        var bytes = new byte[(int)(rate * duration) * 2];
        var random = new Random(1001 + kind);
        double last = 0;
        for (int i = 0; i < bytes.Length / 2; i++)
        {
            double t = (double)i / rate, p = t / duration;
            double noise = random.NextDouble() * 2 - 1;
            last = last * 0.65 + noise * 0.35;
            double envelope = Math.Pow(1 - p, kind == 2 ? 1 : 2) * Math.Min(1, t * 140);
            double tone = Math.Sin(t * Math.Tau * (kind == 2 ? 85 : 180) * (1 - p * 0.5));
            short sample = (short)(Math.Clamp((last * 0.7 + tone * 0.3) * envelope, -1, 1) * 22000);
            bytes[i * 2] = (byte)(sample & 255); bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Data = bytes, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false };
    }

    public static void Clear() => _channels = new();
    internal static double Length(string name) => Clip(name)?.GetLength() ?? 0;
}
