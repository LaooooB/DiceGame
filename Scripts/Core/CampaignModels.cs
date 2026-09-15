namespace DiceGame.Core;

// Authoring data never contains engine objects. IDs, not translated names, are the save keys.
public sealed class CampaignDefinition
{
    public int Version { get; set; } = 1;
    public string[] InitialDice { get; set; } = [];
    public string[] InitialMechanics { get; set; } = [];
    public string[] InitialBlueprints { get; set; } = [];
    public Dictionary<string, long> InitialResources { get; set; } = [];
    public ResourceDefinition[] Resources { get; set; } = [];
    public MechanicDefinition[] Mechanics { get; set; } = [];
    public RegionDefinition[] Regions { get; set; } = [];
    public BuildingDefinition[] Buildings { get; set; } = [];
    public TownDefinition Town { get; set; } = new();
    public ProgressRequirement[] NextCycleRequirements { get; set; } = [];
    public double CycleHealthMultiplier { get; set; } = 1.3;
    public double CycleRewardMultiplier { get; set; } = 1.15;
}
public sealed class ResourceDefinition { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class MechanicDefinition { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Description { get; set; } = ""; }
public sealed class ProgressRequirement
{
    public string Region { get; set; } = "";
    public int Clears { get; set; }
    public int DistinctDice { get; set; }
    public int Gears { get; set; }
    public string Building { get; set; } = "";
    public int BuildingLevel { get; set; } = 1;
    public string Mechanic { get; set; } = "";
    public string Dice { get; set; } = "";
}
public sealed class RegionDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#72EAC8";
    public ProgressRequirement[] Requirements { get; set; } = [];
    public RegionPhase[] Phases { get; set; } = [];
    public EnemyProfile Enemies { get; set; } = new();
    public RewardDefinition VictoryReward { get; set; } = new();
    public RewardDefinition FirstClearReward { get; set; } = new();
    public string[] BlueprintPool { get; set; } = [];
    public double WaveSeconds { get; set; } = 18;
    public int UpgradeEvery { get; set; } = 3;
    public int CoinsPerKill { get; set; } = 2;
    public int WoodPerWave { get; set; } = 2;
    public int StonePerBoss { get; set; } = 6;
    public int SuppliesPerBoss { get; set; } = 1;
    public int SuppliesOnReturn { get; set; } = 2;
    public int TotalWaves => Phases.Sum(p => p.Waves);
    public int PhaseIndex(int wave)
    {
        int end = 0;
        for (int i = 0; i < Phases.Length; i++) { end += Phases[i].Waves; if (wave <= end) return i; }
        return Phases.Length - 1;
    }
    public bool IsBossWave(int wave)
    { int end = 0; foreach (var p in Phases) { end += p.Waves; if (wave == end) return true; } return false; }
}
public sealed class RegionPhase
{
    public string Name { get; set; } = "";
    public string BossName { get; set; } = "";
    public int Waves { get; set; } = 12;
    public double BossHealthMultiplier { get; set; } = 1;
}
public sealed class EnemyProfile
{
    public string Formation { get; set; } = "mixed";
    public double HealthMultiplier { get; set; } = 1;
    public double SpeedMultiplier { get; set; } = 1;
    public double DifficultyWaveScale { get; set; } = .42;
    public double ArmoredChance { get; set; } = .14;
    public double VolatileChance { get; set; } = .20;
    public int BossEscorts { get; set; } = 7;
}
public sealed class RewardDefinition
{
    public Dictionary<string, long> Resources { get; set; } = [];
    public string[] Dice { get; set; } = [];
    public string[] Mechanics { get; set; } = [];
    public string[] Blueprints { get; set; } = [];
    public RunBonuses Bonuses { get; set; } = new();
}
public sealed class RunBonuses
{
    public Dictionary<string, double> DiceDamagePercent { get; set; } = [];
    public double DamagePercent { get; set; }
    public double ReloadPercent { get; set; }
    public double StartEnergy { get; set; }
    public double PassiveEnergy { get; set; }
    public void Add(RunBonuses b)
    { foreach(var (id,amount) in b.DiceDamagePercent) DiceDamagePercent[id]=DiceDamagePercent.GetValueOrDefault(id)+amount; DamagePercent += b.DamagePercent; ReloadPercent += b.ReloadPercent; StartEnergy += b.StartEnergy; PassiveEnergy += b.PassiveEnergy; }
}
public sealed class BuildingDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#72EAC8";
    public ProgressRequirement[] Requirements { get; set; } = [];
    public BuildingLevel[] Levels { get; set; } = [];
}
public sealed class BuildingLevel
{
    public Dictionary<string, long> Cost { get; set; } = [];
    public int Work { get; set; } = 4;
    public RewardDefinition Reward { get; set; } = new();
}
public sealed class TownDefinition
{
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 4;
    public int[] WoodTiles { get; set; } = [0, 7, 16, 31];
    public int[] StoneTiles { get; set; } = [3, 12, 24, 28];
    public int ResourcePerHit { get; set; } = 2;
    public int MaxHits { get; set; } = 20;
    public int WorkPerHit { get; set; } = 1;
    public double DispatchSeconds { get; set; } = 10;
}
public sealed class CampaignState
{
    public int Version { get; set; } = 1;
    public int Revision { get; set; }
    public int Cycle { get; set; }
    public Dictionary<string, long> Resources { get; set; } = [];
    public HashSet<string> UnlockedDice { get; set; } = [];
    public HashSet<string> UnlockedMechanics { get; set; } = [];
    public HashSet<string> Blueprints { get; set; } = [];
    public Dictionary<string, BuildingState> Buildings { get; set; } = [];
    public Dictionary<string, RegionRecord> RegionRecords { get; set; } = [];
    public RunBonuses Bonuses { get; set; } = new();
    public long NextRunSerial { get; set; } = 1;
    public long LastSettledSerial { get; set; }
    public string ActiveRunId { get; set; } = "";
    public string SelectedRegion { get; set; } = "";
    public SettlementReceipt? LastResult { get; set; }
    public TownFlight? Flight { get; set; }
    public bool AutoDispatch { get; set; }
    public string RecordKey(string region, int? cycle = null) => $"{cycle ?? Cycle}:{region}";
    public RegionRecord Record(string region) => RegionRecords.GetValueOrDefault(RecordKey(region)) ?? new();
    public int Gears => RegionRecords.Where(p => p.Key.StartsWith(Cycle + ":", StringComparison.Ordinal)).Sum(p => p.Value.ClearedWith.Count);
    public long Balance(string resource) => Resources.GetValueOrDefault(resource);
}
public sealed class RegionRecord
{
    public long Attempts { get; set; }
    public long Clears { get; set; }
    public HashSet<string> ClearedWith { get; set; } = [];
    public int BestWave { get; set; }
    public double BestTime { get; set; }
}
public sealed class BuildingState
{
    public int Tile { get; set; }
    public int Level { get; set; }
    public int Work { get; set; }
    public bool Constructing { get; set; }
}
public sealed class TownFlight
{
    public double X { get; set; } = 400;
    public double Y { get; set; } = 430;
    public double Vx { get; set; }
    public double Vy { get; set; }
    public double Remaining { get; set; }
    public int Hits { get; set; }
    public int LastTile { get; set; } = -1;
    public string Die { get; set; } = "pulse";
}
public sealed class ExpeditionState
{
    public string RunId { get; set; } = "";
    public long Serial { get; set; }
    public string RegionId { get; set; } = "";
    public int Cycle { get; set; }
    public string LeadDice { get; set; } = "";
    public bool Endless { get; set; }
    public bool LegacyRules { get; set; }
    public bool CanContinueEndless { get; set; }
    public bool FinalBossDefeated { get; set; }
    public long StartKills { get; set; }
    public int StartWave { get; set; } = 1;
    public double StartTime { get; set; }
    public uint RewardRng { get; set; }
    public string[] AvailableBlueprints { get; set; } = [];
    public HashSet<int> DefeatedBossWaves { get; set; } = [];
    public HashSet<int> RewardedWaves { get; set; } = [];
    public HashSet<string> FoundBlueprints { get; set; } = [];
    public Dictionary<string, long> Loot { get; set; } = [];
    public RegionDefinition Region { get; set; } = new(); // Pinned snapshot: editing authoring data cannot change a suspended run.
    public RunBonuses Bonuses { get; set; } = new();
    public double HealthMultiplier { get; set; } = 1;
    public double RewardMultiplier { get; set; } = 1;
    public string Outcome { get; set; } = "";
    public string EndReason { get; set; } = "";
    public int LocalWave(int wave) => Endless ? (wave - 1) % Region.TotalWaves + 1 : wave;
    public bool BossWave(int wave) => Region.IsBossWave(LocalWave(wave));
    public void Gain(string resource, long count)
    { if (count > 0) Loot[resource] = checked(Loot.GetValueOrDefault(resource) + count); }
}
public sealed class SettlementReceipt
{
    public string RunId { get; set; } = "";
    public long Serial { get; set; }
    public string RegionName { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool FirstClear { get; set; }
    public bool NewGear { get; set; }
    public int Wave { get; set; }
    public long Kills { get; set; }
    public double Seconds { get; set; }
    public Dictionary<string, long> Resources { get; set; } = [];
    public List<string> NewDice { get; set; } = [];
    public List<string> NewMechanics { get; set; } = [];
    public List<string> NewBlueprints { get; set; } = [];
    public List<string> NewRegions { get; set; } = [];
}
