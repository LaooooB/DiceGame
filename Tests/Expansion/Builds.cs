using DiceGame.Core;

namespace DiceGame.ExpansionTests;
internal static partial class Program
{
    static void Epic()
    {
        Test("reactor A: five neighbor releases fill the cheaper core",()=>{
            var s=Sim("reactor");var d=Die(s,"reactor",3,"A","",7);var ally=Die(s,"pulse",1,slot:8);
            for(int i=0;i<5;i++)Volley(s,ally);Near(d.MechanicEnergy,5);Near(Volley(s,d)[0].Stats.Volley,Base(d)*1.8);Near(d.MechanicEnergy,0);
        });
        Test("reactor B: twelve charges buy the heavier empowered salvo",()=>{
            var s=Sim("reactor");var d=Die(s,"reactor",3,"B","",7);var ally=Die(s,"pulse",1,slot:8);
            for(int i=0;i<12;i++)Volley(s,ally);Near(d.MechanicEnergy,12);Near(Volley(s,d)[0].Stats.Volley,Base(d)*3.4);
        });
        Test("reactor C: discharge gives adjacent cooldown reduction after the release",()=>{
            var s=Sim("reactor");var d=Die(s,"reactor",6,"A","C",7);var ally=Die(s,"pulse",1,slot:8);d.MechanicEnergy=5;Volley(s,d);Near(ally.Cooldown,19.6);
        });
        Test("reactor D: only the first main projectile produces the nuclear blast",()=>{
            var s=Sim("reactor");var d=Die(s,"reactor",6,"A","D");d.MechanicEnergy=5;var shots=Volley(s,d);var e=Enemy(s);var nearby=Enemy(s,245,250);var p=Pellet(s,shots[0]);Hit(s,e,p,true);double after=nearby.Hp;Check(after<nearby.MaxHp);Hit(s,e,p,true);Near(nearby.Hp,after);Hit(s,e,Pellet(s,shots[1]),true);Near(nearby.Hp,after);
        });
        Test("catalyst A: broad categories recognize causal debt and soul marks",()=>{
            var s=Sim("catalyst");var d=Die(s,"catalyst",3,"A","");var e=Enemy(s);e.Debt=new(){Due=2,Cap=1000,Ratio=.2};e.Souls[d.Id]=3;var p=Shot(s,d);Near(Hit(s,e,p),p.Stats.Damage*1.36);
        });
        Test("catalyst B: one actual status provides high base catalytic damage",()=>{
            var s=Sim("catalyst");var p=Shot(s,Die(s,"catalyst",3,"B",""));var e=Enemy(s);e.SlowUntil=3;e.SlowFactor=.8;Near(Hit(s,e,p),p.Stats.Damage*1.65);
        });
        Test("catalyst C: only shortest condition is extended with a total two-second cap",()=>{
            var s=Sim("catalyst");var d=Die(s,"catalyst");var e=Enemy(s);e.SlowUntil=10;e.SlowFactor=.8;e.MarkUntil=9;e.MarkFactor=1.1;
            Hit(s,e,Shot(s,d));Near(e.MarkUntil,9.35);Near(e.SlowUntil,10);Hit(s,e,Shot(s,d));Near(e.MarkUntil,9.35);
            for(int i=1;i<10;i++){s.State.Time=i*.51;Hit(s,e,Shot(s,d));}Near(e.ExtensionSpent,2);Near(e.MarkUntil+e.SlowUntil,21);
        });
        Test("catalyst D: three conditions produce one gated reaction burst",()=>{
            var s=Sim("catalyst");var d=Die(s,"catalyst",6,"A","D");var e=Enemy(s);e.SlowUntil=4;e.SlowFactor=.8;e.Souls[d.Id]=4;e.Debt=new(){Due=4,Cap=1000,Ratio=.2};var p=Shot(s,d);
            double first=Hit(s,e,p),second=Hit(s,e,Shot(s,d));Near(first-second,p.Stats.Damage*.8);Near(e.CatalystNext,1);
        });
        Test("phase A: two genuine wall crossings, then ordinary reflection",()=>{
            var s=Sim("phase");var d=Die(s,"phase",3,"A","");var p=Shot(s,d,400,300,0);s.MoveProjectile(p,.7);Eq(p.PhaseCount,2);Check(p.Vx>0);s.MoveProjectile(p,.55);Eq(p.PhaseCount,2);Check(p.Vx<0);
        });
        Test("phase B: crossing preserves travel direction and increases damage",()=>{
            var s=Sim("phase");var p=Shot(s,Die(s,"phase",3,"B",""),400,300,0);double raw=p.Stats.Damage;s.MoveProjectile(p,.02);Eq(p.PhaseCount,1);Check(p.X<60 && p.Vx>0);Near(p.Stats.Damage,raw*1.4);
        });
        Test("phase C: each allowed crossing supplies bounded penetration",()=>{
            var s=Sim("phase");var p=Shot(s,Die(s,"phase"),400,300,0);s.MoveProjectile(p,.02);Eq(p.PiercesLeft,2);s.MoveProjectile(p,.65);Eq(p.PiercesLeft,4);
        });
        Test("phase D: two child images cannot phase or reproduce again",()=>{
            var s=Sim("phase");var p=Shot(s,Die(s,"phase",6,"A","D"),400,300,0);s.MoveProjectile(p,.02);Eq(s.State.PendingShots.Count,2);var q=s.State.PendingShots[0].Snapshot;Near(q.Stats.Damage,p.Stats.Damage*.25);Eq(q.Stats.Auxiliary,AuxiliaryKind.Phase);
            s.State.PendingShots.Clear();var child=s.MakeProjectile(400,300,0,q,true);s.MoveProjectile(child,.02);Eq(child.PhaseCount,0);Eq(s.State.PendingShots.Count,0);
        });
        Test("soul A: longer assist marks and five souls release actual tracking shots",()=>{
            var s=Sim("soul");var d=Die(s,"soul",3,"A","");for(int i=0;i<5;i++){var e=Enemy(s);Hit(s,e,Shot(s,d));Near(e.Souls[d.Id],6);s.ApplyDamage(e,2e9,"#FFFFFF");}
            Near(d.MechanicEnergy,5);Volley(s,d);Eq(s.State.PendingShots.Count(p=>p.Snapshot.Stats.Auxiliary==AuxiliaryKind.Soul),3);Near(d.MechanicEnergy,0);
        });
        Test("soul B: one heavy soul uses the captured volley damage",()=>{
            var s=Sim("soul");var d=Die(s,"soul",3,"B","");d.MechanicEnergy=8;var main=Volley(s,d);var souls=s.State.PendingShots.Where(p=>p.Snapshot.Stats.Auxiliary==AuxiliaryKind.Soul).ToArray();Eq(souls.Length,1);Near(souls[0].Snapshot.Stats.Damage,main[0].Stats.Volley*.85);
        });
        Test("soul C: fractional soul cost rounds up and leaves a reserve",()=>{
            var s=Sim("soul");var d=Die(s,"soul",6,"A","C");d.MechanicEnergy=5;Volley(s,d);Near(d.MechanicEnergy,1);
        });
        Test("soul D: a soul marks assists but never autonomously spawns another salvo",()=>{
            var s=Sim("soul");var d=Die(s,"soul",6,"A","D");d.MechanicEnergy=5;Volley(s,d);var q=s.State.PendingShots.First(p=>p.Snapshot.Stats.Auxiliary==AuxiliaryKind.Soul).Snapshot;s.State.PendingShots.Clear();var e=Enemy(s);var p=Pellet(s,q);Hit(s,e,p);Check(p.Dead && e.Souls.ContainsKey(d.Id));s.ApplyDamage(e,2e9,"#FFFFFF");Near(d.MechanicEnergy,1);Eq(s.State.PendingShots.Count,0);
        });
        Test("gravity A: third stack produces the lighter backward pull",()=>{
            var s=Sim("gravity");var d=Die(s,"gravity",3,"A","");var e=Enemy(s);for(int i=0;i<3;i++)Hit(s,e,Shot(s,d));Near(e.Y,248);Check(!e.Conditions.ContainsKey("mass"));Near(e.MassNext,2);
        });
        Test("gravity B: eighth stack produces the heavier pull and burst",()=>{
            var s=Sim("gravity");var d=Die(s,"gravity",3,"B","");var e=Enemy(s);for(int i=0;i<7;i++)Hit(s,e,Shot(s,d));Near(e.Y,250);var p=Shot(s,d);Near(Hit(s,e,p),p.Stats.Damage*2.1);Near(e.Y,243);
        });
        Test("gravity C: propagated mass adds two layers without instant chain collapses",()=>{
            var s=Sim("gravity");var d=Die(s,"gravity");var e=Enemy(s);var neighbor=Enemy(s,250,250);for(int i=0;i<3;i++)Hit(s,e,Shot(s,d));Eq(neighbor.Conditions["mass"].Stacks,2);Near(neighbor.Y,250);
        });
        Test("gravity D: boss movement is reduced but stronger than the default resistance",()=>{
            var s=Sim("gravity");var d=Die(s,"gravity",6,"B","D");var e=Enemy(s,kind:"boss");for(int i=0;i<8;i++)Hit(s,e,Shot(s,d));Near(e.Y,250-7*.45);for(int i=0;i<8;i++)Hit(s,e,Shot(s,d));Near(e.Y,250-7*.45);
        });
    }
    static void Legendary()
    {
        Test("blackhole A: larger, longer field actually damages and pulls distant enemies",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole",3,"A","");var e=Enemy(s);Hit(s,e,Shot(s,d));var other=Enemy(s,296,250);Eq(s.State.Expansion.Fields.Count,1);Near(s.State.Expansion.Fields[0].Until,2.2);Clock(s,.3);Check(other.X<296 && other.Hp<other.MaxHp);
        });
        Test("blackhole B: compact field trades coverage for higher periodic damage",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole",3,"B","");var e=Enemy(s);Hit(s,e,Shot(s,d));Near(s.State.Expansion.Fields[0].Radius,40);double before=e.Hp;Clock(s,.2);Near(before-e.Hp,Base(d)*.55*.2);
        });
        Test("blackhole C: nearby friendly projectiles bend without gaining lifetime",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole");var e=Enemy(s);Hit(s,e,Shot(s,d));var ally=Die(s,"pulse",1,slot:1);var p=Shot(s,ally,150,250);double life=p.Life;s.MoveProjectile(p,.05);Check(p.Vx>0);Near(p.Life,life-.05);
        });
        Test("blackhole D: overlapping event horizons apply vulnerability once",()=>{
            var s=Sim("blackhole");var d=Die(s,"blackhole",6,"A","D");var e=Enemy(s);Hit(s,e,Shot(s,d));Hit(s,e,Shot(s,d));double hp=e.Hp;s.ApplyDamage(e,100,"#FFFFFF");Near(hp-e.Hp,120);
        });
        Test("forge A: neighboring result slots charge fast forging, not arbitrary merges",()=>{
            var s=Sim("forge");var d=Die(s,"forge",3,"A","",7);
            for(int i=0;i<2;i++){Die(s,"pulse",1,slot:0);Die(s,"pulse",1,slot:1);Check(s.Merge(0,1).Ok);}Near(d.MechanicEnergy,2);Near(Volley(s,d)[0].Stats.Volley,Base(d)*2);
        });
        Test("forge B: six charged merges pay the stronger forge salvo",()=>{
            var s=Sim("forge");var d=Die(s,"forge",3,"B","",7);
            for(int i=0;i<6;i++){Die(s,"pulse",1,slot:0);Die(s,"pulse",1,slot:1);s.Merge(0,1);}Near(d.MechanicEnergy,6);Near(Volley(s,d)[0].Stats.Volley,Base(d)*4);
        });
        Test("forge C: real timed ally blessing expires and does not multiply duplicate auras",()=>{
            var s=Sim("forge");var d=Die(s,"forge",6,"A","C",7);var ally=Die(s,"pulse",1,slot:8);d.MechanicEnergy=2;Volley(s,d);Near(s.Stats(ally).Volley,Base(ally)*1.18);Enemy(s);Clock(s,3.1);Near(s.Stats(ally).Volley,Base(ally));
        });
        Test("forge D: empowered shots get six real extra pierces and bounces",()=>{
            var s=Sim("forge");var d=Die(s,"forge",6,"B","D");d.MechanicEnergy=6;var q=Volley(s,d)[0];Eq(q.Stats.Pierces,6);Eq(q.Stats.Bounces,s.Stats(d).Bounces+6);
        });
        Test("swarm A: higher proc chance and sixteen shared pending-or-live slots",()=>{
            var s=Sim("swarm");var d=Die(s,"swarm",3,"A","");var e=Enemy(s);for(int i=0;i<30;i++){ForceRoll(s,.22);Hit(s,e,Shot(s,d));}Eq(s.State.PendingShots.Count,16);Check(s.State.PendingShots.All(p=>p.Snapshot.Stats.Auxiliary==AuxiliaryKind.Swarm));
        });
        Test("swarm B: six heavier drones replace a larger weaker cloud",()=>{
            var s=Sim("swarm");var d=Die(s,"swarm",3,"B","");var e=Enemy(s);for(int i=0;i<10;i++){ForceRoll(s,.1);Hit(s,e,Shot(s,d));}Eq(s.State.PendingShots.Count,6);Near(s.State.PendingShots[0].Snapshot.Stats.Damage,s.Stats(d).Damage*1.4);
        });
        Test("swarm C: one child generation only, both generations consume capacity",()=>{
            var s=Sim("swarm");var d=Die(s,"swarm");ForceRoll(s,.1);Hit(s,Enemy(s),Shot(s,d));var q=s.State.PendingShots[0].Snapshot;s.State.PendingShots.Clear();var p=Pellet(s,q);Hit(s,Enemy(s,hp:1),p);Eq(s.State.PendingShots.Count,1);var child=s.State.PendingShots[0].Snapshot;Eq(child.Stats.AuxiliaryGeneration,1);Near(child.Stats.Damage,q.Stats.Damage*.75);s.State.PendingShots.Clear();Hit(s,Enemy(s,hp:1),Pellet(s,child));Eq(s.State.PendingShots.Count,0);
        });
        Test("swarm D: other swarm dice improve drone damage with a five-ally limit",()=>{
            var s=Sim("swarm");var d=Die(s,"swarm",6,"A","D");for(int i=1;i<8;i++)Die(s,"swarm",1,slot:i);ForceRoll(s,.1);Hit(s,Enemy(s),Shot(s,d));Near(s.State.PendingShots[0].Snapshot.Stats.Damage,s.Stats(d).Damage*.6*1.4);
        });
        Test("throne A: a tied crown remains legal but provides the smaller bonus",()=>{
            var s=Sim("throne");var d=Die(s,"throne",3,"A","");Die(s,"pulse",3,"A","",1);Near(s.Stats(d).Volley,Base(d)*1.4);
        });
        Test("throne B: strong crown vanishes on a highest-point tie",()=>{
            var s=Sim("throne");var d=Die(s,"throne",3,"B","");Near(s.Stats(d).Volley,Base(d)*2.4);Die(s,"pulse",3,"A","",1);Near(s.Stats(d).Volley,Base(d));
        });
        Test("throne C: crown grants the real adjacent ally aura",()=>{
            var s=Sim("throne");var d=Die(s,"throne",6,"B","C");var ally=Die(s,"pulse",1,slot:1);Near(s.Stats(ally).Volley,Base(ally)*1.18);Die(s,"pulse",6,"A","C",2);Near(s.Stats(ally).Volley,Base(ally));
        });
        Test("throne D: fourth crowned release adds the royal attack, losing crown does not reset count",()=>{
            var s=Sim("throne");var d=Die(s,"throne",6,"B","D");double normal=Volley(s,d)[0].Stats.Volley;Volley(s,d);Volley(s,d);var royal=Volley(s,d)[0];Near(royal.Stats.Volley,normal*2.6);Eq(royal.Stats.Pierces,3);Die(s,"pulse",6,"A","C",1);Volley(s,d);Eq(d.NormalAttacks,5L);
        });
        Test("rift A: bigger persistent entry really teleports another die's projectile",()=>{
            var s=Sim("rift");var d=Die(s,"rift",3,"A","");var gate=Pellet(s,Volley(s,d)[0],400,300,0);s.MoveProjectile(gate,.02);Eq(s.State.Expansion.Rifts.Count,1);Near(s.State.Expansion.Rifts[0].Width,28);Near(s.State.Expansion.Rifts[0].Until,3.5);
            var ally=Die(s,"pulse",1,slot:1);var p=Shot(s,ally,400,326,0);s.MoveProjectile(p,.02);Check(p.RiftUsed && p.X<60 && p.Vx>0);
        });
        Test("rift B: narrow gate has higher throughput damage and misses outside its mouth",()=>{
            var s=Sim("rift");var d=Die(s,"rift",3,"B","");s.MoveProjectile(Pellet(s,Volley(s,d)[0],400,300,0),.02);var ally=Die(s,"pulse",1,slot:1);var p=Shot(s,ally,400,300,0);double damage=p.Stats.Damage;s.MoveProjectile(p,.02);Near(p.Stats.Damage,damage*1.6);var outside=Shot(s,ally,400,316,0);s.MoveProjectile(outside,.02);Check(!outside.RiftUsed && outside.Vx<0);
        });
        Test("rift C: passage gives two pierces once without resetting lifetime",()=>{
            var s=Sim("rift");var d=Die(s,"rift");s.MoveProjectile(Pellet(s,Volley(s,d)[0],400,300,0),.02);var p=Shot(s,Die(s,"pulse",1,slot:1),400,300,0);double life=p.Life;s.MoveProjectile(p,.02);Eq(p.PiercesLeft,2);Near(p.Life,life-.02);s.MoveProjectile(p,.65);Eq(p.PiercesLeft,2);
        });
        Test("rift D: exit shock uses the transported projectile and cannot open new gates",()=>{
            var s=Sim("rift");var d=Die(s,"rift",6,"A","D");s.MoveProjectile(Pellet(s,Volley(s,d)[0],400,300,0),.02);var target=Enemy(s,60,330);var p=Shot(s,Die(s,"pulse",1,slot:1),400,300,0);s.MoveProjectile(p,.02);s.FlushDamage();Check(target.Hp<target.MaxHp);Eq(s.State.Expansion.Rifts.Count,1);
        });
    }
}
