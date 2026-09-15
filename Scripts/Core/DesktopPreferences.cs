namespace DiceGame.Core;

public sealed class DesktopPreferences
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string WindowMode { get; set; } = "windowed";
    public bool VSync { get; set; } = true;
    public int MaxFps { get; set; } = 120;
    public int Msaa { get; set; } = 2;
    public double UiScale { get; set; } = 1;
    public double MasterVolume { get; set; } = .8;
    public double SfxVolume { get; set; } = 1;
    public double MusicVolume { get; set; } = .6;
    public bool ShowFps { get; set; }
    public bool ScreenShake { get; set; } = true;
    public bool FlashEffects { get; set; } = true;
    public bool ShowAim { get; set; } = true;
    public Dictionary<string, string> Bindings { get; set; } = new()
    { ["summon"] = "Space", ["pause"] = "Escape", ["mute"] = "M", ["fullscreen"] = "F11" };
    public void Validate()
    {
        if (Width is < 960 or > 7680 || Height is < 640 or > 4320 || WindowMode is not ("windowed" or "borderless" or "fullscreen") || MaxFps is < 0 or > 1000 || Msaa is < 0 or > 3 || !MathEx.Finite(UiScale, .8, 1.3))
            throw new InvalidDataException("Invalid desktop display settings.");
        foreach (double volume in new[] { MasterVolume, SfxVolume, MusicVolume }) if (!MathEx.Finite(volume, 0, 1)) throw new InvalidDataException("Invalid audio volume.");
        if (Bindings.Count != 4 || new[] { "summon", "pause", "mute", "fullscreen" }.Any(k => !Bindings.ContainsKey(k)) || Bindings.Values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 32) || Bindings.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new InvalidDataException("Invalid or duplicate key bindings.");
    }
}
