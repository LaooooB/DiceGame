using System.Diagnostics;
using System.Text.Json;
using DiceGame.Core;
using DiceGame.App;

namespace DiceGame.ExpansionTests;
internal static partial class Program
{
    static readonly GameData Data=GameData.FromDirectory(Path.Combine(AppContext.BaseDirectory,"Data"));
    static readonly List<object> Results=[];
    static int Passed,Failed;
    static void Test(string name,Action run)
    {
        try {run();Passed++;Results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}
        catch(Exception e) {Failed++;Results.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+"\n"+e);}
    }
    static int Main()
    {
        Common();Rare();Epic();Legendary();Mythic();Boundaries();
        Directory.CreateDirectory("Artifacts");
        File.WriteAllText("Artifacts/expansion-test-results.json",JsonSerializer.Serialize(new{passed=Passed,failed=Failed,tests=Results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"\n{Passed} expansion tests passed, {Failed} failed.");return Failed==0?0:1;
    }
    static Simulation Sim(params string[] types)
    {
        var deck=types.Concat(Data.DefaultDeck).Distinct().Take(6).ToArray();var s=new Simulation(Data,deck,324719);
        Array.Clear(s.State.Board);s.State.Enemies.Clear();s.State.Projectiles.Clear();s.State.PendingShots.Clear();s.State.DamageQueue.Clear();s.State.PendingSkills.Clear();s.State.Expansion=new();s.TakeEvents();s.Grid.Rebuild(s.State.Enemies);return s;
    }
    static DieState Die(Simulation s,string type,int pips=6,string a="A",string b="C",int slot=0)
    {var d=s.MakeDie(type,pips);d.Tier3=pips>=3?a:"";d.Tier6=pips==6?b:"";s.State.Board[slot]=d;return d;}
    static EnemyState Enemy(Simulation s,double x=216,double y=250,double hp=1e9,string kind="normal")
    {var e=s.AddEnemy(new EnemyState{X=x,Y=y,Hp=hp,Kind=kind,Speed=0,Wave=1});s.Grid.Rebuild(s.State.Enemies);return e;}
    static ProjectileState Shot(Simulation s,DieState d,double x=216,double y=350,double angle=-Math.PI/2)
    {var q=s.Stats(d);q.Traits["rootFirst"]=1;return s.MakeProjectile(x,y,angle,new ShotSnapshot{Type=d.Type,Pips=d.Pips,SourceDieId=d.Id,Stats=q});}
    static ShotSnapshot[] Volley(Simulation s,DieState d,double angle=-Math.PI/2)
    {
        s.State.PendingShots.Clear();foreach(var ally in s.State.Board.OfType<DieState>())ally.Cooldown=ally.Id==d.Id?0:20;
        Check(s.Fire(angle).Ok);return s.State.PendingShots.Where(p=>p.Snapshot.SourceDieId==d.Id && p.Snapshot.Stats.Auxiliary==AuxiliaryKind.None).Select(p=>p.Snapshot).ToArray();
    }
    static ProjectileState Pellet(Simulation s,ShotSnapshot q,double x=216,double y=350,double angle=-Math.PI/2)=>s.MakeProjectile(x,y,angle,q);
    static double Hit(Simulation s,EnemyState e,ProjectileState p,bool flush=false)
    {double hp=e.Hp;s.PrimaryHit(e,p);if(flush)s.FlushDamage();return hp-e.Hp;}
    static void Clock(Simulation s,double time)
    {for(double t=0;t<time-1e-9;) {double dt=Math.Min(.05,time-t);s.Step(dt);t+=dt;}}
    static double Base(DieState d)
    {
        var set=Data.Skills[d.Type];double multiplier=1;
        if(d.Pips>=3 && set.Find(3,d.Tier3) is { } a)multiplier*=a.Modifiers.DamageMultiplier;
        if(d.Pips==6 && set.Find(6,d.Tier6) is { } b)multiplier*=b.Modifiers.DamageMultiplier;
        return Data.Types[d.Type].BaseDamage*Data.Game.Levels[d.Pips-1].VolleyPower*Data.Game.Rules.DamageScale*multiplier;
    }
    static void ForceRoll(Simulation s,double target)
    {for(uint seed=1;seed<1000000;seed++){if(Math.Abs(new SeededRandom(seed).Next()-target)<.0001){s.Random.State=seed;return;}}throw new Exception("Seed search failed.");}
    static string J<T>(T value)=>JsonSerializer.Serialize(value,GameData.JsonOptions);
    static void Check(bool ok,string why="Assertion failed") {if(!ok)throw new Exception(why);}
    static void Eq<T>(T actual,T expected)=>Check(EqualityComparer<T>.Default.Equals(actual,expected),$"Expected {Brief(expected)}, got {Brief(actual)}");
    static string Brief<T>(T value) {string text=value?.ToString()??"null";return text.Length<300?text:text[..300]+"… [length "+text.Length+"]";}
    static void Near(double actual,double expected,double tolerance=1e-5)=>Check(Math.Abs(actual-expected)<=tolerance*Math.Max(1,Math.Abs(expected)),$"Expected {expected:R}, got {actual:R}");
    static void Reject(Action action) {try{action();}catch{return;}throw new Exception("Expected rejection");}
    sealed class Store:IDesktopStorage {public string? Value;public bool Fail;public string? Read()=>Value;public uint NewSeed()=>531;public void Write(string s){if(Fail)throw new IOException("Simulated disk full");Value=s;}}
    sealed class Sound:ISoundOutput {public bool Enabled{get;set;}public bool Music{get;set;}public void Unlock(){}public void Play(string kind,int variant=0){}public void Tick(double dt,bool active){}public void Suspend(){}}
    static GameApp App(Store store,string mythic)
    {
        var app=new GameApp(Data,store,new Sound());app.Action("editDeck");app.EditingDeck.Clear();app.EditingDeck.AddRange(new[]{mythic}.Concat(Data.DefaultDeck).Take(6));app.Action("deckSave");app.StartNew(531);Array.Clear(app.Sim!.State.Board);app.Sim.State.Enemies.Clear();Enemy(app.Sim);return app;
    }
    static void Common()
    {
        Test("scatter A: four side pellets, wide angles, conserved volley",()=>{
            var s=Sim("scatter");var d=Die(s,"scatter",3,"A","");var shots=Volley(s,d);
            Eq(shots.Length,7);Eq(shots.Count(p=>p.Stats.SidePellet),4);Near(shots.Sum(p=>p.Stats.Damage),s.Stats(d).Volley);
            Check(s.State.PendingShots.Max(p=>p.Angle)-s.State.PendingShots.Min(p=>p.Angle)>.7);
        });
        Test("scatter B: distinct pellet contacts focus, one projectile cannot farm stacks",()=>{
            var s=Sim("scatter");var d=Die(s,"scatter",3,"B","");var e=Enemy(s);var shots=Volley(s,d);var p=Pellet(s,shots[0]);
            Near(Hit(s,e,p),p.Stats.Damage);Near(Hit(s,e,p),p.Stats.Damage);
            var second=Pellet(s,shots[1]);Near(Hit(s,e,second),second.Stats.Damage*1.1);Check(s.State.PendingShots.Max(q=>q.Angle)-s.State.PendingShots.Min(q=>q.Angle)<.18);
        });
        Test("scatter C: extra bounces belong only to side pellets",()=>{
            var s=Sim("scatter");var d=Die(s,"scatter");var shots=Volley(s,d);var center=shots.First(q=>!q.Stats.SidePellet);var side=shots.First(q=>q.Stats.SidePellet);Eq(side.Stats.Bounces,center.Stats.Bounces+4);
        });
        Test("scatter D: one central spear is actually stronger and piercing",()=>{
            var s=Sim("scatter");var d=Die(s,"scatter",6,"A","D");var shots=Volley(s,d);Eq(shots.Count(q=>q.Stats.FirstCenter),1);
            var first=Pellet(s,shots[0]);var other=Pellet(s,shots[1]);Near(first.Stats.Damage,other.Stats.Damage*1.8);Eq(first.PiercesLeft,3);Eq(other.PiercesLeft,0);
        });
        Test("flywheel A: longer flight life yields damage beyond the original cap",()=>{
            var s=Sim("flywheel");var d=Die(s,"flywheel",3,"A","");var p=Shot(s,d);Near(p.Life,Data.Game.Rules.ProjectileLife+3);p.FlightAge=8;Near(Hit(s,Enemy(s),p),p.Stats.Damage*2.44);
        });
        Test("flywheel B: faster ramp respects the lower cap",()=>{
            var s=Sim("flywheel");var p=Shot(s,Die(s,"flywheel",3,"B",""));p.FlightAge=4;Near(Hit(s,Enemy(s),p),p.Stats.Damage*1.8);
        });
        Test("flywheel C: flight threshold grants pierces only once",()=>{
            var s=Sim("flywheel");var p=Shot(s,Die(s,"flywheel"),160,350);p.FlightAge=2.99;s.MoveProjectile(p,.02);Eq(p.PiercesLeft,2);p.PiercesLeft=0;s.MoveProjectile(p,.02);Eq(p.PiercesLeft,0);
        });
        Test("flywheel D: mature impact damages nearby enemies only once",()=>{
            var s=Sim("flywheel");var p=Shot(s,Die(s,"flywheel",6,"A","D"));p.FlightAge=3.1;var e=Enemy(s);var nearby=Enemy(s,240,250);Hit(s,e,p,true);double after=nearby.Hp;Check(after<nearby.MaxHp);Hit(s,e,p,true);Near(nearby.Hp,after);
        });
        Test("execution A: wider health window starts finisher damage earlier",()=>{
            var s=Sim("execution");var d=Die(s,"execution",3,"A","");var e=Enemy(s,hp:10000);e.Hp=6000;var p=Shot(s,d);Near(Hit(s,e,p),p.Stats.Damage*(1+(.65-.6)/.65*.9));
        });
        Test("execution B: very low health receives the steeper curve",()=>{
            var s=Sim("execution");var p=Shot(s,Die(s,"execution",3,"B",""));var e=Enemy(s,hp:10000);e.Hp=500;Near(Hit(s,e,p),p.Stats.Damage*(1+(.25-.05)/.25*2.2));
        });
        Test("execution C: kills empower exactly the next normal volley",()=>{
            var s=Sim("execution");var d=Die(s,"execution");s.ApplyDamage(Enemy(s,hp:1),2,"#FFFFFF",d.Id);Eq(d.ExecuteStacks,1);
            double baseline=s.Stats(d).Volley;var first=Volley(s,d)[0].Stats;Near(first.Volley,baseline*1.12);Eq(d.ExecuteStacks,0);Near(Volley(s,d)[0].Stats.Volley,baseline);
        });
        Test("execution D: boss missing-health curve works above normal finisher threshold",()=>{
            var s=Sim("execution");var p=Shot(s,Die(s,"execution",6,"A","D"));var e=Enemy(s,hp:10000,kind:"boss");e.Hp=8000;Near(Hit(s,e,p),p.Stats.Damage*(1+.2*.2*2.4));
        });
        Test("trajectory A: twelve-degree tolerance keeps ten-degree adjustments",()=>{
            var s=Sim("trajectory");var d=Die(s,"trajectory",3,"A","");Volley(s,d,-1.5);Volley(s,d,-1.5+.174532925);Eq(d.AimStacks,1);Near(s.Stats(d).Volley,Base(d)*1.05);
        });
        Test("trajectory B: precise aim builds larger stacks but rejects three degrees",()=>{
            var s=Sim("trajectory");var d=Die(s,"trajectory",3,"B","");Volley(s,d,-1.5);Volley(s,d,-1.5);Eq(d.AimStacks,1);Near(s.Stats(d).Volley,Base(d)*1.1);Volley(s,d,-1.5+.05236);Eq(d.AimStacks,0);
        });
        Test("trajectory C: full calibration really launches piercing shots",()=>{
            var s=Sim("trajectory");var d=Die(s,"trajectory");for(int i=0;i<9;i++)Volley(s,d);Eq(d.AimStacks,8);Eq(Volley(s,d)[0].Stats.Pierces,3);
        });
        Test("trajectory D: turning preserves half the accumulated calibration",()=>{
            var s=Sim("trajectory");var d=Die(s,"trajectory",6,"A","D");for(int i=0;i<8;i++)Volley(s,d,-1.5);Eq(d.AimStacks,7);Volley(s,d,-.8);Eq(d.AimStacks,3);
        });
        Test("rhythm A: third normal volley has the moderate fixed bonus",()=>{
            var s=Sim("rhythm");var d=Die(s,"rhythm",3,"A","");double baseline=Volley(s,d)[0].Stats.Volley;Volley(s,d);Near(Volley(s,d)[0].Stats.Volley,baseline*1.65);
        });
        Test("rhythm B: fifth normal volley has the larger fixed bonus",()=>{
            var s=Sim("rhythm");var d=Die(s,"rhythm",3,"B","");double baseline=Volley(s,d)[0].Stats.Volley;for(int i=0;i<3;i++)Near(Volley(s,d)[0].Stats.Volley,baseline);Near(Volley(s,d)[0].Stats.Volley,baseline*2.8);
        });
        Test("rhythm C: piercing appears only on an empowered beat",()=>{
            var s=Sim("rhythm");var d=Die(s,"rhythm");Eq(Volley(s,d)[0].Stats.Pierces,0);Volley(s,d);Eq(Volley(s,d)[0].Stats.Pierces,3);Eq(Volley(s,d)[0].Stats.Pierces,0);
        });
        Test("rhythm D: two following reloads shorten without consuming beats on echo",()=>{
            var s=Sim("rhythm");var d=Die(s,"rhythm",6,"B","D");double reload=Volley(s,d)[0].Stats.Reload;for(int i=0;i<4;i++)Volley(s,d);Eq(d.BeatRemainders,2);
            Near(Volley(s,d)[0].Stats.Reload,reload*.75);Near(d.Cooldown,reload*.75);Near(Volley(s,d)[0].Stats.Reload,reload*.75);Near(Volley(s,d)[0].Stats.Reload,reload);
        });
    }
    static void Rare()
    {
        Test("sunder A: ten layers last six seconds and amplify armor damage",()=>{
            var s=Sim("sunder");var d=Die(s,"sunder",3,"A","");var e=Enemy(s,kind:"armored");for(int i=0;i<10;i++)Hit(s,e,Shot(s,d));Eq(e.Conditions["sunder"].Stacks,10);Near(e.Conditions["sunder"].Until,6);double hp=e.Hp;s.ApplyDamage(e,100,"#FFFFFF");Near(hp-e.Hp,160);
        });
        Test("sunder B: four stronger layers, not unbounded multiplicative vulnerability",()=>{
            var s=Sim("sunder");var d=Die(s,"sunder",3,"B","");var e=Enemy(s,kind:"armored");for(int i=0;i<10;i++)Hit(s,e,Shot(s,d));Eq(e.Conditions["sunder"].Stacks,4);double hp=e.Hp;s.ApplyDamage(e,100,"#FFFFFF");Near(hp-e.Hp,140);
        });
        Test("sunder C: max stacks trigger a bounded extra hit",()=>{
            var s=Sim("sunder");var d=Die(s,"sunder",6,"B","C");var e=Enemy(s);for(int i=0;i<3;i++)Hit(s,e,Shot(s,d));var p=Shot(s,d);double damage=Hit(s,e,p);Check(damage>p.Stats.Damage*1.8);double ordinary=Hit(s,e,Shot(s,d));Check(damage>ordinary);Near(e.SunderNext,2);
        });
        Test("sunder D: max stacks spread once, spread targets do not cascade",()=>{
            var s=Sim("sunder");var d=Die(s,"sunder",6,"B","D");var e=Enemy(s);var nearby=Enemy(s,250,250);var beyond=Enemy(s,310,250);for(int i=0;i<4;i++)Hit(s,e,Shot(s,d));Eq(nearby.Conditions["sunder"].Stacks,3);Check(!beyond.Conditions.ContainsKey("sunder"));
        });
        Test("magnetic A: wall shot can acquire a target outside the original range",()=>{
            var s=Sim("magnetic");var d=Die(s,"magnetic",3,"A","");Enemy(s,300,370);var p=Shot(s,d,400,250,0);s.MoveProjectile(p,.02);Check(p.WallGuided);Check(p.Vy>0);
        });
        Test("magnetic B: weak early steering is separate from wall acquisition",()=>{
            var s=Sim("magnetic");var d=Die(s,"magnetic",3,"B","");Enemy(s,245,300);var p=Shot(s,d,216,350);p.FlightAge=.14;s.MoveProjectile(p,.02);Check(p.EarlyGuided && !p.WallGuided);Check(p.Vx>0);
        });
        Test("magnetic C: first guided hit gets one damage bonus only",()=>{
            var s=Sim("magnetic");var p=Shot(s,Die(s,"magnetic"));p.WallGuided=true;var e=Enemy(s);Near(Hit(s,e,p),p.Stats.Damage*1.6);Near(Hit(s,e,p),p.Stats.Damage);
        });
        Test("magnetic D: relock happens once and spends remaining damage",()=>{
            var s=Sim("magnetic");var p=Shot(s,Die(s,"magnetic",6,"A","D"));p.WallGuided=true;var e=Enemy(s);Enemy(s,230,330);double raw=p.Stats.Damage;Hit(s,e,p);Check(p.Relocked);Near(p.Stats.Damage,raw*.7);Hit(s,e,p);Near(p.Stats.Damage,raw*.7);
        });
        Test("barrier A: sixteen time-gated contacts generate the smaller shared shield",()=>{
            var s=Sim("barrier");var d=Die(s,"barrier",3,"A","");var e=Enemy(s);for(int i=0;i<48;i++){s.State.Time=i*.11;Hit(s,e,Shot(s,d));}Eq(s.State.Expansion.Shield,2);
        });
        Test("barrier B: forty contacts grant two points and breach spends shield first",()=>{
            var s=Sim("barrier");var d=Die(s,"barrier",3,"B","");var e=Enemy(s);for(int i=0;i<40;i++){s.State.Time=i*.11;Hit(s,e,Shot(s,d));}Eq(s.State.Expansion.Shield,2);int hp=s.State.Health;Enemy(s,350,512);s.AdvanceEnemies(0);Eq(s.State.Health,hp);Eq(s.State.Expansion.Shield,1);
        });
        Test("barrier C: shield break gives a non-stacking two-second haste window",()=>{
            var s=Sim("barrier");var d=Die(s,"barrier");s.State.Expansion.Shield=1;d.Cooldown=10;Enemy(s,350,512);s.AdvanceEnemies(0);Eq(s.State.Expansion.Shield,0);Near(s.State.Expansion.ShieldReactionPower,.25);s.Step(.1);Near(d.Cooldown,9.875);
        });
        Test("barrier D: full capacity gives offensive value, one missing point removes it",()=>{
            var s=Sim("barrier");var d=Die(s,"barrier",6,"B","D");s.State.Expansion.Shield=5;Near(s.Stats(d).Volley,Base(d)*1.5);s.State.Expansion.Shield=4;Near(s.Stats(d).Volley,Base(d));
        });
        Test("spree A: remaining bullets gain bounded damage from twelve direct kills",()=>{
            var s=Sim("spree");var d=Die(s,"spree",3,"A","");var shots=Volley(s,d);var p=Pellet(s,shots[0]);for(int i=0;i<14;i++)Hit(s,Enemy(s,hp:1),p);Eq(s.State.Expansion.Volleys[p.Stats.VolleyId].Kills,12);var target=Enemy(s);Near(Hit(s,target,Pellet(s,shots[1])),shots[1].Stats.Damage*2.8);
        });
        Test("spree B: inherited stacks are captured, later old kills cannot rewrite them",()=>{
            var s=Sim("spree");var d=Die(s,"spree",3,"B","");var p=Pellet(s,Volley(s,d)[0]);for(int i=0;i<6;i++)Hit(s,Enemy(s,hp:1),p);var next=Volley(s,d)[0];Eq(s.State.Expansion.Volleys[next.Stats.VolleyId].Kills,2);Hit(s,Enemy(s,hp:1),p);Eq(s.State.Expansion.Volleys[next.Stats.VolleyId].Kills,2);
        });
        Test("spree C: fifth kill grants each remaining bullet pierces once",()=>{
            var s=Sim("spree");var d=Die(s,"spree");var shots=Volley(s,d);var p=Pellet(s,shots[0]);for(int i=0;i<5;i++)Hit(s,Enemy(s,hp:1),p);var remaining=Pellet(s,shots[1],330,400);s.MoveProjectile(remaining,.01);Eq(remaining.PiercesLeft,2);remaining.PiercesLeft=0;s.MoveProjectile(remaining,.01);Eq(remaining.PiercesLeft,0);
        });
        Test("spree D: armored kill counts three and boss milestones only once",()=>{
            var s=Sim("spree");var d=Die(s,"spree",6,"A","D");var p=Pellet(s,Volley(s,d)[0]);Hit(s,Enemy(s,hp:1,kind:"armored"),p);Eq(s.State.Expansion.Volleys[p.Stats.VolleyId].Kills,3);var boss=Enemy(s,hp:1000,kind:"boss");s.ApplyDamage(boss,260,"#FFFFFF",d.Id,DamageFlags.Direct,p.Stats.VolleyId);Eq(s.State.Expansion.Volleys[p.Stats.VolleyId].Kills,5);Eq(boss.BossMilestones,1);
        });
        Test("formation A: diagonal kinds count without crossing board rows",()=>{
            var s=Sim("formation","cannon");var d=Die(s,"formation",3,"A","",7);Die(s,"pulse",1,slot:0);Die(s,"blast",1,slot:14);Near(s.Stats(d).Volley,Base(d)*1.16);s.State.Board[14]=null;Die(s,"cannon",1,slot:5);Near(s.Stats(d).Volley,Base(d)*1.08);
        });
        Test("formation B: orthogonal distinct kinds give larger individual gains",()=>{
            var s=Sim("formation");var d=Die(s,"formation",3,"B","",7);Die(s,"pulse",1,slot:6);Die(s,"pulse",1,slot:8);Die(s,"blast",1,slot:1);Near(s.Stats(d).Volley,Base(d)*1.4);
        });
        Test("formation C: ally receives one support aura, not one per supporting die",()=>{
            var s=Sim("formation");Die(s,"formation",6,"A","C",7);Die(s,"formation",6,"A","C",9);var ally=Die(s,"pulse",1,slot:8);Near(s.Stats(ally).Volley,Base(ally)*1.08);
        });
        Test("formation D: four different orthogonal allies empower every third release",()=>{
            var s=Sim("formation");var d=Die(s,"formation",6,"B","D",7);Die(s,"pulse",1,slot:6);Die(s,"blast",1,slot:8);Die(s,"arc",1,slot:1);Die(s,"frost",1,slot:13);double normal=Volley(s,d)[0].Stats.Volley;Volley(s,d);Near(Volley(s,d)[0].Stats.Volley,normal*1.8);
        });
    }
}
