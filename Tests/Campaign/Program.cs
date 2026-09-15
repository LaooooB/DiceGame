using DiceGame.App;
using DiceGame.Core;
using System.Text.Json;

namespace DiceGame.CampaignTests;

internal static class Program
{
    private static readonly GameData Data = GameData.FromDirectory(Path.Combine(AppContext.BaseDirectory,"Data"));
    private static readonly string Json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data/campaign.json"));
    private static readonly CampaignCatalog Catalog = new(Data,Json);
    private static readonly CampaignProgression Progress = new(Catalog);
    private static readonly List<object> Results = [];
    private static int Passed,Failed;
    public static int Main()
    {
        Test("catalog: eight finite regions and reachable unlock graph",()=>
        { Eq(Catalog.Regions.Count,8); foreach(var r in Catalog.Regions.Values){Eq(r.Phases.Length,3);Eq(r.TotalWaves,36);Check(r.IsBossWave(12)&&r.IsBossWave(24)&&r.IsBossWave(36));Check(!r.IsBossWave(11));} });
        Test("fresh profile: town, initial dice, one open region",()=>
        { var a=App();Eq(a.Scene,"town");Eq(a.Deck.Count,6);Check(a.Campaign!.UnlockedDice.SetEquals(a.Deck));Eq(Catalog.Regions.Keys.Count(id=>Progress.IsRegionOpen(a.Campaign,id)),1); });
        Test("gates: locked dice and locked regions reject launch without spending",()=>
        { var s=Progress.NewState();long n=s.NextRunSerial;Throws(()=>Progress.PrepareRun(s,"region_02",["pulse"],1));Throws(()=>Progress.PrepareRun(s,"region_01",["arc"],1));Eq(s.NextRunSerial,n);Eq(s.ActiveRunId,""); });
        Test("launch: one immutable main die and configured slots",()=>
        { var a=App();Check(a.StartExpedition(7));Eq(a.Sim!.State.Board.Length,24);Eq(a.Sim.State.Expedition!.LeadDice,a.Deck[0]);Check(a.Sim.State.Deck.All(a.Campaign!.UnlockedDice.Contains)); });
        Test("launch save failure: no run published and no attempt consumed",()=>
        { var store=new MemoryStorage{FailWrites=true};var a=App(store);Check(!a.StartExpedition());Check(a.Sim is null);Eq(a.Campaign!.ActiveRunId,"");Eq(a.Campaign.NextRunSerial,1L);Eq(a.Campaign.Record("region_01").Attempts,0L); });
        Test("one active expedition: second launch is rejected",()=>
        {var a=App();Check(a.StartExpedition(1));long serial=a.Campaign!.NextRunSerial;Check(!a.StartExpedition(2));Eq(a.Campaign.NextRunSerial,serial);});
        Test("real battle: all stages, two minibosses and final boss, then victory",()=>
        {var a=App();a.StartExpedition(41);ClearRegion(a.Sim!);Eq(a.Sim!.State.Expedition!.Outcome,"victory");Eq(a.Sim.State.Wave,36);Eq(a.Sim.State.Expedition.DefeatedBossWaves.Count,3);Check(a.Sim.State.Over);});
        Test("boss cannot be skipped by the wave timer",()=>
        {var a=App();a.StartExpedition();var sim=a.Sim!;sim.State.Enemies.Clear();sim.StartWave(12);foreach(var enemy in sim.State.Enemies)enemy.Speed=0;sim.State.WaveTime=100;sim.Step(.1);Eq(sim.State.Wave,12);Check(!sim.State.Over);});
        Test("approach wave must clear before spawning a boss",()=>
        {var a=App();a.StartExpedition();var sim=a.Sim!;sim.State.Enemies.Clear();sim.StartWave(11);foreach(var enemy in sim.State.Enemies)enemy.Speed=0;sim.State.WaveTime=100;sim.Step(.1);Eq(sim.State.Wave,11);});
        Test("boss breach: defeat even with spare health, no victory flag",()=>
        {var a=App();a.StartExpedition();var sim=a.Sim!;sim.State.Enemies.Clear();sim.StartWave(12);var boss=sim.State.Enemies.Single(e=>e.Kind=="boss");sim.State.Enemies.RemoveAll(e=>e!=boss);boss.Y=Data.Game.Arena.Breach;sim.Step(.01);Eq(sim.State.Expedition!.Outcome,"defeat");Check(sim.State.Health>0);Check(!sim.State.Expedition.FinalBossDefeated);});
        Test("victory requires actual final-boss kill",()=>
        {var a=App();a.StartExpedition();Throws(()=>a.Sim!.EndExpedition("victory","forged"));Check(!a.Sim!.State.Over);});
        Test("victory: fixed rewards, first-clear blueprint, one lead-die gear",()=>
        {var a=App();a.StartExpedition(5);ClearRegion(a.Sim!);Check(a.TrySettle());var receipt=a.Campaign!.LastResult!;Check(receipt.FirstClear&&receipt.NewGear);Eq(a.Campaign.Gears,1);Check(a.Campaign.Blueprints.Contains("arc_workshop"));Check(Progress.IsRegionOpen(a.Campaign,"region_02"));});
        Test("settlement idempotency: duplicate callback cannot double rewards",()=>
        {var a=Win();string before=Encode(a.Campaign!);Check(a.TrySettle());a.ConsumeEvents();Eq(Encode(a.Campaign!),before);Progress.Settle(a.Campaign!,a.Sim!.State);Eq(Encode(a.Campaign!),before);});
        Test("settlement write failure: wallet unchanged until retry succeeds",()=>
        {var store=new MemoryStorage();var a=App(store);a.StartExpedition();ClearRegion(a.Sim!);long coins=a.Campaign!.Balance("coins");store.FailWrites=true;Check(!a.TrySettle());Check(!a.SettlementSaved);Eq(a.Campaign.Balance("coins"),coins);Check(!a.ReturnToTown());store.FailWrites=false;Check(a.TrySettle());Check(a.Campaign.Balance("coins")>coins);});
        Test("crash after terminal snapshot: load retries unconfirmed settlement",()=>
        {var store=new MemoryStorage();var a=App(store);a.StartExpedition();ClearRegion(a.Sim!);a.Save();var loaded=App(store);Check(loaded.SettlementSaved);Eq(loaded.Scene,"settlement");Eq(loaded.Campaign!.Gears,1);});
        Test("crash after confirmed settlement: reload does not pay twice",()=>
        {var store=new MemoryStorage();var a=Win(store);long coin=a.Campaign!.Balance("coins");var loaded=App(store);Check(loaded.SettlementSaved);Eq(loaded.Campaign!.Balance("coins"),coin);});
        Test("repeat same lead: reward resources, no duplicate gear / first clear",()=>
        {var a=Win();a.ReturnToTown();a.StartExpedition();ClearRegion(a.Sim!);a.TrySettle();Eq(a.Campaign!.Gears,1);Check(!a.Campaign.LastResult!.NewGear&&!a.Campaign.LastResult.FirstClear);Eq(a.Campaign.Record("region_01").Clears,2L);});
        Test("different lead: exactly one extra gear, not all carried dice",()=>
        {var a=Win();a.ReturnToTown();a.Action("editDeck");a.SetLeadDice("blast");Check(a.SaveDeck());a.StartExpedition();ClearRegion(a.Sim!);a.TrySettle();Eq(a.Campaign!.Gears,2);Check(a.Campaign.Record("region_01").ClearedWith.SetEquals(["pulse","blast"]));});
        Test("defeat keeps collected resource / blueprint, no region clear",()=>
        {var a=App();a.StartExpedition();var sim=a.Sim!;sim.State.Enemies.Clear();sim.StartWave(12);foreach(var enemy in sim.State.Enemies.ToArray())sim.ApplyDamage(enemy,1e15,"#FFFFFF");sim.State.Health=0;sim.EndExpedition("defeat","test");Check(a.TrySettle());Check(a.Campaign!.LastResult!.Resources["coins"]>0);Check(a.Campaign.LastResult.NewBlueprints.Count>0);Eq(a.Campaign.Gears,0);});
        Test("abandon at t=0 cannot farm dispatch supplies",()=>
        {var a=App();long before=a.Campaign!.Balance("supplies");a.StartExpedition();a.AbandonExpedition();Eq(a.Campaign!.Balance("supplies"),before);Eq(a.Campaign.Gears,0);});
        Test("endless lock is effective at the terminal boundary",()=>
        {var a=Win();Check(!a.ContinueEndless());Check(a.Sim!.State.Over);});
        Test("endless: keeps build, uses new journal identity, pays only new loot",()=>
        {var a=App();a.MutateTown(s=>s.UnlockedMechanics.Add("endless"));a.StartExpedition();ClearRegion(a.Sim!);a.TrySettle();var old=a.Sim!.State.Expedition!;var board=Encode(a.Sim.State.Board);long coins=a.Campaign!.Balance("coins");Check(a.ContinueEndless());Check(a.Sim.State.Expedition!.RunId!=old.RunId);Check(a.Sim.State.Expedition.Endless);Eq(a.Sim.State.Wave,37);Eq(Encode(a.Sim.State.Board),board);Eq(a.Sim.State.Expedition.Loot.Count,0);a.AbandonExpedition();Eq(a.Campaign.Balance("coins"),coins);Eq(a.Campaign.Gears,1);});
        Test("endless final stage wraps and never grants another normal victory",()=>
        {var a=App();a.MutateTown(s=>s.UnlockedMechanics.Add("endless"));a.StartExpedition();ClearRegion(a.Sim!);a.TrySettle();a.ContinueEndless();var sim=a.Sim!;sim.State.Enemies.Clear();sim.StartWave(72);foreach(var enemy in sim.State.Enemies.ToArray())sim.ApplyDamage(enemy,1e15,"#FFFFFF");sim.State.Enemies.RemoveAll(e=>e.Dead);sim.AdvanceWave();if(sim.State.AwaitingUpgrade)sim.ChooseUpgrade(sim.State.Offers[0]);Eq(sim.State.Wave,73);Check(!sim.State.Over);Eq(sim.State.Expedition!.Outcome,"");});
        Test("blueprint RNG is independent of combat RNG",()=>
        {var a=App();a.StartExpedition(9);var s=a.Sim!;s.State.Enemies.Clear();s.StartWave(12);uint before=s.Random.State;var boss=s.State.Enemies.Single(e=>e.Kind=="boss");s.ApplyDamage(boss,1e15,"#FFFFFF");Eq(s.Random.State,before);});
        Test("boss blueprint draws are deduplicated within the run",()=>
        {var a=App();a.StartExpedition();ClearRegion(a.Sim!);Check(a.Sim!.State.Expedition!.FoundBlueprints.Count<=Catalog.Regions["region_01"].BlueprintPool.Length);});
        Test("pinned region definition and bonuses survive authoring changes",()=>
        {var a=App();a.StartExpedition();var e=a.Sim!.State.Expedition!;double old=e.Region.Enemies.HealthMultiplier;Catalog.Regions["region_01"].Enemies.HealthMultiplier=old+1;Eq(e.Region.Enemies.HealthMultiplier,old);Catalog.Regions["region_01"].Enemies.HealthMultiplier=old;});
        Test("resume: exact battle and reward RNG continuation",()=>
        {var a=App();a.StartExpedition(110);a.Sim!.Fire(-1.4);for(int i=0;i<80;i++)a.Sim.Step(1.0/120);var b=Simulation.Restore(Data,a.Sim.ExportSave());for(int i=0;i<220;i++){a.Sim.Step(1.0/120);b.Step(1.0/120);}Eq(Encode(a.Sim.ExportSave()),Encode(b.ExportSave()));});
        Test("suspend to town: building prohibited and resume keeps offer",()=>
        {var a=App();a.StartExpedition();a.Sim!.OfferUpgrades();a.ConsumeEvents();var offers=string.Join(",",a.Sim.State.Offers);Check(a.ReturnToTown());Eq(a.Scene,"town");Check(a.Sim is null);Check(!a.MutateTown(s=>Progress.StartConstruction(s,"workshop",1)));a.Resume();Eq(a.Scene,"upgrade");Eq(string.Join(",",a.Sim!.State.Offers),offers);});
        Test("building construction: cost once, rewards only on completion",()=>
        {var a=App();long coins=a.Campaign!.Balance("coins");Check(a.MutateTown(s=>Progress.StartConstruction(s,"workshop",1)));Eq(a.Campaign.Balance("coins"),coins-60);Eq(a.Campaign.Bonuses.DamagePercent,0d);Check(!a.MutateTown(s=>Progress.StartConstruction(s,"workshop",1)));Check(a.MutateTown(s=>{s.Buildings["workshop"].Work=3;Check(Progress.CompleteConstruction(s,"workshop"));}));Eq(a.Campaign.Bonuses.DamagePercent,.05);Check(a.MutateTown(s=>Check(!Progress.CompleteConstruction(s,"workshop"))));Eq(a.Campaign.Bonuses.DamagePercent,.05);});
        Test("building write failure does not spend funds or publish construction",()=>
        {var store=new MemoryStorage();var a=App(store);long coins=a.Campaign!.Balance("coins");store.FailWrites=true;Check(!a.MutateTown(s=>Progress.StartConstruction(s,"workshop",1)));Eq(a.Campaign.Balance("coins"),coins);Check(!a.Campaign.Buildings.ContainsKey("workshop"));});
        Test("construction rejects resource tiles, occupied plots and locked blueprints",()=>
        {var s=Progress.NewState();Throws(()=>Progress.StartConstruction(s,"workshop",0));Throws(()=>Progress.StartConstruction(s,"arc_workshop",1));Progress.StartConstruction(s,"workshop",1);Throws(()=>Progress.StartConstruction(s,"supply_depot",1));});
        Test("relocation preserves level and work without replaying reward",()=>
        {var s=Progress.NewState();Progress.StartConstruction(s,"workshop",1);s.Buildings["workshop"].Work=1;Progress.Relocate(s,"workshop",2);Eq(s.Buildings["workshop"].Tile,2);Eq(s.Buildings["workshop"].Work,1);Eq(s.Bonuses.DamagePercent,0d);});
        Test("town bounce advances construction and reaches a finite end",()=>
        {var s=Progress.NewState();Progress.StartConstruction(s,"workshop",17);var town=new TownSimulation(Progress);var box=town.TileBounds(17);double angle=Math.Atan2((box.Top+box.Bottom)/2-430,(box.Left+box.Right)/2-400);town.Launch(s,angle,"pulse");for(int i=0;i<2000&&s.Flight is not null;i++)town.Step(s,1.0/120);Check(s.Flight is null);Check(s.Buildings["workshop"].Work>0||s.Buildings["workshop"].Level>0);});
        Test("town state checkpoint: no repeated resource on reload",()=>
        {var s=Progress.NewState();var town=new TownSimulation(Progress);town.Launch(s,-1.8,"pulse");for(int i=0;i<200;i++)town.Step(s,1.0/120);var clone=CampaignCatalog.Copy(s);for(int i=0;i<500;i++){town.Step(s,1.0/120);town.Step(clone,1.0/120);}Eq(Encode(s),Encode(clone));});
        Test("town dispatch cannot overlap expedition or another dispatch",()=>
        {var s=Progress.NewState();var town=new TownSimulation(Progress);town.Launch(s,-1.6,"pulse");Throws(()=>town.Launch(s,-1.5,"pulse"));Throws(()=>Progress.PrepareRun(s,"region_01",["pulse"],5));});
        Test("new cycle uses configured gate and preserves permanent unlocks",()=>
        {var s=Progress.NewState();Throws(()=>Progress.StartNextCycle(s));s.UnlockedMechanics.Add("next_cycle");s.RegionRecords[s.RecordKey("region_08")]=new RegionRecord{Clears=2,ClearedWith=["pulse","blast"]};s.UnlockedDice.Add("arc");long coins=s.Balance("coins");Check(Progress.CanStartNextCycle(s));Progress.StartNextCycle(s);Eq(s.Cycle,1);Eq(s.Gears,0);Eq(s.Balance("coins"),coins);Check(s.UnlockedDice.Contains("arc"));Check(!Progress.IsRegionOpen(s,"region_02"));});
        Test("legacy save migration: retain previously usable six-dice deck",()=>
        {var store=new MemoryStorage{Value=SaveCodec.Encode(new SaveEnvelope{Version=1,Deck=Data.Dice.Select(x=>x.Id).ToList()})};var a=App(store);Eq(a.Deck.Count,6);Eq(a.Campaign!.UnlockedDice.Count,6);a.Save();Eq(SaveCodec.Decode(Data,store.Value!).Version,3);});
        Test("legacy active run migration: not replaced or restarted",()=>
        {var old=new Simulation(GameData.FromDirectory(Path.Combine(AppContext.BaseDirectory,"LegacyBalanceData")),Data.Dice.Select(x=>x.Id),710);old.Fire(-1.6);for(int i=0;i<30;i++)old.Step(1.0/120);var before=old.ExportSave();var store=new MemoryStorage{Value=SaveCodec.Encode(new SaveEnvelope{Version=1,Deck=before.Deck,Run=before})};var a=App(store);Check(a.ResumeData!.Expedition is {LegacyRules:true,Endless:true});Eq(a.ResumeData.Wave,before.Wave);Eq(a.ResumeData.Rng,before.Rng);Eq(Encode(a.ResumeData.Board.Take(8)),Encode(before.Board));Eq(a.ResumeData.Board.Length,24);a.Resume();Check(a.Sim!.State.Expedition is {LegacyRules:true});Check(a.Sim.State.Time==before.Time);});
        Test("invalid save: no silent overwrite",()=>
        {var store=new MemoryStorage{Value="{damaged"};var a=App(store);Check(a.LoadProblem!="");Check(!a.StartExpedition());a.Save();Eq(store.Value,"{damaged");Eq(store.Writes,0);});
        Test("mismatched active session is rejected on load",()=>
        {var store=new MemoryStorage();var a=App(store);a.StartExpedition();var env=SaveCodec.Decode(Data,store.Value!);env.Campaign!.ActiveRunId=Guid.NewGuid().ToString("N");store.Value=SaveCodec.Encode(env);var restored=App(store);Check(restored.LoadProblem!="");});
        Test("catalog validation rejects unknown region / blueprint references",()=>
        {var d=CampaignCatalog.Copy(Catalog.Definition);d.Regions[1].Requirements[0].Region="missing";Throws(()=>new CampaignCatalog(Data,Encode(d)));d=CampaignCatalog.Copy(Catalog.Definition);d.Regions[0].BlueprintPool=["missing"];Throws(()=>new CampaignCatalog(Data,Encode(d)));});
        Test("catalog detects circular building prerequisites",()=>
        {var d=CampaignCatalog.Copy(Catalog.Definition);d.Buildings[0].Requirements=[new ProgressRequirement{Building=d.Buildings[1].Id}];d.Buildings[1].Requirements=[new ProgressRequirement{Building=d.Buildings[0].Id}];Throws(()=>new CampaignCatalog(Data,Encode(d)));});
        Test("upgrade exhaustion: no invalid three-choice modal or capped upgrade",()=>
        {var a=App();a.StartExpedition();var s=a.Sim!;foreach(var u in Data.Upgrades)s.State.Upgrades[u.Id]=u.Max;s.OfferUpgrades();Check(!s.State.AwaitingUpgrade);Eq(s.State.Wave,2);s.State.Upgrades["power"]=Data.UpgradeTypes["power"].Max-1;s.OfferUpgrades();Eq(s.State.Offers.Count,1);SaveCodec.ValidateRun(Data,s.ExportSave());Check(s.ChooseUpgrade("power"));Eq(s.State.Upgrades["power"],Data.UpgradeTypes["power"].Max);});
        Test("terminal callback flood cannot block settlement",()=>
        {var a=App();a.StartExpedition();ClearRegion(a.Sim!);a.Sim!.Events.Clear();a.Scene="play";a.Tick(.01);Eq(a.Scene,"settlement");Check(a.SettlementSaved);});
        Test("configuration: no character-unlock state or six-die clear multiplication",()=>
        {var s=Progress.NewState();s.UnlockedDice.UnionWith(Data.Dice.Select(d=>d.Id));var e=Progress.PrepareRun(s,"region_01",Data.Dice.Select(d=>d.Id).ToList(),1);var sim=new Simulation(Data,Data.Dice.Select(d=>d.Id),1,e);ClearRegion(sim);Progress.Settle(s,sim.State);Eq(s.Gears,1);});
        Console.WriteLine($"\n{Passed} campaign tests passed, {Failed} failed.");
        File.WriteAllText("campaign-test-results.json",JsonSerializer.Serialize(new{passed=Passed,failed=Failed,tests=Results},new JsonSerializerOptions{WriteIndented=true}));
        return Failed==0?0:1;
    }
    private static GameApp App(MemoryStorage? storage=null)=>new(Data,storage??new MemoryStorage(),new SilentAudio(),Catalog){NativeUi=true};
    private static GameApp Win(MemoryStorage? storage=null){var a=App(storage);Check(a.StartExpedition());ClearRegion(a.Sim!);Check(a.TrySettle());return a;}
    private static void ClearRegion(Simulation s)
    {
        for(int step=0;step<50000&&!s.State.Over;step++)
        {
            if(s.State.AwaitingUpgrade){Check(s.ChooseUpgrade(s.State.Offers[0]));continue;}
            foreach(var enemy in s.State.Enemies.ToArray())if(!enemy.Dead)s.ApplyDamage(enemy,1e15,"#FFFFFF");
            s.Step(.1);
        }
        Check(s.State.Over,"region did not terminate");Check(s.State.Expedition!.FinalBossDefeated,"final boss did not die");
    }
    private static void Test(string name,Action body)
    {try{body();Passed++;Results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){Failed++;Results.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+"\n"+ex);}}
    private static void Check(bool condition,string message="assertion failed"){if(!condition)throw new Exception(message);}
    private static void Eq<T>(T a,T b){if(!EqualityComparer<T>.Default.Equals(a,b))throw new Exception($"Expected {b}, got {a}");}
    private static void Throws(Action action){try{action();}catch{return;}throw new Exception("Expected rejection");}
    private static string Encode<T>(T value)=>JsonSerializer.Serialize(value,GameData.JsonOptions);
    private sealed class MemoryStorage:IDesktopStorage
    {public string? Value;public bool FailWrites;public int Writes;public string? Read()=>Value;public uint NewSeed()=>151;public void Write(string json){if(FailWrites)throw new IOException("Injected disk failure");Value=json;Writes++;}}
    private sealed class SilentAudio:ISoundOutput
    {public bool Enabled{get;set;}public bool Music{get;set;}public void Unlock(){}public void Play(string kind,int variant=0){}public void Tick(double dt,bool active){}public void Suspend(){}}
}
