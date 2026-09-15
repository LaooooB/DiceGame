using System.Text.Json;
using DiceGame.Core;
using Godot;

namespace DiceGame.App;

public partial class GameRoot
{
    // --capture-content selects PreviewStorage in _Ready; no player save is read or written.
    private async void CaptureContent()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath("res://Artifacts/ContentScreenshots");
            Directory.CreateDirectory(path);
            int screens = 0, faces = 0;
            async Task Frames()
            {
                _ui.Invalidate(); _ui.Refresh();
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            async Task Capture(string name)
            {
                await Frames();
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(Path.Combine(path, name + ".png")) != Error.Ok) throw new IOException("Capture failed: " + name);
                _ui.AssertLayout(); screens++; GD.Print("CONTENT CAPTURED " + name);
            }
            foreach (var type in App.Data.Dice)
            for (int pips = 1; pips <= 6; pips++)
            {
                var texture = Art.ContentDie(type.Id, pips);
                if (texture is null || texture.GetWidth() <= 0 || texture.GetHeight() <= 0)
                    throw new InvalidOperationException("Missing dice face " + type.Id + ":" + pips);
                faces++;
            }
            var ordinary=App.Data.Dice.Where(d=>d.Rarity!="mythic").Select(d=>d.Id).ToArray();
            var groups=ordinary.Chunk(6).Concat(App.Data.Dice.Where(d=>d.Rarity=="mythic").Select(d=>new[]{d.Id})).ToArray();
            for (int group = 0; group < groups.Length; group++)
            {
                var deck = groups[group].Concat(App.Data.DefaultDeck).Distinct().Take(6).ToArray();
                App.Action("editDeck"); App.EditingDeck.Clear(); App.EditingDeck.AddRange(deck);
                if (!App.SaveDeck() || !App.StartExpedition(24137)) throw new InvalidOperationException("Content deck failed to launch.");
                var sim = App.Sim!;
                for (int i = 0; i < sim.State.Board.Length; i++)
                {
                    var die = sim.MakeDie(deck[i % 6], 6);
                    die.Tier3 = i / 6 % 2 == 0 ? "A" : "B"; die.Tier6 = i / 6 < 2 ? "C" : "D";
                    sim.State.Board[i] = die;
                }
                sim.Fire(-1.57); for (int i = 0; i < 25; i++) sim.Step(1d / 120); App.ConsumeEvents();
                await Capture("roster_" + (group + 1));
                App.AbandonExpedition(); App.ReturnToTown();
            }
            screens += await CheckExpansionControls(path);
            App.Action("editDeck"); await Frames();
            foreach (string id in new[] { "poison", "mirror", "evolution", "scatter", "blackhole", "reincarnation", "order" })
            {
                var panel = _ui.ShowDiceCatalog(id);
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!panel.IsVisibleInTree() || !GetViewport().GetVisibleRect().Grow(2).Encloses(panel.GetGlobalRect()))
                    throw new InvalidOperationException("Clipped content catalogue: " + id);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(Path.Combine(path, "catalog_" + id + ".png")) != Error.Ok) throw new IOException("Catalogue capture failed.");
                _ui.CloseCatalog(); screens++; GD.Print("CONTENT CAPTURED catalog_" + id);
            }
            File.WriteAllText(Path.Combine(path, "capture-result.json"), JsonSerializer.Serialize(new { completed = true, screens, faces, engine = Engine.GetVersionInfo()["string"].AsString() }));
            Audio.Shutdown();
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"CONTENT CAPTURE COMPLETE | {faces} faces | {screens} screens");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
        }
        catch (Exception ex) { GD.PushError("CONTENT CAPTURE FAILED: " + ex); GetTree().Quit(1); }
    }
}
