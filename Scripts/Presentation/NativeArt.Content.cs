using System.Security;
using System.Text;
using System.Text.Json;
using DiceGame.Core;
using Godot;

namespace DiceGame.Presentation;

public sealed partial class NativeArt
{
    private Dictionary<string, DiceDefinition>? _contentArt;
    private Dictionary<string, DiceDefinition> ContentArt => _contentArt ??= (JsonSerializer.Deserialize<DiceDefinition[]>(
        Godot.FileAccess.GetFileAsString("res://Data/dice.json"), GameData.JsonOptions) ?? []).ToDictionary(d => d.Id);
    private static string Xml(string text) => SecurityElement.Escape(text) ?? "";
    public Texture2D? ContentGlyph(string id)
    {
        if (!ContentArt.TryGetValue(id, out var d)) return null;
        string path = d.GlyphPath == "" ? "M12 2L22 12L12 22L2 12Z" : d.GlyphPath;
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><path d='{Xml(path)}' fill='none' stroke='white' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/></svg>";
        return Gradient("dice-icon:" + id, svg);
    }
    /// <summary>Native ImageTexture cache; no web view, font bundle or missing-atlas fallback.</summary>
    public Texture2D? ContentDie(string id, int pips)
    {
        if (!ContentArt.TryGetValue(id, out var d)) return null;
        pips = Math.Clamp(pips, 1, 6);
        var svg = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 128 128'><defs><linearGradient id='face' x2='0.8' y2='1'><stop stop-color='{Xml(d.Color)}'/><stop offset='1' stop-color='{Xml(d.Shade)}'/></linearGradient></defs><rect x='12' y='16' width='104' height='104' rx='22' fill='#08111E' opacity='.65'/><rect x='12' y='8' width='104' height='104' rx='22' fill='url(#face)' stroke='{DiceContent.RarityColor(d.Rarity)}' stroke-width='3'/><rect x='19' y='15' width='90' height='90' rx='16' fill='none' stroke='white' stroke-opacity='.25'/>");
        string path = d.GlyphPath == "" ? "M12 2L22 12L12 22L2 12Z" : d.GlyphPath;
        svg.Append($"<g transform='translate(39 23) scale(2.1)'><path d='{Xml(path)}' fill='none' stroke='#0A1A27' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/></g>");
        for (int i = 0; i < pips; i++)
        {
            int x = 64 - (pips - 1) * 6 + i * 12;
            svg.Append($"<circle cx='{x}' cy='91' r='4' fill='#F4FBFF' stroke='#143044' stroke-width='1'/>");
        }
        int marks = Math.Max(1, Array.IndexOf(DiceContent.Rarities, d.Rarity) + 1);
        for (int i = 0; i < marks; i++) svg.Append($"<path d='M{24 + i * 7} 18v5' stroke='{DiceContent.RarityColor(d.Rarity)}' stroke-width='3'/>");
        svg.Append("</svg>"); return Gradient("dice-face:" + id + ":" + pips, svg.ToString());
    }
}
