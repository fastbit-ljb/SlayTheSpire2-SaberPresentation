namespace SaberPresentation;

// Only our character presentation assembly runs. Reused Spine/audio resources
// are in our own pack and use the game's native loaders.
internal static class Standalone
{
    internal static readonly string Root = System.IO.Path.GetDirectoryName(typeof(Entry).Assembly.Location)!;
    internal static void Initialize()
    {
        SaberSkin.PreserveInstalledSkin();
    }
}
