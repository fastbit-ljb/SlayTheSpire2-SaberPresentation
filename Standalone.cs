using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace SaberPresentation;

// One native mod entry. The unmodified presentation libraries and their packs
// are private payloads, loaded in dependency order, never separate mod installs.
internal static class Standalone
{
    internal static readonly string Root = System.IO.Path.GetDirectoryName(typeof(Entry).Assembly.Location)!;
    private static readonly HashSet<MethodBase> Initialized = [];
    private static readonly HashSet<ulong> DeferredAudioOwners = [];
    internal static int LibrariesLoaded { get; private set; }
    internal static void Initialize()
    {
        ApplyLobbyDefault();
        var context = AssemblyLoadContext.GetLoadContext(typeof(Entry).Assembly)!;
        var harmony = new Harmony("local.saber.presentation.bundled-runtime");
        foreach (var (name, type) in new[] {
            ("CznCore", "CznCore.Standalone.StandaloneEntry"), ("CznStyleUI", "CznStyleUI.Entry"),
            ("CznMainMenu", "CznMainMenu.Entry") })
        {
            string folder = System.IO.Path.Combine(Root, "bundled");
            bool alreadyLoaded = ModManager.GetLoadedMods().Any(m => m.manifest?.id == name);
            if (alreadyLoaded) { Log.Info("[SaberPresentation] Sharing active presentation library " + name); continue; }
            string pack = System.IO.Path.Combine(folder, name + ".pck");
            if (!ProjectSettings.LoadResourcePack(pack, replaceFiles: true))
                throw new InvalidOperationException("Cannot mount bundled presentation pack: " + pack);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
                ?? context.LoadFromAssemblyPath(System.IO.Path.Combine(folder, name + ".dll"));
            ModManager.AssociateAssemblyWithMod("SaberPresentation", assembly);
            if (name == "CznStyleUI")
                harmony.Patch(AccessTools.Method(assembly.GetType("CznStyleUI.GlobalUiAudio.GlobalUiAudioRuntime"), "Attach"),
                    prefix: new HarmonyMethod(typeof(Standalone), nameof(AudioOwnerReady)));
            var init = assembly.GetType(type, throwOnError: true)!.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!;
            init.Invoke(null, null);
            Initialized.Add(init);
            // If a separately installed copy is encountered later in native mod
            // ordering, its identical assembly must not install every patch twice.
            harmony.Patch(init, prefix: new HarmonyMethod(typeof(Standalone), nameof(ShouldInitialize)));
            LibrariesLoaded++;
            Log.Info("[SaberPresentation] Bundled presentation library initialized: " + name);
        }
        SaberSkin.PreserveInstalledSkin();
        EnergyPresentation.Install();
    }
    private static bool ShouldInitialize(MethodBase __originalMethod) => !Initialized.Contains(__originalMethod);
    private static bool AudioOwnerReady(Node owner, MethodBase __originalMethod)
    {
        if (!GodotObject.IsInstanceValid(owner)) return false;
        if (owner.IsInsideTree()) return true;
        // The bundled UI library warms audio during mod loading, before the
        // native audio manager has entered the tree. Attach once it is ready.
        if (DeferredAudioOwners.Add(owner.GetInstanceId()))
            owner.Connect(Node.SignalName.TreeEntered, Callable.From(() =>
                Callable.From(() => {
                    if (GodotObject.IsInstanceValid(owner) && owner.IsInsideTree()) __originalMethod.Invoke(null, [owner]);
                }).CallDeferred()), (uint)GodotObject.ConnectFlags.OneShot);
        return false;
    }

    private static void ApplyLobbyDefault()
    {
        if (Settings.Current.StandaloneDefaultsApplied) return;
        string path = ProjectSettings.GlobalizePath("user://CznModConfig/CznMainMenu/settings.cfg");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "[main_menu]\n";
        if (System.IO.File.Exists(path) && !System.IO.File.Exists(path + ".before-saber-standalone"))
            System.IO.File.Copy(path, path + ".before-saber-standalone");
        foreach (var (key, value) in new[] { ("background_mode", "fixed"), ("background_id", "skin_lobby_saber"),
            ("music_mode", "follow_background"), ("music_id", "story_memorial_precious_moments") })
        {
            string pattern = "(?m)^" + key + "=.*$";
            text = System.Text.RegularExpressions.Regex.IsMatch(text, pattern)
                ? System.Text.RegularExpressions.Regex.Replace(text, pattern, key + "=" + value)
                : text.TrimEnd() + "\n" + key + "=" + value + "\n";
        }
        System.IO.File.WriteAllText(path, text);
        Settings.Current.StandaloneDefaultsApplied = true;
        Settings.Save();
    }
}
