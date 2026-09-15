using System.Text.Json;

namespace DiceGame.Core;

/// <summary>Per-type, per-instance branches. The compact branch key is remapped after a random merge;
/// it never carries a foreign die's skill implementation into the result.</summary>
public sealed class DiceSkillSet
{
    public string Type { get; set; } = "";
    public DiceSkillDefinition[] Level3 { get; set; } = [];
    public DiceSkillDefinition[] Level6 { get; set; } = [];
    public DiceSkillDefinition? Find(int tier, string key) => (tier == 3 ? Level3 : Level6).FirstOrDefault(s => s.Key == key);
}
public sealed class DiceSkillDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DiceSkillModifiers Modifiers { get; set; } = new();
}
public sealed class DiceSkillModifiers
{
    public Dictionary<string, double> ContentAdd { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> ContentMultiply { get; set; } = new(StringComparer.Ordinal);
    public double DamageMultiplier { get; set; } = 1;
    public double ReloadMultiplier { get; set; } = 1;
    public int ExtraProjectiles { get; set; }
    public int ExtraBounces { get; set; }
    public double BlastRadiusMultiplier { get; set; } = 1;
    public double SplashMultiplier { get; set; } = 1;
    public int ExtraChains { get; set; }
    public double ChainRangeMultiplier { get; set; } = 1;
    public double ChainDamageMultiplier { get; set; } = 1;
    public double SlowFactorOverride { get; set; }
    public double SlowDurationMultiplier { get; set; } = 1;
    public double SlowRadius { get; set; }
    public int ExtraChildren { get; set; }
    public double ChildDamageMultiplier { get; set; } = 1;
    public double ChildLifeBonus { get; set; }
    public int ChildBounceBonus { get; set; }
    public int Pierces { get; set; }
    public int ChildPierces { get; set; }
    public double WallBoostMultiplier { get; set; } = 1;
    public double MaxBoostBonus { get; set; }
    public double BossDamageMultiplier { get; set; } = 1;
    public double ChilledDamageMultiplier { get; set; } = 1;
    public double KillExplosionFactor { get; set; }
    public double ArcReturnFactor { get; set; }
    public double ShatterRadius { get; set; }
    public double ShatterFactor { get; set; }
    public double WallRetention { get; set; }
    public double BankShockRadius { get; set; }
    public double BankShockFactor { get; set; }

    public void Apply(ShotStats s)
    {
        DiceContent.Apply(s.Traits, this);
        s.Volley *= DamageMultiplier; s.Reload *= ReloadMultiplier; s.Count += ExtraProjectiles; s.Bounces += ExtraBounces;
        s.BlastRadius *= BlastRadiusMultiplier; s.SplashFactor *= SplashMultiplier;
        s.ChainCount += ExtraChains; s.ChainRange *= ChainRangeMultiplier; s.ChainFactor *= ChainDamageMultiplier;
        if (SlowFactorOverride > 0) s.SlowFactor = SlowFactorOverride;
        s.SlowSeconds *= SlowDurationMultiplier; s.SlowRadius = Math.Max(s.SlowRadius, SlowRadius);
        s.ChildCount += ExtraChildren; s.ChildFactor *= ChildDamageMultiplier; s.ChildLifeBonus += ChildLifeBonus;
        s.ChildBounceBonus += ChildBounceBonus; s.Pierces += Pierces; s.ChildPierces += ChildPierces;
        s.WallBoost *= WallBoostMultiplier; s.MaxBoost += MaxBoostBonus;
        s.BossDamageMultiplier *= BossDamageMultiplier; s.ChilledDamageMultiplier *= ChilledDamageMultiplier;
        s.KillExplosionFactor += KillExplosionFactor; s.ArcReturnFactor += ArcReturnFactor;
        s.ShatterRadius = Math.Max(s.ShatterRadius, ShatterRadius); s.ShatterFactor += ShatterFactor;
        s.WallRetention = Math.Max(s.WallRetention, WallRetention);
        s.BankShockRadius = Math.Max(s.BankShockRadius, BankShockRadius); s.BankShockFactor += BankShockFactor;
    }
    public void Validate()
    {
        DiceContent.CheckModifiers(ContentAdd, true); DiceContent.CheckModifiers(ContentMultiply);
        foreach (double v in new[] { DamageMultiplier, ReloadMultiplier, BlastRadiusMultiplier, SplashMultiplier,
            ChainRangeMultiplier, ChainDamageMultiplier, SlowDurationMultiplier, ChildDamageMultiplier, WallBoostMultiplier,
            BossDamageMultiplier, ChilledDamageMultiplier })
            if (!MathEx.Finite(v, .1, 4)) throw new InvalidDataException("Skill multipliers must be finite and between 0.1 and 4.");
        if (ExtraProjectiles is < 0 or > 6 || ExtraBounces is < 0 or > 12 || ExtraChains is < -5 or > 6 ||
            ExtraChildren is < -3 or > 6 || ChildBounceBonus is < 0 or > 12 || Pierces is < 0 or > 4 || ChildPierces is < 0 or > 4)
            throw new InvalidDataException("Skill count modifier is outside its supported range.");
        foreach (double v in new[] { SlowFactorOverride, WallRetention })
            if (!MathEx.Finite(v, 0, .95)) throw new InvalidDataException("Invalid skill control / retention fraction.");
        foreach (double v in new[] { SlowRadius, ShatterRadius, BankShockRadius })
            if (!MathEx.Finite(v, 0, 150)) throw new InvalidDataException("Skill radius exceeds 150.");
        foreach (double v in new[] { KillExplosionFactor, ArcReturnFactor, ShatterFactor, BankShockFactor, MaxBoostBonus })
            if (!MathEx.Finite(v, 0, 2)) throw new InvalidDataException("Invalid skill secondary damage / cap modifier.");
        if (!MathEx.Finite(ChildLifeBonus, 0, 5)) throw new InvalidDataException("Invalid child projectile lifetime modifier.");
    }
}
public sealed class DiceSkillChoice
{
    public long ChoiceId { get; set; }
    public long DieId { get; set; }
    public string DiceType { get; set; } = "";
    public int ResultPips { get; set; }
    public int Tier { get; set; }
    public bool FinishMerge { get; set; }
    public double SurgeAngle { get; set; } = -Math.PI / 2;
    public double SurgeMultiplier { get; set; } = 1;
}
public static class DiceSkillCatalog
{
    public static Dictionary<string, DiceSkillSet> Parse(string json)
    {
        var sets = JsonSerializer.Deserialize<DiceSkillSet[]>(json, new JsonSerializerOptions(GameData.JsonOptions) { UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Empty dice_skills.json.");
        var result = sets.ToDictionary(s => s.Type, StringComparer.Ordinal);
        foreach (var pair in result) ValidateSet(pair.Key, pair.Value);
        return result;
    }
    public static void ValidateSet(string type, DiceSkillSet? set)
    {
        if (set is null || set.Type != type || set.Level3 is null || set.Level6 is null ||
            set.Level3.Length != 2 || set.Level6.Length != 2 ||
            !set.Level3.Select(s => s.Key).SequenceEqual(new[] { "A", "B" }) ||
            !set.Level6.Select(s => s.Key).SequenceEqual(new[] { "C", "D" }))
            throw new InvalidDataException("Each die needs A/B at level 3 and C/D at level 6: " + type);
        foreach (var skill in set.Level3.Concat(set.Level6))
        {
            if (string.IsNullOrWhiteSpace(skill.Name) || skill.Name.Length > 80 || skill.Description.Length > 1200 || skill.Modifiers is null)
                throw new InvalidDataException("Invalid skill description: " + type);
            skill.Modifiers.Validate();
        }
    }
}
