namespace DiceGame.Core;

public sealed partial class Simulation
{
    private uint LawSeed(uint salt) {uint seed=S.Seed^salt;return seed==0?0x91a45be3u:seed;}
    private void EnsureFate()
    {
        if(!R.EnableDiceContent || S.Expansion.FateNumbers.Count>0 || LawLeader("fatePeriod") is not { } die)return;
        RollFate(die);
    }
    private void RollFate(DieState die)
    {
        var x=S.Expansion;var v=ProfileFor(die);var rng=new SeededRandom(x.FateRng==0?LawSeed(0xFA7E129Bu):x.FateRng);
        x.FateNumbers=rng.Shuffle(Enumerable.Range(1,6)).Take(Math.Clamp((int)Value(v,"fateCount",1),1,2)).ToList();x.FateRng=rng.State;
        x.FateMinimumIds.Clear();
        if(Value(v,"fateMinimum")>0)
        {
            int minimum=S.Board.OfType<DieState>().Min(d=>d.Pips);
            x.FateMinimumIds=S.Board.OfType<DieState>().Where(d=>d.Pips==minimum).Select(d=>d.Id).ToList();
        }
        if(Value(v,"fateBlessing")>0 && x.FateNumbers.Contains(die.Pips)) {x.FateBlessingPower=Value(v,"fateBlessing");x.FateBlessingUntil=S.Time+3;}
        Emit(new CombatEvent{Type="conduit",Slot=Array.IndexOf(S.Board,die),Color=Data.Types[die.Type].Color,Surge=true});
    }
    private void TickLaws(double dt)
    {
        EnsureFate();
        if(LawLeader("fatePeriod") is { } fate)
        {
            var x=S.Expansion;double period=Math.Max(1,Value(ProfileFor(fate),"fatePeriod",8));x.FateClock+=dt;
            if(x.FateClock>=period) {x.FateClock%=period;RollFate(fate);}
        }
        int empty=S.Board.Count(d=>d is null);
        foreach(var d in S.Board.OfType<DieState>())
        {
            var v=ProfileFor(d);double period=Value(v,"voidPeriod");
            if(period>0 && empty>=Value(v,"voidBurstThreshold",12)) d.VoidClock=Math.Min(period,d.VoidClock+dt);
        }
    }
    private List<string> NewOrderBag(SeededRandom rng,string last,Dictionary<string,double> profile)
    {
        var bag=rng.Shuffle(S.Deck);
        if(Value(profile,"orderNoRepeat")>0 && bag.Count>1 && bag[0]==last)
        {int j=1+rng.Int(bag.Count-1);(bag[0],bag[j])=(bag[j],bag[0]);}
        return bag;
    }
    private string NextMergeType()
    {
        if(!R.EnableDiceContent || LawLeader("orderLaw") is not { } leader)return Random.Pick(S.Deck);
        var x=S.Expansion;
        if(x.OrderBag.Count==0)
        {
            var rng=new SeededRandom(x.OrderRng==0?LawSeed(0x0D3E621Du):x.OrderRng);
            x.OrderBag=NewOrderBag(rng,x.OrderLast,ProfileFor(leader));x.OrderRng=rng.State;
        }
        string type=x.OrderBag[0];x.OrderBag.RemoveAt(0);x.OrderLast=type;return type;
    }
    public IReadOnlyList<string> OrderPreview()
    {
        if(!R.EnableDiceContent || LawLeader("orderLaw") is not { } leader)return [];
        var v=ProfileFor(leader);int count=Math.Clamp((int)Value(v,"orderPreview"),0,3);
        // This works across a bag boundary without writing the bag OR either PRNG.
        var bag=S.Expansion.OrderBag.ToList();string last=S.Expansion.OrderLast;
        var rng=new SeededRandom(S.Expansion.OrderRng==0?LawSeed(0x0D3E621Du):S.Expansion.OrderRng);var result=new List<string>();
        for(int i=0;i<count;i++)
        {if(bag.Count==0)bag=NewOrderBag(rng,last,v);last=bag[0];result.Add(last);bag.RemoveAt(0);}
        return result;
    }
    private long OrderStage()
    {
        if(S.Expedition is {LegacyRules:false} e)
            return (long)((S.Wave-1)/e.Region.TotalWaves)*e.Region.Phases.Length+e.Region.PhaseIndex(e.LocalWave(S.Wave));
        return (S.Wave-1)/Math.Max(1,R.BossEvery);
    }
    public bool CanSkipOrder => R.EnableDiceContent && !S.Over && !S.AwaitingUpgrade && !AwaitingDiceSkill &&
        LawLeader("orderLaw") is { } d && Value(ProfileFor(d),"orderSkip")>0 && S.Expansion.OrderSkipStage!=OrderStage() && S.Expansion.OrderBag.Count!=1;
    public ActionResult SkipOrder()
    {
        if(!CanSkipOrder)return new(false,"unavailable");
        var x=S.Expansion;var leader=LawLeader("orderLaw")!;
        if(x.OrderBag.Count==0)
        {
            var rng=new SeededRandom(x.OrderRng==0?LawSeed(0x0D3E621Du):x.OrderRng);
            x.OrderBag=NewOrderBag(rng,x.OrderLast,ProfileFor(leader));x.OrderRng=rng.State;
        }
        string first=x.OrderBag[0];x.OrderBag.RemoveAt(0);x.OrderBag.Add(first);x.OrderSkipStage=OrderStage();
        Emit(new CombatEvent{Type="conduit",Slot=Array.IndexOf(S.Board,leader),Color=Data.Types[leader.Type].Color,Surge=true});return new(true);
    }
    public bool CanReincarnate(DieState die)=>R.EnableDiceContent && die.Pips==R.MaxPips && Value(ProfileFor(die),"rebirth")>0;
    private ActionResult Reincarnate(int index,DieState die)
    {
        var v=ProfileFor(die);var x=S.Expansion;bool rewarded=x.Rebirths<(int)Value(v,"rebirthCap",4);
        if(rewarded) {x.Rebirths++;x.RebirthPower=Math.Min(.36,x.RebirthPower+Value(v,"rebirthGain",.05));}
        var newborn=MakeDie(die.Type,Math.Clamp((int)Value(v,"rebirthStart",1),1,2));
        S.Board[index]=newborn;_contentProfiles.Remove(die.Id);PruneVolleys();
        Emit(new CombatEvent{Type="summon",Slot=index,Die=newborn.Copy(),Surge=true});
        return new(true,rewarded?"rebirth":"rebirth_cap",Slot:index,Die:newborn);
    }
    public string ExpansionStatus(DieState die)
    {
        var v=ProfileFor(die);var rows=new List<string>();
        if(Value(v,"aimTolerance")>0)rows.Add($"校准 {die.AimStacks}/{Value(v,"aimCap",8):0}");
        if(Value(v,"beatPeriod")>0)rows.Add($"节拍 {die.NormalAttacks%(int)Value(v,"beatPeriod")}/{Value(v,"beatPeriod"):0} · 余韵 {die.BeatRemainders}");
        if(Value(v,"executeNext")>0)rows.Add($"待用连斩 {die.ExecuteStacks}/5");
        foreach(var (key,name) in new[]{("reactorNeed","堆芯"),("forgeNeed","锻造"),("soulNeed","灵魂"),("barrierHits","护盾命中")})
            if(Value(v,key)>0)rows.Add($"{name} {die.MechanicEnergy:0}/{Value(v,key):0}");
        if(Value(v,"spreeStep")>0)rows.Add($"当前连杀 {(S.Expansion.Volleys.TryGetValue(die.LatestVolley,out var rec)?rec.Kills:0)} 层");
        if(Value(v,"thronePower")>0)rows.Add(WearsCrown(die)?"持有王冠":"未持有王冠");
        if(Value(v,"voidPeriod")>0)rows.Add($"湮灭 {die.VoidClock:0.0}/{Value(v,"voidPeriod"):0}秒");
        if(CanReincarnate(die))rows.Add(S.Expansion.Rebirths<Value(v,"rebirthCap",4)?$"转世：变为{Value(v,"rebirthStart",1):0}点，本局全队 +{Value(v,"rebirthGain",.05):P0}（总上限36%），不返还能量。":"已到本分支轮回上限；仍可转世，但不再增加永久加成。");
        return string.Join("\n",rows);
    }
    public string GlobalExpansionStatus()
    {
        var x=S.Expansion;var rows=new List<string>();
        if(x.Shield>0 || LawLeader("barrierHits") is not null)rows.Add($"屏障 {x.Shield}/5");
        if(LawLeader("fatePeriod") is { } f)rows.Add($"命运：{string.Join(" / ",x.FateNumbers)} · {Math.Max(0,Value(ProfileFor(f),"fatePeriod")-x.FateClock):0.0}秒");
        if(LawLeader("voidCap") is not null)rows.Add($"虚无空位 {S.Board.Count(d=>d is null)}/{S.Board.Length}");
        if(x.Rebirths>0)rows.Add($"轮回 {x.Rebirths}次 · 全队 +{x.RebirthPower:P0}");
        if(LawLeader("orderLaw") is not null)
        {
            var peek=OrderPreview();rows.Add(peek.Count>0?"合成预言："+string.Join(" → ",peek.Select(id=>Data.Types[id].Name)):"秩序：六骰洗牌袋");
            if(LawLeader("orderLaw") is { } o && Value(ProfileFor(o),"orderSkip")>0)rows.Add(x.OrderSkipStage==OrderStage()?"裁定：本阶段已使用":x.OrderBag.Count==1?"裁定：袋中只剩一张":"裁定：本阶段可用");
        }
        return string.Join("\n",rows);
    }
}
