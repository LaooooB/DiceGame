using DiceGame.Core;

namespace DiceGame.App;

public sealed partial class GameApp
{
    public CampaignCatalog? Catalog { get; }
    public CampaignProgression? Progression { get; }
    public CampaignState? Campaign { get; private set; }
    public DesktopPreferences Preferences { get; private set; } = new();
    public bool NativeUi { get; set; }
    public string LoadProblem { get; private set; } = "";
    public string SettingsReturn { get; private set; } = "town";
    public bool SettlementSaved => Sim?.State.Expedition is { } e && Campaign?.LastResult?.RunId == e.RunId && Campaign.LastSettledSerial >= e.Serial;
    public bool StorageFailed => _storageFailed;
    private bool _hadSave, _migratedBattle;
    private double _townSaveClock;
    private TownSimulation? _townSimulation;
    public TownSimulation TownSimulation => _townSimulation ??= new TownSimulation(Progression!);
    private void InitializeCampaign()
    {
        if (Catalog is null) return;
        if (Campaign is null)
        {
            Campaign = Progression!.NewState();
            if (_hadSave)
            {
                Campaign.UnlockedDice.UnionWith(Deck);
                if (ResumeData is not null)
                {
                    Campaign.UnlockedDice.UnionWith(ResumeData.Deck);
                    var e = Progression.PrepareRun(Campaign, Campaign.SelectedRegion, ResumeData.Deck, ResumeData.Seed);
                    e.Endless = true; e.LegacyRules = true; e.StartKills = ResumeData.Kills; e.StartTime = ResumeData.Time; e.StartWave = ResumeData.Wave;
                    e.Bonuses = new(); ResumeData.Expedition = e;
                }
            }
            else Deck = Catalog.Definition.InitialDice.ToList();
        }
        try
        {
            if (_migratedBattle)
            {
                Campaign.UnlockedDice.UnionWith(Data.DefaultDeck);
                // Old dice-only workshops are now per-die training; grant only the new delta once during envelope migration.
                foreach(var (id,building) in Campaign.Buildings)
                    if(Catalog.Buildings.TryGetValue(id,out var definition))
                        foreach(var level in definition.Levels.Take(building.Level))
                            foreach(var (type,value) in level.Reward.Bonuses.DiceDamagePercent)
                                Campaign.Bonuses.DiceDamagePercent[type]=Campaign.Bonuses.DiceDamagePercent.GetValueOrDefault(type)+value;
            }
            Catalog.ValidateState(Campaign);
            if (!Progression!.CanUseDeck(Campaign, Deck))
            {
                Deck = Deck.Where(id => Data.Types.ContainsKey(id) && Campaign.UnlockedDice.Contains(id)).Distinct().Take(Data.Game.Rules.MaxDeck).ToList();
                foreach (var type in Data.Dice.Select(d => d.Id).Where(Campaign.UnlockedDice.Contains))
                    if (Deck.Count < Data.Game.Rules.MinDeck && !Deck.Contains(type)) Deck.Add(type);
                if (!Progression.CanUseDeck(Campaign, Deck)) throw new InvalidDataException($"存档中不足 {Data.Game.Rules.MinDeck} 种可用骰子；请检查内容目录或迁移，原存档不会被覆盖。");
            }
            if (ResumeData?.Expedition is { } active && active.Serial > Campaign.LastSettledSerial && active.RunId != Campaign.ActiveRunId)
                throw new InvalidDataException("远征与永久进度不属于同一次存档。");
            if (ResumeData is null && Campaign.ActiveRunId != "") throw new InvalidDataException("永久进度记载有未结束远征，但战斗快照缺失。");
        }
        catch (Exception ex) { LoadProblem = ex.Message; }
        Scene = "town";
        if (LoadProblem == "" && ResumeData?.Over == true) { RestoreRun(ResumeData); TrySettle(); }
    }
    private SaveEnvelope Envelope(CampaignState state, RunState? run, List<string>? deck = null) => new()
    { Version = Data.Game.Rules.EnableDiceSkills ? SaveCodec.CurrentVersion : 2, Settings = Settings, Meta = Meta, Deck = deck ?? Deck, Run = run, Campaign = state, Preferences = Preferences };
    private bool CommitCampaign(CampaignState state, RunState? run, List<string>? deck = null)
    {
        if (LoadProblem != "") { Notify("为保护原存档，写入已停止：" + LoadProblem, 4); return false; }
        try
        {
            Catalog!.ValidateState(state);
            string json = SaveCodec.Encode(Envelope(state, run, deck)); SaveCodec.Decode(Data, json);
            _storage.Write(json); Campaign = state; ResumeData = run;
            if (deck is not null) Deck = deck;
            _storageFailed = false; return true;
        }
        catch (Exception ex) { _storageFailed = true; Notify("保存失败，操作没有确认：" + ex.Message, 4); return false; }
    }
    private RunState? Snapshot() => Sim?.ExportSave() ?? ResumeData;
    private void SaveCampaignSnapshot()
    { if (Campaign is not null) { RecordRun(); CommitCampaign(Campaign, Snapshot()); } }
    public bool StartExpedition(uint? seed = null)
    {
        if (Catalog is null || Campaign is null) return false;
        try
        {
            uint runSeed = seed ?? _storage.NewSeed();
            var candidate = CampaignCatalog.Copy(Campaign);
            var expedition = Progression!.PrepareRun(candidate, candidate.SelectedRegion, Deck, runSeed);
            var simulation = new Simulation(Data, Deck, runSeed, expedition);
            if (!CommitCampaign(candidate, simulation.ExportSave())) return false;
            Effects.Clear(); Sim = simulation; Scene = "play"; Pointer = null; AimAngle = -Math.PI / 2; Accumulator = 0; _saveClock = 0;
            ConsumeEvents(); return true;
        }
        catch (Exception ex) { Notify(ex.Message, 3); return false; }
    }
    public bool TrySettle()
    {
        if (Campaign is null || Sim?.State.Expedition is null || !Sim.State.Over) return false;
        if (SettlementSaved) return true;
        try
        {
            var candidate = CampaignCatalog.Copy(Campaign);
            Progression!.Settle(candidate, Sim.State); RecordRun();
            return CommitCampaign(candidate, Sim.ExportSave());
        }
        catch (Exception ex) { Notify("结算未确认：" + ex.Message, 4); return false; }
    }
    public bool ReturnToTown()
    {
        if (Campaign is null) return false;
        if (Sim?.State.Over == true && !TrySettle()) return false;
        RunState? pending = Sim?.State.Over == true ? null : Snapshot();
        if (!CommitCampaign(Campaign, pending)) return false;
        Sim = null; Effects.Clear(); Pointer = null; Scene = "town"; Accumulator = 0; return true;
    }
    public bool AbandonExpedition()
    {
        if (Campaign?.ActiveRunId == "") return false;
        if (Sim is null && ResumeData is not null) RestoreRun(ResumeData);
        if (Sim?.State.Expedition is null) return false;
        Sim.EndExpedition("abandoned", "主动撤回，保留已获得的资源与蓝图");
        Scene = "settlement"; Pointer = null; ConsumeEvents(); return TrySettle();
    }
    public bool ContinueEndless()
    {
        if (!SettlementSaved || Campaign is null || Sim?.State.Expedition is not { Outcome: "victory", CanContinueEndless: true } previous) return false;
        var candidate = CampaignCatalog.Copy(Campaign);
        var next = CampaignCatalog.Copy(previous);
        next.RunId = Guid.NewGuid().ToString("N"); next.Serial = candidate.NextRunSerial++; next.Endless = true; next.Outcome = ""; next.EndReason = ""; next.FinalBossDefeated = false;
        next.StartTime = Sim.State.Time; next.StartKills = Sim.State.Kills; next.StartWave = Sim.State.Wave + 1;
        next.Loot.Clear(); next.FoundBlueprints.Clear(); next.DefeatedBossWaves.Clear(); next.RewardedWaves.Clear();
        next.AvailableBlueprints = next.Region.BlueprintPool.Where(id => !candidate.Blueprints.Contains(id)).ToArray();
        candidate.ActiveRunId = next.RunId;
        var extended = Simulation.Restore(Data, Sim.ExportSave()); extended.ContinueAsEndless(next);
        if (!CommitCampaign(candidate, extended.ExportSave())) return false;
        Sim = extended; Scene = DecisionScene; Accumulator = 0; Pointer = null; ConsumeEvents(); return true;
    }
    public bool MutateTown(Action<CampaignState> mutation)
    {
        if (Campaign is null) return false;
        try
        {
            var candidate = CampaignCatalog.Copy(Campaign); mutation(candidate);
            return CommitCampaign(candidate, Snapshot());
        }
        catch (Exception ex) { Notify(ex.Message, 3); return false; }
    }
    public void SelectRegion(string id)
    {
        if (Campaign is null || !Catalog!.Regions.ContainsKey(id)) return;
        if (!Progression!.IsRegionOpen(Campaign, id)) { Notify("先完成区域面板中列出的条件。", 3); return; }
        if (MutateTown(s => s.SelectedRegion = id)) { EditingDeck = Deck.ToList(); Scene = "deck"; }
    }
    public bool SaveDeck()
    {
        if (Campaign is null || !Progression!.CanUseDeck(Campaign, EditingDeck)) { Notify($"卡组必须携带 {Data.Game.Rules.MaxDeck} 种已解锁、互不重复的骰子。"); return false; }
        if (!CommitCampaign(Campaign, Snapshot(), EditingDeck.ToList())) return false;
        Scene = "regions"; return true;
    }
    public void SetLeadDice(string id)
    { if (EditingDeck.Remove(id)) EditingDeck.Insert(0, id); }
    public bool SetPreferences(DesktopPreferences preferences)
    {
        preferences.Validate(); var previous = Preferences; Preferences = CampaignCatalog.Copy(preferences);
        if (Campaign is null) { Save(); return true; }
        if (CommitCampaign(Campaign, Snapshot())) return true; Preferences = previous; return false;
    }
    public void OpenSettings()
    { SettingsReturn = Scene; Pointer = null; Scene = "settings"; Save(); }
    public void CloseSettings() { Scene = SettingsReturn; Pointer = null; Accumulator = 0; }
    private void TickCampaign(double dt)
    {
        if (Campaign is null) return;
        // Terminal state is authoritative even if the presentation event budget is exhausted.
        if (Sim?.State.Over == true && (Scene is "play" or "upgrade" or "diceSkill")) { Scene = "settlement"; CancelPointer(); TrySettle(); }
        if (Scene != "town") return;
        bool hadFlight = Campaign.Flight is not null;
        TownSimulation.Step(Campaign, dt);
        _townSaveClock += dt;
        if (hadFlight && (Campaign.Flight is null || _townSaveClock >= 2)) { _townSaveClock = 0; Save(); }
        if (hadFlight && Campaign.Flight is null && Campaign.AutoDispatch && Campaign.UnlockedMechanics.Contains("auto_dispatch") && Campaign.Balance("supplies") > 0)
            MutateTown(s => TownSimulation.Launch(s, TownSimulation.AutoAngle(s), Deck[0]));
    }
    private bool CampaignAction(string id)
    {
        switch (id)
        {
            case "start": case "confirmStart": StartExpedition(); return true;
            case "restart": case "new": case "cancelNew": case "regions": if (ReturnToTown()) Scene = "regions"; return true;
            case "home": case "town": ReturnToTown(); return true;
            case "retrySettlement": TrySettle(); return true;
            case "endless": ContinueEndless(); return true;
            case "settings": OpenSettings(); return true;
            case "closeSettings": CloseSettings(); return true;
            case "abandon": AbandonExpedition(); return true;
            case "editDeck": EditingDeck = Deck.ToList(); Scene = "deck"; return true;
            case "deckBack": Scene = "regions"; return true;
            case "deckSave": SaveDeck(); return true;
            case "help": HelpReturn = Scene; Scene = "help"; Save(); return true;
            case "nextCycle": if (MutateTown(s => Progression!.StartNextCycle(s))) Scene = "regions"; return true;
        }
        if (id.StartsWith("deck:", StringComparison.Ordinal))
        {
            string type = id[5..];
            if (Campaign is null || !Campaign.UnlockedDice.Contains(type) || !Data.Types.ContainsKey(type)) { Notify("先在城镇解锁这颗骰子。"); return true; }
            if (!EditingDeck.Remove(type))
            { if (EditingDeck.Count < Data.Game.Rules.MaxDeck) EditingDeck.Add(type); else Notify($"卡组必须为 {Data.Game.Rules.MaxDeck} 种；请先移除一种再替换。"); }
            return true;
        }
        return false;
    }
}
