using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SaberPresentation;

internal static class Art
{
    private static readonly Dictionary<string, Texture2D> Textures = [];
    private static AtlasTexture? _icon;
    private static VideoStream? _excaliburVideo;
    private static VideoStream? _defeatVideo;
    internal static Texture2D Icon => _icon ??= new AtlasTexture {
        Atlas = Texture("saber_ready_cast"), Region = new Rect2(310, 25, 400, 400), FilterClip = true
    };
    internal static Texture2D Texture(string name)
    {
        if (Textures.TryGetValue(name, out var texture)) return texture;
        return Textures[name] = ResourceLoader.Load<Texture2D>($"res://SaberPresentation/generated/{name}.png");
    }
    internal static VideoStream? ExcaliburVideo => _excaliburVideo ??=
        ResourceLoader.Load<VideoStream>("res://SaberPresentation/video/excalibur_np.ogv");
    internal static VideoStream? DefeatVideo => _defeatVideo ??=
        ResourceLoader.Load<VideoStream>("res://SaberPresentation/video/saber_defeat.ogv");
    internal static bool IsNoblePhantasm(CardModel card) =>
        Settings.Current.NoblePhantasmCards.Contains(card.Id.Entry, StringComparer.OrdinalIgnoreCase);
    internal static Texture2D Frame(int row, int frame)
    {
        var texture = Texture("saber_vfx_atlas");
        var cell = texture.GetSize() / 4;
        return new AtlasTexture { Atlas = texture,
            Region = new Rect2(new Vector2(frame, row) * cell, cell), FilterClip = true };
    }
}
