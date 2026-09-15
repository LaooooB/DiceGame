using Godot;
using DiceGame.Core;
using DiceGame.Platform;
using DiceGame.Presentation;
using System.Text.Json;

namespace DiceGame.App;

/// <summary>Godot 4.6 .NET desktop entry. Only mouse/keyboard input is accepted.</summary>
public partial class ReferenceRoot : Node
{
    private NativeArt _art=null!;
    private NativeAudio _audio=null!;
    private GameApp _app=null!;
    private NativeRenderer _renderer=null!;
    private readonly DesktopChrome _chrome=new();
    private readonly List<PaintLayer> _layers=[];
    private SubViewport _gameViewport=null!,_fieldViewport=null!;
    private TextureRect _gameTexture=null!,_fieldTexture=null!;
    private Shader _rounded=null!;
    private bool _captureMode,_initialized;
    private Window _window=null!;
    public override void _Ready()
    {
        try
        {
            _window=GetWindow();_window.MinSize=new Vector2I(960,640);
            var data=new GameData(Godot.FileAccess.GetFileAsString("res://Tests/LegacyBalanceData/game.json"),Godot.FileAccess.GetFileAsString("res://Tests/LegacyBalanceData/dice.json"),Godot.FileAccess.GetFileAsString("res://Tests/LegacyBalanceData/upgrades.json"));
            _art=new NativeArt();_rounded=GD.Load<Shader>("res://Shaders/RoundedTexture.gdshader");
            _audio=new NativeAudio {Name="SynthesizedAudio"};AddChild(_audio);
            var args=OS.GetCmdlineUserArgs();_captureMode=args.Contains("--capture-reference");
            IDesktopStorage storage=_captureMode?new CaptureStorage():new DesktopStorage(data);
            _app=new GameApp(data,storage,_audio);_renderer=new NativeRenderer(_app);
            CreateLayer(this,"DesktopSurround",_chrome.Paint);
            _fieldViewport=CreateViewport("BattlefieldViewport",new Vector2I(764,810));
            // Original rounded clip rect: (25,132) .. (407,537). 2x render resolution, unchanged logical coordinates.
            var fieldRoot=new Node2D {Name="OriginalBattlefieldCoordinates",Scale=Vector2.One*2,Position=new Vector2(-50,-264)};_fieldViewport.AddChild(fieldRoot);
            CreateLayer(fieldRoot,"WallsEnemiesAim",_renderer.FieldBelow);
            var additive=CreateLayer(fieldRoot,"AdditiveProjectiles",_renderer.Projectiles);
            additive.Material=new CanvasItemMaterial {BlendMode=CanvasItemMaterial.BlendModeEnum.Add};
            CreateLayer(fieldRoot,"CombatEffects",_renderer.FieldAbove);
            _gameViewport=CreateViewport("GameCanvasViewport",new Vector2I(864,1728));
            var gameRoot=new Node2D {Name="Original432x864Canvas",Scale=Vector2.One*2};_gameViewport.AddChild(gameRoot);
            CreateLayer(gameRoot,"BackgroundMenusHeader",_renderer.Base);
            _fieldTexture=Texture(_fieldViewport.GetTexture(),new Vector2(382,405),10);
            _fieldTexture.Name="ClippedBattlefield";_fieldTexture.Position=new Vector2(25,132);gameRoot.AddChild(_fieldTexture);
            CreateLayer(gameRoot,"DiceBoardLauncher",_renderer.Foreground);CreateLayer(gameRoot,"ModalsAndToast",_renderer.Overlay);
            _gameTexture=Texture(_gameViewport.GetTexture(),new Vector2(432,864),25);_gameTexture.Name="DesktopGameFrame";AddChild(_gameTexture);
            GetTree().AutoAcceptQuit=false;_window.CloseRequested+=Quit;
            _window.FocusExited+=LoseFocus;_window.MouseExited+=CancelMouse;GetViewport().SizeChanged+=Layout;
            Layout();_initialized=true;
            GD.Print("Dice Ricochet native desktop | Godot "+Engine.GetVersionInfo()["string"]+" | 432x864 reference canvas | 120Hz simulation");
            if(_captureMode)Callable.From(CaptureReference).CallDeferred();
        }
        catch(Exception ex){GD.PushError("DiceGame startup failed: "+ex);ShowStartupError(ex.Message);}
    }
    private PaintLayer CreateLayer(Node parent,string name,System.Action<NativeCanvas> paint)
    {
        var layer=new PaintLayer {Name=name};parent.AddChild(layer);layer.Initialize(_art,paint);_layers.Add(layer);return layer;
    }
    private SubViewport CreateViewport(string name,Vector2I size)
    {
        var viewport=new SubViewport {Name=name,Size=size,Disable3D=true,TransparentBg=true,
            Msaa2D=Viewport.Msaa.Msaa4X,RenderTargetUpdateMode=SubViewport.UpdateMode.Always,RenderTargetClearMode=SubViewport.ClearMode.Always};
        AddChild(viewport);return viewport;
    }
    private TextureRect Texture(Texture2D texture,Vector2 logicalSize,float radius)
    {
        var material=new ShaderMaterial {Shader=_rounded};material.SetShaderParameter("logical_size",logicalSize);material.SetShaderParameter("corner_radius",radius);
        return new TextureRect {Texture=texture,Size=logicalSize,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=Control.MouseFilterEnum.Ignore,TextureFilter=CanvasItem.TextureFilterEnum.Linear,Material=material};
    }
    private void Layout()
    {
        if(_gameTexture is null)return;_app.CancelPointer();_chrome.Layout(GetViewport().GetVisibleRect().Size);
        _gameTexture.Position=_chrome.GamePosition;_gameTexture.Size=new Vector2(432,864)*(float)_chrome.GameScale;Redraw();
    }
    private void Redraw()
    {
        if(_fieldTexture is not null)_fieldTexture.Visible=_renderer.ShowsBattle;
        foreach(var layer in _layers)layer.QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if(!_initialized)return;
        if(!_captureMode)_app.Tick(delta);_audio.Pump();Redraw();
    }
    public override void _Input(InputEvent e)
    {
        if(!_initialized || _captureMode)return;
        if(e is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch(key.Keycode)
            {
                case Key.F11:_app.CancelPointer();_window.Mode=_window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen?Window.ModeEnum.Windowed:Window.ModeEnum.Fullscreen;break;
                case Key.Escape:_app.Key("Escape");break;case Key.Space:_app.Key(" ");break;case Key.M:_app.Key("m");break;
            }
        }
        else if(e is InputEventMouseButton button)
        {
            var p=LogicalPoint(button.Position);
            if(button.ButtonIndex==MouseButton.Right && button.Pressed)_app.CancelPointer();
            else if(button.ButtonIndex==MouseButton.Left)
            {if(button.Pressed)_app.OnDown(p.X,p.Y);else _app.OnUp(p.X,p.Y);}
        }
        else if(e is InputEventMouseMotion motion){var p=LogicalPoint(motion.Position);_app.OnMove(p.X,p.Y);}
        // ScreenTouch/ScreenDrag are intentionally unsupported. There is no mobile path in this project.
    }
    private PointD LogicalPoint(Vector2 p)=>new((p.X-_gameTexture.Position.X)/_chrome.GameScale,(p.Y-_gameTexture.Position.Y)/_chrome.GameScale);
    private void CancelMouse(){if(_initialized)_app.CancelPointer();}
    private void LoseFocus(){if(_initialized && !_captureMode)_app.OnFocusLost();}
    private void Quit(){if(_initialized)_app.Save();GetTree().Quit();}
    public override void _ExitTree()
    {
        if(_initialized){_app.Save();_window.CloseRequested-=Quit;_window.FocusExited-=LoseFocus;_window.MouseExited-=CancelMouse;GetViewport().SizeChanged-=Layout;}
        // Engine frees queued CanvasItem commands and their resources after nodes leave the tree.
    }
    private void ShowStartupError(string message)
    {
        GD.PushError("Reference capture startup failed: "+message);
        if(_captureMode)GetTree().Quit(1);
    }
    private sealed class CaptureStorage : IDesktopStorage
    {public string? Read()=>null;public void Write(string json){}public uint NewSeed()=>24137;}
    /// <summary>Opt-in native screenshot harness. Does not touch the player's real save or claim a test has already run.</summary>
    private async void CaptureReference()
    {
        try
        {
            string[] args=OS.GetCmdlineUserArgs();string folder=ProjectSettings.GlobalizePath("res://Artifacts/Native");
            int index=Array.IndexOf(args,"--capture-dir");if(index>=0 && index+1<args.Length)folder=System.IO.Path.GetFullPath(args[index+1]);Directory.CreateDirectory(folder);
            async System.Threading.Tasks.Task Capture(string name)
            {
                Redraw();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var image=_gameViewport.GetTexture().GetImage();var result=image.SavePng(System.IO.Path.Combine(folder,name+".png"));
                if(result!=Error.Ok)throw new IOException("Screenshot failed: "+result);
            }
            _app.Effects.T=1.25;await Capture("menu");_app.Action("editDeck");await Capture("deck");_app.Action("deckBack");
            _app.StartNew(24137);_app.Effects.T=1.25;
            string statesFile=ProjectSettings.GlobalizePath("res://Tests/Fixtures/visual_states.json");
            if(!System.IO.File.Exists(statesFile))throw new FileNotFoundException("Missing visual comparison fixtures.",statesFile);
            using var fixtures=JsonDocument.Parse(System.IO.File.ReadAllText(statesFile));
            var state=JsonSerializer.Deserialize<RunState>(fixtures.RootElement.GetProperty("battle").GetRawText(),GameData.JsonOptions)!;
            _app.RestoreRun(state);_app.Effects.T=1.25;_app.OnDown(305,222);await Capture("battle_aim");_app.CancelPointer();
            _app.Sim!.OfferUpgrades();_app.ConsumeEvents();await Capture("upgrade");
            _app.RestoreRun(state);_app.Scene="paused";await Capture("pause");
            _app.Scene="play";_app.OnDown(75,638);_app.OnUp(75,638);await Capture("dice_info");
            _app.Scene="play";_app.Action("help");await Capture("help");
            _app.Action("closeHelp");_app.Scene="over";_app.Sim!.State.Over=true;_app.RecordRun();await Capture("game_over");
            _app.RestoreRun(state);_app.Effects.Clear();
            foreach(var evt in fixtures.RootElement.GetProperty("effects").EnumerateArray())
                _app.Effects.OnEvent(JsonSerializer.Deserialize<CombatEvent>(evt.GetRawText(),GameData.JsonOptions)!);
            _app.Effects.Update(1.0/60);_app.Effects.T=1.25;await Capture("effects");
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"capture_result.json"),JsonSerializer.Serialize(new {status="completed",engine=Engine.GetVersionInfo()["string"].AsString(),renderer=RenderingServer.GetCurrentRenderingMethod(),screenshots=9},GameData.JsonOptions));
            GD.Print("NATIVE CAPTURE COMPLETE: "+folder);GetTree().Quit();
        }
        catch(Exception ex){GD.PushError("Native capture failed: "+ex);GetTree().Quit(1);}
    }
}
