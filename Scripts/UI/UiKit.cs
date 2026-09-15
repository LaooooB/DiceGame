using Godot;
using DiceGame.Presentation;

namespace DiceGame.UI;

/// <summary>
/// Non-authoring UI helpers only. All controls, layout, styles and interaction surfaces live in .tscn scenes.
/// This class only binds dice textures and applies the optional typography scale to already-authored nodes.
/// </summary>
public static class UiKit
{
    public const string Ink = "#E5F1F5", Muted = "#9CAFBE", Mint = "#72EAC8", Gold = "#F8DE87", Danger = "#FF8F86";

    public static void SetDice(TextureRect rect, NativeArt art, string id, int pips)
    {
        int row = Array.IndexOf(NativeArt.TypeRows, id);
        if (row < 0) { rect.Texture = art.ContentDie(id, pips); return; }
        const int face = 64;
        const int cell = (face + 16) * 3;
        rect.Texture = new AtlasTexture
        {
            Atlas = art.Dice(face),
            Region = new Rect2((Math.Clamp(pips, 1, 6) - 1) * cell, row * cell, cell, cell),
            FilterClip = true
        };
    }

    public static void ScaleTypography(Node root, double scale)
    {
        if (root is Control c && c.HasThemeFontSizeOverride("font_size"))
        {
            if (!c.HasMeta("unscaled_font_size")) c.SetMeta("unscaled_font_size", c.GetThemeFontSize("font_size"));
            c.AddThemeFontSizeOverride("font_size", (int)Math.Round(c.GetMeta("unscaled_font_size").AsInt32() * scale));
        }
        foreach (var child in root.GetChildren()) ScaleTypography(child, scale);
    }
}
