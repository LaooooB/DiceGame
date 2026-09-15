using DiceGame.Core;

namespace DiceGame.App;

public sealed partial class GameApp
{
    // Mythic planning actions publish only after storage succeeds. A failed write cannot
    // consume a stage's adjudication, a six-pip die, or the deterministic order bag.
    private ActionResult CommitMythicAction(Func<Simulation,ActionResult> action)
    {
        if(Sim is null || LoadProblem!="")return new(false,"unavailable");
        try
        {
            var candidate=Simulation.Restore(Data,Sim.ExportSave());var result=action(candidate);
            if(!result.Ok)return result;
            var snapshot=candidate.ExportSave();
            if(Campaign is not null)
            {if(!CommitCampaign(Campaign,snapshot))return new(false,"storage");}
            else
            {
                var envelope=new SaveEnvelope {Version=SaveCodec.CurrentVersion,Settings=Settings,Meta=Meta,Deck=Deck,Run=snapshot,Preferences=Preferences};
                string json=SaveCodec.Encode(envelope);SaveCodec.Decode(Data,json);_storage.Write(json);ResumeData=snapshot;
            }
            foreach(var e in Sim.TakeEvents())candidate.Emit(e);
            Sim=candidate;_storageFailed=false;Accumulator=0;ConsumeEvents();return result;
        }
        catch(Exception e) {_storageFailed=true;Notify("操作未生效，保存失败："+e.Message,4);return new(false,"storage");}
    }
    public bool UseOrderSkip()
    {
        if(Scene!="play" || Sim?.CanSkipOrder!=true)return false;
        var result=CommitMythicAction(s=>s.SkipOrder());
        if(result.Ok)Notify("裁定已保存：下一结果移到袋尾，本阶段机会已使用。");
        return result.Ok;
    }
    private bool TryRebirthAction(int slot)
    {
        if(Sim is null || slot<0 || slot>=Sim.State.Board.Length || Sim.State.Board[slot] is not { } die || !Sim.CanReincarnate(die))return false;
        if(Scene is not ("die" or "play"))return true;
        var result=CommitMythicAction(s=>s.Recycle(slot));
        if(result.Ok)
        {
            Scene="play";SelectedSlot=-1;
            Notify(result.Reason=="rebirth_cap"?"已转世；本分支奖励次数已用完，没有增加全队加成。":$"已转世并保存，本局全队轮回增伤 +{Sim!.State.Expansion.RebirthPower:P0}。");
        }
        return true;
    }
    public bool CanAddMythic(string id)=>!DiceExpansion.IsMythic(Data,id) || EditingDeck.Count(t=>DiceExpansion.IsMythic(Data,t))<Data.Game.Rules.MaxMythicTypes;
}
