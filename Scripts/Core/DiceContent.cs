namespace DiceGame.Core;

/// <summary>Serialized combat state belongs to a die identity, never to a board slot or a UI node.</summary>
public sealed partial class DieState
{
    public double Charge { get; set; }
    public double Age { get; set; }
    public double AbilityClock { get; set; }
    public double PulseUntil { get; set; }
    public double LastHitTime { get; set; }
    public long LastTarget { get; set; }
    public int Streak { get; set; }
    public long GrowthKills { get; set; }
    public long Attacks { get; set; }
    public int BadRolls { get; set; }
}
public sealed partial class ShotStats
{
    public string AttackType { get; set; } = "";
    public Dictionary<string, double> Traits { get; set; } = new(StringComparer.Ordinal);
    public double Trait(string key, double fallback = 0) => Traits.GetValueOrDefault(key, fallback);
}
public sealed partial class ShotSnapshot { public long SourceDieId { get; set; } }
public sealed partial class ProjectileState
{
    public long SourceDieId { get; set; }
    public int Pierced { get; set; }
    public bool FirstHitDone { get; set; }
    public bool PierceRewarded { get; set; }
    public bool AftershockDone { get; set; }
    public bool RampBurstDone { get; set; }
    public int WallsHit { get; set; }
    public HashSet<long> PassingEnemies { get; set; } = [];
}
public sealed class PoisonStack
{
    public long SourceId { get; set; }
    public double DamagePerSecond { get; set; }
    public double Until { get; set; }
    public double LastTick { get; set; }
    public string Color { get; set; } = "#91D773";
    public bool Spread { get; set; }
}
public sealed class TimedAreaHit
{
    public double Due { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Radius { get; set; }
    public double Damage { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public long SourceId { get; set; }
}
public sealed partial class EnemyState
{
    public List<PoisonStack> Poison { get; set; } = [];
    public Dictionary<long, double> Contributors { get; set; } = [];
    public double MarkUntil { get; set; }
    public double MarkFactor { get; set; } = 1;
    public double NextKnockback { get; set; }
}
public sealed partial class SecondaryDamage { public long SourceId { get; set; } }
public sealed partial class RunState
{
    public int ContentVersion { get; set; }
    public List<TimedAreaHit> TimedHits { get; set; } = [];
    public double RageUntil { get; set; }
    public double LastTimeKick { get; set; } = -10;
    public double LastEvolutionGift { get; set; } = -100;
}

/// <summary>Authorable traits supplement the existing typed projectile modifiers. Unknown keys fail loading.</summary>
public static class DiceContent
{
    public const int Version = 2;
    public static readonly string[] Rarities = ["common", "rare", "epic", "legendary", "mythic"];
    public static string RarityName(string id) => id switch { "mythic" => "神话", "rare" => "稀有", "epic" => "史诗", "legendary" => "传说", _ => "普通" };
    public static string RarityColor(string id) => id switch { "mythic" => "#FF95D0", "rare" => "#87D6FF", "epic" => "#CBA7FF", "legendary" => "#F8DE87", _ => "#C2D4DA" };
    public static readonly HashSet<string> Keys = new((
        "pipHaste pierce pipPierce pierceRetention pierceGain pierceEnergy armorBonus bossBonus aftershock " +
        "deathBurst chilledBonus seekCone finisher mark rampHaste rampDamage rampMax rampKeep rampBurst " +
        "poison poisonStacks poisonCap poisonDuration poisonSpread poisonDetonate echoChance echoFactor echoCopies " +
        "auraEcho mergeRefund recycleBonus auraDamage energyFirst chargeRate chargeMax chargeKeep chargePierce " +
        "resonance resonanceLoose resonanceHaste resonanceAura aloneBonus chainSlow " +
        "dangerGain woundedGain safeDamage dangerThreshold breachBoost knockback dangerEnergy " +
        "auraPierce auraSplit auraRow focusedAura growthEvery growthStep growthMax assistGrowth matureAura " +
        "weakChance doubleChance jackpotChance jackpotFactor noWeak pity jackpotEnergy " +
        "mirror mirrorFactor mirrorRow mirrorBranch mirrorEcho timePeriod timeDuration timeHaste timeKick " +
        "evolutionPeriod evolutionScale evolutionDamage evolutionGift finalDamage finalReload " +
        "wallEnergy").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    static DiceContent() { Keys.UnionWith(DiceExpansion.Keys); }
    public static void Apply(Dictionary<string, double> values, DiceSkillModifiers modifier)
    {
        foreach (var (key, value) in modifier.ContentAdd) values[key] = values.GetValueOrDefault(key, Default(key)) + value;
        foreach (var (key, value) in modifier.ContentMultiply) values[key] = values.GetValueOrDefault(key, Default(key)) * value;
    }
    public static double Default(string key) => key is "pierceRetention" or "safeDamage" or "finalDamage" or "finalReload" ? 1 : 0;
    public static Dictionary<string, double> Profile(DiceDefinition definition, DieState die, DiceSkillSet? skills)
    {
        var values = new Dictionary<string, double>(definition.Traits, StringComparer.Ordinal);
        if (die.Pips >= 3 && skills?.Find(3, die.Tier3) is { } a) Apply(values, a.Modifiers);
        if (die.Pips >= 6 && skills?.Find(6, die.Tier6) is { } b) Apply(values, b.Modifiers);
        return values;
    }
    public static void Validate(GameData data)
    {
        if (data.Game.Rules.EnableDiceContent && !data.Game.Rules.EnableDiceSkills)
            throw new InvalidDataException("Extended dice content requires instance skills.");
        foreach (var d in data.Dice)
        {
            if (d.Rarity == "mythic" && d.Copyable) throw new InvalidDataException("Mythic laws cannot be mirrored: " + d.Id);
            if (!Rarities.Contains(d.Rarity) || d.GlyphPath is null || d.GlyphPath.Length > 4000)
                throw new InvalidDataException("Invalid rarity / glyph: " + d.Id);
            CheckModifiers(d.Traits);
            if (data.Skills.TryGetValue(d.Id,out var set))
                foreach(string a in new[]{"", "A", "B"}) foreach(string b in new[]{"", "C", "D"})
                {
                    var values=Profile(d,new DieState{Pips=6,Tier3=a,Tier6=b},set);
                    CheckModifiers(values);
                    if(values.GetValueOrDefault("jackpotFactor")>0 && values.GetValueOrDefault("weakChance")+values.GetValueOrDefault("doubleChance")+values.GetValueOrDefault("jackpotChance")>1+1e-10)
                        throw new InvalidDataException("Gambling probabilities exceed one: "+d.Id);
                }
        }
    }
    public static void CheckModifiers(Dictionary<string, double>? values, bool signed = false, bool runtime = false)
    {
        if (values is null || values.Count > Keys.Count + 1) throw new InvalidDataException("Invalid content modifier list.");
        foreach (var (key, value) in values)
            if (!(Keys.Contains(key) || runtime && key == "rootFirst") || !double.IsFinite(value) || value < (signed ? -1000 : 0) || value > 10000)
                throw new InvalidDataException("Unknown or invalid content modifier: " + key);
    }
    public static void ValidateRuntime(RunState s)
    {
        DiceExpansion.ValidateRuntime(s);
        static void Check(bool condition) { if (!condition) throw new InvalidDataException("Invalid saved dice content state."); }
        static bool Number(double v, double min = 0, double max = 1e9) => MathEx.Finite(v, min, max);
        Check(s.ContentVersion is 0 or 1 or Version && s.TimedHits is not null && s.TimedHits.Count <= 256);
        Check(Number(s.RageUntil) && Number(s.LastTimeKick, -10) && Number(s.LastEvolutionGift, -100));
        foreach (var d in s.Board.OfType<DieState>())
            Check(Number(d.Charge, 0, 10) && Number(d.Age) && Number(d.AbilityClock, 0, 1e6) && Number(d.PulseUntil) && Number(d.LastHitTime) &&
                d.LastTarget >= 0 && d.Streak is >= 0 and <= 100 && d.GrowthKills is >= 0 and <= 1000000 && d.Attacks is >= 0 and <= 1000000000000 && d.BadRolls is >= 0 and <= 10000);
        foreach (var e in s.Enemies)
        {
            Check(e.Poison is not null && e.Poison.Count <= 20 && e.Contributors is not null && e.Contributors.Count <= 64);
            Check(Number(e.MarkUntil) && Number(e.MarkFactor, 1, 1.3) && Number(e.NextKnockback));
            foreach (var p in e.Poison!) Check(p is not null && p.SourceId >= 0 && Number(p.DamagePerSecond, 0, 1e18) && Number(p.Until) && Number(p.LastTick) && p.LastTick <= p.Until && p.Color is not null && p.Color.Length <= 32);
            foreach (var (id, time) in e.Contributors!) Check(id > 0 && Number(time));
        }
        foreach (var p in s.Projectiles)
            Check(p.SourceDieId >= 0 && p.Pierced is >= 0 and <= 64 && p.WallsHit is >= 0 and <= 40 && p.PassingEnemies is not null && p.PassingEnemies.Count <= 128 && p.PassingEnemies.All(id => id > 0));
        foreach (var h in s.TimedHits!) Check(h is not null && Number(h.Due) && Number(h.X, -1000, 1000) && Number(h.Y, -1000, 1000) && Number(h.Radius, 0, 200) && Number(h.Damage, 0, 1e18) && h.SourceId >= 0 && h.Color is not null && h.Color.Length <= 32);
    }
}
