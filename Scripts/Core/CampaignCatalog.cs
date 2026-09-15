using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiceGame.Core;

public sealed class CampaignCatalog
{
    public CampaignDefinition Definition { get; }
    public IReadOnlyDictionary<string, RegionDefinition> Regions { get; }
    public IReadOnlyDictionary<string, BuildingDefinition> Buildings { get; }
    public IReadOnlyDictionary<string, MechanicDefinition> Mechanics { get; }
    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }
    public GameData Game { get; }
    public CampaignCatalog(GameData game, string json)
    {
        Game = game;
        Definition = JsonSerializer.Deserialize<CampaignDefinition>(json, GameData.JsonOptions) ?? throw new InvalidDataException("campaign.json is empty.");
        Regions = Definition.Regions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Buildings = Definition.Buildings.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Mechanics = Definition.Mechanics.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Resources = Definition.Resources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Validate();
    }
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, GameData.JsonOptions), GameData.JsonOptions)!;
    private static void Need(bool yes, string message) { if (!yes) throw new InvalidDataException("Campaign: " + message); }
    private void Validate()
    {
        Need(Definition.Version == 1 && Regions.Count > 0, "unsupported version / missing regions");
        foreach (string id in Regions.Keys.Concat(Buildings.Keys).Concat(Mechanics.Keys).Concat(Resources.Keys))
            Need(Regex.IsMatch(id, "^[a-z][a-z0-9_]{0,63}$"), "invalid id: " + id);
        Need(Game.ValidDeck(Definition.InitialDice), "invalid initial dice");
        Need(Definition.InitialMechanics.All(Mechanics.ContainsKey), "unknown initial mechanic");
        Need(Definition.InitialBlueprints.All(Buildings.ContainsKey), "unknown initial blueprint");
        foreach (string id in new[] { "coins", "wood", "stone", "supplies" }) Need(Resources.ContainsKey(id), "missing resource: " + id);
        ValidateResources(Definition.InitialResources);
        Need(MathEx.Finite(Definition.CycleHealthMultiplier, 1, 10) && MathEx.Finite(Definition.CycleRewardMultiplier, 1, 10), "invalid cycle multipliers");
        ValidateRequirements(Definition.NextCycleRequirements);
        foreach (var r in Regions.Values)
        {
            ValidateRegion(r);
            ValidateRequirements(r.Requirements);
            Need(r.Requirements.All(q => q.Region != r.Id), "region depends on its own clear: " + r.Id);
            ValidateReward(r.VictoryReward); ValidateReward(r.FirstClearReward);
            Need(r.BlueprintPool.All(Buildings.ContainsKey), "unknown region blueprint: " + r.Id);
        }
        foreach (var b in Buildings.Values)
        {
            Need(b.Levels.Length is > 0 and <= 100, "building must have levels: " + b.Id);
            ValidateRequirements(b.Requirements);
            Need(b.Requirements.All(q => q.Building != b.Id), "self-dependent building: " + b.Id);
            foreach (var level in b.Levels)
            { ValidateResources(level.Cost); Need(level.Work is >= 0 and <= 10000, "invalid construction work"); ValidateReward(level.Reward); }
        }
        var t = Definition.Town;
        Need(t.Columns is >= 2 and <= 12 && t.Rows is >= 2 and <= 8, "invalid town size");
        var tiles = t.WoodTiles.Concat(t.StoneTiles).ToArray();
        Need(tiles.All(i => i >= 0 && i < t.Columns * t.Rows) && tiles.Distinct().Count() == tiles.Length, "overlapping resource tiles");
        Need(t.ResourcePerHit is > 0 and <= 100 && t.WorkPerHit is > 0 and <= 100 && t.MaxHits is > 0 and <= 100, "invalid town hit limits");
        Need(MathEx.Finite(t.DispatchSeconds, 1, 60), "invalid dispatch duration");
        Need(t.Columns * t.Rows - tiles.Length >= Buildings.Count, "not enough town plots for the building catalog");
        Need(Definition.Regions[0].Requirements.Length == 0, "first region must be accessible");
        ValidateReachability();
    }
    public static void ValidateRegion(RegionDefinition r)
    {
        Need(r.Phases.Length is > 0 and <= 8 && r.Phases.All(p => p.Waves is >= 2 and <= 100 && MathEx.Finite(p.BossHealthMultiplier, .1, 100)), "invalid phases: " + r.Id);
        Need(MathEx.Finite(r.WaveSeconds, 3, 60) && r.UpgradeEvery is >= 1 and <= 100, "invalid cadence: " + r.Id);
        var e = r.Enemies;
        Need(MathEx.Finite(e.HealthMultiplier, .1, 100) && MathEx.Finite(e.SpeedMultiplier, .1, 3) && MathEx.Finite(e.DifficultyWaveScale, .01, 3), "invalid enemy multipliers");
        Need(MathEx.Finite(e.ArmoredChance, 0, 1) && MathEx.Finite(e.VolatileChance, 0, 1) && e.ArmoredChance + e.VolatileChance <= 1, "invalid enemy probabilities");
        Need(new[] { "mixed", "lanes", "stagger", "mirror", "center", "flanks", "checker", "dense" }.Contains(e.Formation), "unknown formation");
        Need(e.BossEscorts is >= 0 and <= 28, "invalid escort count");
        foreach (int amount in new[] { r.CoinsPerKill, r.WoodPerWave, r.StonePerBoss, r.SuppliesPerBoss, r.SuppliesOnReturn })
            Need(amount is >= 0 and <= 10000, "invalid reward amount");
    }
    private void ValidateRequirements(IEnumerable<ProgressRequirement> list)
    {
        foreach (var q in list)
        {
            Need(q.Clears >= 0 && q.DistinctDice >= 0 && q.DistinctDice <= Game.Dice.Length && q.Gears >= 0, "impossible requirement count");
            Need(q.Region == "" || Regions.ContainsKey(q.Region), "unknown required region: " + q.Region);
            Need((q.Clears == 0 && q.DistinctDice == 0) || q.Region != "", "clear requirement has no region");
            Need(q.Building == "" || Buildings.ContainsKey(q.Building) && q.BuildingLevel > 0 && q.BuildingLevel <= Buildings[q.Building].Levels.Length, "unknown building/level");
            Need(q.Mechanic == "" || Mechanics.ContainsKey(q.Mechanic), "unknown required mechanic");
            Need(q.Dice == "" || Game.Types.ContainsKey(q.Dice), "unknown required dice");
        }
    }
    private void ValidateResources(Dictionary<string, long> values)
    { foreach (var (id, amount) in values) Need(Resources.ContainsKey(id) && amount is >= 0 and <= 1_000_000_000, "invalid resource: " + id); }
    private void ValidateReward(RewardDefinition reward)
    {
        ValidateResources(reward.Resources);
        Need(reward.Dice.All(Game.Types.ContainsKey) && reward.Mechanics.All(Mechanics.ContainsKey) && reward.Blueprints.All(Buildings.ContainsKey), "reward points at missing content");
        ValidateBonuses(reward.Bonuses);
    }
    public static void ValidateBonuses(RunBonuses b)
    {
        Need(MathEx.Finite(b.DamagePercent, 0, 1000) && MathEx.Finite(b.ReloadPercent, 0, 1000) && MathEx.Finite(b.StartEnergy, 0, 100000) && MathEx.Finite(b.PassiveEnergy, 0, 1000), "invalid persistent bonuses");
    }
    // A fixed-point unlock simulation catches circular/unreachable authoring rules before a player saves.
    private void ValidateReachability()
    {
        var service = new CampaignProgression(this);
        var state = service.NewState();
        int prior = -1;
        for (int pass = 0; pass < Regions.Count + Buildings.Count + Game.Dice.Length + 10; pass++)
        {
            foreach (var r in Regions.Values.Where(r => service.IsRegionOpen(state, r.Id)))
            {
                state.RegionRecords[state.RecordKey(r.Id)] = new RegionRecord { Clears = 100000, ClearedWith = state.UnlockedDice.ToHashSet() };
                service.Grant(state, r.FirstClearReward); service.Grant(state, r.VictoryReward);
                state.Blueprints.UnionWith(r.BlueprintPool);
            }
            foreach (var b in Buildings.Values.Where(b => state.Blueprints.Contains(b.Id) && service.MissingRequirements(state, b.Requirements).Count == 0))
            {
                if (state.Buildings.ContainsKey(b.Id)) continue;
                state.Buildings[b.Id] = new BuildingState { Level = b.Levels.Length };
                foreach (var level in b.Levels) service.Grant(state, level.Reward);
            }
            int score = state.RegionRecords.Count + state.Buildings.Count + state.UnlockedDice.Count + state.UnlockedMechanics.Count;
            if (score == prior) break; prior = score;
        }
        Need(Regions.Keys.All(id => state.Record(id).Clears > 0), "an area cannot be unlocked with the available dice/blueprints");
        Need(Buildings.Keys.All(state.Buildings.ContainsKey), "a blueprint/building is unreachable");
    }
    public void ValidateState(CampaignState s)
    {
        Need(s.Version == 1 && s.Cycle is >= 0 and <= 999 && s.Resources.Count <= 100 && s.RegionRecords.Count <= 100000, "invalid permanent state");
        Need(s.Resources.Values.All(v => v is >= 0 and <= 1_000_000_000_000), "invalid wallet");
        Need(s.NextRunSerial > 0 && s.LastSettledSerial >= 0 && s.LastSettledSerial < s.NextRunSerial, "invalid settlement journal");
        Need(s.UnlockedDice.Count > 0 && s.UnlockedDice.Count <= 10000 && s.UnlockedMechanics.Count <= 10000 && s.Blueprints.Count <= 10000, "invalid unlock collection");
        var occupied = new HashSet<int>();
        foreach (var (id, b) in s.Buildings)
        {
            Need(occupied.Add(b.Tile) && b.Tile >= 0 && b.Tile < Definition.Town.Columns * Definition.Town.Rows && !IsResourceTile(b.Tile), "invalid/overlapping building plot");
            Need(b.Level >= 0 && b.Work >= 0, "invalid construction state");
            if (Buildings.TryGetValue(id, out var d)) Need(b.Level <= d.Levels.Length && (!b.Constructing || b.Level < d.Levels.Length), "invalid building level");
        }
        foreach (var r in s.RegionRecords.Values)
            Need(r.Clears >= 0 && r.Attempts >= 0 && r.BestWave >= 0 && r.ClearedWith.Count <= 10000 && MathEx.Finite(r.BestTime, 0, 1e9), "invalid region record");
        ValidateBonuses(s.Bonuses);
        if (s.Flight is { } f)
            Need(MathEx.Finite(f.X, 0, 800) && MathEx.Finite(f.Y, 0, 480) && MathEx.Finite(f.Vx, -600, 600) && MathEx.Finite(f.Vy, -600, 600) && MathEx.Finite(f.Remaining, 0, 60) && f.Hits >= 0 && f.Hits <= 100 && Game.Types.ContainsKey(f.Die), "invalid town dispatch");
    }
    public bool IsResourceTile(int tile) => Definition.Town.WoodTiles.Contains(tile) || Definition.Town.StoneTiles.Contains(tile);
}
