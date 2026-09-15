using Godot;
using DiceGame.Core;
using DiceGame.Platform;
using DiceGame.Presentation;
using DiceGame.UI;
using System.Text.Json;

namespace DiceGame.App;

/// <summary>Desktop composition root. The reference renderer and 120 Hz simulation retain their logical coordinates.</summary>
public partial class GameRoot : Node
{
    public GameApp App { get; private set; } = null!;
    public NativeArt Art { get; private set; } = null!;
    public NativeAudio Audio { get; private set; } = null!;
    public SubViewport Battlefield { get; private set; } = null!;
    public DesktopStorage Storage { get; private set; } = null!;
    private CampaignUi _ui = null!;
    private Window _window = null!;
    private readonly List<PaintLayer> _layers = [];
    private bool _ready, _capture, _allowCaptureInput, _exitPrompt;
    private DesktopPreferences? _videoRollback, _videoPending;
    private ConfirmationDialog? _videoDialog;
    private double _videoSeconds;
    private NativeRenderer _renderer = null!;

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--capture-reference"))
        { AddChild(new ReferenceRoot { Name = "ReferenceCaptureOnly" }); return; }
        try
        {
            _window = GetWindow(); _window.MinSize = new Vector2I(960, 640); GetTree().AutoAcceptQuit = false;
            var data = new GameData(Read("game"), Read("dice"), Read("upgrades"), Read("dice_skills"));
            var catalog = new CampaignCatalog(data, Read("campaign"));
            Art = new NativeArt(); Audio = new NativeAudio { Name = "NativeAudio" }; AddChild(Audio);
            Storage = new DesktopStorage(data); _capture = OS.GetCmdlineUserArgs().Any(a => a is "--capture-campaign" or "--capture-content");
            App = new GameApp(data, _capture ? new PreviewStorage() : Storage, Audio, catalog) { NativeUi = true };
            _renderer = new NativeRenderer(App);
            Battlefield = new SubViewport { Name = "Battlefield", Size = new Vector2I(764, 888), Disable3D = true,
                TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, RenderTargetClearMode = SubViewport.ClearMode.Always };
            AddChild(Battlefield);
            var logical = new Node2D { Name = "OriginalBattleCoordinates", Scale = Vector2.One * 2, Position = new Vector2(-50, -264) };
            var combatClip = new Control { Name = "OriginalCombatClip", Size = new Vector2(764,810), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            Battlefield.AddChild(combatClip); combatClip.AddChild(logical);
            Layer(logical, "WallsEnemiesAim", _renderer.FieldBelow);
            Layer(logical, "AdditiveProjectiles", _renderer.Projectiles).Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            Layer(logical, "CombatEffects", _renderer.FieldAbove);
            var launcherCoordinates = new Node2D { Name = "LauncherCoordinates", Scale = Vector2.One * 2, Position = new Vector2(-50,-264) };
            Battlefield.AddChild(launcherCoordinates); Layer(launcherCoordinates,"Launcher",_renderer.BattleLauncher);
            _ui = new CampaignUi { Name = "NativeDesktopUI" }; AddChild(_ui); _ui.Initialize(this);
            ApplyPreferences(App.Preferences);
            _window.CloseRequested += Quit; _window.FocusExited += LoseFocus; _window.MouseExited += CancelInput;
            GetViewport().SizeChanged += CancelInput; _ready = true;
            GD.Print("CAMPAIGN READY | Godot 4.6 Mono | 1920x1080 | native Control UI | 120Hz combat");
            if (_capture) { if (OS.GetCmdlineUserArgs().Contains("--capture-content")) Callable.From(CaptureContent).CallDeferred(); else Callable.From(CaptureCampaign).CallDeferred(); }
        }
        catch (Exception ex)
        {
            GD.PushError("Startup failed: " + ex);
            var panel = new Label { Text = "工程启动失败\n\n" + ex.Message + "\n\n请查看 Godot 输出面板。原存档不会被重置。", Position = new Vector2(40, 40), Size = new Vector2(1200, 600), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            AddChild(panel); if (_capture) GetTree().Quit(1);
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString($"res://Data/{name}.json");
    private PaintLayer Layer(Node parent, string name, Action<NativeCanvas> draw)
    { var layer = new PaintLayer { Name = name }; parent.AddChild(layer); layer.Initialize(Art, draw); _layers.Add(layer); return layer; }
    public override void _Process(double delta)
    {
        if (!_ready) return;
        if (!_capture) App.Tick(delta);
        Audio.Pump(); foreach (var layer in _layers) layer.QueueRedraw(); _ui.Refresh();
        if (_videoRollback is not null)
        {
            _videoSeconds -= Math.Max(0, delta);
            if (_videoDialog is not null) _videoDialog.DialogText = $"保留新的显示设置？\n{Math.Max(0, (int)Math.Ceiling(_videoSeconds))} 秒后自动恢复。";
            if (_videoSeconds <= 0) FinishVideo(false);
        }
    }
    public override void _Input(InputEvent ev)
    {
        if (!_ready || (_capture && !_allowCaptureInput)) return;
        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (_ui.HandleKeyBinding(key)) { GetViewport().SetInputAsHandled(); return; }
            if (_videoRollback is not null) return;
            var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
            bool Is(string action) => Enum.TryParse<Key>(App.Preferences.Bindings[action], true, out var binding) && code == binding;
            if (Is("fullscreen"))
            {
                var p = CampaignCatalog.Copy(App.Preferences); p.WindowMode = p.WindowMode == "windowed" ? "borderless" : "windowed";
                PreviewPreferences(p); GetViewport().SetInputAsHandled();
            }
            else if (App.Scene != "settings" && Is("mute")) { App.Audio.Unlock(); App.Action("sound"); GetViewport().SetInputAsHandled(); }
            else if (App.Scene == "play" && Is("summon")) { App.Audio.Unlock(); App.Summon(); GetViewport().SetInputAsHandled(); }
            else if (Is("pause") || code == Key.Escape)
            { _ui.Back(); GetViewport().SetInputAsHandled(); }
        }
        else if (ev is InputEventMouseButton b)
        {
            if (b.ButtonIndex == MouseButton.Right && b.Pressed) CancelInput();
            else if (b.ButtonIndex == MouseButton.Left && !b.Pressed) _ui.ReleasePointer(b.Position);
        }
        else if (ev is InputEventMouseMotion m) _ui.MovePointer(m.Position);
    }
    public void PreviewPreferences(DesktopPreferences preferences)
    {
        if (_videoRollback is not null) return;
        try
        {
            preferences.Validate(); App.CancelPointer();
            var old = App.Preferences;
            bool displayChanged = old.Width != preferences.Width || old.Height != preferences.Height || old.WindowMode != preferences.WindowMode;
            if (!displayChanged)
            { if (App.SetPreferences(preferences)) { ApplyPreferences(preferences); App.Notify("设置已保存。"); _ui.Invalidate(); } return; }
            _videoRollback = CampaignCatalog.Copy(old); _videoPending = CampaignCatalog.Copy(preferences); _videoSeconds = 15;
            ApplyPreferences(preferences);
            _videoDialog = new ConfirmationDialog { Title = "确认显示设置", OkButtonText = "保留", CancelButtonText = "恢复", DialogText = "保留新的显示设置？" };
            AddChild(_videoDialog); _videoDialog.Confirmed += () => FinishVideo(true); _videoDialog.Canceled += () => FinishVideo(false);
            _videoDialog.PopupCentered(new Vector2I(540, 220));
        }
        catch (Exception ex) { if (_videoRollback is not null) FinishVideo(false); App.Notify("设置未应用：" + ex.Message, 4); }
    }
    private void FinishVideo(bool keep)
    {
        if (_videoRollback is null) return;
        var rollback = _videoRollback; var pending = _videoPending!; _videoRollback = null; _videoPending = null;
        if (_videoDialog is not null) { _videoDialog.Hide(); _videoDialog.QueueFree(); _videoDialog = null; }
        bool saved = keep && App.SetPreferences(pending); ApplyPreferences(saved ? pending : rollback); _ui.Invalidate();
        App.Notify(saved ? "显示设置已保存。" : "已恢复原显示设置。");
    }
    public void ApplyPreferences(DesktopPreferences p)
    {
        p.Validate(); Audio.MasterGain = p.MasterVolume; Audio.SfxGain = p.SfxVolume; Audio.MusicGain = p.MusicVolume;
        Engine.MaxFps = p.MaxFps;
        DisplayServer.WindowSetVsyncMode(p.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Battlefield.Msaa2D = RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility" ? Viewport.Msaa.Disabled : (Viewport.Msaa)p.Msaa;
        _window.Borderless = p.WindowMode == "borderless";
        if (p.WindowMode is "borderless" or "fullscreen") _window.Mode = p.WindowMode=="fullscreen"?Window.ModeEnum.ExclusiveFullscreen:Window.ModeEnum.Fullscreen;
        else
        {
            _window.Mode = Window.ModeEnum.Windowed;
            var usable = DisplayServer.ScreenGetUsableRect(_window.CurrentScreen);
            _window.Size = new Vector2I(Math.Min(p.Width, Math.Max(960, usable.Size.X)), Math.Min(p.Height, Math.Max(640, usable.Size.Y)));
            _window.Position = usable.Position + (usable.Size - _window.Size) / 2;
        }
        _ui.Theme = UiKit.Theme(Art, p.UiScale); UiKit.ScaleTypography(_ui,p.UiScale);
    }
    private void CancelInput() { if (_ready) { App.CancelPointer(); _ui.CancelTownAim(); } }
    private void LoseFocus()
    {
        if (!_ready || _capture) return;
        if (_videoRollback is not null) FinishVideo(false);
        _ui.CancelTownAim(); App.OnFocusLost();
    }
    public void Quit()
    {
        if (_exitPrompt) return;
        if (_ready && !_capture)
        {
            if (_videoRollback is not null) FinishVideo(false); App.Save();
            if(App.StorageFailed && App.LoadProblem=="")
            {
                _exitPrompt=true; App.OnFocusLost();
                var dialog = new ConfirmationDialog {Title="无法保存",DialogText="存档写入失败。现在退出会丢失最近尚未成功保存的进度。仍要退出？",OkButtonText="仍然退出",CancelButtonText="留在游戏"};
                AddChild(dialog); dialog.Confirmed+=()=>GetTree().Quit(); dialog.Canceled+=()=>{_exitPrompt=false;dialog.QueueFree();};dialog.PopupCentered(new Vector2I(650,250));return;
            }
        }
        GetTree().Quit();
    }
    public void BackupSave()
    { try { App.Save(); App.Notify("已备份：" + Storage.Backup(), 6); } catch (Exception ex) { App.Notify("备份失败：" + ex.Message, 5); } }
    public void OpenSaveFolder() => OS.ShellOpen(Storage.DirectoryPath);
    public void ResetProgress()
    {
        try { Storage.ArchiveAndReset(); _ready = false; GetTree().ReloadCurrentScene(); }
        catch (Exception ex) { _ready = true; App.Notify("重置失败，原文件未丢弃：" + ex.Message, 5); }
    }
    public override void _ExitTree()
    {
        if (_window is not null)
        {
            _window.CloseRequested -= Quit; _window.FocusExited -= LoseFocus; _window.MouseExited -= CancelInput; GetViewport().SizeChanged -= CancelInput;
        }
        if (_ready && !_capture) App.Save();
    }
    private sealed class PreviewStorage : IDesktopStorage
    { public string? Read() => null; public void Write(string value) { } public uint NewSeed() => 24137; }
    /// <summary>Isolated UI smoke/capture harness; never opens the player's real save.</summary>
    private async void CaptureCampaign()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath("res://Artifacts/CampaignScreenshots"); Directory.CreateDirectory(path);
            bool layoutOnly=OS.GetCmdlineUserArgs().Contains("--layout-only");
            if(!layoutOnly) await CheckNativeInput(path);
            async Task Capture(string name)
            {
                if(layoutOnly && name!="battle_large_text") return;
                GD.Print("CAPTURING " + name);
                _ui.Invalidate(); _ui.Refresh(); foreach (var layer in _layers) layer.QueueRedraw();
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(Path.Combine(path, name + ".png")) != Error.Ok) throw new IOException("Capture failed: " + name);
                _ui.AssertLayout(); GD.Print("CAPTURED " + name);
            }
            await Capture("town"); App.Scene = "regions"; await Capture("regions"); App.Action("editDeck"); await Capture("deck");
            App.Scene = "town"; App.StartExpedition(24137); await Capture("battle");
            var simulation=App.Sim!;
            for(int i=0;i<App.Data.Game.Board.Slots;i++)
            {
                int pip=1+i/6;var die=simulation.MakeDie(App.Data.DefaultDeck[i%6],pip);
                if(pip>=3)die.Tier3=i%2==0?"A":"B";simulation.State.Board[i]=die;
            }
            simulation.Fire(-1.68);for(int i=0;i<50;i++)simulation.Step(1d/120);App.ConsumeEvents();await Capture("battle_24_slots");
            async Task CapturePair(int pips,string name)
            {
                simulation=App.Sim!;simulation.State.PendingShots.Clear();simulation.State.Projectiles.Clear();
                simulation.State.Board[0]=simulation.MakeDie("pulse",pips);simulation.State.Board[1]=simulation.MakeDie("pulse",pips);
                if(pips>=3){simulation.State.Board[0]!.Tier3="A";simulation.State.Board[1]!.Tier3="B";}
                if(!simulation.Merge(0,1).Ok)throw new InvalidOperationException("Capture merge rejected");App.ConsumeEvents();await Capture(name);
            }
            await CapturePair(2,"skill_level3");App.ChooseDiceSkill(App.Sim!.CurrentSkillChoice!.ChoiceId,"A");
            await CapturePair(5,"skill_level6_reselect");App.ChooseDiceSkill(App.Sim!.CurrentSkillChoice!.ChoiceId,"B");
            await Capture("skill_level6_final");App.ChooseDiceSkill(App.Sim!.CurrentSkillChoice!.ChoiceId,"D");
            await Capture("battle_skilled");
            var standardPreferences=CampaignCatalog.Copy(App.Preferences);var largePreferences=CampaignCatalog.Copy(standardPreferences);largePreferences.UiScale=1.3;
            App.SetPreferences(largePreferences);ApplyPreferences(largePreferences);await Capture("battle_large_text");
            App.SetPreferences(standardPreferences);ApplyPreferences(standardPreferences);
            App.Sim!.OfferUpgrades(); App.ConsumeEvents(); await Capture("upgrade");
            App.Scene = "paused"; await Capture("pause"); App.OpenSettings(); await Capture("settings"); App.CloseSettings();
            App.AbandonExpedition(); await Capture("settlement"); App.ReturnToTown();
            File.WriteAllText(Path.Combine(path, "capture_result.json"), JsonSerializer.Serialize(new { completed = true, screens = layoutOnly?1:14, layout_only=layoutOnly, engine = Engine.GetVersionInfo()["string"].AsString() }));
            Audio.Shutdown();
            for(int i=0;i<3;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("CAMPAIGN CAPTURE COMPLETE"); GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
        }
        catch (Exception ex) { GD.PushError("CAMPAIGN CAPTURE FAILED: " + ex); GetTree().Quit(1); }
    }
}
