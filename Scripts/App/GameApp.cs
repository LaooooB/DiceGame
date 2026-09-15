using DiceGame.Core;
using DiceGame.Presentation;

namespace DiceGame.App;

public interface ISoundOutput
{
    bool Enabled { get; set; } bool Music { get; set; }
    void Unlock(); void Play(string kind,int variant=0); void Tick(double dt,bool active); void Suspend();
}
public interface IDesktopStorage
{
    string? Read(); void Write(string json); uint NewSeed();
}
public sealed class MouseGesture
{
    public string Mode="",Button="";public double X,Y,StartX,StartY;public int Slot=-1;
}
public sealed class ToastState {public string Text="";public double Life;}
public sealed record UiHit(string Id,double X,double Y,double W,double H,bool Disabled=false)
{
    public bool Contains(double x,double y)=>x>=X && x<=X+W && y>=Y && y<=Y+H;
}

/// <summary>Desktop-only interaction and scene flow; no DOM, WeChat API or touch adapter.</summary>
public sealed partial class GameApp
{
    public GameData Data { get; }
    public EffectSystem Effects { get; }
    public ISoundOutput Audio { get; }
    public GameSettings Settings { get; private set; }=new();
    public MetaState Meta { get; private set; }=new();
    public List<string> Deck { get; private set; }
    public List<string> EditingDeck { get; private set; }=[];
    public string Scene { get; set; }="menu";
    public string HelpReturn { get; private set; }="menu";
    public Simulation? Sim { get; private set; }
    public RunState? ResumeData { get; private set; }
    public MouseGesture? Pointer { get; private set; }
    public int SelectedSlot { get; private set; }=-1;
    public double AimAngle { get; private set; }=-Math.PI/2;
    public ToastState? Toast { get; private set; }
    public double Accumulator { get; private set; }
    private readonly IDesktopStorage _storage;
    private double _toastCooldown,_saveClock;
    private bool _storageFailed;
    public GameApp(GameData data,IDesktopStorage storage,ISoundOutput audio,CampaignCatalog? campaign=null)
    {
        Data=data;_storage=storage;Audio=audio;Effects=new(data);Deck=data.Dice.Select(d=>d.Id).ToList();
        Catalog=campaign;Progression=campaign is null?null:new CampaignProgression(campaign);
        Load();InitializeCampaign();
        Audio.Enabled=Settings.Sound;Audio.Music=Settings.Music;Effects.ReduceMotion=Settings.ReduceMotion;
    }
    private void Load()
    {
        try
        {
            string? raw=_storage.Read();if(string.IsNullOrEmpty(raw)) return;
            var saved=SaveCodec.Decode(Data,raw);Deck=saved.Deck;Settings=saved.Settings;Meta=saved.Meta;Preferences=saved.Preferences;Campaign=saved.Campaign;_hadSave=true;
            if(saved.Run is not null && (!saved.Run.Over || Catalog is not null)) ResumeData=saved.Run;
        }
        catch(Exception e) {ResumeData=null;LoadProblem="存档无法读取："+e.Message;Notify("存档已保留。请在设置中检查备份或明确重置。");}
    }
    public void RecordRun()
    {
        if(Sim is null) return;
        Meta.BestWave=Math.Max(Meta.BestWave,Sim.State.Wave);Meta.BestScore=Math.Max(Meta.BestScore,Sim.State.Score);
    }
    public void Save()
    {
        if(LoadProblem!="")return;
        if(Catalog is not null){SaveCampaignSnapshot();return;}
        if(Sim is not null && !Sim.State.Over) {RecordRun();ResumeData=Sim.ExportSave();}
        if(Sim?.State.Over==true) ResumeData=null;
        try {_storage.Write(SaveCodec.Encode(new SaveEnvelope {Version=Data.Game.Schema,Settings=Settings,Meta=Meta,Deck=Deck,Run=ResumeData}));}
        catch(Exception) {if(!_storageFailed){_storageFailed=true;Notify("无法保存到本地；游戏仍可继续。");}}
    }
    public void StartNew(uint? seed=null)
    {
        if(Catalog is not null){StartExpedition(seed);return;}
        Effects.Clear();ResumeData=null;Sim=new Simulation(Data,Deck,seed??_storage.NewSeed());Scene="play";Pointer=null;
        AimAngle=-Math.PI/2;Accumulator=0;_saveClock=0;ConsumeEvents();Save();
    }
    public void Resume()
    {
        if(ResumeData is null) {StartNew();return;}
        try {RestoreRun(ResumeData);Audio.Unlock();}
        catch(Exception ex) { if(Catalog is null){ResumeData=null;Scene="menu";}else Scene="town";Notify("续玩校验失败，原存档未改动："+ex.Message,4); }
    }
    public void RestoreRun(RunState state)
    {
        Effects.Clear();Sim=Simulation.Restore(Data,state);AimAngle=Sim.State.LastAim;
        Scene=Sim.State.Over?(Catalog is null?"over":"settlement"):Sim.State.AwaitingUpgrade?"upgrade":"play";Accumulator=0;Pointer=null;
    }
    public void Notify(string message,double duration=2)
    {
        if(Toast is not null && Toast.Text==message && Toast.Life>1) return;
        Toast=new ToastState {Text=message,Life=duration};
    }
    public void ConsumeEvents()
    {
        if(Sim is null) return;
        foreach(var e in Sim.TakeEvents())
        {
            Effects.OnEvent(e);
            switch(e.Type)
            {
                case "hit": Audio.Play("hit",Math.Min(6,Sim.State.Combo/3));break;
                case "kill": Audio.Play("kill",Math.Min(6,e.Combo/2));break;
                case "explosion": Audio.Play("explode");break;
                case "volley": Audio.Play("launch");break;
                case "merge": Audio.Play("merge");break;
                case "summon": Audio.Play("summon");break;
                case "breach": Audio.Play("breach");break;
                case "clear": Audio.Play("clear");break;
                case "upgrade": CancelPointer();Scene="upgrade";Audio.Play("upgrade");Save();break;
                case "upgraded": Audio.Play("upgrade");break;
                case "victory": case "gameover":
                    if(Catalog is not null){CancelPointer();Scene="settlement";TrySettle();}
                    else {CancelPointer();Scene="over";RecordRun();ResumeData=null;Save();}break;
            }
        }
    }
    public void Tick(double delta)
    {
        double dt=MathEx.Clamp(double.IsFinite(delta)?delta:0,0,Data.Game.Limits.MaxFrameDelta);
        if(Toast is not null) {Toast.Life-=dt;if(Toast.Life<=0) Toast=null;}
        _toastCooldown=Math.Max(0,_toastCooldown-dt);bool active=Scene=="play";
        if(active && Sim is not null)
        {
            Accumulator+=dt;int steps=0;var limits=Data.Game.Limits;
            while(Accumulator>=limits.FixedStep && steps<limits.MaxSteps && Scene=="play")
            {Sim.Step(limits.FixedStep);Accumulator-=limits.FixedStep;steps++;ConsumeEvents();}
            if(steps==limits.MaxSteps) Accumulator=0;
            _saveClock+=dt;if(_saveClock>=4) {_saveClock=0;Save();}
        }
        else Accumulator=0;
        TickCampaign(dt);
        Effects.ReduceMotion=Settings.ReduceMotion;Effects.Update(dt,active);Audio.Tick(dt,Scene is "play" or "menu");
    }
    public void OnFocusLost()
    {
        CancelPointer();if(Scene is "play" or "die") Scene="paused";Save();Audio.Suspend();Accumulator=0;
    }
    public void Pause()
    {
        if(Scene!="play") return;CancelPointer();Scene="paused";Save();
    }
    public void CancelPointer() => Pointer=null;
    public int SlotAt(double x,double y)
    {
        for(int i=0;i<Data.Game.Board.Slots;i++)
        {var p=Data.SlotPosition(i);if(Math.Abs(x-p.X)<=41 && y>=p.Y-42 && y<=p.Y+44)return i;}
        return -1;
    }
    public UiHit? FindButton(double x,double y)=>UiLayout.Buttons(this).LastOrDefault(b=>b.Contains(x,y));
    public void OnDown(double x,double y)
    {
        if(Pointer is not null) return;Audio.Unlock();
        if(x<0 || x>Data.Game.View.Width || y<0 || y>Data.Game.View.Height) return;
        var b=NativeUi?null:FindButton(x,y);
        if(b is not null) {Pointer=new MouseGesture {Mode="button",Button=b.Id,X=x,Y=y,StartX=x,StartY=y};return;}
        if(Scene!="play" || Sim is null) return;
        int slot=SlotAt(x,y);
        if(slot>=0)
        {
            bool occupied=Sim.State.Board[slot] is not null;
            Pointer=new MouseGesture {Mode=occupied?"diepress":"button",Button=occupied?"":"summonAt:"+slot,Slot=slot,X=x,Y=y,StartX=x,StartY=y};return;
        }
        if(x>=22 && x<=410 && y>=124 && y<=575)
        {Pointer=new MouseGesture {Mode="aim",X=x,Y=y,StartX=x,StartY=y};UpdateAim(x,y);}
    }
    public void OnMove(double x,double y)
    {
        var p=Pointer;if(p is null) return;p.X=x;p.Y=y;
        if(p.Mode=="diepress" && Math.Sqrt(MathEx.Dist2(x,y,p.StartX,p.StartY))>8) p.Mode="drag";
        if(p.Mode=="aim") UpdateAim(x,y);
    }
    public void UpdateAim(double x,double y)
    {if(Sim is not null) AimAngle=Sim.ClampAim(Math.Atan2(Math.Min(y-Data.Game.Arena.LaunchY,-24),x-216));}
    public void OnUp(double x,double y)
    {
        var p=Pointer;if(p is null) return;Pointer=null;
        if(p.Mode=="button")
        {
            if(p.Button.StartsWith("summonAt:",StringComparison.Ordinal)) {if(SlotAt(x,y)==p.Slot) Action(p.Button);}
            else {var b=FindButton(x,y);if(b is not null && b.Id==p.Button) Action(p.Button);}
        }
        else if(p.Mode=="aim" && Scene=="play" && Sim is not null)
        {
            if(x>=22 && x<=410 && y>=124 && y<=575)
            {
                UpdateAim(x,y);var result=Sim.Fire(AimAngle);
                if(!result.Ok && _toastCooldown<=0)
                {Notify(result.Reason=="reloading"?"骰子正在装填，稍后再松手发射。":result.Reason=="busy"?"弹丸正在通过回廊，请稍后发射。":"先召唤一颗骰子。",1.1);_toastCooldown=1;}
                ConsumeEvents();
            }
        }
        else if(p.Mode=="diepress" && Scene=="play" && Sim?.State.Board[p.Slot] is not null)
        {SelectedSlot=p.Slot;Scene="die";Save();Audio.Play("tap");}
        else if(p.Mode=="drag" && Scene=="play" && Sim is not null)
        {
            int target=SlotAt(x,y);
            if(target>=0 && target!=p.Slot)
            {
                if(Sim.State.Board[target] is null) Sim.Move(p.Slot,target);
                else
                {
                    var result=Sim.Merge(p.Slot,target);
                    if(!result.Ok) {Audio.Play("error");Notify(Sim.State.Board[p.Slot]?.Pips==6?"六点骰子已达上限，可保留或回收。":"只能合成同种类、同点数的骰子。");}
                }
                ConsumeEvents();Save();
            }
        }
    }
    public void Summon(int preferred=-1)
    {
        if(Scene!="play" || Sim is null) return;var result=Sim.Summon();
        if(!result.Ok) {Audio.Play("error");Notify(result.Reason=="full"?"八格已满：合成配对，或单击骰子回收。":"能量不足，击破敌人或等待自然恢复。");}
        else
        {
            if(preferred>=0 && preferred!=result.Slot && Sim.State.Board[preferred] is null)
            {Sim.Move(result.Slot,preferred);foreach(var e in Sim.Events)if(e.Type=="summon")e.Slot=preferred;}
            ConsumeEvents();Save();
        }
    }
    public void Action(string id)
    {
        Audio.Play("tap");
        if(Catalog is not null && CampaignAction(id)){Pointer=null;return;}
        switch(id)
        {
            case "start": case "restart": case "confirmStart": StartNew();break;
            case "new":Scene="confirmNew";break;
            case "cancelNew":Scene="menu";break;
            case "resume":Resume();break;
            case "pause":Pause();break;
            case "continue":Scene=Sim?.State.AwaitingUpgrade==true?"upgrade":"play";Accumulator=0;Audio.Unlock();break;
            case "sound":Settings.Sound=!Settings.Sound;Audio.Enabled=Settings.Sound;Save();break;
            case "music":Settings.Music=!Settings.Music;Audio.Music=Settings.Music;Save();break;
            case "motion":Settings.ReduceMotion=!Settings.ReduceMotion;Effects.ReduceMotion=Settings.ReduceMotion;Save();break;
            case "home":Save();Sim=null;Scene="menu";Pointer=null;Effects.Clear();break;
            case "summon":Summon();break;
            case "help":HelpReturn=Scene=="menu"?"menu":Scene;Scene="help";Save();break;
            case "closeHelp":Scene=HelpReturn;Accumulator=0;break;
            case "closeDie":Scene="play";SelectedSlot=-1;Accumulator=0;break;
            case "editDeck":EditingDeck=Deck.ToList();Scene="deck";break;
            case "deckBack":Scene="menu";break;
            case "deckSave":
                if(!Data.ValidDeck(EditingDeck)) {Notify("请携带一到六种不同骰子。");break;}
                Deck=EditingDeck.ToList();Scene="menu";Save();if(ResumeData is not null) Notify("卡组已更新，将在新的一局生效。");break;
            default:
                if(id.StartsWith("summonAt:") && int.TryParse(id[9..],out int slot)) Summon(slot);
                else if(id.StartsWith("recycle:") && Sim is not null && int.TryParse(id[8..],out int index))
                {
                    var r=Sim.Recycle(index);Scene="play";SelectedSlot=-1;
                    if(r.Ok) {Notify("已回收，获得 "+r.Amount+" 能量。");ConsumeEvents();Save();}
                }
                else if(id.StartsWith("upgrade:") && Sim is not null)
                {if(Sim.ChooseUpgrade(id[8..])) {Scene="play";Accumulator=0;ConsumeEvents();Save();}}
                else if(id.StartsWith("deck:"))
                {
                    string type=id[5..];if(!Data.Types.ContainsKey(type)) break;
                    int i=EditingDeck.IndexOf(type);
                    if(i>=0) EditingDeck.RemoveAt(i);else if(EditingDeck.Count<Data.Game.Rules.MaxDeck) EditingDeck.Add(type);else Notify("一个卡组最多携带六种骰子。");
                }
                break;
        }
        Pointer=null;
    }
    public void Key(string key)
    {
        if(key=="Escape")
        {
            if(Scene=="play") Pause();else if(Scene=="paused") Action("continue");else if(Scene=="die") Action("closeDie");else if(Scene=="help") Action("closeHelp");
        }
        else if(key==" " && Scene=="play") Summon();
        else if(key.Equals("m",StringComparison.OrdinalIgnoreCase)) Action("sound");
    }
}
