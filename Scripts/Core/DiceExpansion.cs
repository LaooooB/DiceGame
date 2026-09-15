namespace DiceGame.Core;

// Extension state is made of serializable values, not scene nodes. Die copies retain identity
// only for previews; a merge/rebirth uses MakeDie and never inherits these counters.
public sealed partial class DieState
{
    public long NormalAttacks { get; set; }
    public bool HasAimMemory { get; set; }
    public double AimMemory { get; set; }
    public int AimStacks { get; set; }
    public int ExecuteStacks { get; set; }
    public int BeatRemainders { get; set; }
    public double MechanicEnergy { get; set; }
    public double BarrierLastHit { get; set; } = -1;
    public double VoidClock { get; set; }
    public double ForgeBlessingUntil { get; set; }
    public double ForgeBlessingPower { get; set; }
    public long LatestVolley { get; set; }
    public double IssuedReload { get; set; }
}
public enum AuxiliaryKind { None, Soul, Swarm, Phase }
[Flags]
public enum DamageFlags { None = 0, Direct = 1, Causal = 2, Settled = 4 }
public sealed partial class ShotStats
{
    public long VolleyId { get; set; }
    public bool Echo { get; set; }
    public bool SidePellet { get; set; }
    public bool FirstCenter { get; set; }
    public bool ReactorCharged { get; set; }
    public bool VoidCharged { get; set; }
    public AuxiliaryKind Auxiliary { get; set; }
    public int AuxiliaryGeneration { get; set; }
    public double AuxiliaryMarkSeconds { get; set; }
    public double AuxiliaryDuplicate { get; set; }
}
public sealed partial class ProjectileState
{
    public double FlightAge { get; set; }
    public int PhaseCount { get; set; }
    public bool FlightPierceGranted { get; set; }
    public bool FlightShockDone { get; set; }
    public bool SpreePierceGranted { get; set; }
    public bool EarlyGuided { get; set; }
    public bool WallGuided { get; set; }
    public bool GuidedDamageDone { get; set; }
    public bool Relocked { get; set; }
    public bool RiftUsed { get; set; }
    public bool MainEffectDone { get; set; }
    public HashSet<long> FocusTargets { get; set; } = [];
}
public sealed class VolleyRecord
{
    public long SourceId { get; set; }
    public int Kills { get; set; }
    public double Step { get; set; }
    public int Cap { get; set; }
    public int Elite { get; set; }
    public bool RiftCreated { get; set; }
    public Dictionary<long, int> FocusCounts { get; set; } = [];
}
public sealed class StackedStatus
{
    public int Stacks { get; set; }
    public double Until { get; set; }
    public double ArmorPower { get; set; }
    public double OtherPower { get; set; }
    public long SourceId { get; set; }
}
public sealed class CausalDebt
{
    public long SourceId { get; set; }
    public double Due { get; set; }
    public double Recorded { get; set; }
    public double Cap { get; set; }
    public double Ratio { get; set; }
    public double Transfer { get; set; }
    public bool Sealed { get; set; }
    public string Color { get; set; } = "#FFB2CF";
}
public sealed partial class EnemyState
{
    public Dictionary<string, StackedStatus> Conditions { get; set; } = [];
    public Dictionary<long, double> Souls { get; set; } = [];
    public CausalDebt? Debt { get; set; }
    public double SunderNext { get; set; }
    public double MassNext { get; set; }
    public double CatalystNext { get; set; }
    public double ExtensionNext { get; set; }
    public double ExtensionSpent { get; set; }
    public int BossMilestones { get; set; }
}
public sealed class GravityField
{
    public long SourceId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Until { get; set; }
    public double LastTick { get; set; }
    public double Radius { get; set; }
    public double Dps { get; set; }
    public double Pull { get; set; }
    public double Steer { get; set; }
    public double Vulnerable { get; set; }
    public string Color { get; set; } = "#E8CE88";
}
public sealed class WallRift
{
    // 0=left, 1=right, 2=top. The lower edge is an exit, not an entry/collider.
    public int Wall { get; set; }
    public double Position { get; set; }
    public double Width { get; set; }
    public double Until { get; set; }
    public double Power { get; set; }
    public int Pierces { get; set; }
    public double Shock { get; set; }
    public string Color { get; set; } = "#DCD1A5";
}
public sealed class ExpansionState
{
    public Dictionary<long, VolleyRecord> Volleys { get; set; } = [];
    public List<GravityField> Fields { get; set; } = [];
    public List<WallRift> Rifts { get; set; } = [];
    public int Shield { get; set; }
    public double ShieldReactionUntil { get; set; }
    public double ShieldReactionPower { get; set; }
    public uint FateRng { get; set; }
    public double FateClock { get; set; }
    public List<int> FateNumbers { get; set; } = [];
    public List<long> FateMinimumIds { get; set; } = [];
    public double FateBlessingUntil { get; set; }
    public double FateBlessingPower { get; set; }
    public uint OrderRng { get; set; }
    public List<string> OrderBag { get; set; } = [];
    public string OrderLast { get; set; } = "";
    public long OrderSkipStage { get; set; } = -1;
    public int Rebirths { get; set; }
    public double RebirthPower { get; set; }
}
public sealed partial class RunState { public ExpansionState Expansion { get; set; } = new(); }
public sealed partial class SecondaryDamage
{
    public DamageFlags Flags { get; set; }
    public long VolleyId { get; set; }
}

