namespace DiceGame.Core;

public sealed partial class Simulation
{
    private void InitializeExpansionProjectile(ProjectileState p)
    {
        if(p.Stats.Auxiliary!=AuxiliaryKind.None)p.Life=4;
        else p.Life=Math.Min(20,p.Life+(p.Child?0:p.Stats.Trait("flightLife")));
    }
    private bool SteerToEnemy(ProjectileState p,double range,double maximumTurn,long exclude=0)
    {
        var target=S.Enemies.Where(e=>!e.Dead && e.Id!=exclude && MathEx.Dist2(p.X,p.Y,e.X,e.Y)<=range*range)
            .OrderBy(e=>MathEx.Dist2(p.X,p.Y,e.X,e.Y)).ThenBy(e=>e.Id).FirstOrDefault();
        if(target is null)return false;
        TurnTowards(p,target.X,target.Y,maximumTurn);return true;
    }
    private static void TurnTowards(ProjectileState p,double x,double y,double maximumTurn)
    {
        double angle=Math.Atan2(p.Vy,p.Vx),target=Math.Atan2(y-p.Y,x-p.X),speed=Math.Sqrt(p.Vx*p.Vx+p.Vy*p.Vy);
        angle+=Math.Clamp(AngleDelta(angle,target),-Math.Max(0,maximumTurn),Math.Max(0,maximumTurn));p.Vx=Math.Cos(angle)*speed;p.Vy=Math.Sin(angle)*speed;
    }
    private void TickExpansionProjectile(ProjectileState p,double dt)
    {
        var q=p.Stats;p.FlightAge=Math.Min(20,p.FlightAge+dt);
        if(q.Auxiliary is AuxiliaryKind.Soul or AuxiliaryKind.Swarm)SteerToEnemy(p,2000,3.5*dt,p.LastEnemy);
        if(q.Auxiliary!=AuxiliaryKind.None)return;
        if(!p.FlightPierceGranted && q.Trait("flightPierce")>0 && p.FlightAge>=q.Trait("flightThreshold",3))
        {p.FlightPierceGranted=true;p.PiercesLeft=Math.Min(8,p.PiercesLeft+(int)q.Trait("flightPierce"));}
        if(!p.SpreePierceGranted && q.Trait("spreePierce")>0 && S.Expansion.Volleys.TryGetValue(q.VolleyId,out var rec) && rec.Kills>=5)
        {p.SpreePierceGranted=true;p.PiercesLeft=Math.Min(8,p.PiercesLeft+(int)q.Trait("spreePierce"));}
        if(q.Trait("magnetEarly")>0 && !p.EarlyGuided && p.WallsHit==0 && p.FlightAge>=.15)
        {p.EarlyGuided=SteerToEnemy(p,q.Trait("magnetRange",100),q.Trait("magnetEarly"));}
        var field=S.Expansion.Fields.Where(f=>f.Until>S.Time && f.Steer>0 && MathEx.Dist2(p.X,p.Y,f.X,f.Y)<=f.Radius*f.Radius)
            .OrderByDescending(f=>f.Steer).ThenBy(f=>MathEx.Dist2(p.X,p.Y,f.X,f.Y)).FirstOrDefault();
        if(field is not null)TurnTowards(p,field.X,field.Y,field.Steer*dt);
    }
    private void ExpansionWall(ProjectileState p,double nx,double ny,double incomingVx,double incomingVy)
    {
        var q=p.Stats;if(q.Auxiliary!=AuxiliaryKind.None)return;
        int wall=ny>0?2:nx>0?0:1;double position=wall==2?p.X:p.Y;
        bool teleported=false;
        if(!p.RiftUsed && !p.Child)
        {
            var rift=S.Expansion.Rifts.Where(r=>r.Until>S.Time && r.Wall==wall && Math.Abs(r.Position-position)<=r.Width)
                .OrderByDescending(r=>r.Power).ThenByDescending(r=>r.Until).FirstOrDefault();
            if(rift is not null)
            {
                p.RiftUsed=true;TeleportProjectile(p,wall,incomingVx,incomingVy);teleported=true;
                q.Damage*=1+rift.Power;q.Volley*=1+rift.Power;p.PiercesLeft=Math.Min(8,p.PiercesLeft+rift.Pierces);
                if(rift.Shock>0)ExpansionExplosion(p.X,p.Y,38,q.Damage*rift.Shock,q.Color,p.SourceDieId);
            }
        }
        if(!p.Child && !q.Echo && q.Trait("riftDuration")>0 && S.Expansion.Volleys.TryGetValue(q.VolleyId,out var volley) && !volley.RiftCreated)
        {
            volley.RiftCreated=true;
            if(S.Expansion.Rifts.Count<DiceExpansion.MaxRifts)S.Expansion.Rifts.Add(new WallRift {Wall=wall,Position=position,Width=q.Trait("riftWidth",18),Until=S.Time+q.Trait("riftDuration"),Power=q.Trait("riftPower"),Pierces=(int)q.Trait("riftPierce"),Shock=q.Trait("riftShock"),Color=q.Color});
        }
        if(!teleported && !p.Child && p.PhaseCount<q.Trait("phaseUses"))
        {
            p.PhaseCount++;TeleportProjectile(p,wall,incomingVx,incomingVy);q.Damage*=1+q.Trait("phasePower");q.Volley*=1+q.Trait("phasePower");p.PiercesLeft=Math.Min(8,p.PiercesLeft+(int)q.Trait("phasePierce"));
            if(p.PhaseCount==1 && q.Trait("phaseChildren")>0)
            {
                for(int i=0;i<2;i++)QueueAuxiliary(p.Type,p.Pips,p.SourceDieId,AuxiliaryStats(q,AuxiliaryKind.Phase,q.Damage*.25),p.X,p.Y,Math.Atan2(p.Vy,p.Vx)+(i==0?-.22:.22),S.Time+.00001);
            }
        }
        if(q.Trait("magnetRange")>0 && !p.WallGuided)p.WallGuided=SteerToEnemy(p,q.Trait("magnetRange"),q.Trait("magnetTurn"));
    }
    private void TeleportProjectile(ProjectileState p,int wall,double vx,double vy)
    {
        double inset=R.ProjectileRadius+.08;
        if(wall==0)p.X=A.Right-inset;else if(wall==1)p.X=A.Left+inset;else p.Y=A.Bottom-inset;
        p.Vx=vx;p.Vy=vy;p.Px=p.X;p.Py=p.Y;p.Trail.Clear();p.LastEnemy=0;p.PassingEnemies.Clear();
        Emit(new CombatEvent {Type="explosion",X=p.X,Y=p.Y,Radius=12,Color=p.Stats.Color});
    }
    private int SwarmCapacity() => Math.Clamp((int)S.Board.OfType<DieState>().Select(d=>Value(ProfileFor(d),"swarmCapacity")).DefaultIfEmpty(0).Max(),0,DiceExpansion.MaxSwarms);
    private void SpawnSwarm(ProjectileState parent,EnemyState from,double damage,int generation)
    {
        int live=S.Projectiles.Count(p=>!p.Dead && p.Stats.Auxiliary==AuxiliaryKind.Swarm)+S.PendingShots.Count(p=>p.Snapshot.Stats.Auxiliary==AuxiliaryKind.Swarm);
        if(live>=SwarmCapacity())return;
        var stats=AuxiliaryStats(parent.Stats,AuxiliaryKind.Swarm,damage);stats.AuxiliaryGeneration=generation;
        stats.AuxiliaryDuplicate=generation==0?parent.Stats.Trait("swarmDuplicate"):0;
        QueueAuxiliary(parent.Type,parent.Pips,parent.SourceDieId,stats,parent.X,parent.Y,Math.Atan2(parent.Vy,parent.Vx),S.Time+.03,from.Id);
    }
}
