namespace DiceGame.Core;

// Doubles intentionally match JavaScript's Number. Do NOT replace the simulation with Godot float physics.
public sealed record PointD(double X = 0, double Y = 0);
public sealed record TracePoint(double X, double Y, long? Enemy = null);
public sealed partial class DieState
{
    public long Id { get; set; } public string Type { get; set; } = "pulse"; public int Pips { get; set; } = 1;
    public double Cooldown { get; set; } public double Flash { get; set; }
    public string Tier3 { get; set; } = ""; public string Tier6 { get; set; } = "";
    public DieState Copy() => (DieState)MemberwiseClone();
}
public sealed partial class ShotStats
{
    public string Effect { get; set; } = "pulse"; public double Damage { get; set; } public double Volley { get; set; }
    public int Count { get; set; } public double Reload { get; set; } public int Bounces { get; set; } public string Color { get; set; } = "#FFFFFF";
    public double BlastRadius { get; set; } public double SplashFactor { get; set; } public int ChainCount { get; set; }
    public double ChainRange { get; set; } public double ChainFactor { get; set; } public double SlowFactor { get; set; }
    public double SlowSeconds { get; set; } public int ChildCount { get; set; } public double ChildFactor { get; set; }
    public double WallBoost { get; set; } public double MaxBoost { get; set; }
    public int Pierces { get; set; } public int ChildPierces { get; set; }
    public int ChildBounceBonus { get; set; } public double ChildLifeBonus { get; set; }
    public double SlowRadius { get; set; } public double BossDamageMultiplier { get; set; } = 1;
    public double ChilledDamageMultiplier { get; set; } = 1; public double KillExplosionFactor { get; set; }
    public double ArcReturnFactor { get; set; } public double ShatterRadius { get; set; } public double ShatterFactor { get; set; }
    public double WallRetention { get; set; } public double BankShockRadius { get; set; } public double BankShockFactor { get; set; }
    public ShotStats Copy() { var copy = (ShotStats)MemberwiseClone(); copy.Traits = new(Traits, StringComparer.Ordinal); return copy; }
}
public sealed partial class ShotSnapshot
{
    public string Type { get; set; } = "pulse"; public int Pips { get; set; } = 1; public ShotStats Stats { get; set; } = new();
    public PointD? Source { get; set; } public bool Surge { get; set; }
}
public sealed class PendingShot
{
    public double Due { get; set; } public double Angle { get; set; } public ShotSnapshot Snapshot { get; set; } = new();
    public bool Child { get; set; } public double X { get; set; } public double Y { get; set; } public long LastEnemy { get; set; }
}
public sealed partial class EnemyState
{
    public long Id { get; set; } public double X { get; set; } public double Y { get; set; } public double W { get; set; } = 39;
    public double H { get; set; } = 39; public double Hp { get; set; } public double MaxHp { get; set; } public double Speed { get; set; }
    public string Kind { get; set; } = "normal"; public int Wave { get; set; } public double SlowUntil { get; set; }
    public double SlowFactor { get; set; } = 1; public double Flash { get; set; } public bool Dead { get; set; }
}
public sealed partial class ProjectileState
{
    public long Id { get; set; } public double X { get; set; } public double Y { get; set; }
    public double Px { get; set; } public double Py { get; set; } public double Vx { get; set; } public double Vy { get; set; }
    public string Type { get; set; } = "pulse"; public int Pips { get; set; } = 1; public ShotStats Stats { get; set; } = new();
    public double Life { get; set; } public int Bounces { get; set; } public bool Child { get; set; } public bool SplitDone { get; set; }
    public int PiercesLeft { get; set; }
    public double WallPower { get; set; } = 1; public long LastEnemy { get; set; } public bool Surge { get; set; } public bool Dead { get; set; }
    public List<PointD> Trail { get; set; } = [];
}
public sealed partial class SecondaryDamage
{
    public long Id { get; set; } public double Amount { get; set; } public string Color { get; set; } = "#FFFFFF";
}
public sealed class CombatEvent
{
    public string Type { get; set; } = ""; public string? Id { get; set; }
    public double X { get; set; } public double Y { get; set; } public double Tx { get; set; } public double Ty { get; set; }
    public double W { get; set; } public double Radius { get; set; } public string Color { get; set; } = "#FFFFFF";
    public double Amount { get; set; } public int Reward { get; set; } public int Combo { get; set; } public string Kind { get; set; } = "normal";
    public bool Boss { get; set; } public bool Boost { get; set; } public bool Volatile { get; set; } public bool Surge { get; set; }
    public int Slot { get; set; } public int A { get; set; } public int B { get; set; } public DieState? Die { get; set; }
    public string? OldType { get; set; } public int Wave { get; set; } public double Score { get; set; } public int Lost { get; set; }
    public int Count { get; set; } public int Dice { get; set; } public double Angle { get; set; } public List<string>? Offers { get; set; }
}
public sealed record ActionResult(bool Ok, string Reason = "", int Slot = -1, DieState? Die = null, int Count = 0, int Dice = 0, int Amount = 0);

/// <summary>Serializable state, independent of presentation and live engine objects. Version matches the original save.</summary>
public sealed partial class RunState
{
    public ExpeditionState? Expedition { get; set; }
    public int Schema { get; set; } = 1; public List<string> Deck { get; set; } = []; public uint Rng { get; set; }
    public uint Seed { get; set; } public double Time { get; set; } public long NextId { get; set; } = 1;
    public int Wave { get; set; } public double WaveTime { get; set; } public int Health { get; set; }
    public double Energy { get; set; } public double Score { get; set; } public long Kills { get; set; }
    public long Merges { get; set; } public long Shots { get; set; } public int Combo { get; set; } public double ComboTime { get; set; }
    public int BestCombo { get; set; } public bool ClearRewarded { get; set; } public double NextWaveIn { get; set; } = -1;
    public double LastAim { get; set; } = -Math.PI / 2;
    public DieState?[] Board { get; set; } = [];
    public Dictionary<string, DiceSkillSet> SkillSets { get; set; } = [];
    public List<DiceSkillChoice> PendingSkills { get; set; } = [];
    public List<EnemyState> Enemies { get; set; } = [];
    public List<ProjectileState> Projectiles { get; set; } = [];
    public List<PendingShot> PendingShots { get; set; } = [];
    public List<SecondaryDamage> DamageQueue { get; set; } = [];
    public Dictionary<string, int> Upgrades { get; set; } = new(StringComparer.Ordinal);
    public List<string> Offers { get; set; } = [];
    public bool AwaitingUpgrade { get; set; } public bool Over { get; set; }
    public double TotalDamage { get; set; } public long ManualVolleys { get; set; } public long Escaped { get; set; }
}
