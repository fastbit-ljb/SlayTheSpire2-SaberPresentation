using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace SaberPresentation;

internal static class SaberSkin
{
    private static Resource? _body;
    internal static bool Active => Settings.Current.Enabled && Settings.Current.PreferSaber && _body != null;
    internal static void PreserveInstalledSkin()
    {
        if (!Settings.Current.PreferSaber) return;
        try
        {
            var mod = ModManager.GetLoadedMods().FirstOrDefault(m => m.manifest?.id == "saber");
            if (mod == null) return;
            string pack = System.IO.Path.Combine(mod.path, "saber.pck");
            // Other installed Ironclad skins export the same native paths. Mount
            // the user's original Saber pack last and load its own exported body
            // explicitly, without copying or modifying the Workshop source.
            if (!ProjectSettings.LoadResourcePack(pack, replaceFiles: true)) return;
            using var file = System.IO.File.OpenRead(pack);
            using var reader = new System.IO.BinaryReader(file);
            if (reader.ReadUInt32() != 0x43504447 || reader.ReadUInt32() != 3) return;
            file.Position = 32;
            ulong directory = reader.ReadUInt64();
            file.Position = (long)directory;
            uint count = reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                string path = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length)).TrimEnd('\0');
                file.Position += (4 - length % 4) % 4 + 36;
                if (!path.EndsWith("-ironclad_skel_data.res", StringComparison.Ordinal)) continue;
                _body = ResourceLoader.Load("res://" + path.Replace("res://", ""), cacheMode: ResourceLoader.CacheMode.ReplaceDeep);
                Log.Info("[SaberPresentation] Original Saber body retained alongside other Ironclad skins.");
                return;
            }
        }
        catch (Exception ex) { Log.Warn("[SaberPresentation] Original skin compatibility: " + ex.Message); }
    }
    internal static void Apply(MegaSprite sprite)
    {
        if (!Settings.Current.PreferSaber || _body == null || sprite.HasAnimation("attackvfff")) return;
        sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(_body));
    }
}
