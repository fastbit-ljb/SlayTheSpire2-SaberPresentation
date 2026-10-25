using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace SaberPresentation;

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconTexture), MethodType.Getter)]
internal static class SaberIconTexturePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CharacterModel __instance, ref Texture2D __result)
    {
        if (SaberSkin.Active && __instance is Ironclad) __result = Art.Icon;
    }
}
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.Icon), MethodType.Getter)]
internal static class SaberIconScenePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CharacterModel __instance, Control __result)
    {
        if (!SaberSkin.Active || __instance is not Ironclad) return;
        foreach (var node in Runtime.Descendants(__result).Prepend(__result))
        {
            if (node is TextureRect texture && texture.Texture?.ResourcePath.Contains("character_icon_ironclad", StringComparison.OrdinalIgnoreCase) == true)
                texture.Texture = Art.Icon;
            if (node is Sprite2D sprite && sprite.Texture?.ResourcePath.Contains("character_icon_ironclad", StringComparison.OrdinalIgnoreCase) == true)
            {
                sprite.Scale *= sprite.Texture.GetSize() / Art.Icon.GetSize();
                sprite.Texture = Art.Icon;
            }
        }
    }
}
