using System.Text.Json;

namespace DiceGame.Core;

public sealed class GameSettings { public bool Sound { get; set; }=true; public bool Music { get; set; } public bool ReduceMotion { get; set; } }
public sealed class MetaState { public double BestWave { get; set; } public double BestScore { get; set; } }
public sealed class SaveEnvelope
{
    public int Version { get; set; }=1; public GameSettings Settings { get; set; }=new(); public MetaState Meta { get; set; }=new();
    public List<string> Deck { get; set; }=[]; public RunState? Run { get; set; }
    public CampaignState? Campaign { get; set; }
    public DesktopPreferences Preferences { get; set; }=new();
}
public static class SaveCodec
{
    public const int MaxBytes=8_000_000;
    public static SaveEnvelope Decode(GameData data,string json)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(json)>MaxBytes) throw new InvalidDataException("Save is too large.");
        var saved=JsonSerializer.Deserialize<SaveEnvelope>(json,GameData.JsonOptions) ?? throw new InvalidDataException("Empty save.");
        if((saved.Version!=data.Game.Schema && saved.Version!=2) || !data.ValidDeck(saved.Deck) || saved.Settings is null || saved.Meta is null)
            throw new InvalidDataException("Invalid save header.");
        Check(saved.Meta.BestWave,0,1e18);Check(saved.Meta.BestScore,0,1e18);
        if(saved.Preferences is null) throw new InvalidDataException("Missing desktop preferences.");
        saved.Preferences.Validate();
        if(saved.Version==2 && saved.Campaign is null) throw new InvalidDataException("Missing campaign state.");
        if(saved.Run is not null) ValidateRun(data,saved.Run);
        return saved;
    }
    public static string Encode(SaveEnvelope saved)=>JsonSerializer.Serialize(saved,GameData.JsonOptions);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,string message="Invalid saved state.") {if(!condition) throw new InvalidDataException(message);}
    private static void Check(double v,double min=0,double max=double.PositiveInfinity)=>Require(MathEx.Finite(v,min,max));
    private static void ValidateStats(ShotStats? s)
    {
        Require(s is not null);var q=s!;
        Require(new[]{"pulse","blast","arc","frost","split","bank"}.Contains(q.Effect));
        foreach(double v in new[]{q.Damage,q.Count,q.Reload,q.Bounces,q.BlastRadius,q.SplashFactor,q.ChainCount,q.ChainRange,q.ChainFactor,
            q.SlowFactor,q.SlowSeconds,q.ChildCount,q.ChildFactor,q.WallBoost,q.MaxBoost}) Check(v,0,1e18);
        Require(q.Color is not null && q.Color.Length<=32);
    }
    public static void ValidateRun(GameData data,RunState s)
    {
        var c=data.Game;var r=c.Rules;
        Require(s.Schema==c.Schema && data.ValidDeck(s.Deck) && s.Board is not null && s.Board.Length==c.Board.Slots);
        Check(s.Time,0,1e9);Check(s.NextId,1,1e12);Check(s.Wave,1,1e7);Check(s.WaveTime,0,s.Expedition is null?60:1e9);
        Check(s.Health,0,r.MaxHealth);Check(s.Energy,0,1e7);Check(s.Score,0,1e18);Check(s.Kills,0,1e12);Check(s.Merges,0,1e12);
        Check(s.Shots,0,1e12);Check(s.Combo,0,1e6);Check(s.ComboTime,0,10);Check(s.BestCombo,0,1e9);
        Check(s.NextWaveIn,-2,10);Check(s.LastAim,-4,1);Check(s.TotalDamage,0,1e30);Check(s.ManualVolleys,0,1e12);Check(s.Escaped,0,1e12);
        foreach(var d in s.Board!) if(d is not null)
        {
            Require(s.Deck.Contains(d.Type) && d.Pips>=1 && d.Pips<=r.MaxPips);Check(d.Cooldown,0,30);Check(d.Id,1);Check(d.Flash,0,2);
        }
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
            Check(p.Life,0,20);Check(p.Bounces,0,40);Check(p.WallPower,1,4);ValidateStats(p.Stats);
            Require(p.Trail is not null && p.Trail.Count<=7);
            foreach(var point in p.Trail!) {Check(point.X,-100,1000);Check(point.Y,-100,1000);}
        }
        foreach(var shot in s.PendingShots!)
        {
            Require(shot is not null);Check(shot.Due,0,1e9);Check(shot.Angle,-Math.PI*2,Math.PI*2);
            Require(shot.Snapshot is not null && data.Types.ContainsKey(shot.Snapshot.Type));ValidateStats(shot.Snapshot!.Stats);
            Require(shot.Snapshot.Pips>=1 && shot.Snapshot.Pips<=r.MaxPips);
            if(shot.Child) {Check(shot.X,-100,1000);Check(shot.Y,-100,1000);Check(shot.LastEnemy);}
        }
        foreach(var d in s.DamageQueue!) {Require(d is not null);Check(d.Id,1);Check(d.Amount,0,1e18);Require(d.Color is not null && d.Color.Length<=32);}
        Require(s.Offers is not null && s.Offers.Count<=3 && s.Offers.All(data.UpgradeTypes.ContainsKey) && (!s.AwaitingUpgrade || (s.Expedition is null?s.Offers.Count==3:s.Offers.Count>0)));
        Check(s.Rng,1,uint.MaxValue);
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
