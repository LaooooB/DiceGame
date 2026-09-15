namespace DiceGame.Core;

public sealed partial class Simulation
{
    // Adapters bridge legacy statuses and new registered conditions without converting old saves.
    private sealed record StatusClock(string Kind,double Until,Action<double> Extend);
    private List<StatusClock> StatusClocks(EnemyState e,bool broad)
    {
        var clocks=new List<StatusClock>();
        var poison=e.Poison.Where(p=>p.Until>S.Time).OrderBy(p=>p.Until).FirstOrDefault();
        if(poison is not null)clocks.Add(new("poison",poison.Until,d=>poison.Until+=d));
        if(e.SlowUntil>S.Time && e.SlowFactor<1)clocks.Add(new("slow",e.SlowUntil,d=>e.SlowUntil+=d));
        if(e.MarkUntil>S.Time && e.MarkFactor>1)clocks.Add(new("mark",e.MarkUntil,d=>e.MarkUntil+=d));
        foreach(var (key,value) in e.Conditions.OrderBy(p=>p.Key,StringComparer.Ordinal))
            if(value.Stacks>0 && value.Until>S.Time)clocks.Add(new(key,value.Until,d=>value.Until+=d));
        if(broad)
        {
            var soul=e.Souls.Where(p=>p.Value>S.Time).OrderBy(p=>p.Value).ThenBy(p=>p.Key).FirstOrDefault();
            if(soul.Key>0)clocks.Add(new("soul",soul.Value,d=>e.Souls[soul.Key]+=d));
            if(e.Debt is { } debt && debt.Due>S.Time)clocks.Add(new("causality",debt.Due,d=>debt.Due+=d));
        }
        return clocks;
    }
    private double ExpansionHitDamage(EnemyState e,ProjectileState p,double damage)
    {
        var q=p.Stats;if(q.Auxiliary!=AuxiliaryKind.None)return damage;
        damage*=1+Math.Min(q.Trait("flightCap"),p.FlightAge*q.Trait("flightRate"));
        double threshold=q.Trait("executeThreshold");
        if(e.Kind=="boss" && q.Trait("executeBoss")>0)damage*=1+Math.Pow(1-Math.Clamp(e.Hp/e.MaxHp,0,1),2)*q.Trait("executeBoss");
        else if(threshold>0)damage*=1+Math.Clamp((threshold-e.Hp/e.MaxHp)/threshold,0,1)*q.Trait("executeGain");
        if(S.Expansion.Volleys.TryGetValue(q.VolleyId,out var record))
        {
            damage*=1+Math.Min(record.Cap,record.Kills)*record.Step;
            if(q.Trait("scatterFocus")>0 && !p.FocusTargets.Contains(e.Id))
            {
                int previous=record.FocusCounts.GetValueOrDefault(e.Id);
                damage*=1+Math.Min(4,previous)*q.Trait("scatterFocus");
                if(record.FocusCounts.ContainsKey(e.Id) || record.FocusCounts.Count<128)record.FocusCounts[e.Id]=Math.Min(54,previous+1);
                if(p.FocusTargets.Count<128)p.FocusTargets.Add(e.Id);
            }
        }
        if((p.WallGuided || p.EarlyGuided) && !p.GuidedDamageDone)
        { damage*=1+q.Trait("magnetDamage");p.GuidedDamageDone=true; }
        if(q.Trait("catalystCap")>0)
        {
            int categories=Math.Min((int)q.Trait("catalystCap"),StatusClocks(e,q.Trait("catalystAll")>0).Count);
            if(categories>0)damage*=1+q.Trait("catalystBase")+categories*q.Trait("catalystStep");
        }
        return damage;
    }
    private void BeforeExpansionHit(EnemyState e,ProjectileState p)
    {
        var q=p.Stats;double mark=q.Auxiliary==AuxiliaryKind.Soul?q.AuxiliaryMarkSeconds:q.Auxiliary==AuxiliaryKind.None?q.Trait("soulMark"):0;
        if(mark>0 && SourceDie(p.SourceDieId) is not null && !e.Dead && (e.Souls.Count<64 || e.Souls.ContainsKey(p.SourceDieId)))
            e.Souls[p.SourceDieId]=Math.Max(e.Souls.GetValueOrDefault(p.SourceDieId),S.Time+mark);
    }
    private void ExpansionHit(EnemyState e,ProjectileState p,double rawDirectDamage)
    {
        var q=p.Stats;bool first=!p.MainEffectDone;p.MainEffectDone=true;
        if(q.Auxiliary!=AuxiliaryKind.None)
        {
            p.Dead=true;
            if(e.Dead && q.Auxiliary==AuxiliaryKind.Swarm && q.AuxiliaryGeneration==0 && q.AuxiliaryDuplicate>0)
                SpawnSwarm(p,e,q.Damage*q.AuxiliaryDuplicate,1);
            return;
        }
        if(SourceDie(p.SourceDieId) is { } owner && q.Trait("barrierHits")>0 && S.Time-owner.BarrierLastHit>=.1-1e-8 && !q.Echo && !p.Child)
        {
            owner.BarrierLastHit=S.Time;owner.MechanicEnergy++;
            if(owner.MechanicEnergy>=q.Trait("barrierHits"))
            {owner.MechanicEnergy-=q.Trait("barrierHits");S.Expansion.Shield=Math.Max(S.Expansion.Shield,Math.Min((int)q.Trait("barrierCapacity",3),S.Expansion.Shield+(int)q.Trait("barrierGrant",1)));}
        }
        if(!e.Dead)
        {
            if(q.Trait("sunderCap")>0) ApplySunder(e,q,p.SourceDieId,1,true);
            if(q.Trait("massNeed")>0) ApplyMass(e,q,p.SourceDieId,1,true);
            if(q.Trait("causalWindow")>0 && e.Debt is null)
                e.Debt=new CausalDebt { SourceId=p.SourceDieId,Due=S.Time+q.Trait("causalWindow"),Ratio=q.Trait("causalRatio")*(e.Kind=="boss"?1+q.Trait("causalBoss"):1),Cap=Math.Min(1e18,q.Volley*q.Trait("causalCap",12)),Transfer=q.Trait("causalTransfer"),Color=q.Color };
            if(q.Trait("catalystExtend")>0 && e.ExtensionNext<=S.Time && e.ExtensionSpent<2)
            {
                var shortest=StatusClocks(e,true).OrderBy(v=>v.Until).ThenBy(v=>v.Kind,StringComparer.Ordinal).FirstOrDefault();
                if(shortest is not null) {double extra=Math.Min(2-e.ExtensionSpent,q.Trait("catalystExtend"));shortest.Extend(extra);e.ExtensionSpent+=extra;e.ExtensionNext=S.Time+.5;}
            }
            if(q.Trait("catalystBurst")>0 && e.CatalystNext<=S.Time && StatusClocks(e,q.Trait("catalystAll")>0).Count>=3)
            {e.CatalystNext=S.Time+1;ApplyDamage(e,q.Damage*q.Trait("catalystBurst"),q.Color,p.SourceDieId);}
        }
        if(!p.FlightShockDone && q.Trait("flightShock")>0 && p.FlightAge>=q.Trait("flightThreshold",3))
        {p.FlightShockDone=true;ExpansionExplosion(e.X,e.Y,42,rawDirectDamage*q.Trait("flightShock"),q.Color,p.SourceDieId,e.Id);}
        if(p.WallGuided && !p.Relocked && q.Trait("magnetRelock")>0)
        {
            p.Relocked=true;
            if(SteerToEnemy(p,q.Trait("magnetRange",100),q.Trait("magnetTurn",.5),e.Id))
            {q.Damage*=q.Trait("magnetRelock");q.Volley*=q.Trait("magnetRelock");}
        }
        if(first && !p.Child && !q.Echo)
        {
            if(q.Trait("swarmChance")>0 && Random.Next()<q.Trait("swarmChance"))
            {
                int other=S.Board.OfType<DieState>().Count(d=>d.Id!=p.SourceDieId && Value(ProfileFor(d),"swarmChance")>0);
                SpawnSwarm(p,e,q.Damage*q.Trait("swarmDamage")*(1+Math.Min(5,other)*q.Trait("swarmQueen")),0);
            }
            if(q.Trait("rootFirst")>0)
            {
                if(q.ReactorCharged && q.Trait("reactorBlast")>0)ExpansionExplosion(e.X,e.Y,76,q.Volley*q.Trait("reactorBlast"),q.Color,p.SourceDieId);
                if(q.VoidCharged)ExpansionExplosion(e.X,e.Y,90,q.Volley*q.Trait("voidBurst"),q.Color,p.SourceDieId);
                if(q.Trait("holeRadius")>0 && S.Expansion.Fields.Count<DiceExpansion.MaxFields)
                    S.Expansion.Fields.Add(new GravityField {SourceId=p.SourceDieId,X=e.X,Y=e.Y,Until=S.Time+q.Trait("holeDuration"),LastTick=S.Time,Radius=q.Trait("holeRadius"),Dps=q.Volley*q.Trait("holeDamage"),Pull=q.Trait("holePull"),Steer=q.Trait("holeSteer"),Vulnerable=q.Trait("holeVulnerable"),Color=q.Color});
            }
        }
    }
    private void ExpansionExplosion(double x,double y,double radius,double damage,string color,long source,long exclude=0,DamageFlags flags=DamageFlags.None)
    {
        AreaDamage(x,y,radius,damage,color,exclude,source,flags);
        Emit(new CombatEvent {Type="explosion",X=x,Y=y,Radius=radius,Color=color});
    }
    private IEnumerable<EnemyState> Nearby(EnemyState e,double range,int count) => S.Enemies.Where(t=>!t.Dead && t.Id!=e.Id && MathEx.Dist2(e.X,e.Y,t.X,t.Y)<=range*range)
        .OrderBy(t=>MathEx.Dist2(e.X,e.Y,t.X,t.Y)).ThenBy(t=>t.Id).Take(count);
    private void ApplySunder(EnemyState e,ShotStats q,long source,int stacks,bool trigger)
    {
        int cap=Math.Clamp((int)q.Trait("sunderCap",6),1,20);double armor=q.Trait("sunderArmor"),other=q.Trait("sunderOther");
        if(!e.Conditions.TryGetValue("sunder",out var status) || status.Until<=S.Time)
            e.Conditions["sunder"]=status=new StackedStatus();
        // A weaker source cannot keep stronger layers alive or claim their attribution.
        if(status.Stacks>0 && status.Until>S.Time && (armor<status.ArmorPower-1e-9 || other<status.OtherPower-1e-9))return;
        status.Stacks=Math.Min(cap,status.Stacks+stacks);status.Until=Math.Max(status.Until,S.Time+q.Trait("sunderDuration",4));status.ArmorPower=armor;status.OtherPower=other;status.SourceId=source;
        if(!trigger || status.Stacks<cap || e.SunderNext>S.Time || q.Trait("sunderBurst")+q.Trait("sunderSpread")<=0)return;
        e.SunderNext=S.Time+2;
        if(q.Trait("sunderSpread")>0)foreach(var target in Nearby(e,70,3).ToArray())ApplySunder(target,q,source,(int)q.Trait("sunderSpread"),false);
        if(q.Trait("sunderBurst")>0)ApplyDamage(e,q.Damage*q.Trait("sunderBurst"),q.Color,source);
    }
    private void ApplyMass(EnemyState e,ShotStats q,long source,int stacks,bool trigger)
    {
        if(e.Dead)return;int need=Math.Clamp((int)q.Trait("massNeed",5),1,20);
        if(!e.Conditions.TryGetValue("mass",out var status) || status.Until<=S.Time)e.Conditions["mass"]=status=new StackedStatus();
        status.Stacks=Math.Min(need,status.Stacks+stacks);status.Until=S.Time+4;status.SourceId=source;
        if(!trigger || status.Stacks<need || e.MassNext>S.Time || e.NextKnockback>S.Time)return;
        e.MassNext=S.Time+2;e.NextKnockback=S.Time+2;e.Conditions.Remove("mass");
        double pull=q.Trait("massPull")*(e.Kind=="boss"?q.Trait("massBoss",.2):1);
        e.Y=Math.Max(A.Top+e.H/2,e.Y-pull);Grid.Rebuild(S.Enemies);
        if(q.Trait("massSpread")>0)foreach(var target in Nearby(e,70,3).ToArray())ApplyMass(target,q,source,(int)q.Trait("massSpread"),false);
        ApplyDamage(e,q.Damage*q.Trait("massPower"),q.Color,source);
        Emit(new CombatEvent {Type="explosion",X=e.X,Y=e.Y,Radius=25,Color=q.Color});
    }
    private double ExpansionIncomingDamage(EnemyState e,double amount,DamageFlags flags)
    {
        if((flags & DamageFlags.Settled)!=0)return amount;
        if(e.Conditions.TryGetValue("sunder",out var status) && status.Until>S.Time)
            amount*=1+Math.Min(e.Kind=="armored"?.6:.3,status.Stacks*(e.Kind=="armored"?status.ArmorPower:status.OtherPower));
        double vulnerable=0;
        foreach(var f in S.Expansion.Fields)if(f.Until>=S.Time && MathEx.Dist2(f.X,f.Y,e.X,e.Y)<=f.Radius*f.Radius)vulnerable=Math.Max(vulnerable,f.Vulnerable);
        return amount*(1+vulnerable);
    }
    private void RecordExpansionDamage(EnemyState e,double actual,double previousHp,DamageFlags flags,long volleyId)
    {
        if(e.Debt is {Sealed:false} debt && debt.Due>S.Time && (flags & DamageFlags.Causal)==0)debt.Recorded=Math.Min(debt.Cap,debt.Recorded+actual);
        bool direct=(flags & DamageFlags.Direct)!=0;
        if(e.Kind=="boss")
        {
            for(int i=0;i<3;i++)
            {
                double line=e.MaxHp*(.75-.25*i);int bit=1<<i;
                if((e.BossMilestones&bit)==0 && previousHp>line && e.Hp<=line)
                {
                    e.BossMilestones|=bit;
                    if(direct && S.Expansion.Volleys.TryGetValue(volleyId,out var record) && record.Elite>0)record.Kills=Math.Min(record.Cap,record.Kills+2);
                }
            }
        }
        if(e.Hp<=0 && direct && S.Expansion.Volleys.TryGetValue(volleyId,out var kills))
            kills.Kills=Math.Min(kills.Cap,kills.Kills+(kills.Elite>0 && e.Kind is "armored" or "boss"?kills.Elite:1));
    }
    private void ExpansionDeath(EnemyState e,long source)
    {
        if(SourceDie(source) is { } owner && Value(ProfileFor(owner),"executeNext")>0)owner.ExecuteStacks=Math.Min(5,owner.ExecuteStacks+1);
        foreach(var (id,until) in e.Souls)
            if(until>=S.Time && SourceDie(id) is { } soul && Value(ProfileFor(soul),"soulNeed")>0)soul.MechanicEnergy=Math.Min(Value(ProfileFor(soul),"soulNeed")*2,soul.MechanicEnergy+1);
        if(e.Debt is {Sealed:false,Transfer:>0,Recorded:>0} c && c.Due>S.Time)
        {
            var target=Nearby(e,100,128).FirstOrDefault(t=>t.Debt is null);
            if(target is not null)target.Debt=new CausalDebt {SourceId=c.SourceId,Due=c.Due,Recorded=c.Recorded*c.Transfer,Cap=c.Cap,Ratio=c.Ratio,Sealed=true,Color=c.Color};
        }
        e.Debt=null;
    }
    private int AbsorbBreach(int lost)
    {
        var x=S.Expansion;int absorbed=Math.Min(lost,x.Shield);x.Shield-=absorbed;
        if(absorbed>0 && x.Shield==0)
        {
            double reaction=S.Board.OfType<DieState>().Select(d=>Value(ProfileFor(d),"barrierReaction")).DefaultIfEmpty(0).Max();
            if(reaction>0) {x.ShieldReactionPower=reaction;x.ShieldReactionUntil=S.Time+2;}
        }
        return lost-absorbed;
    }
    private void TickExpansionEffects(double dt)
    {
        TickLaws(dt);
        foreach(var e in S.Enemies.Where(e=>!e.Dead).ToArray())
        {
            foreach(string key in e.Conditions.Where(p=>p.Value.Until<=S.Time).Select(p=>p.Key).ToArray())e.Conditions.Remove(key);
            foreach(long id in e.Souls.Where(p=>p.Value<=S.Time || SourceDie(p.Key) is null).Select(p=>p.Key).ToArray())e.Souls.Remove(id);
            if(e.Debt is { } debt && debt.Due<=S.Time)
            {
                e.Debt=null;ApplyDamage(e,debt.Recorded*debt.Ratio,debt.Color,debt.SourceId,DamageFlags.Causal|DamageFlags.Settled);
                Emit(new CombatEvent{Type="explosion",X=e.X,Y=e.Y,Radius=30,Color=debt.Color});
            }
        }
        TickGravityFields(dt);S.Expansion.Rifts.RemoveAll(r=>r.Until<=S.Time);PruneVolleys();
    }
    private void TickGravityFields(double dt)
    {
        var fields=S.Expansion.Fields;
        foreach(var e in S.Enemies.Where(e=>!e.Dead).ToArray())
        {
            var pull=fields.Where(f=>f.Until>S.Time-dt && MathEx.Dist2(e.X,e.Y,f.X,f.Y)<=f.Radius*f.Radius)
                .OrderByDescending(f=>f.Pull).ThenBy(f=>MathEx.Dist2(e.X,e.Y,f.X,f.Y)).FirstOrDefault();
            if(pull is null)continue;
            if(e.Kind=="boss") {ApplySlow(e,.9,Math.Min(.2,Math.Max(0,pull.Until-S.Time)));continue;}
            double dx=pull.X-e.X,dy=pull.Y-e.Y,distance=Math.Sqrt(dx*dx+dy*dy),step=Math.Min(distance,pull.Pull*Math.Min(dt,Math.Max(0,pull.Until-(S.Time-dt))));
            if(distance>.001)
            {e.X=Math.Clamp(e.X+dx/distance*step,A.Left+e.W/2,A.Right-e.W/2);e.Y=Math.Max(A.Top+e.H/2,e.Y+dy/distance*step);}
        }
        Grid.Rebuild(S.Enemies);
        foreach(var f in fields.ToArray())
        {
            double elapsed=Math.Min(S.Time,f.Until)-f.LastTick;
            if(elapsed>0 && (elapsed>=.2-1e-8 || f.Until<=S.Time))
            {f.LastTick=Math.Min(S.Time,f.Until);AreaDamage(f.X,f.Y,f.Radius,elapsed*f.Dps,f.Color,sourceId:f.SourceId);}
        }
        FlushDamage();fields.RemoveAll(f=>f.Until<=S.Time);
    }
}
