namespace DiceGame.Core;

public sealed partial class Simulation
{
    // Events collected across one release are applied together, so board iteration order
    // cannot decide whether a reactor/forge empowers an ally in the very same release.
    private readonly List<(long Id, int Slot, double Return, double Blessing)> _released = [];
    private bool _collectingRelease;
    private IEnumerable<int> FormationSlots(int slot, bool diagonal)
    {
        if (!diagonal) return AdjacentSlots(slot);
        if (slot < 0) return [];
        int row=slot/C.Board.Columns, col=slot%C.Board.Columns;
        return Enumerable.Range(0,S.Board.Length).Where(i=>i!=slot && Math.Abs(i/C.Board.Columns-row)<=1 && Math.Abs(i%C.Board.Columns-col)<=1);
    }
    private bool WearsCrown(DieState die, Dictionary<string,double>? profile=null)
    {
        var v=profile??ProfileFor(die);
        return Value(v,"thronePower")>0 && S.Board.OfType<DieState>().All(n=>n.Id==die.Id || (Value(v,"throneTies")>0 ? n.Pips<=die.Pips : n.Pips<die.Pips));
    }
    private DieState? LawLeader(string feature, DieState? preview=null) => S.Board.OfType<DieState>()
        .Select(d=>preview is not null && d.Id==preview.Id ? preview : d)
        .Where(d=>Value(ProfileFor(d),feature)>0).OrderByDescending(d=>d.Pips).ThenBy(d=>d.Id).FirstOrDefault();
    private double ExpansionAura(DieState source,DieState target,int from,int to)
    {
        var v=ProfileFor(source); double amount=0;
        if(Value(v,"formationAura")>0 && FormationSlots(from,Value(v,"formationDiagonal")>0).Contains(to)) amount=Value(v,"formationAura");
        if(IsAdjacent(from,to) && WearsCrown(source,v)) amount=Math.Max(amount,Value(v,"throneAura"));
        return amount;
    }
    private void ExpansionStats(DieState die,ShotStats q,int slot)
    {
        var v=q.Traits; double factor=1;
        int kinds=FormationSlots(slot,Value(v,"formationDiagonal")>0).Select(i=>S.Board[i]?.Type).Where(t=>t is not null).Distinct().Count();
        factor*=1+Math.Min(Value(v,"formationDiagonal")>0?6:4,kinds)*Value(v,"formationStep");
        factor*=1+Math.Min(die.AimStacks,Value(v,"aimCap",8))*Value(v,"aimStep");
        if(Value(v,"aimPierce")>0 && die.AimStacks>=Value(v,"aimCap",8)) q.Pierces+=(int)Value(v,"aimPierce");
        if(Value(v,"barrierFullDamage")>0 && S.Expansion.Shield>=Value(v,"barrierCapacity",3)) factor*=1+Value(v,"barrierFullDamage");
        if(WearsCrown(die,v)) factor*=1+Value(v,"thronePower");
        if(LawLeader("fatePeriod",die) is { } fate)
        {
            var f=ProfileFor(fate);
            if(S.Expansion.FateNumbers.Contains(die.Pips) || Value(f,"fateMinimum")>0 && S.Expansion.FateMinimumIds.Contains(die.Id)) factor*=1+Value(f,"fatePower");
            if(S.Expansion.FateBlessingUntil>S.Time) factor*=1+S.Expansion.FateBlessingPower;
        }
        if(LawLeader("voidCap",die) is { } empty)
        {
            var e=ProfileFor(empty); int spaces=Math.Min((int)Value(e,"voidCap",12),S.Board.Count(d=>d is null));
            factor*=1+spaces*(Value(v,"voidCap")>0?Value(v,"voidSelf"):Value(e,"voidTeam"));
            if(spaces>=Value(e,"voidThreshold",8)) q.Pierces+=(int)Value(e,"voidPierce");
        }
        factor*=1+S.Expansion.RebirthPower;
        q.Volley*=Math.Clamp(factor,.05,100);
        q.Count=Math.Clamp(q.Count+(int)Value(v,"scatterSides"),1,18);
        q.Pierces=Math.Clamp(q.Pierces,0,8); q.Damage=q.Volley/q.Count;
    }
    private ShotStats PrepareExpansionVolley(DieState die,int slot,double angle,bool surge)
    {
        var v=Value(ProfileFor(die),"mirror")>0 ? Stats(die).Traits : ProfileFor(die);
        if(!surge)
        {
            die.NormalAttacks=Math.Min(1000000000000,die.NormalAttacks+1);
            if(Value(v,"aimTolerance")>0)
            {
                if(die.HasAimMemory)
                    die.AimStacks=Math.Abs(AngleDelta(die.AimMemory,angle))<=Value(v,"aimTolerance")+1e-8
                        ? Math.Min((int)Value(v,"aimCap",8),die.AimStacks+1) : (int)Math.Floor(die.AimStacks*Value(v,"aimKeep"));
                die.AimMemory=angle; die.HasAimMemory=true;
            }
        }
        var q=Stats(die); v=q.Traits;
        if(!surge)
        {
            double factor=1;
            if(Value(v,"executeNext")>0) { factor*=1+die.ExecuteStacks*Value(v,"executeNext"); die.ExecuteStacks=0; }
            if(die.BeatRemainders>0) { q.Reload*=1-Value(v,"beatHaste"); die.BeatRemainders--; }
            if(Value(v,"beatPeriod")>0 && die.NormalAttacks%Math.Max(1,(int)Value(v,"beatPeriod"))==0)
            { factor*=1+Value(v,"beatPower"); q.Pierces+=(int)Value(v,"beatPierce"); if(Value(v,"beatHaste")>0) die.BeatRemainders=2; }
            if(Value(v,"formationBurst")>0 && die.NormalAttacks%3==0 && AdjacentSlots(slot).Count()==4 &&
                AdjacentSlots(slot).Select(i=>S.Board[i]?.Type).Where(t=>t is not null).Distinct().Count()==4) factor*=1+Value(v,"formationBurst");
            if(Value(v,"throneBurst")>0 && WearsCrown(die,v) && die.NormalAttacks%4==0) {factor*=1+Value(v,"throneBurst");q.Pierces+=3;}
            double returnEnergy=0,blessing=0;
            if(Value(v,"reactorNeed")>0 && die.MechanicEnergy>=Value(v,"reactorNeed"))
            {
                die.MechanicEnergy-=Value(v,"reactorNeed"); factor*=1+Value(v,"reactorPower"); q.ReactorCharged=true; returnEnergy=Value(v,"reactorReturn");
            }
            if(Value(v,"forgeNeed")>0 && die.MechanicEnergy>=Value(v,"forgeNeed"))
            {
                die.MechanicEnergy-=Value(v,"forgeNeed"); factor*=1+Value(v,"forgePower"); q.Pierces+=(int)Value(v,"forgePierce"); q.Bounces+=(int)Value(v,"forgeBounces"); blessing=Value(v,"forgeBlessing");
            }
            if(Value(v,"voidPeriod")>0 && die.VoidClock>=Value(v,"voidPeriod") && S.Board.Count(d=>d is null)>=Value(v,"voidBurstThreshold",12))
            { die.VoidClock=0; q.VoidCharged=true; }
            if(_collectingRelease)_released.Add((die.Id,slot,returnEnergy,blessing));
            q.Volley*=factor;
        }
        if(Value(v,"spreeStep")>0 || Value(v,"scatterFocus")>0 || Value(v,"riftDuration")>0)
        {
            PruneVolleys();
            if(S.Expansion.Volleys.Count>=DiceExpansion.MaxVolleys) throw new InvalidOperationException("Volley ledger budget invariant failed.");
            int carry=S.Expansion.Volleys.TryGetValue(die.LatestVolley,out var old)?(int)Math.Floor(old.Kills*Value(v,"spreeKeep")):0;
            q.VolleyId=S.NextId++; S.Expansion.Volleys[q.VolleyId]=new VolleyRecord { SourceId=die.Id,Kills=Math.Min((int)Value(v,"spreeCap",8),carry),Step=Value(v,"spreeStep"),Cap=(int)Value(v,"spreeCap",8),Elite=(int)Value(v,"spreeElite") };
            die.LatestVolley=q.VolleyId;
        }
        q.Pierces=Math.Clamp(q.Pierces,0,8);q.Bounces=Math.Clamp(q.Bounces,1,36);q.Reload=Math.Clamp(q.Reload,.06,20);q.Damage=q.Volley/q.Count;
        die.IssuedReload=q.Reload;
        return q;
    }
    private void DecoratePellet(ShotStats captured,int k,int count,int copy)
    {
        int sides=(int)captured.Trait("scatterSides"),centerCount=count-sides;
        captured.Echo=copy>0;captured.SidePellet=sides>0 && k>=centerCount;captured.FirstCenter=k==0;
        if(captured.SidePellet) captured.Bounces=Math.Min(36,captured.Bounces+(int)captured.Trait("scatterSideBounces"));
        if(captured.FirstCenter && sides>0)
        {captured.Damage*=1+captured.Trait("scatterCenterDamage");captured.Pierces=Math.Min(8,captured.Pierces+(int)captured.Trait("scatterCenterPierce"));}
    }
    private double PelletAngle(ShotStats q,int k,double angle)
    {
        int sides=(int)q.Trait("scatterSides"),sideIndex=k-(q.Count-sides);
        if(sides<=0 || sideIndex<0) return angle;
        int pairs=Math.Max(1,sides/2);double magnitude=(sideIndex/2+1)/(double)pairs*q.Trait("scatterAngle");
        return ClampAim(angle+(sideIndex%2==0?-magnitude:magnitude));
    }
    private void CompleteExpansionRelease()
    {
        foreach(var die in S.Board.OfType<DieState>())
        {
            int slot=Array.IndexOf(S.Board,die);var v=ProfileFor(die);
            if(Value(v,"reactorNeed")>0) die.MechanicEnergy=Math.Min(Value(v,"reactorNeed")*2,die.MechanicEnergy+_released.Count(e=>IsAdjacent(e.Slot,slot)));
            double kick=0,bless=0;
            foreach(var e in _released) if(IsAdjacent(e.Slot,slot)) {kick=Math.Max(kick,e.Return);bless=Math.Max(bless,e.Blessing);}
            die.Cooldown=Math.Max(0,die.Cooldown-kick);
            if(bless>0 && (die.ForgeBlessingUntil<=S.Time || bless>=die.ForgeBlessingPower)) {die.ForgeBlessingPower=bless;die.ForgeBlessingUntil=S.Time+3;}
        }
        _released.Clear();
    }
    private void ExpansionMerged(int resultSlot)
    {
        foreach(int slot in AdjacentSlots(resultSlot)) if(S.Board[slot] is { } die)
        {
            double need=Value(ProfileFor(die),"forgeNeed");if(need>0) die.MechanicEnergy=Math.Min(need*2,die.MechanicEnergy+1);
        }
        EnsureFate();PruneVolleys();
    }
    private void PruneVolleys()
    {
        if(S.Expansion.Volleys.Count==0)return;
        var live=S.Projectiles.Where(p=>!p.Dead).Select(p=>p.Stats.VolleyId)
            .Concat(S.PendingShots.Select(p=>p.Snapshot.Stats.VolleyId)).Concat(S.Board.OfType<DieState>().Select(d=>d.LatestVolley)).Concat(S.DamageQueue.Select(h=>h.VolleyId)).ToHashSet();
        foreach(long id in S.Expansion.Volleys.Keys.Where(id=>!live.Contains(id)).ToArray())S.Expansion.Volleys.Remove(id);
        var enemies=S.Enemies.Where(e=>!e.Dead).Select(e=>e.Id).ToHashSet();
        foreach(var v in S.Expansion.Volleys.Values)foreach(long id in v.FocusCounts.Keys.Where(id=>!enemies.Contains(id)).ToArray())v.FocusCounts.Remove(id);
    }
    private int ExtraVolleyReservation(DieState die,ShotStats q) => q.Trait("soulNeed")>0 && die.MechanicEnergy>=q.Trait("soulNeed") ? (int)q.Trait("soulCount",3) : 0;
    private void QueueSoulVolley(DieState die,ShotStats q,double angle,bool surge)
    {
        if(surge || q.Trait("soulNeed")<=0 || die.MechanicEnergy<q.Trait("soulNeed"))return;
        int count=Math.Clamp((int)q.Trait("soulCount",3),1,6);
        // Reservation is checked before counters/energy change. Auxiliary shots use the same queue budget.
        if(S.PendingShots.Count+count>R.MaxQueuedShots)return;
        die.MechanicEnergy=Math.Max(0,die.MechanicEnergy-Math.Ceiling(q.Trait("soulNeed")*(1-q.Trait("soulKeep"))));
        for(int i=0;i<count;i++)
        {
            var a=AuxiliaryStats(q,AuxiliaryKind.Soul,q.Volley*q.Trait("soulDamage"));a.AuxiliaryMarkSeconds=q.Trait("soulTransfer")>0?q.Trait("soulMark"):0;
            QueueAuxiliary(die.Type,die.Pips,die.Id,a,216,A.LaunchY,ClampAim(angle+(i-(count-1)/2.0)*.1),S.Time+.09+i*.04);
        }
    }
    private ShotStats AuxiliaryStats(ShotStats parent,AuxiliaryKind kind,double damage)
    {
        return new ShotStats {AttackType=parent.AttackType,Effect="pulse",Damage=Math.Min(1e18,damage),Volley=Math.Min(1e18,damage),Count=1,Reload=parent.Reload,Bounces=4,Color=parent.Color,SlowFactor=1,MaxBoost=1,Auxiliary=kind};
    }
    private bool QueueAuxiliary(string type,int pips,long source,ShotStats stats,double x,double y,double angle,double due,long lastEnemy=0)
    {
        if(S.PendingShots.Count>=R.MaxQueuedShots)return false;
        S.PendingShots.Add(new PendingShot {Due=due,Angle=angle,Child=true,X=x,Y=y,LastEnemy=lastEnemy,Snapshot=new ShotSnapshot{Type=type,Pips=pips,SourceDieId=source,Stats=stats}});
        SortPending();return true;
    }
    private static double AngleDelta(double from,double to)=>Math.Atan2(Math.Sin(to-from),Math.Cos(to-from));
}
