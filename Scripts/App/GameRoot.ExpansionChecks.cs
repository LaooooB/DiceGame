using Godot;
using DiceGame.Core;
using System.Text.Json;

namespace DiceGame.App;

public partial class GameRoot
{
    // Native Control signal wiring, isolated from disk by --capture-content's PreviewStorage.
    private async Task<int> CheckExpansionControls(string path)
    {
        int screens=0;var checks=new List<string>();
        IEnumerable<Node> Walk(Node n) {yield return n;foreach(var child in n.GetChildren())foreach(var item in Walk(child))yield return item;}
        void Check(string name,bool ok) {if(!ok)throw new InvalidOperationException(name);checks.Add(name);GD.Print("EXPANSION CONTROL PASS: "+name);}
        async Task Frames()
        {
            _ui.Invalidate();_ui.Refresh();
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        async Task Capture(string name)
        {
            await Frames();using var image=GetViewport().GetTexture().GetImage();
            if(image.SavePng(Path.Combine(path,name+".png"))!=Error.Ok)throw new IOException(name);
            _ui.AssertLayout();screens++;
        }
        void Start(string mythic)
        {
            App.Action("editDeck");App.EditingDeck.Clear();App.EditingDeck.AddRange(new[]{mythic}.Concat(App.Data.DefaultDeck).Take(6));
            if(!App.SaveDeck() || !App.StartExpedition(24137))throw new InvalidOperationException("Expansion control setup failed.");
        }
        Start("order");
        var order=App.Sim!.MakeDie("order",6);order.Tier3="A";order.Tier6="D";App.Sim.State.Board[0]=order;
        await Capture("order_adjudication_ready");
        var button=Walk(_ui).OfType<Button>().Single(b=>b.Text=="裁定 · 下一结果移至袋尾");
        Check("adjudication is visible and enabled for the controlling D branch",button.IsVisibleInTree() && !button.Disabled && App.Sim.CanSkipOrder);
        button.EmitSignal(Button.SignalName.Pressed);await Frames();
        Check("native adjudication button consumes exactly the current stage",!App.Sim!.CanSkipOrder && App.Sim.State.Expansion.OrderSkipStage>=0);
        button=Walk(_ui).OfType<Button>().Single(b=>b.Text=="裁定 · 下一结果移至袋尾");Check("spent adjudication is disabled",button.Disabled);
        App.AbandonExpedition();App.ReturnToTown();

        Start("reincarnation");
        var die=App.Sim!.MakeDie("reincarnation",6);die.Tier3="B";die.Tier6="D";App.Sim.State.Board[0]=die;
        await Frames();var slot=App.Data.SlotPosition(0);App.OnDown(slot.X,slot.Y);App.OnUp(slot.X,slot.Y);await Frames();
        Check("six-pip reincarnation has a native details page",App.Scene=="die");
        var rebirth=Walk(_ui).OfType<Button>().Single(b=>b.Text=="主动转世 · 不返还能量");rebirth.EmitSignal(Button.SignalName.Pressed);await Frames();
        var confirmation=Walk(_ui).OfType<ConfirmationDialog>().Single(d=>d.Visible);
        Check("opening confirmation does not consume the original die",App.Sim.State.Board[0]!.Id==die.Id && App.Sim.State.Expansion.Rebirths==0);
        using(var image=confirmation.GetTexture().GetImage())
            if(image.SavePng(Path.Combine(path,"reincarnation_confirmation.png"))!=Error.Ok)throw new IOException("Confirmation capture failed.");
        screens++;confirmation.GetOkButton().EmitSignal(Button.SignalName.Pressed);await Frames();
        Check("confirm button commits a new two-pip identity and resumes battle",App.Scene=="play" && App.Sim!.State.Board[0]!.Pips==2 && App.Sim.State.Board[0]!.Id!=die.Id && App.Sim.State.Expansion.Rebirths==1);
        await Capture("reincarnation_committed");App.AbandonExpedition();App.ReturnToTown();
        File.WriteAllText(Path.Combine(path,"expansion-control-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,failed=0,checks}));
        return screens;
    }
}
