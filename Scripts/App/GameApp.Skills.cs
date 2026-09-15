using DiceGame.Core;

namespace DiceGame.App;

public sealed partial class GameApp
{
    public string DecisionScene => Sim?.State.Over == true ? Catalog is null ? "over" : "settlement"
        : Sim?.AwaitingDiceSkill == true ? "diceSkill" : Sim?.State.AwaitingUpgrade == true ? "upgrade" : "play";

    /// <summary>Persist the chosen branch before publishing it. Disk failure keeps the exact same result and modal.
    /// The choice token prevents a queued double click on level-3 A from being consumed as the next choice.</summary>
    public bool ChooseDiceSkill(long choiceId, string branch)
    {
        if (Sim is null || Scene != "diceSkill") return false;
        try
        {
            var candidate=Simulation.Restore(Data,Sim.ExportSave());
            if(!candidate.ChooseDiceSkill(choiceId,branch)) return false;
            var snapshot=candidate.ExportSave();
            if(Campaign is not null)
            { if(!CommitCampaign(CampaignCatalog.Copy(Campaign),snapshot)) return false; }
            else
            {
                _storage.Write(SaveCodec.Encode(new SaveEnvelope {Version=SaveCodec.CurrentVersion,Settings=Settings,Meta=Meta,Deck=Deck,Run=snapshot}));
                ResumeData=snapshot;_storageFailed=false;
            }
            Sim=candidate;CancelPointer();Accumulator=0;Scene=DecisionScene;ConsumeEvents();return true;
        }
        catch(Exception ex) { _storageFailed=true;Notify("强化未确认，保留当前选择："+ex.Message,4);return false; }
    }
}
