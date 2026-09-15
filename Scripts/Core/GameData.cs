using System.Text.Json;

namespace DiceGame.Core;

/// <summary>Unmodified JSON is the authority. Rendering and simulation share this registry.</summary>
public sealed class GameData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true
    };
    public GameConfig Game { get; }
    public DiceDefinition[] Dice { get; }
    public UpgradeDefinition[] Upgrades { get; }
    public IReadOnlyDictionary<string, DiceDefinition> Types { get; }
    public IReadOnlyDictionary<string, UpgradeDefinition> UpgradeTypes { get; }

    public IReadOnlyDictionary<string, DiceSkillSet> Skills { get; }
    public string[] DefaultDeck => Game.Rules.StarterDeck.Length > 0 ? Game.Rules.StarterDeck : Dice.Take(Game.Rules.MaxDeck).Select(d => d.Id).ToArray();

    public GameData(string gameJson, string diceJson, string upgradesJson, string? skillsJson = null)
    {
        Game = JsonSerializer.Deserialize<GameConfig>(gameJson, JsonOptions) ?? throw new InvalidDataException("game.json is empty.");
        Dice = JsonSerializer.Deserialize<DiceDefinition[]>(diceJson, JsonOptions) ?? throw new InvalidDataException("dice.json is empty.");
        Upgrades = JsonSerializer.Deserialize<UpgradeDefinition[]>(upgradesJson, JsonOptions) ?? throw new InvalidDataException("upgrades.json is empty.");
        Skills = skillsJson is null ? new Dictionary<string, DiceSkillSet>() : DiceSkillCatalog.Parse(skillsJson);
        Types = Dice.ToDictionary(x => x.Id, StringComparer.Ordinal);
        UpgradeTypes = Upgrades.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Validate();
        DiceContent.Validate(this);
    }
    public static GameData FromDirectory(string path) => new(
        File.ReadAllText(Path.Combine(path, "game.json")),
        File.ReadAllText(Path.Combine(path, "dice.json")),
        File.ReadAllText(Path.Combine(path, "upgrades.json")),
        File.Exists(Path.Combine(path, "dice_skills.json")) ? File.ReadAllText(Path.Combine(path, "dice_skills.json")) : null);
    public bool ValidDeck(IEnumerable<string>? deck)
    {
        if (deck is null) return false;
        var a = deck.ToArray();
        return a.Length >= Game.Rules.MinDeck && a.Length <= Game.Rules.MaxDeck && a.Distinct().Count() == a.Length && a.All(Types.ContainsKey);
    }
    public PointD SlotPosition(int index) => new(Game.Board.Left + index % Game.Board.Columns * Game.Board.StepX,
        Game.Board.Top + index / Game.Board.Columns * Game.Board.StepY);
    private void Validate()
    {
        if (Game.Board.Slots is < 1 or > 64 || Game.Board.Columns < 1 || Game.Board.Columns > Game.Board.Slots ||
            Game.Rules.MaxDeck < 1 || Game.Rules.MaxDeck > Dice.Length || Game.Rules.MinDeck < 1 ||
            Game.Rules.MinDeck > Game.Rules.MaxDeck || Game.Rules.MaxPips != 6 || !MathEx.Finite(Game.Rules.DamageScale, .01, 1))
            throw new InvalidDataException("Invalid board, required deck size or damage budget.");
        if (!ValidDeck(DefaultDeck) || Game.Rules.StartingDicePattern.Length is < 1 or > 64 ||
            Game.Rules.StartingDicePattern.Length > Game.Board.Slots || Game.Rules.StartingDicePattern.Any(i => i < 0 || i >= Game.Rules.MinDeck && i != 1))
            throw new InvalidDataException("Invalid initial dice pattern.");
        if (Game.Rules.EnableDiceSkills && Dice.Any(d => !Skills.ContainsKey(d.Id)))
            throw new InvalidDataException("Every registered die needs its own A/B and C/D skill definitions.");
        if (Game.Levels.Length != Game.Rules.MaxPips || Dice.Length < 1 || Game.Limits.FixedStep <= 0 ||
            Game.Arena.Left >= Game.Arena.Right || Game.Arena.Top >= Game.Arena.Bottom)
            throw new InvalidDataException("Invalid gameplay configuration.");
        for (int i = 0; i < Game.Levels.Length; i++)
            if (Game.Levels[i].Pips != i + 1 || Game.Levels[i].VolleyPower <= 0 || Game.Levels[i].ReloadFactor <= 0)
                throw new InvalidDataException("Invalid pip progression.");
        string[] effects = ["pulse", "blast", "arc", "frost", "split", "bank"];
        foreach (var d in Dice)
            if (d.BaseDamage <= 0 || d.Reload <= 0 || !effects.Contains(d.Effect) || string.IsNullOrEmpty(d.Id))
                throw new InvalidDataException($"Invalid dice definition: {d.Id}");
        foreach (var u in Upgrades)
            if (u.Max < 1 || (u.Requires is not null && !Types.ContainsKey(u.Requires)))
                throw new InvalidDataException($"Invalid upgrade definition: {u.Id}");
    }
}
public sealed class GameConfig
{
    public string Title { get; set; } = "骰子回响";
    public string Subtitle { get; set; } = "DICE RICOCHET";
    public int Schema { get; set; } = 1;
    public ViewConfig View { get; set; } = new();
    public ArenaConfig Arena { get; set; } = new();
    public BoardConfig Board { get; set; } = new();
    public RulesConfig Rules { get; set; } = new();
    public LevelConfig[] Levels { get; set; } = [];
    public WavesConfig Waves { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
}
public sealed class ViewConfig { public int Width { get; set; } = 432; public int Height { get; set; } = 864; }
public sealed class ArenaConfig
{
    public double Left { get; set; } public double Right { get; set; } public double Top { get; set; }
    public double Bottom { get; set; } public double Breach { get; set; } public double LaunchY { get; set; }
}
public sealed class BoardConfig
{
    public int Slots { get; set; } public int Columns { get; set; } public double Left { get; set; }
    public double Top { get; set; } public double StepX { get; set; } public double StepY { get; set; } public double Size { get; set; }
}
public sealed class RulesConfig
{
    public int MinDeck { get; set; } = 1;
    public double DamageScale { get; set; } = 1;
    public bool EnableDiceSkills { get; set; }
    public bool EnableDiceContent { get; set; }
    public string[] StarterDeck { get; set; } = [];
    public int[] StartingDicePattern { get; set; } = [0, 0, 1];
    public int MaxDeck { get; set; } public int MaxPips { get; set; } public double StartEnergy { get; set; }
    public double SummonCost { get; set; } public double PassiveEnergy { get; set; } public int MaxHealth { get; set; }
    public double MergeSurge { get; set; } public double AimMaxDegrees { get; set; }
    public double ProjectileSpeed { get; set; } public double ProjectileRadius { get; set; } public double ProjectileLife { get; set; }
    public int BaseBounces { get; set; } public int MaxProjectiles { get; set; } public int MaxQueuedShots { get; set; }
    public int MaxEnemies { get; set; } public double WaveSeconds { get; set; } public int UpgradeEvery { get; set; } public int BossEvery { get; set; }
}
public sealed class LevelConfig
{
    public int Pips { get; set; } public double VolleyPower { get; set; } public double ReloadFactor { get; set; } public int Recycle { get; set; }
}
public sealed class WavesConfig
{
    public double BaseHp { get; set; } public double HpExponential { get; set; } public double HpLinear { get; set; }
    public double BaseSpeed { get; set; } public double SpeedPerWave { get; set; } public double MaxSpeed { get; set; }
    public int BaseCount { get; set; } public double CountPerWave { get; set; } public int MaxCount { get; set; }
    public int ClearEnergy { get; set; } public int KillEnergy { get; set; } public double BossHpFactor { get; set; }
}
public sealed class LimitsConfig
{
    public int Particles { get; set; } public int Rings { get; set; } public int Floaters { get; set; } public int Arcs { get; set; }
    public double FixedStep { get; set; } public int MaxSteps { get; set; } public double MaxFrameDelta { get; set; }
}
public sealed class DiceDefinition
{
    public string Rarity { get; set; } = "common";
    public bool AvailableFromStart { get; set; }
    public bool Copyable { get; set; } = true;
    public string GlyphPath { get; set; } = "";
    public Dictionary<string, double> Traits { get; set; } = new(StringComparer.Ordinal);
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Tag { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF"; public string Shade { get; set; } = "#FFFFFF";
    public double BaseDamage { get; set; } public double Reload { get; set; } public string Effect { get; set; } = "pulse";
    public string Description { get; set; } = ""; public string Icon { get; set; } = "pulse";
    public double Radius { get; set; } public double SplashFactor { get; set; } public double ChainRange { get; set; }
    public double ChainFactor { get; set; } public double SlowFactor { get; set; } = 1; public double SlowSeconds { get; set; }
    public double ChildFactor { get; set; } public double WallBoost { get; set; } public double MaxBoost { get; set; } = 1;
}
public sealed class UpgradeDefinition
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Tag { get; set; } = "";
    public string Description { get; set; } = ""; public string Color { get; set; } = "#FFFFFF";
    public string Icon { get; set; } = "pulse"; public string? Requires { get; set; } public int Max { get; set; }
}
