namespace DiceGame.Core;

/// <summary>Pure progression rules. The app persists a candidate BEFORE publishing it as the live state.</summary>
public sealed class CampaignProgression(CampaignCatalog catalog)
{
    public CampaignCatalog Catalog { get; } = catalog;
    public CampaignState NewState()
    {
        var d = Catalog.Definition;
        return new CampaignState { Resources = new(d.InitialResources), UnlockedDice = d.InitialDice.ToHashSet(), UnlockedMechanics = d.InitialMechanics.ToHashSet(), Blueprints = d.InitialBlueprints.ToHashSet(), SelectedRegion = d.Regions[0].Id };
    }
    public List<string> MissingRequirements(CampaignState state, IEnumerable<ProgressRequirement> requirements)
    {
        List<string> result = [];
        foreach (var q in requirements)
        {
            var r = state.Record(q.Region);
            string name = Catalog.Regions.TryGetValue(q.Region, out var region) ? region.Name : q.Region;
            if (r.Clears < q.Clears) result.Add($"{name}通关 {r.Clears}/{q.Clears} 次");
            if (r.ClearedWith.Count < q.DistinctDice) result.Add($"{name}不同主骰通关 {r.ClearedWith.Count}/{q.DistinctDice}");
            if (state.Gears < q.Gears) result.Add($"区域齿轮 {state.Gears}/{q.Gears}");
            if (q.Building != "" && (state.Buildings.GetValueOrDefault(q.Building)?.Level ?? 0) < q.BuildingLevel) result.Add($"建设{Catalog.Buildings[q.Building].Name} Lv.{q.BuildingLevel}");
            if (q.Mechanic != "" && !state.UnlockedMechanics.Contains(q.Mechanic)) result.Add($"解锁{Catalog.Mechanics[q.Mechanic].Name}");
            if (q.Dice != "" && !state.UnlockedDice.Contains(q.Dice)) result.Add($"解锁{Catalog.Game.Types[q.Dice].Name}");
        }
        return result;
    }
    public bool IsRegionOpen(CampaignState state, string id) => Catalog.Regions.TryGetValue(id, out var r) && MissingRequirements(state, r.Requirements).Count == 0;
    public bool CanUseDeck(CampaignState state, IEnumerable<string> deck) => Catalog.Game.ValidDeck(deck) && deck.All(state.UnlockedDice.Contains);
    public ExpeditionState PrepareRun(CampaignState state, string regionId, IReadOnlyList<string> deck, uint seed)
    {
        if (state.ActiveRunId != "" || state.Flight is not null) throw new InvalidOperationException("请先结束当前远征或城镇派遣。");
        if (!IsRegionOpen(state, regionId) || !CanUseDeck(state, deck)) throw new InvalidOperationException("区域尚未开放，或卡组含有未解锁的骰子。");
        var region = Catalog.Regions[regionId];
        var run = new ExpeditionState { RunId = Guid.NewGuid().ToString("N"), Serial = state.NextRunSerial++, RegionId = regionId, Cycle = state.Cycle, LeadDice = deck[0], Region = CampaignCatalog.Copy(region), Bonuses = CampaignCatalog.Copy(state.Bonuses), RewardRng = seed ^ 0xA511E9B3u, AvailableBlueprints = region.BlueprintPool.Where(id => !state.Blueprints.Contains(id)).ToArray(), CanContinueEndless = state.UnlockedMechanics.Contains("endless"), HealthMultiplier = Math.Min(1000000, Math.Pow(Catalog.Definition.CycleHealthMultiplier, state.Cycle)), RewardMultiplier = Math.Min(10000, Math.Pow(Catalog.Definition.CycleRewardMultiplier, state.Cycle)) };
        state.ActiveRunId = run.RunId; state.SelectedRegion = regionId;
        string key = state.RecordKey(regionId);
        if (!state.RegionRecords.TryGetValue(key, out var record)) state.RegionRecords[key] = record = new();
        record.Attempts++; state.Revision++;
        return run;
    }
    public SettlementReceipt Settle(CampaignState state, RunState run)
    {
        var e = run.Expedition ?? throw new InvalidOperationException("这不是区域远征。");
        if (e.Serial <= state.LastSettledSerial)
        {
            if (state.LastResult?.RunId == e.RunId) return state.LastResult;
            throw new InvalidOperationException("这局已经结算，不能再次领取。");
        }
        if (!run.Over || e.Outcome is not ("victory" or "defeat" or "abandoned") || state.ActiveRunId != e.RunId || e.Serial != state.NextRunSerial - 1)
            throw new InvalidOperationException("当前远征尚未结束，或结算身份不匹配。");
        if (e.Outcome == "victory" && (!e.FinalBossDefeated || e.Endless)) throw new InvalidOperationException("最终头目尚未击败。");
        var before = Catalog.Regions.Keys.Where(id => IsRegionOpen(state, id)).ToHashSet();
        var receipt = new SettlementReceipt { RunId = e.RunId, Serial = e.Serial, RegionName = e.Region.Name, Outcome = e.Outcome, Reason = e.EndReason, Wave = run.Wave, Kills = Math.Max(0, run.Kills - e.StartKills), Seconds = Math.Max(0, run.Time - e.StartTime) };
        var key = state.RecordKey(e.RegionId, e.Cycle);
        if (!state.RegionRecords.TryGetValue(key, out var record)) state.RegionRecords[key] = record = new();
        record.BestWave = Math.Max(record.BestWave, run.Wave);
        Grant(state, new RewardDefinition { Resources = new(e.Loot), Blueprints = e.FoundBlueprints.ToArray() }, receipt);
        // A meaningful expedition always brings dispatch food home; abandoning at t=0 cannot farm food.
        if (run.Kills > e.StartKills || e.DefeatedBossWaves.Count > 0)
            Grant(state, new RewardDefinition { Resources = new() { ["supplies"] = e.Region.SuppliesOnReturn } }, receipt);
        if (e.Outcome == "victory")
        {
            receipt.FirstClear = record.Clears == 0; receipt.NewGear = record.ClearedWith.Add(e.LeadDice); record.Clears++;
            record.BestTime = record.BestTime == 0 ? receipt.Seconds : Math.Min(record.BestTime, receipt.Seconds);
            Grant(state, e.Region.VictoryReward, receipt);
            if (receipt.FirstClear) Grant(state, e.Region.FirstClearReward, receipt);
        }
        receipt.NewRegions = Catalog.Regions.Keys.Where(id => !before.Contains(id) && IsRegionOpen(state, id)).ToList();
        state.ActiveRunId = ""; state.LastSettledSerial = e.Serial; state.LastResult = receipt; state.Revision++;
        return receipt;
    }
    public void Grant(CampaignState state, RewardDefinition reward, SettlementReceipt? receipt = null)
    {
        foreach (var (id, count) in reward.Resources)
        {
            state.Resources[id] = checked(state.Balance(id) + count);
            if (receipt is not null) receipt.Resources[id] = checked(receipt.Resources.GetValueOrDefault(id) + count);
        }
        foreach (var id in reward.Dice) if (state.UnlockedDice.Add(id)) receipt?.NewDice.Add(id);
        foreach (var id in reward.Mechanics) if (state.UnlockedMechanics.Add(id)) receipt?.NewMechanics.Add(id);
        foreach (var id in reward.Blueprints) if (state.Blueprints.Add(id)) receipt?.NewBlueprints.Add(id);
        state.Bonuses.Add(reward.Bonuses);
    }
    public List<string> BuildingBlockers(CampaignState state, string id, int tile = -1)
    {
        if (!Catalog.Buildings.TryGetValue(id, out var definition)) return ["没有这个建筑配置。"];
        var reasons = MissingRequirements(state, definition.Requirements);
        if (!state.Blueprints.Contains(id)) reasons.Add("尚未找到蓝图。");
        if (state.ActiveRunId != "") reasons.Add("请先结束远征。");
        if (state.Flight is not null) reasons.Add("请等待本次派遣结束。");
        var b = state.Buildings.GetValueOrDefault(id);
        if (b?.Constructing == true) reasons.Add("正在建设：弹射命中工地增加进度。");
        int level = b?.Level ?? 0;
        if (level >= definition.Levels.Length) { reasons.Add("已达到最高等级。"); return reasons; }
        if (b is null && tile >= 0 && (tile >= Catalog.Definition.Town.Columns * Catalog.Definition.Town.Rows || Catalog.IsResourceTile(tile) || state.Buildings.Values.Any(v => v.Tile == tile))) reasons.Add("该地块无法建设。");
        foreach (var (resource, amount) in definition.Levels[level].Cost)
            if (state.Balance(resource) < amount) reasons.Add($"{Catalog.Resources[resource].Name} {state.Balance(resource)}/{amount}");
        return reasons;
    }
    public void StartConstruction(CampaignState state, string id, int tile)
    {
        var reasons = BuildingBlockers(state, id, tile);
        if (tile < 0) reasons.Add("请先选择空地。");
        if (reasons.Count > 0) throw new InvalidOperationException(string.Join("；", reasons));
        if (!state.Buildings.TryGetValue(id, out var b)) state.Buildings[id] = b = new BuildingState { Tile = tile };
        var level = Catalog.Buildings[id].Levels[b.Level];
        foreach (var (resource, cost) in level.Cost) state.Resources[resource] = state.Balance(resource) - cost;
        b.Work = 0; b.Constructing = true;
        CompleteConstruction(state, id); state.Revision++;
    }
    public bool CompleteConstruction(CampaignState state, string id)
    {
        if (!state.Buildings.TryGetValue(id, out var b) || !b.Constructing || !Catalog.Buildings.TryGetValue(id, out var definition)) return false;
        var level = definition.Levels[b.Level];
        if (b.Work < level.Work) return false;
        b.Level++; b.Constructing = false; b.Work = 0; Grant(state, level.Reward); state.Revision++; return true;
    }
    public void Relocate(CampaignState state, string id, int tile)
    {
        if (state.Flight is not null || state.ActiveRunId != "" || !state.Buildings.TryGetValue(id, out var b) || tile < 0 || tile >= Catalog.Definition.Town.Columns * Catalog.Definition.Town.Rows || Catalog.IsResourceTile(tile) || state.Buildings.Values.Any(v => v.Tile == tile))
            throw new InvalidOperationException("这块空地不能用于搬迁，或当前有未结束的派遣。");
        b.Tile = tile; state.Revision++;
    }
    public bool CanStartNextCycle(CampaignState state) => state.Cycle < 999 && state.ActiveRunId == "" && state.Flight is null && MissingRequirements(state, Catalog.Definition.NextCycleRequirements).Count == 0;
    public void StartNextCycle(CampaignState state)
    {
        if (!CanStartNextCycle(state)) throw new InvalidOperationException("尚未满足深潜条件：" + string.Join("；",MissingRequirements(state,Catalog.Definition.NextCycleRequirements)));
        state.Cycle++; state.SelectedRegion = Catalog.Definition.Regions[0].Id; state.Revision++;
    }
}
