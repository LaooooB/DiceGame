using System.Text.Json;

namespace DiceGame.Core;

/// <summary>
/// Native C# port of the shipped simulation.js. The 1/120-second clock, double precision,
/// event order, swept collisions and seeded random sequence are deliberately retained.
/// This is NOT delegated to Godot rigid-body physics: that would change the game.
/// </summary>
public sealed class Simulation
{
    public GameData Data { get; }
    public RunState State { get; private set; }
    public SeededRandom Random { get; }
    public SpatialGrid Grid { get; } = new(58);
    public List<CombatEvent> Events { get; private set; } = [];
    private GameConfig C => Data.Game;
    private RulesConfig R => C.Rules;
    private ArenaConfig A => C.Arena;
    private RunState S => State;
    public Simulation(GameData data, IEnumerable<string>? deck=null, uint? seed=null, ExpeditionState? expedition=null)
    {
        Data=data;
        var selected=(deck ?? data.Dice.Select(d=>d.Id)).ToList();
        if(!data.ValidDeck(selected)) throw new ArgumentException("A deck must contain 1–6 distinct registered dice types.",nameof(deck));
        Random=new SeededRandom(seed ?? unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        State=new RunState { Schema=C.Schema, Deck=selected, Seed=Random.State, Rng=Random.State,
            Health=R.MaxHealth, Energy=R.StartEnergy+(expedition?.Bonuses.StartEnergy??0), Board=new DieState?[C.Board.Slots], Expedition=expedition is null?null:CampaignCatalog.Copy(expedition) };
        S.Board[0]=MakeDie(S.Deck[0],1); S.Board[1]=MakeDie(S.Deck[0],1); S.Board[2]=MakeDie(S.Deck.Count>1?S.Deck[1]:S.Deck[0],1);
        StartWave(1); Grid.Rebuild(S.Enemies);
    }
    public DieState MakeDie(string type,int pips) => new() { Id=S.NextId++, Type=type, Pips=pips };
    public void Emit(CombatEvent e) { if(Events.Count<1600) Events.Add(e); }
    public List<CombatEvent> TakeEvents() { var events=Events; Events=[]; return events; }
    public int Count => S.Board.Count(d=>d is not null);
    public int ReadyCount => S.Board.Count(d=>d is not null && d.Cooldown<=1e-5);
    public int UpgradeLevel(string id) => S.Upgrades.GetValueOrDefault(id);
    public bool CanMerge(int a,int b)
    {
        if(a==b || a<0 || b<0 || a>=C.Board.Slots || b>=C.Board.Slots) return false;
        var x=S.Board[a]; var y=S.Board[b];
        return x is not null && y is not null && x.Type==y.Type && x.Pips==y.Pips && x.Pips<R.MaxPips;
    }
    public bool HasPair()
    {
        for(int a=0;a<C.Board.Slots;a++) for(int b=a+1;b<C.Board.Slots;b++) if(CanMerge(a,b)) return true;
        return false;
    }
    public ShotStats Stats(DieState die)
    {
        var type=Data.Types[die.Type]; var level=C.Levels[die.Pips-1];
        double multiplier=(1+0.18*UpgradeLevel("power"))*(1+(S.Expedition?.Bonuses.DamagePercent??0));
        if(die.Type=="pulse") multiplier*=1+0.2*UpgradeLevel("pulse");
        if(die.Type=="frost") multiplier*=1+0.1*UpgradeLevel("frost");
        double volley=type.BaseDamage*level.VolleyPower*multiplier;
        return new ShotStats
        {
            Effect=type.Effect, Damage=volley/die.Pips, Volley=volley, Count=die.Pips,
            Reload=type.Reload*level.ReloadFactor*Math.Pow(0.92,UpgradeLevel("reload"))*Math.Max(.25,1-(S.Expedition?.Bonuses.ReloadPercent??0)),
            Bounces=Math.Min(25,R.BaseBounces+2*UpgradeLevel("bounce")), Color=type.Color,
            BlastRadius=type.Radius*(1+0.2*UpgradeLevel("blast")), SplashFactor=type.SplashFactor*(1+0.15*UpgradeLevel("blast")),
            ChainCount=1+(die.Pips-1)/2+UpgradeLevel("arc"), ChainRange=type.ChainRange, ChainFactor=type.ChainFactor,
            SlowFactor=type.SlowFactor, SlowSeconds=type.SlowSeconds+die.Pips*0.1+0.6*UpgradeLevel("frost"),
            ChildCount=2+(die.Pips-1)/2, ChildFactor=type.ChildFactor*(1+0.25*UpgradeLevel("split")),
            WallBoost=type.WallBoost+0.15*UpgradeLevel("bank"), MaxBoost=type.MaxBoost
        };
    }
    public ActionResult Summon()
    {
        if(S.Over || S.AwaitingUpgrade) return new(false,"paused");
        int slot=Array.IndexOf(S.Board,null); if(slot<0) return new(false,"full");
        if(S.Energy+1e-6<R.SummonCost) return new(false,"energy");
        S.Energy-=R.SummonCost; var die=MakeDie(Random.Pick(S.Deck),1); S.Board[slot]=die;
        Emit(new CombatEvent { Type="summon", Slot=slot, Die=die.Copy() });
        return new(true,Slot:slot,Die:die);
    }
    public bool Move(int a,int b)
    {
        if(S.Over || S.AwaitingUpgrade || a<0 || b<0 || a>=C.Board.Slots || b>=C.Board.Slots || S.Board[a] is null || S.Board[b] is not null) return false;
        S.Board[b]=S.Board[a]; S.Board[a]=null; Emit(new CombatEvent {Type="move",A=a,B=b}); return true;
    }
    public ActionResult Merge(int a,int b)
    {
        if(S.Over || S.AwaitingUpgrade) return new(false,"paused");
        if(!CanMerge(a,b)) return new(false,"mismatch");
        var old=S.Board[b]!; int pips=old.Pips+1;
        if(S.PendingShots.Count+pips>R.MaxQueuedShots) return new(false,"busy");
        var die=MakeDie(Random.Pick(S.Deck),pips); S.Board[a]=null; S.Board[b]=die; S.Merges++;
        // Existing projectiles and queued snapshots do not reference either material die.
        QueueVolley(die,b,S.LastAim,1+R.MergeSurge+0.35*UpgradeLevel("surge"),true);
        die.Cooldown=Stats(die).Reload*0.36;
        Emit(new CombatEvent {Type="merge",A=a,B=b,OldType=old.Type,Die=die.Copy()});
        return new(true,Slot:b,Die:die);
    }
    public ActionResult Recycle(int index)
    {
        if(S.Over || S.AwaitingUpgrade || index<0 || index>=C.Board.Slots || S.Board[index] is null) return new(false);
        var die=S.Board[index]!; int amount=C.Levels[die.Pips-1].Recycle;
        S.Energy+=amount; S.Board[index]=null; Emit(new CombatEvent {Type="recycle",Slot=index,Amount=amount});
        return new(true,Amount:amount);
    }
    public double ClampAim(double angle)
    {
        if(!double.IsFinite(angle)) return S.LastAim;
        double limit=R.AimMaxDegrees*Math.PI/180;
        return MathEx.Clamp(angle,-Math.PI/2-limit,-Math.PI/2+limit);
    }
    public ActionResult Fire(double angle)
    {
        if(S.Over || S.AwaitingUpgrade) return new(false,"paused");
        S.LastAim=ClampAim(angle);
        var ready=S.Board.Select((d,i)=>(d,i)).Where(x=>x.d is not null && x.d.Cooldown<=1e-5).ToList();
        if(ready.Count==0) return new(false,"reloading");
        int needed=ready.Sum(x=>x.d!.Pips);
        if(S.PendingShots.Count+needed>R.MaxQueuedShots) return new(false,"busy");
        foreach(var (d,i) in ready)
        {
            QueueVolley(d!,i,S.LastAim,1,false); d!.Cooldown=Stats(d).Reload; d.Flash=0.35;
        }
        S.ManualVolleys++; Emit(new CombatEvent {Type="volley",Count=needed,Dice=ready.Count,Angle=S.LastAim});
        return new(true,Count:needed,Dice:ready.Count);
    }
    private void SortPending() => S.PendingShots=S.PendingShots.OrderBy(p=>p.Due).ToList(); // Stable, like JS Array.sort.
    public void QueueVolley(DieState die,int slot,double angle,double multiplier,bool surge)
    {
        var stats=Stats(die); var captured=stats.Copy(); captured.Damage*=multiplier;
        var snapshot=new ShotSnapshot {Type=die.Type,Pips=die.Pips,Stats=captured,Source=Data.SlotPosition(slot),Surge=surge};
        for(int k=0;k<stats.Count;k++) S.PendingShots.Add(new PendingShot {Due=S.Time+0.075+k*0.056+slot%4*0.019,Angle=angle,Snapshot=snapshot});
        SortPending(); Emit(new CombatEvent {Type="conduit",Slot=slot,Color=stats.Color,Surge=surge});
    }
    public ProjectileState MakeProjectile(double x,double y,double angle,ShotSnapshot snapshot,bool child=false)
    {
        double speed=R.ProjectileSpeed*(child?1.03:1);
        return new ProjectileState {Id=S.NextId++,X=x,Y=y,Px=x,Py=y,Vx=Math.Cos(angle)*speed,Vy=Math.Sin(angle)*speed,
            Type=snapshot.Type,Pips=snapshot.Pips,Stats=snapshot.Stats.Copy(),Life=child?3.4:R.ProjectileLife,
            Bounces=snapshot.Stats.Bounces,Child=child,SplitDone=child,WallPower=1,Surge=snapshot.Surge};
    }
    public void StartWave(int wave)
    {
        S.Wave=wave; S.WaveTime=0; S.ClearRewarded=false; S.NextWaveIn=-1;
        var w=C.Waves; var expedition=S.Expedition; bool campaign=expedition is {LegacyRules:false};
        bool boss=campaign?expedition!.BossWave(wave):wave%R.BossEvery==0;
        double baseHp=Math.Min(1e12,w.BaseHp*Math.Pow(w.HpExponential,Math.Min(wave-1,220))+w.HpLinear*(wave-1));
        double speed=Math.Min(w.MaxSpeed,w.BaseSpeed+w.SpeedPerWave*(wave-1));
        int count=Math.Min(w.MaxCount,(int)Math.Floor(w.BaseCount+(wave-1)*w.CountPerWave));
        if(campaign)
        {
            var profile=expedition!.Region.Enemies;
            double difficulty=(wave-1)*profile.DifficultyWaveScale;
            baseHp=Math.Min(1e12,(w.BaseHp*Math.Pow(w.HpExponential,Math.Min(difficulty,180))+w.HpLinear*difficulty)*profile.HealthMultiplier*expedition.HealthMultiplier);
            speed=Math.Min(80,Math.Min(w.MaxSpeed,w.BaseSpeed+w.SpeedPerWave*difficulty)*profile.SpeedMultiplier);
            count=boss?profile.BossEscorts:Math.Min(w.MaxCount,(int)Math.Floor(w.BaseCount+difficulty*w.CountPerWave));
        }
        var cells=new List<(int col,int row)>();
        if(wave==1 && !campaign) cells=[(1,0),(2,0),(3,0),(4,0),(5,0),(0,1),(2,1),(4,1),(6,1)];
        else
        {
            int rows=(int)Math.Ceiling(count/5.0);
            for(int row=0;row<rows;row++)
            {
                var lanes=Random.Shuffle(new[]{0,1,2,3,4,5,6});
                int n=Math.Min(5,count-cells.Count);
                if(campaign) lanes=FormationLanes(expedition!.Region.Enemies.Formation,lanes,row);
                foreach(int col in lanes.Take(n)) cells.Add((col,row));
            }
        }
        int room=R.MaxEnemies-S.Enemies.Count(e=>!e.Dead);
        double bossFactor=campaign?expedition!.Region.Phases[expedition.Region.PhaseIndex(expedition.LocalWave(wave))].BossHealthMultiplier:1;
        if(boss && room>0) AddEnemy(new EnemyState {X=216,Y=166,W=90,H=68,Hp=MathEx.JsRound(Math.Min(1e14,baseHp*w.BossHpFactor*bossFactor)),Speed=speed*0.67,Kind="boss",Wave=wave});
        int add=Math.Min(cells.Count,Math.Max(0,room-(boss?1:0)));
        for(int i=0;i<add;i++)
        {
            var (col,row)=cells[i]; double roll=Random.Next();
            string kind=wave>=2 && roll<0.14?"armored":roll<0.34?"volatile":"normal";
            if(campaign) {var profile=expedition!.Region.Enemies;kind=roll<profile.ArmoredChance?"armored":roll<profile.ArmoredChance+profile.VolatileChance?"volatile":"normal";}
            if(wave==1 && i==7 && !campaign) kind="volatile";
            double hp=Math.Max(1,MathEx.JsRound(baseHp*(0.84+Random.Next()*0.28)*(kind=="armored"?1.7:kind=="volatile"?0.78:1)));
            AddEnemy(new EnemyState {X=66+col*50,Y=160+row*49+(boss?92:0),W=39,H=39,Hp=hp,Speed=speed,Kind=kind,Wave=wave});
        }
        Grid.Rebuild(S.Enemies); Emit(new CombatEvent {Type="wave",Wave=wave,Boss=boss});
    }
    public EnemyState AddEnemy(EnemyState enemy)
    {
        enemy.Id=S.NextId++; enemy.MaxHp=enemy.Hp; S.Enemies.Add(enemy); return enemy;
    }
    public List<UpgradeDefinition> AvailableUpgrades() => Data.Upgrades.Where(u=>UpgradeLevel(u.Id)<u.Max &&
        (u.Requires is null || S.Deck.Contains(u.Requires)) && (u.Id!="repair" || S.Health<R.MaxHealth)).ToList();
    public void OfferUpgrades()
    {
        var pool=AvailableUpgrades();
        if(pool.Count==0 && S.Expedition is not null) {S.Energy+=R.SummonCost;StartWave(S.Wave+1);return;}
        if(pool.Count<3 && S.Expedition is null) pool=Data.Upgrades.Where(u=>u.Id is "power" or "income" or "surge").ToList();
        S.Offers=Random.Shuffle(pool).Take(3).Select(u=>u.Id).ToList(); S.AwaitingUpgrade=true;
        Emit(new CombatEvent {Type="upgrade",Offers=S.Offers.ToList()});
    }
    public bool ChooseUpgrade(string id)
    {
        if(!S.AwaitingUpgrade || !S.Offers.Contains(id) || !Data.UpgradeTypes.ContainsKey(id)) return false;
        if(S.Expedition is not null && UpgradeLevel(id)>=Data.UpgradeTypes[id].Max)return false;
        S.Upgrades[id]=UpgradeLevel(id)+1;
        if(id=="repair") S.Health=Math.Min(R.MaxHealth,S.Health+4);
        S.AwaitingUpgrade=false; S.Offers=[]; Emit(new CombatEvent {Type="upgraded",Id=id}); StartWave(S.Wave+1); return true;
    }
    public void AdvanceWave()
    {
        if(S.Expedition is {LegacyRules:false} e)
        {
            if(e.BossWave(S.Wave) && !e.DefeatedBossWaves.Contains(S.Wave)) return;
            if(!e.Endless && S.Wave>=e.Region.TotalWaves)
            { if(e.FinalBossDefeated) EndExpedition("victory","已击败区域最终头目"); return; }
            if(S.Wave%e.Region.UpgradeEvery==0) OfferUpgrades(); else StartWave(S.Wave+1);
            return;
        }
        if(S.Wave%R.UpgradeEvery==0) OfferUpgrades(); else StartWave(S.Wave+1);
    }
    public void AdvanceEnemies(double dt)
    {
        var sorted=S.Enemies.Where(e=>!e.Dead).OrderByDescending(e=>e.Y).ThenBy(e=>e.Id).ToList();
        foreach(var e in S.Enemies)
        {
            if(e.Dead) continue;
            double factor=S.Time<e.SlowUntil?(e.Kind=="boss"?Math.Max(0.72,e.SlowFactor):e.SlowFactor):1;
            e.Y+=e.Speed*factor*dt; e.Flash=Math.Max(0,e.Flash-dt*6);
        }
        double[] front=Enumerable.Repeat(double.PositiveInfinity,7).ToArray();
        foreach(var e in sorted)
        {
            int lo=(int)MathEx.Clamp(Math.Floor((e.X-e.W/2-41)/50),0,6), hi=(int)MathEx.Clamp(Math.Floor((e.X+e.W/2-41)/50),0,6);
            double limit=double.PositiveInfinity;
            for(int lane=lo;lane<=hi;lane++) limit=Math.Min(limit,front[lane]-e.H/2-4);
            e.Y=Math.Min(e.Y,limit);
            for(int lane=lo;lane<=hi;lane++) front[lane]=e.Y-e.H/2;
            if(e.Y+e.H/2>=A.Breach)
            {
                e.Dead=true; int lost=e.Kind=="boss"?3:1; S.Health=Math.Max(0,S.Health-lost); S.Escaped++;
                Emit(new CombatEvent {Type="breach",X=e.X,Y=A.Breach,Lost=lost});
                if(S.Expedition is {LegacyRules:false} && e.Kind=="boss")
                {EndExpedition("defeat","头目突破了防线，未完成区域挑战");break;}
                if(S.Health<=0)
                {if(S.Expedition is not null)EndExpedition("defeat","防线生命耗尽");else {S.Over=true;Emit(new CombatEvent {Type="gameover",Wave=S.Wave,Score=S.Score});}break;}
            }
        }
    }
    public void Step(double dt)
    {
        if(S.Over || S.AwaitingUpgrade) return;
        if(!MathEx.Finite(dt,0,0.101)) throw new ArgumentOutOfRangeException(nameof(dt));
        S.Time+=dt; S.WaveTime+=dt; S.Energy=Math.Min(999999,S.Energy+(R.PassiveEnergy+(S.Expedition?.Bonuses.PassiveEnergy??0))*dt);
        S.ComboTime=Math.Max(0,S.ComboTime-dt); if(S.ComboTime==0) S.Combo=0;
        foreach(var die in S.Board) if(die is not null) {die.Cooldown=Math.Max(0,die.Cooldown-dt);die.Flash=Math.Max(0,die.Flash-dt);}
        AdvanceEnemies(dt); if(S.Over) return;
        Grid.Rebuild(S.Enemies);
        while(S.PendingShots.Count>0 && S.PendingShots[0].Due<=S.Time && S.Projectiles.Count<R.MaxProjectiles)
        {
            var shot=S.PendingShots[0]; S.PendingShots.RemoveAt(0);
            var spawned=MakeProjectile(shot.Child?shot.X:216,shot.Child?shot.Y:A.LaunchY,shot.Angle,shot.Snapshot,shot.Child);
            spawned.LastEnemy=shot.LastEnemy; S.Projectiles.Add(spawned);
            if(!shot.Child) {S.Shots++;Emit(new CombatEvent {Type="launch",X=216,Y=A.LaunchY,Color=shot.Snapshot.Stats.Color,Surge=shot.Snapshot.Surge});}
        }
        int count=S.Projectiles.Count;
        for(int i=0;i<count;i++) if(!S.Projectiles[i].Dead) MoveProjectile(S.Projectiles[i],dt);
        FlushDamage(); S.Projectiles.RemoveAll(p=>p.Dead); S.Enemies.RemoveAll(e=>e.Dead);
        if(S.Enemies.Count==0 && !S.ClearRewarded && S.WaveTime>=2)
        {
            S.ClearRewarded=true; S.NextWaveIn=1.35; int reward=C.Waves.ClearEnergy+Math.Min(25,S.Wave);
            S.Energy+=reward; S.Score+=S.Wave*35; Emit(new CombatEvent {Type="clear",Reward=reward,Wave=S.Wave});
            if(S.Expedition is { } expedition && expedition.RewardedWaves.Add(S.Wave)) expedition.Gain("wood",expedition.Region.WoodPerWave);
        }
        if(S.NextWaveIn>=0) {S.NextWaveIn-=dt;if(S.NextWaveIn<=0) AdvanceWave();}
        else if(S.Expedition is {LegacyRules:false} e)
        {
            // A checkpoint cannot be skipped by waiting; clear the approach before a boss is spawned.
            if(!e.BossWave(S.Wave) && !e.BossWave(S.Wave+1) && S.WaveTime>=e.Region.WaveSeconds) AdvanceWave();
        }
        else if(S.WaveTime>=R.WaveSeconds) AdvanceWave();
    }
    public void MoveProjectile(ProjectileState p,double dt)
    {
        p.Life-=dt; if(p.Life<=0) {p.Dead=true;return;} p.Px=p.X;p.Py=p.Y;
        p.X=MathEx.Clamp(p.X,A.Left+R.ProjectileRadius,A.Right-R.ProjectileRadius); p.Y=Math.Max(A.Top+R.ProjectileRadius,p.Y);
        p.Trail.Add(new PointD(p.X,p.Y)); if(p.Trail.Count>7) p.Trail.RemoveAt(0);
        double remaining=dt;
        for(int iteration=0;iteration<7 && remaining>1e-6 && !p.Dead;iteration++)
        {
            double dx=p.Vx*remaining,dy=p.Vy*remaining,r=R.ProjectileRadius; SweepHit? hit=null;
            void Wall(double t,double nx,double ny) {if(t>=0 && t<=1 && (hit is null || t<hit.T)) hit=new SweepHit {T=t,Nx=nx,Ny=ny,Wall=true};}
            if(dx<0) Wall((A.Left+r-p.X)/dx,1,0); else if(dx>0) Wall((A.Right-r-p.X)/dx,-1,0);
            if(dy<0) Wall((A.Top+r-p.Y)/dy,0,1);
            if(p.Y>A.Bottom+18 && p.Vy>0) {p.Dead=true;break;}
            var candidates=Grid.Query(Math.Min(p.X,p.X+dx)-r,Math.Min(p.Y,p.Y+dy)-r,Math.Max(p.X,p.X+dx)+r,Math.Max(p.Y,p.Y+dy)+r);
            foreach(var e in candidates)
            {
                var box=new BoundsD(e.X-e.W/2-r,e.X+e.W/2+r,e.Y-e.H/2-r,e.Y+e.H/2+r);
                if(e.Id==p.LastEnemy)
                {
                    if(p.X>=box.Left-0.2 && p.X<=box.Right+0.2 && p.Y>=box.Top-0.2 && p.Y<=box.Bottom+0.2) continue;
                    p.LastEnemy=0;
                }
                var h=Collision.SweepAabb(p.X,p.Y,dx,dy,box);
                if(h is not null && (hit is null || h.T<hit.T)) {h.Enemy=e;hit=h;}
            }
            if(hit is null) {p.X+=dx;p.Y+=dy;remaining=0;break;}
            p.X+=dx*hit.T; p.Y+=dy*hit.T;
            double push=hit.Penetration+0.035; p.X+=hit.Nx*push; p.Y+=hit.Ny*push;
            double dot=p.Vx*hit.Nx+p.Vy*hit.Ny;
            if(dot<0) {p.Vx-=2*dot*hit.Nx;p.Vy-=2*dot*hit.Ny;}
            remaining*=Math.Max(0,1-hit.T); p.Bounces--;
            if(hit.Wall)
            {
                if(p.Stats.Effect=="bank") p.WallPower=Math.Min(p.Stats.MaxBoost,p.WallPower+p.Stats.WallBoost);
                Emit(new CombatEvent {Type="wall",X=p.X,Y=p.Y,Color=p.Stats.Color,Boost=p.Stats.Effect=="bank"});
            }
            else
            {
                p.LastEnemy=hit.Enemy!.Id; PrimaryHit(hit.Enemy,p); FlushDamage();
            }
            if(p.Bounces<=0) p.Dead=true;
            if(hit.T<1e-6) remaining=Math.Max(0,remaining-1e-5);
        }
        if(p.Y>A.Bottom+18 || p.X<A.Left-8 || p.X>A.Right+8 || !double.IsFinite(p.X) || !double.IsFinite(p.Y)) p.Dead=true;
    }
    public void PrimaryHit(EnemyState enemy,ProjectileState p)
    {
        double damage=p.Stats.Damage*p.WallPower; p.WallPower=1;
        ApplyDamage(enemy,damage,p.Stats.Color);
        switch(p.Stats.Effect)
        {
            case "blast":
                AreaDamage(enemy.X,enemy.Y,p.Stats.BlastRadius,damage*p.Stats.SplashFactor,p.Stats.Color,enemy.Id);
                Emit(new CombatEvent {Type="explosion",X=enemy.X,Y=enemy.Y,Radius=p.Stats.BlastRadius,Color=p.Stats.Color});
                break;
            case "arc":
                var seen=new HashSet<long>{enemy.Id}; var from=enemy;
                for(int n=0;n<p.Stats.ChainCount;n++)
                {
                    double range=p.Stats.ChainRange;
                    var target=Grid.Query(from.X-range,from.Y-range,from.X+range,from.Y+range)
                        .Where(e=>!seen.Contains(e.Id) && MathEx.Dist2(e.X,e.Y,from.X,from.Y)<=range*range)
                        .OrderBy(e=>MathEx.Dist2(e.X,e.Y,from.X,from.Y)).ThenBy(e=>e.Id).FirstOrDefault();
                    if(target is null) break; seen.Add(target.Id);
                    ApplyDamage(target,damage*p.Stats.ChainFactor*Math.Pow(0.86,n),p.Stats.Color);
                    Emit(new CombatEvent {Type="arc",X=from.X,Y=from.Y,Tx=target.X,Ty=target.Y,Color=p.Stats.Color}); from=target;
                }
                break;
            case "frost":
                if(!enemy.Dead) {enemy.SlowUntil=Math.Max(enemy.SlowUntil,S.Time+p.Stats.SlowSeconds);enemy.SlowFactor=Math.Max(0.35,p.Stats.SlowFactor);}
                break;
            case "split":
                if(p.SplitDone) break;
                p.SplitDone=true; int children=p.Stats.ChildCount;
                for(int n=0;n<children;n++)
                {
                    double angle=Math.Atan2(p.Vy,p.Vx)+(n-(children-1)/2.0)*0.38;
                    var childStats=p.Stats.Copy(); childStats.Damage=p.Stats.Damage*p.Stats.ChildFactor; childStats.Bounces=Math.Min(6,p.Bounces);
                    var snapshot=new ShotSnapshot {Type=p.Type,Pips=p.Pips,Stats=childStats};
                    double x=p.X+Math.Cos(angle)*5,y=p.Y+Math.Sin(angle)*5;
                    if(S.Projectiles.Count<R.MaxProjectiles)
                    {
                        var child=MakeProjectile(x,y,angle,snapshot,true);child.LastEnemy=enemy.Id;S.Projectiles.Add(child);
                    }
                    else S.PendingShots.Add(new PendingShot {Due=S.Time+1e-5,Angle=angle,Snapshot=snapshot,Child=true,X=x,Y=y,LastEnemy=enemy.Id});
                }
                SortPending();Emit(new CombatEvent {Type="split",X=p.X,Y=p.Y,Color=p.Stats.Color,Count=children});
                break;
        }
    }
    public void AreaDamage(double x,double y,double radius,double amount,string color,long exclude=0)
    {
        foreach(var e in Grid.Query(x-radius,y-radius,x+radius,y+radius))
            if(e.Id!=exclude && Math.Sqrt(MathEx.Dist2(e.X,e.Y,x,y))<=radius+Math.Min(e.W,e.H)*0.22)
                S.DamageQueue.Add(new SecondaryDamage {Id=e.Id,Amount=amount,Color=color});
    }
    public void ApplyDamage(EnemyState enemy,double amount,string color)
    {
        if(enemy.Dead || !MathEx.Finite(amount,0) || amount==0) return;
        double actual=Math.Min(enemy.Hp,amount);enemy.Hp-=amount;enemy.Flash=1;S.TotalDamage+=actual;
        Emit(new CombatEvent {Type="hit",X=enemy.X,Y=enemy.Y,Amount=amount,Color=color,Boss=enemy.Kind=="boss"});
        if(enemy.Hp>0) return;
        enemy.Dead=true; RecordExpeditionKill(enemy); S.Kills++;S.Combo++;S.ComboTime=2.1;S.BestCombo=Math.Max(S.BestCombo,S.Combo);
        int reward=C.Waves.KillEnergy+UpgradeLevel("income")+(enemy.Kind=="boss"?16:0);
        S.Energy=Math.Min(999999,S.Energy+reward);
        S.Score+=MathEx.JsRound((10+enemy.Wave*3)*(enemy.Kind=="boss"?12:1)*(1+Math.Min(S.Combo,30)*0.025));
        Emit(new CombatEvent {Type="kill",X=enemy.X,Y=enemy.Y,W=enemy.W,Color=enemy.Kind=="volatile"?"#FFAD76":color,Reward=reward,Combo=S.Combo,Kind=enemy.Kind});
        if(enemy.Kind=="volatile")
        {
            AreaDamage(enemy.X,enemy.Y,78,enemy.MaxHp*0.8,"#FFAD76",enemy.Id);
            Emit(new CombatEvent {Type="explosion",X=enemy.X,Y=enemy.Y,Radius=78,Color="#FFAD76",Volatile=true});
        }
    }
    public void FlushDamage()
    {
        int budget=1024;
        while(S.DamageQueue.Count>0 && budget-->0)
        {
            var d=S.DamageQueue[0];S.DamageQueue.RemoveAt(0);
            var enemy=S.Enemies.FirstOrDefault(e=>e.Id==d.Id);
            if(enemy is not null && !enemy.Dead) ApplyDamage(enemy,d.Amount,d.Color);
        }
    }
    public List<TracePoint> TraceAim(double angle,double maxDistance=1050)
    {
        double x=216,y=A.LaunchY,r=R.ProjectileRadius;
        var points=new List<TracePoint>{new(x,y)};
        double vx=Math.Cos(ClampAim(angle)),vy=Math.Sin(ClampAim(angle)),left=maxDistance;long last=0;
        for(int i=0;i<5 && left>0;i++)
        {
            double? distance=null;double nx=0,ny=0;long? id=null;
            void Wall(double d,double normalX,double normalY)
            {
                if(d>0.02 && d<left && (distance is null || d<distance.Value)) {distance=d;nx=normalX;ny=normalY;id=null;}
            }
            if(vx<0) Wall((A.Left+r-x)/vx,1,0);else if(vx>0) Wall((A.Right-r-x)/vx,-1,0);
            if(vy<0) Wall((A.Top+r-y)/vy,0,1);else Wall((A.Bottom+8-y)/vy,0,-1);
            foreach(var e in S.Enemies)
            {
                if(e.Dead || e.Id==last) continue;
                var h=Collision.SweepAabb(x,y,vx*left,vy*left,new BoundsD(e.X-e.W/2-r,e.X+e.W/2+r,e.Y-e.H/2-r,e.Y+e.H/2+r));
                if(h is not null && h.T>0.00001 && (distance is null || h.T*left<distance.Value)) {distance=h.T*left;nx=h.Nx;ny=h.Ny;id=e.Id;}
            }
            double travel=distance??left;x+=vx*travel;y+=vy*travel;points.Add(new TracePoint(x,y,id));left-=travel;
            if(distance is null || y>A.Bottom+2) break;
            double dot=vx*nx+vy*ny;vx-=2*dot*nx;vy-=2*dot*ny;x+=nx*0.05;y+=ny*0.05;last=id??0;
        }
        return points;
    }
    private static List<int> FormationLanes(string formation,List<int> shuffled,int row) => formation switch
    {
        "lanes" => [0,2,4,6,1,3,5],
        "stagger" => row%2==0?[0,2,4,6,1,3,5]:[1,3,5,0,2,4,6],
        "mirror" => row%2==0?shuffled:shuffled.Select(x=>6-x).ToList(),
        "center" => [3,2,4,1,5,0,6],
        "flanks" => [0,6,1,5,2,4,3],
        "checker" => row%2==0?[1,3,5,0,2,4,6]:[0,2,4,6,1,3,5],
        "dense" => [1,2,3,4,5,0,6],
        _ => shuffled
    };
    private void RecordExpeditionKill(EnemyState enemy)
    {
        if(S.Expedition is not { } e)return;
        e.Gain("coins",(long)Math.Ceiling(e.Region.CoinsPerKill*e.RewardMultiplier));
        if(enemy.Kind!="boss" || !e.DefeatedBossWaves.Add(enemy.Wave))return;
        e.Gain("stone",e.Region.StonePerBoss);e.Gain("supplies",e.Region.SuppliesPerBoss);
        // Loot uses a second PRNG: adding a blueprint never changes combat's random sequence.
        string[] available=e.AvailableBlueprints.Where(id=>!e.FoundBlueprints.Contains(id)).ToArray();
        if(available.Length>0)
        {var rng=new SeededRandom(e.RewardRng);e.FoundBlueprints.Add(rng.Pick(available));e.RewardRng=rng.State;}
        if(!e.Endless && enemy.Wave==e.Region.TotalWaves)e.FinalBossDefeated=true;
    }
    public void EndExpedition(string outcome,string reason)
    {
        if(S.Expedition is not { } e || S.Over)return;
        if(outcome is not ("victory" or "defeat" or "abandoned"))throw new ArgumentException("Invalid outcome.");
        if(outcome=="victory" && (!e.FinalBossDefeated || e.Endless))throw new InvalidOperationException("The final boss has not been defeated.");
        e.Outcome=outcome;e.EndReason=reason;S.Over=true;S.AwaitingUpgrade=false;S.Offers.Clear();
        Emit(new CombatEvent {Type=outcome=="victory"?"victory":"gameover",Wave=S.Wave,Score=S.Score});
    }
    public void ContinueAsEndless(ExpeditionState nextSession)
    {
        if(!S.Over || S.Expedition is not {Outcome:"victory",CanContinueEndless:true})throw new InvalidOperationException("Endless continuation is unavailable.");
        S.Expedition=CampaignCatalog.Copy(nextSession);S.Over=false;S.AwaitingUpgrade=false;S.Offers.Clear();
        S.Expedition.Endless=true;S.Expedition.FinalBossDefeated=false;S.Expedition.Outcome="";S.Expedition.EndReason="";
        StartWave(S.Wave+1);
    }
    public RunState ExportSave()
    {
        S.Rng=Random.State;
        // Deep-copy; a later tick must not mutate the persisted snapshot or resume menu.
        var save=JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(S,GameData.JsonOptions),GameData.JsonOptions)!;
        save.Enemies.RemoveAll(e=>e.Dead);save.Projectiles.RemoveAll(p=>p.Dead);return save;
    }
    public static Simulation Restore(GameData data,RunState saved)
    {
        SaveCodec.ValidateRun(data,saved);
        var clone=JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(saved,GameData.JsonOptions),GameData.JsonOptions)!;
        var sim=new Simulation(data,clone.Deck,clone.Seed) {State=clone};
        sim.Random.State=clone.Rng;sim.Events.Clear();sim.Grid.Rebuild(sim.State.Enemies);return sim;
    }
}