/// <summary>Content registration and storage budgets. These limits do not scale with die count.</summary>
public static class DiceExpansion
{
    public const int MaxVolleys = 4096; // At most 840 live + 1200 queued + 24 last-volley references.
    public const int MaxFields = 8;
    public const int MaxRifts = 8;
    public const int MaxSwarms = 16;
    public static readonly string[] Keys = "aimCap aimKeep aimPierce aimStep aimTolerance barrierCapacity barrierFullDamage barrierGrant barrierHits barrierReaction beatHaste beatPeriod beatPierce beatPower catalystAll catalystBase catalystBurst catalystCap catalystExtend catalystStep causalBoss causalCap causalRatio causalTransfer causalWindow executeBoss executeGain executeNext executeThreshold fateBlessing fateCount fateMinimum fatePeriod fatePower flightCap flightLife flightPierce flightRate flightShock flightThreshold forgeBlessing forgeBounces forgeNeed forgePierce forgePower formationAura formationBurst formationDiagonal formationStep holeDamage holeDuration holePull holeRadius holeSteer holeVulnerable magnetDamage magnetEarly magnetRange magnetRelock magnetTurn massBoss massNeed massPower massPull massSpread orderLaw orderNoRepeat orderPreview orderSkip phaseChildren phasePierce phasePower phaseUses reactorBlast reactorNeed reactorPower reactorReturn rebirth rebirthCap rebirthGain rebirthStart riftDuration riftPierce riftPower riftShock riftWidth scatterAngle scatterCenterDamage scatterCenterPierce scatterFocus scatterSideBounces scatterSides soulCount soulDamage soulKeep soulMark soulNeed soulTransfer spreeCap spreeElite spreeKeep spreePierce spreeStep sunderArmor sunderBurst sunderCap sunderDuration sunderOther sunderSpread swarmCapacity swarmChance swarmDamage swarmDuplicate swarmQueen throneAura throneBurst thronePower throneTies voidBurst voidBurstThreshold voidCap voidPeriod voidPierce voidSelf voidTeam voidThreshold".Split(' ');
    public static readonly HashSet<string> ConditionKeys = ["sunder", "mass"];
    public static bool IsMythic(GameData data, string id) => data.Types.TryGetValue(id, out var d) && d.Rarity == "mythic";
    public static void ValidateStats(ShotStats q)
    {
        Require(q.VolleyId >= 0 && Enum.IsDefined(q.Auxiliary) && q.AuxiliaryGeneration is >= 0 and <= 1);
        Number(q.AuxiliaryMarkSeconds, 0, 10); Number(q.AuxiliaryDuplicate, 0, 1);
    }
    private static void Require(bool ok) { if (!ok) throw new InvalidDataException("Invalid saved expansion state."); }
    private static void Number(double n, double lo = 0, double hi = 1e9) => Require(MathEx.Finite(n, lo, hi));
    public static void ValidateRuntime(RunState s)
    {
        var x = s.Expansion; Require(x is not null);
        Require(x!.Volleys is not null && x.Volleys.Count <= MaxVolleys && x.Fields is not null && x.Fields.Count <= MaxFields && x.Rifts is not null && x.Rifts.Count <= MaxRifts);
        Require(x.Shield is >= 0 and <= 5 && x.Rebirths is >= 0 and <= 6); Number(x.RebirthPower, 0, .360000001);
        Number(x.ShieldReactionUntil); Number(x.ShieldReactionPower, 0, 1); Number(x.FateClock, 0, 10000);
        Number(x.FateBlessingUntil); Number(x.FateBlessingPower, 0, 1);
        Require(x.FateNumbers is not null && x.FateNumbers.Count <= 2 && x.FateNumbers.All(n => n is >= 1 and <= 6) && x.FateNumbers.Distinct().Count() == x.FateNumbers.Count);
        Require(x.FateMinimumIds is not null && x.FateMinimumIds.Count <= s.Board.Length && x.FateMinimumIds.All(n => n > 0 && n < s.NextId));
        Require(x.OrderBag is not null && x.OrderBag.Count <= s.Deck.Count && x.OrderBag.Distinct().Count() == x.OrderBag.Count && x.OrderBag.All(s.Deck.Contains));
        Require(x.OrderLast is not null && (x.OrderLast == "" || s.Deck.Contains(x.OrderLast)) && x.OrderSkipStage >= -1);
        foreach (var (id,v) in x.Volleys!)
        {
            Require(id > 0 && id < s.NextId && v is not null && v.SourceId > 0 && v.SourceId < s.NextId && v.Kills is >= 0 and <= 100 && v.Cap is >= 0 and <= 100 && v.Elite is >= 0 and <= 10);
            Number(v!.Step, 0, 10); Require(v.FocusCounts is not null && v.FocusCounts.Count <= 128 && v.FocusCounts.All(p => p.Key > 0 && p.Value is >= 0 and <= 54));
        }
        foreach (var f in x.Fields!)
        {
            Require(f is not null && f.SourceId >= 0 && f.Color is not null && f.Color.Length <= 32);
            Number(f!.X, -1000,1000); Number(f.Y,-1000,1000); Number(f.Until); Number(f.LastTick,0,f.Until);
            Number(f.Radius,1,200); Number(f.Dps,0,1e18); Number(f.Pull,0,100); Number(f.Steer,0,5); Number(f.Vulnerable,0,1);
        }
        foreach(var w in x.Rifts!)
        {
            Require(w is not null && w.Wall is >=0 and <=2 && w.Pierces is >=0 and <=8 && w.Color is not null && w.Color.Length<=32);
            Number(w!.Position,-1000,1000); Number(w.Width,1,100); Number(w.Until); Number(w.Power,0,2); Number(w.Shock,0,2);
        }
        foreach(var d in s.Board.OfType<DieState>())
        {
            Require(d.NormalAttacks is >=0 and <=1000000000000 && d.AimStacks is >=0 and <=100 && d.ExecuteStacks is >=0 and <=5 && d.BeatRemainders is >=0 and <=2 && d.LatestVolley>=0);
            Number(d.AimMemory,-Math.PI*2,Math.PI*2); Number(d.MechanicEnergy,0,10000); Number(d.BarrierLastHit,-1); Number(d.VoidClock,0,10000);
            Number(d.ForgeBlessingUntil); Number(d.ForgeBlessingPower,0,1); Number(d.IssuedReload,0,30);
        }
        foreach(var e in s.Enemies)
        {
            Require(e.Conditions is not null && e.Conditions.Count <= ConditionKeys.Count && e.Souls is not null && e.Souls.Count <= 64);
            Number(e.SunderNext); Number(e.MassNext); Number(e.CatalystNext); Number(e.ExtensionNext); Number(e.ExtensionSpent,0,2.000001);
            Require(e.BossMilestones is >=0 and <=7);
            foreach(var (key,v) in e.Conditions!)
            {
                Require(ConditionKeys.Contains(key) && v is not null && v.Stacks is >=0 and <=20 && v.SourceId>=0);
                Number(v!.Until); Number(v.ArmorPower,0,1); Number(v.OtherPower,0,1);
            }
            foreach(var (id,t) in e.Souls!) { Require(id>0 && id<s.NextId); Number(t); }
            if(e.Debt is { } c)
            {
                Require(c.SourceId>=0 && c.Color is not null && c.Color.Length<=32); Number(c.Due); Number(c.Cap,0,1e18);
                Number(c.Recorded,0,c.Cap); Number(c.Ratio,0,2); Number(c.Transfer,0,1);
            }
        }
        foreach(var p in s.Projectiles)
        {
            ValidateStats(p.Stats); Number(p.FlightAge,0,20); Require(p.PhaseCount is >=0 and <=2 && p.FocusTargets is not null && p.FocusTargets.Count<=128 && p.FocusTargets.All(n=>n>0));
        }
        foreach(var q in s.PendingShots) ValidateStats(q.Snapshot.Stats);
        foreach(var h in s.DamageQueue) Require((h.Flags & ~(DamageFlags.Direct|DamageFlags.Causal|DamageFlags.Settled))==0 && h.VolleyId>=0);
    }
}
