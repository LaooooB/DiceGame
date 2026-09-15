using System.Diagnostics;
using System.Text.Json.Nodes;
using DiceGame.Core;
using DiceGame.App;

namespace DiceGame.ExpansionTests;
internal static partial class Program
{
    static void Boundaries()
    {
        Test("catalog: exactly twenty-five added dice and one hundred named branches",()=>{
            Eq(Data.Dice.Length,47);var added=Data.Dice.Skip(22).ToArray();Eq(added.Length,25);Eq(added.SelectMany(d=>Data.Skills[d.Id].Level3.Concat(Data.Skills[d.Id].Level6)).Count(),100);
            Check(added.All(d=>d.AvailableFromStart && d.GlyphPath.Length>0));Check(Data.Dice.Where(d=>d.Rarity=="mythic").All(d=>!d.Copyable));
        });
        Test("deck: backend and selection UI reject a second mythic kind",()=>{
            var bad=new[]{"fate","void","pulse","blast","arc","frost"};Check(!Data.ValidDeck(bad));Reject(()=>new Simulation(Data,bad,1));
            var app=new GameApp(Data,new Store(),new Sound());app.Action("editDeck");app.EditingDeck.Clear();app.Action("deck:fate");app.Action("deck:void");Eq(app.EditingDeck.Count,1);Check(app.Toast!.Text.Contains("神话"));
        });
        Test("mythic copies: fate and void global laws do not scale with instance count",()=>{
            var fate=Sim("fate");Die(fate,"fate",6,"B","D",23);var ally=Die(fate,"pulse",1,slot:0);fate.State.Expansion.FateNumbers=[1];double one=fate.Stats(ally).Volley;Die(fate,"fate",6,"B","D",22);Near(fate.Stats(ally).Volley,one);
            var empty=Sim("void");Die(empty,"void",6,"A","C",23);var friend=Die(empty,"pulse",1,slot:0);one=empty.Stats(friend).Volley;Die(empty,"void",6,"A","C",22);Near(empty.Stats(friend).Volley,one);Eq(empty.Stats(friend).Pierces,1);
        });
        Test("manual input: every new die can wait without any automatic volley",()=>{
            foreach(var type in Data.Dice.Skip(22)){var s=Sim(type.Id);Die(s,type.Id,6,"A","D");Enemy(s);Clock(s,1);Eq(s.State.Shots,0L);Eq(s.State.ManualVolleys,0L);Eq(s.State.PendingShots.Count,0);}
        });
        Test("reactor timing: same-release contributions cannot empower an earlier or later slot",()=>{
            foreach(int reactorSlot in new[]{0,1})
            {
                var s=Sim("reactor");var reactor=Die(s,"reactor",3,"A","",reactorSlot);Die(s,"pulse",1,slot:1-reactorSlot);reactor.MechanicEnergy=4;
                Check(s.Fire(-1.5).Ok);var snap=s.State.PendingShots.First(p=>p.Snapshot.SourceDieId==reactor.Id).Snapshot;Check(!snap.Stats.ReactorCharged);Near(reactor.MechanicEnergy,5);
            }
        });
        Test("busy input: a rejected release does not spend rhythm, forge, charge or RNG",()=>{
            foreach(var type in new[]{"rhythm","reactor","forge","soul","void"})
            {
                var s=Sim(type);var d=Die(s,type,6,"A","D");d.MechanicEnergy=12;d.VoidClock=8;
                var snapshot=new ShotSnapshot{Type=type,Pips=6,SourceDieId=d.Id,Stats=s.Stats(d)};
                for(int i=0;i<Data.Game.Rules.MaxQueuedShots;i++)s.State.PendingShots.Add(new(){Due=1,Snapshot=snapshot});
                string before=J(s.ExportSave());Check(!s.Fire(s.State.LastAim).Ok);Eq(J(s.ExportSave()),before);
            }
        });
        Test("busy merge: validation precedes consumption of the order bag and materials",()=>{
            var s=Sim("order");Die(s,"order",6,"A","D",23);var d=Die(s,"pulse",1,slot:0);Die(s,"pulse",1,slot:1);var snap=new ShotSnapshot{Type="pulse",SourceDieId=d.Id,Stats=s.Stats(d)};
            for(int i=0;i<Data.Game.Rules.MaxQueuedShots;i++)s.State.PendingShots.Add(new(){Due=1,Snapshot=snap});string before=J(s.ExportSave());Check(!s.Merge(0,1).Ok);Eq(J(s.ExportSave()),before);
        });
        Test("merge surge: an upgrade shot does not advance normal-attack rhythms",()=>{
            var s=Sim("rhythm");Die(s,"rhythm",1,slot:0);Die(s,"rhythm",1,slot:1);ForceRoll(s,.01);var result=s.Merge(0,1);Check(result.Ok);Eq(result.Die!.Type,"rhythm");Eq(result.Die.NormalAttacks,0L);
        });
        Test("echo: one normal release creates bounded snapshots without repeated beat counters",()=>{
            var defs=CampaignCatalog.Copy(Data.Dice);var definition=defs.Single(d=>d.Id=="rhythm");definition.Traits["echoChance"]=1;definition.Traits["echoCopies"]=2;definition.Traits["echoFactor"]=.5;
            var data=new GameData(J(Data.Game),J(defs),J(Data.Upgrades),J(Data.Skills.Values));var s=new Simulation(data,new[]{"rhythm","pulse","blast","arc","frost","split"},23);Array.Clear(s.State.Board);s.State.Enemies.Clear();var d=Die(s,"rhythm",6,"B","D");d.NormalAttacks=4;Check(s.Fire(-1.5).Ok);Eq(d.NormalAttacks,5L);Eq(d.BeatRemainders,2);Eq(s.State.PendingShots.Count,18);Eq(s.State.PendingShots.Count(p=>p.Snapshot.Stats.Trait("rootFirst")>0),1);
        });
        Test("pause: pending skill selection freezes new fields, debts and fate clocks",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole");var e=Enemy(s);Hit(s,e,Shot(s,d));Die(s,"pulse",2,slot:1);Die(s,"pulse",2,slot:2);s.Merge(1,2);Check(s.AwaitingDiceSkill);string before=J(s.ExportSave());s.Step(.1);Eq(J(s.ExportSave()),before);
        });
        Test("identity: recycled residual projectiles cannot feed a replacement execution die",()=>{
            var s=Sim("execution");var old=Die(s,"execution");var p=Shot(s,old);Check(s.Recycle(0).Ok);var replacement=Die(s,"execution");Hit(s,Enemy(s,hp:1),p);Eq(replacement.ExecuteStacks,0);Check(replacement.Id!=p.SourceDieId);
        });
        Test("identity: soul assists belong to an instance, not its reused board slot",()=>{
            var s=Sim("soul");var old=Die(s,"soul");var e=Enemy(s);Hit(s,e,Shot(s,old));s.Recycle(0);var next=Die(s,"soul");s.ApplyDamage(e,2e9,"#FFFFFF");Near(next.MechanicEnergy,0);
        });
        Test("shield: a boss breach spends up to three shield points without resetting combat",()=>{
            var s=Sim("barrier");Die(s,"barrier");s.State.Expansion.Shield=2;int hp=s.State.Health;Enemy(s,216,512,kind:"boss");s.AdvanceEnemies(0);Eq(s.State.Expansion.Shield,0);Eq(s.State.Health,hp-1);
        });
        Test("shield: same-frame contacts cannot farm unbounded shielding",()=>{
            var s=Sim("barrier");var d=Die(s,"barrier");var e=Enemy(s);for(int i=0;i<100;i++)Hit(s,e,Shot(s,d));Near(d.MechanicEnergy,1);Eq(s.State.Expansion.Shield,0);
        });
        Test("fields: multiple blackholes cap at eight and use one strongest pull",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole");var e=Enemy(s);for(int i=0;i<20;i++)Hit(s,e,Shot(s,d));Eq(s.State.Expansion.Fields.Count,8);s.State.Enemies.Remove(e);var other=Enemy(s,260,250);s.Step(.1);Near(other.X,257.8);
        });
        Test("boss control: blackholes never translate a boss and preserve minimum speed",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole");Hit(s,Enemy(s),Shot(s,d));s.State.Enemies.Clear();var boss=Enemy(s,240,250,kind:"boss");s.Step(.1);Near(boss.X,240);Near(boss.Y,250);Check(boss.SlowFactor>=.72);
        });
        Test("phase limits: teleports consume bounce budget, never life or bounce resets",()=>{
            var s=Sim("phase");var p=Shot(s,Die(s,"phase"),400,300,0);int budget=p.Bounces;double life=p.Life;s.MoveProjectile(p,.7);Eq(p.Bounces,budget-2);Near(p.Life,life-.7);Eq(p.PhaseCount,2);
        });
        Test("rift limits: all pellets of one volley share a single gate creation",()=>{
            var s=Sim("rift");var d=Die(s,"rift");var shots=Volley(s,d);foreach(var q in shots)s.MoveProjectile(Pellet(s,q,400,300,0),.02);Eq(s.State.Expansion.Rifts.Count,1);
        });
        Test("swarm: duplicate source dice do not add their capacities",()=>{
            var s=Sim("swarm");var d=Die(s,"swarm",3,"B","");Die(s,"swarm",3,"B","",1);var e=Enemy(s);for(int i=0;i<20;i++){ForceRoll(s,.1);Hit(s,e,Shot(s,d));}Eq(s.State.PendingShots.Count,6);
        });
        Test("causal accounting: record cap uses actual HP loss, not lethal overkill",()=>{
            var s=Sim("causality");var d=Die(s,"causality");var e=Enemy(s);var target=Enemy(s,250,250);Hit(s,e,Shot(s,d));e.Hp=3;s.ApplyDamage(e,1e12,"#FFFFFF");Near(target.Debt!.Recorded,1.5);
        });
        Test("causal accounting: settled damage does not receive vulnerability a second time",()=>{
            var s=Sim("causality");var d=Die(s,"causality",3,"B","");var e=Enemy(s);Hit(s,e,Shot(s,d));e.MarkFactor=1.2;e.MarkUntil=4;s.ApplyDamage(e,100,"#FFFFFF");Near(e.Debt!.Recorded,120);double hp=e.Hp;Clock(s,1.1);Near(hp-e.Hp,48);
        });
        Test("causal recursion: a causal volatile explosion does not feed adjacent debt",()=>{
            var s=Sim("causality");var d=Die(s,"causality",6,"A","D");var e=Enemy(s,hp:10000,kind:"volatile");var other=Enemy(s,240,250);Hit(s,e,Shot(s,d));Hit(s,other,Shot(s,d));double prior=other.Debt!.Recorded;e.Hp=1;s.ApplyDamage(e,100,"#FFFFFF",d.Id,DamageFlags.Causal|DamageFlags.Settled);s.FlushDamage();Near(other.Debt!.Recorded,prior);
        });
        Test("order preservation: removing and returning the die cannot reroll the unfinished bag",()=>{
            var s=Sim("order");var d=Die(s,"order",6,"A","C",23);MergeOne(s);string before=string.Join(",",s.State.Expansion.OrderBag);s.Recycle(23);Die(s,"order",6,"A","C",23);Eq(string.Join(",",s.State.Expansion.OrderBag),before);
        });
        Test("order last card: adjudication cannot discard the only remaining result",()=>{
            var s=Sim("order");Die(s,"order",6,"A","D",23);for(int i=0;i<5;i++)MergeOne(s);Eq(s.State.Expansion.OrderBag.Count,1);string before=J(s.ExportSave());Check(!s.SkipOrder().Ok);Eq(J(s.ExportSave()),before);
        });
        Test("transaction: order skip is stored before publishing and disk failure consumes nothing",()=>{
            var store=new Store();var app=App(store,"order");Die(app.Sim!,"order",6,"A","D",23);app.Save();string before=J(app.Sim!.ExportSave());store.Fail=true;Check(!app.UseOrderSkip());Eq(J(app.Sim.ExportSave()),before);store.Fail=false;Check(app.UseOrderSkip());var saved=SaveCodec.Decode(Data,store.Value!);Check(saved.Run!.Expansion.OrderSkipStage>=0);Check(!app.UseOrderSkip());
        });
        Test("transaction: reincarnation rolls back on disk failure and persists after success",()=>{
            var store=new Store();var app=App(store,"reincarnation");var d=Die(app.Sim!,"reincarnation",6,"B","D",0);app.Save();store.Fail=true;app.Action("recycle:0");Eq(app.Sim!.State.Board[0]!.Id,d.Id);Eq(app.Sim.State.Expansion.Rebirths,0);store.Fail=false;app.Action("recycle:0");Eq(app.Sim!.State.Board[0]!.Pips,2);var saved=SaveCodec.Decode(Data,store.Value!);Eq(saved.Run!.Expansion.Rebirths,1);
        });
        Test("old save: version-three twenty-two-die-era state defaults extension fields safely",()=>{
            var s=Sim("pulse");var d=Die(s,"pulse");Enemy(s);var envelope=new SaveEnvelope{Version=3,Deck=s.State.Deck,Run=s.ExportSave()};var node=JsonNode.Parse(J(envelope))!;var run=node["run"]!.AsObject();run.Remove("expansion");run["contentVersion"]=1;
            foreach(var die in run["board"]!.AsArray())if(die is JsonObject obj)foreach(string key in new[]{"normalAttacks","mechanicEnergy","latestVolley","issuedReload"})obj.Remove(key);
            var decoded=SaveCodec.Decode(Data,node.ToJsonString());var restored=Simulation.Restore(Data,decoded.Run!);Eq(restored.State.Board[0]!.Id,d.Id);Eq(restored.State.Board[0]!.Pips,6);Eq(restored.State.Expansion.OrderBag.Count,0);Near(restored.State.Expansion.RebirthPower,0);
        });
        Test("save isolation: snapshots copy nested status, ledger and projectile collections",()=>{
            var s=Sim("scatter","sunder");var d=Die(s,"scatter",6,"B","D");var e=Enemy(s);var q=Volley(s,d)[0];var p=Pellet(s,q);s.State.Projectiles.Add(p);Hit(s,e,p);var sunder=Die(s,"sunder",6,"A","C",1);Hit(s,e,Shot(s,sunder));var copy=s.ExportSave();copy.Expansion.Volleys[q.Stats.VolleyId].FocusCounts[e.Id]=33;copy.Enemies[0].Conditions["sunder"].Stacks=15;copy.Projectiles[0].FocusTargets.Clear();Eq(s.State.Expansion.Volleys[q.Stats.VolleyId].FocusCounts[e.Id],1);Eq(e.Conditions["sunder"].Stacks,1);Check(p.FocusTargets.Contains(e.Id));
        });
        Test("save validation: unbounded fields, invalid conditions, debt over-cap and invalid flags reject",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole");var e=Enemy(s);Hit(s,e,Shot(s,d));var good=s.ExportSave();
            void Bad(Action<RunState> change){var copy=CampaignCatalog.Copy(good);change(copy);Reject(()=>SaveCodec.ValidateRun(Data,copy));}
            Bad(x=>x.Expansion=null!);Bad(x=>{while(x.Expansion.Fields.Count<9)x.Expansion.Fields.Add(CampaignCatalog.Copy(x.Expansion.Fields[0]));});
            Bad(x=>x.Enemies[0].Conditions["unregistered"]=new());Bad(x=>x.Enemies[0].Debt=new(){Cap=1,Recorded=2});Bad(x=>x.Expansion.RebirthPower=.9);
            Bad(x=>x.DamageQueue.Add(new(){Id=e.Id,Amount=1,Flags=(DamageFlags)128}));Bad(x=>x.Expansion.OrderBag=["fate"]);
        });
        Test("deterministic mixed save: fields, portals, swarm and order replay identically",()=>{
            var s=Sim("blackhole","rift","swarm","spree","barrier","order");for(int i=0;i<24;i++)Die(s,s.State.Deck[i%6],6,i/6%2==0?"A":"B",i%6==5?"D":"C",i);for(int i=0;i<6;i++)Enemy(s,70+i*55,210,1e10);
            s.SkipOrder();s.Fire(-1.4);Clock(s,1.2);var restored=Simulation.Restore(Data,s.ExportSave());for(int i=0;i<100;i++){if(i%20==0){s.Fire(-1.3);restored.Fire(-1.3);}s.Step(.05);restored.Step(.05);}Eq(J(s.ExportSave()),J(restored.ExportSave()));
        });
        Test("stress: all five mythic deck variants remain bounded under twenty-four active dice",()=>{
            var watch=Stopwatch.StartNew();
            foreach(string mythic in new[]{"fate","causality","void","reincarnation","order"})
            {
                var s=Sim(mythic,"blackhole","swarm","spree","rift","reactor");for(int i=0;i<24;i++)Die(s,s.State.Deck[i%6],6,i/6%2==0?"A":"B",i/12==0?"C":"D",i);
                for(int i=0;i<30;i++)Enemy(s,60+i%7*48,180+i/7*50,1e10,i%8==0?"boss":"normal");
                for(int tick=0;tick<600;tick++){if(tick%25==0)s.Fire(-1.57+Math.Sin(tick)*.5);s.Step(1d/120);}
                SaveCodec.ValidateRun(Data,s.ExportSave());Check(s.State.Projectiles.Count<=840 && s.State.PendingShots.Count<=1200 && s.State.Expansion.Fields.Count<=8 && s.State.Expansion.Rifts.Count<=8);
            }
            Console.WriteLine($"EXPANSION STRESS: {watch.ElapsedMilliseconds} ms for five decks / 25 simulated seconds");
        });
    }
}
