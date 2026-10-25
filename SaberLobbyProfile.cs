using System.Reflection;

[assembly: AssemblyMetadata("CznMainMenu.ProfileProvider", "SaberPresentation.SaberLobbyProfile")]

namespace SaberPresentation;

// CznMainMenu discovers the same metadata contract used by installed CZN skins.
internal static class SaberLobbyProfile
{
    internal sealed record NameText(string Current);
    internal sealed record SettingsText(NameText SettingsHeader);
    internal sealed record Presentation(string LiveLobbyScenePath, string MainMenuThumbnailPath, string LobbyMusicEventPath);
    internal sealed record Profile(string ProfileId, Presentation CharacterSelectPresentation, SettingsText SettingsText);
    internal static Profile RegisteredProfile { get; } = new("saber",
        new("res://SaberPresentation/scenes/saber_lobby.tscn", "res://SaberPresentation/generated/camelot_stage.png", "bgm/story_memorial"),
        new(new("Saber · 阿尔托莉雅")));
}
