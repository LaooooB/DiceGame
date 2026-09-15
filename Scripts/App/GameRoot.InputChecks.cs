using Godot;
using DiceGame.Core;
using System.Text.Json;

namespace DiceGame.App;

public partial class GameRoot
{
    // Invoked only by --capture-campaign, using PreviewStorage rather than a player's files.
    private async Task CheckNativeInput(string outputPath)
    {
        var passed = new List<string>();
        static void Send(InputEvent input) { using(input) Input.ParseInputEvent(input); }
        void Check(string name, bool condition)
        { if (!condition) throw new InvalidOperationException("Native input check: " + name); passed.Add(name); GD.Print("INPUT PASS: "+name); }
        async Task Frames() { for (int i=0;i<4;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
        IEnumerable<Node> Walk(Node node) { yield return node; foreach(var child in node.GetChildren()) foreach(var item in Walk(child)) yield return item; }
        async Task Mouse(Vector2 position,bool pressed)
        {
            GetViewport().WarpMouse(position);
            Send(new InputEventMouseMotion{Position=position,GlobalPosition=position,ButtonMask=pressed?MouseButtonMask.Left:0});
            Send(new InputEventMouseButton{Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=pressed,ButtonMask=pressed?MouseButtonMask.Left:0});
            await Frames();
        }
        async Task Click(string text)
        {
            var button=Walk(_ui).OfType<Button>().First(x=>x.Text==text&&x.IsVisibleInTree()&&!x.Disabled);
            var point=button.GetGlobalRect().GetCenter();await Mouse(point,true);await Mouse(point,false);
        }

        _allowCaptureInput=true;await Frames();
        await Click("开始游戏");Check("primary town CTA opens expedition prep",App.Scene=="regions");
        await Click("调整骰组");Check("expedition prep opens authored deck page",App.Scene=="deck");
        App.Action("deck:pulse");_ui.Invalidate();await Frames();
        Check("five-die deck disables authored launch",Walk(_ui).OfType<Button>().Single(x=>x.Text=="保存并出发").Disabled);
        App.Action("deck:pulse");_ui.Invalidate();await Frames();
        await Click("保存并出发");Check("authored launch enters battle",App.Scene=="play"&&App.Sim is not null);

        var field=_ui.GetNode<Control>("Margin/Layout/Pages/Battle/Body/Left/FieldAspect/FieldFrame/Field");var aim=field.GetGlobalRect().GetCenter();
        await Mouse(aim,true);Check("native field press enters aim",App.Pointer?.Mode=="aim");for(int i=0;i<24;i++)App.Tick(1d/120);Check("holding does not fire",App.Sim!.State.ManualVolleys==0);await Mouse(aim,false);Check("release fires exactly once",App.Sim.State.ManualVolleys==1);
        await Mouse(aim,true);await Mouse(new Vector2(5,5),false);Check("release outside cancels",App.Pointer is null&&App.Sim.State.ManualVolleys==1);

        var slot0=_ui.GetNode<Control>("Margin/Layout/Pages/Battle/Body/Right/Margin/Content/BoardGrid/Slot0");var slot1=_ui.GetNode<Control>("Margin/Layout/Pages/Battle/Body/Right/Margin/Content/BoardGrid/Slot1");
        App.Sim.State.Board[0]=App.Sim.MakeDie("pulse",1);App.Sim.State.Board[1]=App.Sim.MakeDie("pulse",1);_ui.Invalidate();await Frames();
        await Mouse(slot0.GetGlobalRect().GetCenter(),true);await Mouse(slot0.GetGlobalRect().GetCenter(),false);await Mouse(slot1.GetGlobalRect().GetCenter(),true);await Mouse(slot1.GetGlobalRect().GetCenter(),false);
        Check("click then click performs an actual merge",App.Sim.State.Board[0] is null&&App.Sim.State.Board[1]?.Pips==2);

        App.Sim.State.Energy=100;var slot15=_ui.GetNode<Control>("Margin/Layout/Pages/Battle/Body/Right/Margin/Content/BoardGrid/Slot15");await Mouse(slot15.GetGlobalRect().GetCenter(),true);await Mouse(slot15.GetGlobalRect().GetCenter(),false);Check("fourth-row slot 16 receives native summon click",App.Sim.State.Board[15] is not null);
        _ui.AssertLayout();Check("all 16 authored slots fit inside the actual viewport",Walk(_ui).OfType<Control>().Count(x=>x.Name.ToString().StartsWith("Slot"))>=16);

        async Task MergePair(int pips)
        {
            var sim=App.Sim!;sim.State.PendingShots.Clear();sim.State.Projectiles.Clear();sim.State.PendingSkills.Clear();
            sim.State.Board[0]=sim.MakeDie("pulse",pips);sim.State.Board[1]=sim.MakeDie("pulse",pips);if(pips>=3){sim.State.Board[0]!.Tier3="A";sim.State.Board[1]!.Tier3="B";}
            _ui.Invalidate();await Frames();var source=slot0.GetGlobalRect().GetCenter();var destination=slot1.GetGlobalRect().GetCenter();await Mouse(source,true);GetViewport().WarpMouse(destination);Send(new InputEventMouseMotion{Position=destination,GlobalPosition=destination,ButtonMask=MouseButtonMask.Left});await Frames();Check("drag still enters merge gesture",App.Pointer?.Mode=="drag");await Mouse(destination,false);
        }
        await MergePair(2);Check("merge to three opens result-bound A/B choices",App.Scene=="diceSkill"&&App.Sim!.CurrentSkillChoice!.Tier==3&&App.Sim.CurrentSkillChoice.DiceType==App.Sim.State.Board[1]!.Type);
        double frozen=App.Sim.State.Time;for(int i=0;i<24;i++)App.Tick(1d/120);Check("skill overlay freezes battle time",App.Sim.State.Time==frozen);await Click("选择 A");Check("A branch commits and resumes",App.Scene=="play"&&App.Sim!.State.Board[1]!.Tier3=="A");
        await MergePair(5);Check("six-pip result asks A/B again",App.Scene=="diceSkill"&&App.Sim!.CurrentSkillChoice!.Tier==3&&App.Sim.State.Board[1]!.Tier3=="");await Click("选择 B");Check("six-pip first choice stays paused for C/D",App.Scene=="diceSkill"&&App.Sim!.CurrentSkillChoice!.Tier==6&&App.Sim.State.PendingShots.Count==0);await Click("选择 D");Check("six-pip final choice resumes with B+D",App.Scene=="play"&&App.Sim!.State.Board[1]!.Tier3=="B"&&App.Sim.State.Board[1]!.Tier6=="D"&&App.Sim.State.PendingShots.Count>0);

        Send(new InputEventKey{Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=true});await Frames();Send(new InputEventKey{Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=false});Check("escape pauses",App.Scene=="paused");await Click("继续");Check("authored pause overlay resumes",App.Scene=="play");
        App.AbandonExpedition();_ui.Invalidate();await Frames();await Click("返回城镇");Check("settlement returns to town",App.Scene=="town"&&App.Campaign!.ActiveRunId=="");
        _allowCaptureInput=false;File.WriteAllText(Path.Combine(outputPath,"input-checks.json"),JsonSerializer.Serialize(new{passed=passed.Count,failed=0,checks=passed},new JsonSerializerOptions{WriteIndented=true}));GD.Print($"NATIVE INPUT CHECKS: {passed.Count} passed, 0 failed");
    }
}
