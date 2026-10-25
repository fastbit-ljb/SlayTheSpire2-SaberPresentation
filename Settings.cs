using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace SaberPresentation;

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool Dash { get; set; } = true;
    public bool RichMotion { get; set; } = true;
    public bool UseCznEffects { get; set; } = true;
    public bool CardHoverPresentation { get; set; } = true;
    public bool ReadyBodyMotion { get; set; } = true;
    public bool SaberCardArt { get; set; } = true;
    public bool HoverVoice { get; set; } = true;
    public bool PreferSaber { get; set; } = true;
    public float PortraitScale { get; set; } = 1f;
    public float PortraitOpacity { get; set; } = 0.97f;
    public float PortraitOffsetX { get; set; }
    public float PortraitOffsetY { get; set; }
    public bool Cinematics { get; set; } = true;
    public bool KillingBlowExcalibur { get; set; } = true;
    public bool StandaloneDefaultsApplied { get; set; }
    public bool ScreenShake { get; set; } = true;
    public bool ScreenFlash { get; set; } = true;
    public bool JapaneseVoice { get; set; } = true;
    public float VoiceVolume { get; set; } = 0.9f;
    public float EffectsVolume { get; set; } = 0.6f;
    public string[] NoblePhantasmCards { get; set; } = ["BLUDGEON", "FIEND_FIRE", "PACTS_END"];
    public static Settings Current { get; private set; } = new();
    public const string Path = "user://mod_configs/SaberPresentation.json";
    internal static void Save()
    {
        try { System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(Path), JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Settings save: " + ex.Message); }
    }

    public static void Load()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(Path);
            if (System.IO.File.Exists(path))
                Current = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(path)) ?? new();
            Current.VoiceVolume = Math.Clamp(Current.VoiceVolume, 0, 1);
            Current.EffectsVolume = Math.Clamp(Current.EffectsVolume, 0, 1);
            Current.PortraitScale = Math.Clamp(Current.PortraitScale, 0.5f, 1.5f);
            Current.PortraitOpacity = Math.Clamp(Current.PortraitOpacity, 0.1f, 1);
            Current.NoblePhantasmCards ??= [];
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Configuration: " + ex.Message); }
    }
}
