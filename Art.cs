using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SaberPresentation;

internal static class Art
{
    private static readonly Dictionary<string, Texture2D> Textures = [];
    private static readonly Dictionary<int, AtlasTexture> Cards = [];
    private static AtlasTexture? _icon;
    internal static Texture2D Icon => _icon ??= new AtlasTexture {
        Atlas = Texture("saber_ready_cast"), Region = new Rect2(310, 25, 400, 400), FilterClip = true
    };
    internal static Texture2D Texture(string name)
    {
        if (Textures.TryGetValue(name, out var texture)) return texture;
        return Textures[name] = ResourceLoader.Load<Texture2D>($"res://SaberPresentation/generated/{name}.png");
    }
    internal static bool IsNoblePhantasm(CardModel card) =>
        Settings.Current.NoblePhantasmCards.Contains(card.Id.Entry, StringComparer.OrdinalIgnoreCase);
    internal static int CardIndex(CardModel card) => IsNoblePhantasm(card) ? 4 :
        card.Id.Entry is "HEMOKINESIS" or "UPPERCUT" or "BREAKTHROUGH" or "STOMP" ? 3 :
        card.Type == CardType.Power ? 2 : card.Type == CardType.Skill ? 1 : 0;
    internal static Texture2D Card(CardModel card)
    {
        int index = CardIndex(card);
        if (Cards.TryGetValue(index, out var texture)) return texture;
        var atlas = Texture("saber_card_atlas");
        var cell = atlas.GetSize() / new Vector2(3, 2);
        return Cards[index] = new AtlasTexture { Atlas = atlas,
            Region = new Rect2(new Vector2(index % 3, index / 3) * cell, cell), FilterClip = true };
    }
    internal static Texture2D Frame(int row, int frame)
    {
        var texture = Texture("saber_vfx_atlas");
        var cell = texture.GetSize() / 4;
        return new AtlasTexture { Atlas = texture,
            Region = new Rect2(new Vector2(frame, row) * cell, cell), FilterClip = true };
    }
}
