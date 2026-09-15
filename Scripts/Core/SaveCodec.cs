using System.Text.Json;

namespace DiceGame.Core;

public sealed class GameSettings { public bool Sound { get; set; }=true; public bool Music { get; set; } public bool ReduceMotion { get; set; } }
public sealed class MetaState { public double BestWave { get; set; } public double BestScore { get; set; } }
public sealed class SaveEnvelope
{
    public int Version { get; set; }=1;
    [System.Text.Json.Serialization.JsonIgnore] public int MigratedFromVersion { get; set; } public GameSettings Settings { get; set; }=new(); public MetaState Meta { get; set; }=new();
    public List<string> Deck { get; set; }=[]; public RunState? Run { get; set; }
    public CampaignState? Campaign { get; set; }
    public DesktopPreferences Preferences { get; set; }=new();
}
public static class SaveCodec
{
    public const int CurrentVersion=3;
    public const int MaxBytes=8_000_000;
    public static SaveEnvelope Decode(GameData data,string json)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(json)>MaxBytes) throw new InvalidDataException("Save is too large.");
        var saved=JsonSerializer.Deserialize<SaveEnvelope>(json,GameData.JsonOptions) ?? throw new InvalidDataException("Empty save.");
        if (saved.Version is < 1 or > 3 || saved.Settings is null || saved.Meta is null || saved.Deck is null)
            throw new InvalidDataException("Invalid save header.");
        if (data.Game.Rules.EnableDiceSkills && saved.Version < 3) MigrateBattle(data, saved);
        if (!data.ValidDeck(saved.Deck)) throw new InvalidDataException("The saved deck does not meet the current deck-size rule.");
        Check(saved.Meta.BestWave,0,1e18);Check(saved.Meta.BestScore,0,1e18);
        if(saved.Preferences is null) throw new InvalidDataException("Missing desktop preferences.");
        saved.Preferences.Validate();
        if(saved.Version>=2 && saved.Campaign is null && saved.Run?.Expedition is not null) throw new InvalidDataException("Missing campaign state.");
        if(saved.Run is not null) ValidateRun(data,saved.Run);
        return saved;
    }
    private static bool ValidLegacyDeck(GameData data, IEnumerable<string>? deck)
    {
        if (deck is null) return false;
        var a=deck.ToArray(); return a.Length is >=1 and <=6 && a.Distinct().Count()==a.Length && a.All(data.Types.ContainsKey);
    }
    private static List<string> CompleteDeck(GameData data, IEnumerable<string> previous)
    {
        var result=previous.Concat(data.DefaultDeck).Distinct().Take(data.Game.Rules.MaxDeck).ToList();
        Require(data.ValidDeck(result),"Cannot migrate to a complete six-die deck."); return result;
    }
    private static void MigrateBattle(GameData data, SaveEnvelope saved)
    {
        Require(ValidLegacyDeck(data,saved.Deck),"Invalid legacy deck.");
        if (saved.Version==2) Require(saved.Campaign is not null,"Missing legacy campaign state.");
        if (saved.Run is not null) ValidateRun(data,saved.Run,true);
        saved.MigratedFromVersion=saved.Version;
        saved.Deck=CompleteDeck(data,saved.Deck);
        if (saved.Campaign is not null) saved.Campaign.UnlockedDice.UnionWith(data.DefaultDeck);
        if (saved.Run is { } run)
        {
            Require(run.PendingSkills.Count==0 && run.SkillSets.Count==0 && run.Board.All(d=>d is null || d.Tier3=="" && d.Tier6==""),"Legacy save contains incompatible skill data.");
            run.Deck=CompleteDeck(data,run.Deck);
            var board=run.Board; Array.Resize(ref board,data.Game.Board.Slots); run.Board=board;
            run.SkillSets=run.Deck.ToDictionary(id=>id,id=>CampaignCatalog.Copy(data.Skills[id]));
            run.Schema=data.Game.Schema;
            // Existing dice/IDs and shots stay intact. Old high-level dice receive choices, never free merge volleys.
            if (!run.Over) foreach(var die in run.Board.Where(d=>d is {Pips:>=3}).Cast<DieState>())
            {
                run.PendingSkills.Add(new DiceSkillChoice{ChoiceId=run.NextId++,DieId=die.Id,DiceType=die.Type,ResultPips=die.Pips,Tier=3});
                if(die.Pips==6) run.PendingSkills.Add(new DiceSkillChoice{ChoiceId=run.NextId++,DieId=die.Id,DiceType=die.Type,ResultPips=die.Pips,Tier=6});
            }
            if(saved.Campaign is not null) saved.Campaign.UnlockedDice.UnionWith(run.Deck);
        }
        saved.Version=CurrentVersion;
    }
    private static void ValidateSkills(GameData data,RunState s)
    {
        Require(s.SkillSets is not null && s.PendingSkills is not null && s.SkillSets.Count==s.Deck.Count);
        foreach(string type in s.Deck)
        {Require(s.SkillSets!.TryGetValue(type,out var set),"Missing pinned skills."); DiceSkillCatalog.ValidateSet(type,set);}
        Require(s.PendingSkills!.Count<=s.Board.Length*2 && (!s.Over || s.PendingSkills.Count==0));
        var dice=s.Board.Where(d=>d is not null).Cast<DieState>().ToArray();
        Require(dice.Select(d=>d.Id).Distinct().Count()==dice.Length,"Duplicate board die identity.");
        Require(s.PendingSkills.Select(q=>q.ChoiceId).Distinct().Count()==s.PendingSkills.Count);
        foreach(var q in s.PendingSkills)
        {
            Require(q is not null && q.ChoiceId>0 && q.ChoiceId<s.NextId && q.Tier is 3 or 6);
            var d=dice.SingleOrDefault(d=>d.Id==q.DieId);
            Require(d is not null && d.Type==q.DiceType && d.Pips==q.ResultPips && d.Pips>=q.Tier,"Skill choice is not bound to its result die.");
            Check(q.SurgeAngle,-Math.PI,0); Check(q.SurgeMultiplier,1,100);
            var group=s.PendingSkills.Where(x=>x.DieId==q.DieId).ToArray();
            Require(group.Select(x=>x.Tier).Distinct().Count()==group.Length && group.Select(x=>x.Tier).SequenceEqual(group.Select(x=>x.Tier).Order()));
            Require(!q.FinishMerge || group[^1]==q);
            Require(q.Tier==3?d!.Tier3=="":d!.Tier6=="" && (d.Tier3 is "A" or "B" || group.Any(x=>x.Tier==3)));
        }
        foreach(var d in dice)
        {
            Require(d.Tier3 is "" or "A" or "B" && d.Tier6 is "" or "C" or "D");
            Require(d.Pips>=3 || d.Tier3==""); Require(d.Pips==6 || d.Tier6=="");
            if(!s.Over)
            {
                Require(d.Pips<3 || d.Tier3!="" || s.PendingSkills.Any(q=>q.DieId==d.Id&&q.Tier==3),"Missing level-3 branch.");
                Require(d.Pips<6 || d.Tier6!="" || s.PendingSkills.Any(q=>q.DieId==d.Id&&q.Tier==6),"Missing level-6 branch.");
            }
        }
    }
    public static string Encode(SaveEnvelope saved)=>JsonSerializer.Serialize(saved,GameData.JsonOptions);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,string message="Invalid saved state.") {if(!condition) throw new InvalidDataException(message);}
    private static void Check(double v,double min=0,double max=double.PositiveInfinity)=>Require(MathEx.Finite(v,min,max));
    private static void ValidateStats(ShotStats? s)
    {
        Require(s is not null);var q=s!;
        DiceContent.CheckModifiers(q.Traits, runtime:true);
        Require(q.AttackType is not null && q.AttackType.Length<=80);
        Require(new[]{"pulse","blast","arc","frost","split","bank"}.Contains(q.Effect));
        foreach(double v in new[]{q.Damage,q.Count,q.Reload,q.Bounces,q.BlastRadius,q.SplashFactor,q.ChainCount,q.ChainRange,q.ChainFactor,
            q.SlowFactor,q.SlowSeconds,q.ChildCount,q.ChildFactor,q.WallBoost,q.MaxBoost,q.Volley,q.ChildLifeBonus,q.SlowRadius,
            q.BossDamageMultiplier,q.ChilledDamageMultiplier,q.KillExplosionFactor,q.ArcReturnFactor,q.ShatterRadius,q.ShatterFactor,
            q.BankShockRadius,q.BankShockFactor}) Check(v,0,1e18);
        Require(q.Pierces is >=0 and <=8 && q.ChildPierces is >=0 and <=8 && q.ChildBounceBonus is >=0 and <=24);
        Check(q.WallRetention,0,.95);
        Require(q.Color is not null && q.Color.Length<=32);
    }
    public static void ValidateRun(GameData data,RunState s,bool legacy=false)
    {
        var c=data.Game;var r=c.Rules;
        Require(s.Schema==(legacy?1:c.Schema) && (legacy?ValidLegacyDeck(data,s.Deck):data.ValidDeck(s.Deck)) && s.Board is not null && s.Board.Length==(legacy?8:c.Board.Slots));
        Check(s.Time,0,1e9);Check(s.NextId,1,1e12);Check(s.Wave,1,1e7);Check(s.WaveTime,0,s.Expedition is null?60:1e9);
        Check(s.Health,0,r.MaxHealth);Check(s.Energy,0,1e7);Check(s.Score,0,1e18);Check(s.Kills,0,1e12);Check(s.Merges,0,1e12);
        Check(s.Shots,0,1e12);Check(s.Combo,0,1e6);Check(s.ComboTime,0,10);Check(s.BestCombo,0,1e9);
        Check(s.NextWaveIn,-2,10);Check(s.LastAim,-4,1);Check(s.TotalDamage,0,1e30);Check(s.ManualVolleys,0,1e12);Check(s.Escaped,0,1e12);
        foreach(var d in s.Board!) if(d is not null)
        {
            Require(s.Deck.Contains(d.Type) && d.Pips>=1 && d.Pips<=r.MaxPips);Check(d.Cooldown,0,30);Check(d.Id,1);Check(d.Flash,0,2);
        }
        if (c.Rules.EnableDiceSkills && !legacy) ValidateSkills(data,s);
        Require(s.Upgrades is not null);
        foreach(var (id,level) in s.Upgrades!) Require(data.UpgradeTypes.ContainsKey(id) && level>=0 && level<=1_000_000);
        int maxChildren=2+(r.MaxPips-1)/2, maxPending=r.MaxQueuedShots*(maxChildren+1)+r.MaxProjectiles*maxChildren;
        Require(s.Enemies is not null && s.Enemies.Count<=r.MaxEnemies && s.Projectiles is not null && s.Projectiles.Count<=r.MaxProjectiles &&
            s.PendingShots is not null && s.PendingShots.Count<=maxPending && s.DamageQueue is not null && s.DamageQueue.Count<=50000);
        foreach(var e in s.Enemies!)
        {
            Require(e is not null);Check(e.Id,1);Check(e.X,-1000,1000);Check(e.Y,-1e7,1000);Check(e.W,1,200);Check(e.H,1,200);
            Check(e.Hp,0,1e16);Check(e.MaxHp,1,1e16);Check(e.Speed,0,100);Check(e.SlowUntil);Check(e.SlowFactor,0,1);Check(e.Wave,1);
            Require(new[]{"normal","volatile","armored","boss"}.Contains(e.Kind));
        }
        foreach(var p in s.Projectiles!)
        {
            Require(p is not null && data.Types.ContainsKey(p.Type));Check(p.X,-100,1000);Check(p.Y,-100,1000);Check(p.Vx,-2000,2000);Check(p.Vy,-2000,2000);
            Check(p.Life,0,20);Check(p.Bounces,0,40);Check(p.WallPower,1,6);Check(p.PiercesLeft,0,8);ValidateStats(p.Stats);
            Require(p.Trail is not null && p.Trail.Count<=7);
            foreach(var point in p.Trail!) {Check(point.X,-100,1000);Check(point.Y,-100,1000);}
        }
        foreach(var shot in s.PendingShots!)
        {
            Require(shot is not null);Check(shot.Due,0,1e9);Check(shot.Angle,-Math.PI*2,Math.PI*2);
            Require(shot.Snapshot is not null && shot.Snapshot.SourceDieId>=0 && data.Types.ContainsKey(shot.Snapshot.Type));ValidateStats(shot.Snapshot!.Stats);
            Require(shot.Snapshot.Pips>=1 && shot.Snapshot.Pips<=r.MaxPips);
            if(shot.Child) {Check(shot.X,-100,1000);Check(shot.Y,-100,1000);Check(shot.LastEnemy);}
        }
        foreach(var d in s.DamageQueue!) {Require(d is not null);Check(d.Id,1);Check(d.Amount,0,1e18);Require(d.Color is not null && d.Color.Length<=32);}
        Require(s.Offers is not null && s.Offers.Count<=3 && s.Offers.All(data.UpgradeTypes.ContainsKey) && (!s.AwaitingUpgrade || (s.Expedition is null?s.Offers.Count==3:s.Offers.Count>0)));
        Check(s.Rng,1,uint.MaxValue);
        DiceContent.ValidateRuntime(s);
        if(s.Expedition is { } expedition)
        {
            Require(Guid.TryParseExact(expedition.RunId,"N",out _) && expedition.Serial>0 && expedition.Cycle is >=0 and <=999 && s.Deck.Contains(expedition.LeadDice));
            Require(expedition.Region is not null && expedition.Region.Id==expedition.RegionId);CampaignCatalog.ValidateRegion(expedition.Region!);CampaignCatalog.ValidateBonuses(expedition.Bonuses);
            Require(expedition.Outcome is "" or "victory" or "defeat" or "abandoned");
            Require(!s.Over || expedition.Outcome!="");Require(expedition.Outcome!="victory" || expedition.FinalBossDefeated && !expedition.Endless);
            Check(expedition.HealthMultiplier,.1,1e6);Check(expedition.RewardMultiplier,1,10000);Check(expedition.StartTime,0,s.Time);Check(expedition.StartKills,0,s.Kills);
            Require(expedition.Loot.Count<=100 && expedition.Loot.Values.All(v=>v is >=0 and <=1_000_000_000_000));
            Require(expedition.DefeatedBossWaves.Count<=100000 && expedition.RewardedWaves.Count<=100000 && expedition.FoundBlueprints.Count<=10000 && expedition.AvailableBlueprints.Length<=10000);
        }
    }
}
