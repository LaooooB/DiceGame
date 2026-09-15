using Godot;
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
        { if (!condition) throw new InvalidOperationException("Native input check: " + name); passed.Add(name); }
        async Task Frames()
        { for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        IEnumerable<Node> Walk(Node node)
        { yield return node; foreach (var child in node.GetChildren()) foreach (var item in Walk(child)) yield return item; }
        async Task Mouse(Vector2 position, bool pressed)
        {
            GetViewport().WarpMouse(position);
            Send(new InputEventMouseMotion { Position = position, GlobalPosition = position, ButtonMask = pressed ? MouseButtonMask.Left : 0 });
            Send(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 });
            await Frames();
        }
        async Task Click(string text)
        {
            var button = Walk(_ui).OfType<Button>().First(x => x.Text == text && x.IsVisibleInTree() && !x.Disabled);
            var point = button.GetGlobalRect().GetCenter(); await Mouse(point, true); await Mouse(point, false);
        }
        _allowCaptureInput = true;
        await Frames(); await Click("选择区域"); Check("town button opens region selector", App.Scene == "regions");
        await Click("选择骰子并出发"); Check("region button opens deck", App.Scene == "deck");
        await Click("开始远征"); Check("native launch button enters battle", App.Scene == "play" && App.Sim is not null);
        var field = (Control)_ui.FindChild("BattlefieldInput", true, false);
        var aim = field.GetGlobalRect().GetCenter();
        await Mouse(aim, true);
        Check("native field press enters aim", App.Pointer?.Mode == "aim");
        for (int i = 0; i < 24; i++) App.Tick(1.0 / 120);
        Check("holding does not fire", App.Sim!.State.ManualVolleys == 0);
        await Mouse(aim, false); Check("release fires exactly once", App.Sim.State.ManualVolleys == 1);
        await Mouse(aim, true); await Mouse(new Vector2(5, 5), false);
        Check("release outside cancels", App.Pointer is null && App.Sim.State.ManualVolleys == 1);
        await Mouse(aim, true);
        Send(new InputEventMouseButton { Position = aim, GlobalPosition = aim, ButtonIndex = MouseButton.Right, Pressed = true });
        await Frames(); await Mouse(aim, false);
        Send(new InputEventMouseButton { Position = aim, GlobalPosition = aim, ButtonIndex = MouseButton.Right, Pressed = false });
        Check("right button cancels aim", App.Pointer is null && App.Sim.State.ManualVolleys == 1);
        var a = (Control)_ui.FindChild("Slot0", true, false); var b = (Control)_ui.FindChild("Slot1", true, false);
        await Mouse(a.GetGlobalRect().GetCenter(), true);
        var target = b.GetGlobalRect().GetCenter(); GetViewport().WarpMouse(target);
        Send(new InputEventMouseMotion { Position = target, GlobalPosition = target, ButtonMask = MouseButtonMask.Left }); await Frames();
        Check("native drag enters merge gesture", App.Pointer?.Mode == "drag");
        await Mouse(target, false);
        Check("native merge updates actual simulation", App.Sim.State.Board[0] is null && App.Sim.State.Board[1]?.Pips == 2);
        var empty = a.GetGlobalRect().GetCenter(); await Mouse(empty, true); await Mouse(empty, false);
        Check("native empty-slot click summons there", App.Sim.State.Board[0] is not null);
        Send(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true }); await Frames();
        Send(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
        Check("escape pauses", App.Scene == "paused");
        await Click("继续远征"); Check("native pause button resumes", App.Scene == "play");
        App.AbandonExpedition(); _ui.Invalidate(); await Frames();
        await Click("返回城镇 · 建设与解锁"); Check("native settlement button returns to town", App.Scene == "town" && App.Campaign!.ActiveRunId == "");
        _allowCaptureInput = false;
        File.WriteAllText(Path.Combine(outputPath, "input-checks.json"), JsonSerializer.Serialize(new { passed = passed.Count, failed = 0, checks = passed }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"NATIVE INPUT CHECKS: {passed.Count} passed, 0 failed");
    }
}
