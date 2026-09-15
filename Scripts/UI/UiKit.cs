using Godot;
using DiceGame.Presentation;

namespace DiceGame.UI;

/// <summary>Reusable native Control widgets. Scene templates and the theme are the art replacement boundary.</summary>
public static class UiKit
{
    public const string Ink = "#E5F1F5", Muted = "#9CAFBE", Mint = "#72EAC8", Gold = "#F8DE87";
    public static StyleBoxFlat Box(string color = "#142437", string border = "#314858", int radius = 12)
    {
        var style = new StyleBoxFlat { BgColor = Color.FromHtml(color), BorderColor = Color.FromHtml(border),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12 };
        return style;
    }
    public static Theme Theme(NativeArt art, double scale)
    {
        var t = new Theme { DefaultFont = art.Font(500), DefaultFontSize = (int)Math.Round(20 * scale) };
        t.SetColor("font_color", "Label", Color.FromHtml(Ink));
        t.SetColor("font_color", "Button", Color.FromHtml(Ink));
        t.SetColor("font_hover_color", "Button", Color.FromHtml(Mint));
        t.SetColor("font_disabled_color", "Button", Color.FromHtml("#697D8C"));
        t.SetStylebox("panel", "PanelContainer", Box());
        t.SetStylebox("normal", "Button", Box()); t.SetStylebox("hover", "Button", Box("#213C4C", Mint));
        t.SetStylebox("pressed", "Button", Box("#294B57", Mint)); t.SetStylebox("disabled", "Button", Box("#111C29", "#253643"));
        t.SetStylebox("focus", "Button", new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Color.FromHtml(Gold), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
        t.SetStylebox("background", "ProgressBar", Box("#263C4B", "#263C4B", 2));
        t.SetStylebox("fill", "ProgressBar", Box(Mint, Mint, 2));
        foreach (string kind in new[] { "VBoxContainer", "HBoxContainer", "GridContainer" })
        { t.SetConstant("separation", kind, 12); t.SetConstant("h_separation", kind, 12); t.SetConstant("v_separation", kind, 12); }
        return t;
    }
    public static T Add<T>(Node parent, T child, string? name = null) where T : Node
    {
        if (name is not null) child.Name = name;
        parent.AddChild(child); child.Owner = parent.Owner ?? parent; return child;
    }
    public static Label Label(Node parent, string text, int size = 20, string color = Ink, bool wrap = true)
    {
        var l = Add(parent, new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", Color.FromHtml(color)); return l;
    }
    public static Button Button(Node parent, string text, Action action, bool disabled = false)
    {
        var b = Add(parent, new Button { Text = text, Disabled = disabled, CustomMinimumSize = new Vector2(0, 48), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand });
        b.Pressed += action; return b;
    }
    public static VBoxContainer Column(Node parent, bool expand = false) => Add(parent, new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill });
    public static HBoxContainer Row(Node parent, bool expand = false) => Add(parent, new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill });
    public static VBoxContainer Card(Node parent, string title, string subtitle = "")
    {
        var panel = Add(parent, new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var box = Column(panel); Label(box, title, 24); if (subtitle != "") Label(box, subtitle, 18, Muted); return box;
    }
    public static VBoxContainer Scroll(Node parent)
    {
        var sc = Add(parent, new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        return Column(sc);
    }
    public static TextureRect Dice(Node parent, NativeArt art, string id, int pips = 1, int size = 100)
    {
        var rect = Add(parent, new TextureRect { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        SetDice(rect, art, id, pips); return rect;
    }
    public static void SetDice(TextureRect rect, NativeArt art, string id, int pips)
    {
        int row = Array.IndexOf(NativeArt.TypeRows, id); if (row < 0) { rect.Texture = art.ContentDie(id, pips); return; }
        const int cell = (49 + 16) * 3;
        rect.Texture = new AtlasTexture { Atlas = art.Dice(49), Region = new Rect2((Math.Clamp(pips, 1, 6) - 1) * cell, row * cell, cell, cell), FilterClip = true };
    }
    public static void ScaleTypography(Node root, double scale)
    {
        if(root is Control c && c.HasThemeFontSizeOverride("font_size"))
        {
            if(!c.HasMeta("unscaled_font_size")) c.SetMeta("unscaled_font_size",c.GetThemeFontSize("font_size"));
            c.AddThemeFontSizeOverride("font_size",(int)Math.Round(c.GetMeta("unscaled_font_size").AsInt32()*scale));
        }
        foreach(var child in root.GetChildren()) ScaleTypography(child,scale);
    }
    public static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
}
