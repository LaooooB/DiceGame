using Godot;
using DiceGame.App;
using DiceGame.Core;
using DiceGame.Presentation;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

/// <summary>
/// Player-facing UI binder. Every Control, page, card, slot and modal is authored in .tscn scenes.
/// This class only binds data, signals and state; it never creates UI nodes or style resources.
/// </summary>
public partial class CampaignUi : Control
{
    private GameRoot _root = null!;
    private GameApp App => _root.App;
    private CampaignCatalog Catalog => App.Catalog!;
    private CampaignState State => App.Campaign!;

    private Control _pages = null!, _townPage = null!, _expeditionPage = null!, _deckPage = null!, _battlePage = null!, _settlementPage = null!, _settingsPage = null!, _helpPage = null!;
    private Label _pageTitle = null!, _wallet = null!, _toast = null!, _fps = null!;
    private Control _overlay = null!;
    private readonly Control[] _overlayPanels = new Control[7];

    private readonly List<Button> _townTiles = [];
    private Control _townBoard = null!;
    private TextureRect _townBall = null!;
    private ColorRect _townAimGuide = null!;
    private Label _townStatus = null!;
    private bool _townAiming;
    private double _townAngle = -Math.PI / 2;
    private int _selectedTile = -1;
    private string _movingBuilding = "";
    private string[] _blueprintIds = [];
    private string _selectedBuilding = "";

    private readonly Dictionary<string, PanelContainer> _regionCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PanelContainer> _diceCards = new(StringComparer.Ordinal);
    private readonly List<PanelContainer> _deckChips = [];
    private string _deckReturn = "regions";

    private TextureRect _field = null!, _dragArt = null!;
    private readonly List<Control> _slots = [];
    private readonly List<TextureRect> _slotArt = [];
    private readonly List<Label> _slotBranches = [], _slotReady = [];
    private readonly List<ProgressBar> _slotReload = [];
    private readonly List<CanvasItem> _slotHigh = [], _slotReadyFrame = [], _slotSelectedFrame = [];
    private readonly string[] _slotKeys = new string[16];
    private BattleConduits _conduits = null!;
    private int _selectedBoardSlot = -1;

    private DesktopPreferences? _settingsDraft;
    private bool _settingsSync;
    private string _bindingAction = "";
    private readonly Vector2I[] _resolutions = [new(1280,720), new(1600,900), new(1920,1080), new(2560,1440), new(3840,2160)];
    private readonly int[] _fpsOptions = [0,30,60,90,120,144,165,240];

    private bool _catalogOpen, _confirmOpen;
    private Action? _confirmYes, _confirmNo;
    private string _lastScene = "";
    private bool _dirty = true;

    public void Initialize(GameRoot root)
    {
        _root = root;
        GetNode<TextureRect>("Ambient").Texture = root.Art.Background;
        _pages = GetNode<Control>("Margin/Layout/Pages");
        _pageTitle = GetNode<Label>("Margin/Layout/Header/Page");
        _wallet = GetNode<Label>("Margin/Layout/Header/Wallet");
        _fps = GetNode<Label>("Margin/Layout/Header/Fps");
        _toast = GetNode<Label>("Margin/Layout/Toast");
        _townPage = GetNode<Control>("Margin/Layout/Pages/Town");
        _expeditionPage = GetNode<Control>("Margin/Layout/Pages/Expedition");
        _deckPage = GetNode<Control>("Margin/Layout/Pages/Deck");
        _battlePage = GetNode<Control>("Margin/Layout/Pages/Battle");
        _settlementPage = GetNode<Control>("Margin/Layout/Pages/Settlement");
        _settingsPage = GetNode<Control>("Margin/Layout/Pages/Settings");
        _helpPage = GetNode<Control>("Margin/Layout/Pages/Help");
        _overlay = GetNode<Control>("Overlay");
        _overlayPanels[0] = GetNode<Control>("Overlay/Center/PausePanel");
        _overlayPanels[1] = GetNode<Control>("Overlay/Center/UpgradePanel");
        _overlayPanels[2] = GetNode<Control>("Overlay/Center/DiceSkillPanel");
        _overlayPanels[3] = GetNode<Control>("Overlay/Center/DiePanel");
        _overlayPanels[4] = GetNode<Control>("Overlay/Center/CatalogPanel");
        _overlayPanels[5] = GetNode<Control>("Overlay/Center/ConfirmPanel");
        _overlayPanels[6] = GetNode<Control>("Overlay/Center/LoadErrorPanel");

        BindTown(); BindExpedition(); BindDeck(); BindBattle(); BindSettlement(); BindSettings(); BindHelp(); BindOverlay();
        ApplyUiScale(App.Preferences.UiScale);
        Refresh(true);
    }

    private T N<T>(string path) where T : Node => GetNode<T>(path);
    public void Invalidate() => _dirty = true;

    public void Refresh() => Refresh(false);
    private void Refresh(bool force)
    {
        if (_root is null || App.Campaign is null) return;
        UxMotion.Reduced = App.Settings.ReduceMotion;
        _wallet.Text = Resources(State.Resources);
        _toast.Text = App.Toast?.Text ?? "";
        _fps.Visible = App.Preferences.ShowFps; _fps.Text = Engine.GetFramesPerSecond() + " FPS";

        if (App.LoadProblem != "")
        {
            ShowPage(_townPage, "存档保护");
            ShowLoadError();
            return;
        }

        bool sceneChanged = _lastScene != App.Scene;
        if (sceneChanged)
        {
            _lastScene = App.Scene;
            _catalogOpen = false;
            if (App.Scene != "play") _selectedBoardSlot = -1;
            ShowScenePage();
        }

        switch (App.Scene)
        {
            case "town": UpdateTown(); break;
            case "regions": UpdateExpedition(); break;
            case "deck": UpdateDeck(); break;
            case "play": case "paused": case "upgrade": case "diceSkill": case "die": UpdateBattle(); break;
            case "settlement": UpdateSettlement(); break;
            case "settings": UpdateSettings(sceneChanged || force); break;
        }
        RefreshOverlay();
        _conduits?.QueueRedraw();
        _dirty = false;
    }

    private string Resources(IEnumerable<KeyValuePair<string,long>> values) => string.Join("    ", values.Where(p=>p.Value!=0).Select(p => (Catalog.Resources.GetValueOrDefault(p.Key)?.Name ?? p.Key) + " " + Palette.Compact(p.Value)));
    private string RewardText(RewardDefinition reward)
    {
        var text = new List<string>(); if (reward.Resources.Count > 0) text.Add(Resources(reward.Resources));
        text.AddRange(reward.Dice.Select(id => "骰子 · " + App.Data.Types[id].Name));
        text.AddRange(reward.Mechanics.Select(id => "机制 · " + Catalog.Mechanics[id].Name));
        text.AddRange(reward.Blueprints.Select(id => "蓝图 · " + Catalog.Buildings[id].Name));
        foreach (var (type, amount) in reward.Bonuses.DiceDamagePercent) text.Add(App.Data.Types[type].Name + "伤害 +" + amount.ToString("P0"));
        if (reward.Bonuses.DamagePercent > 0) text.Add("永久伤害 +" + reward.Bonuses.DamagePercent.ToString("P0"));
        if (reward.Bonuses.ReloadPercent > 0) text.Add("装填缩短 +" + reward.Bonuses.ReloadPercent.ToString("P0"));
        if (reward.Bonuses.StartEnergy > 0) text.Add("初始能量 +" + reward.Bonuses.StartEnergy.ToString("0.#"));
        if (reward.Bonuses.PassiveEnergy > 0) text.Add("每秒能量 +" + reward.Bonuses.PassiveEnergy.ToString("0.##"));
        return string.Join("；", text);
    }
    private static string Outcome(string outcome) => outcome switch { "victory" => "区域通关", "defeat" => "远征失败", "abandoned" => "主动撤回", _ => outcome };
    private static string TimeText(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

    private void ShowScenePage()
    {
        Control page; string title;
        switch (App.Scene)
        {
            case "town": page = _townPage; title = $"城镇 · 深潜 {State.Cycle + 1}"; break;
            case "regions": page = _expeditionPage; title = $"出战 · 深潜 {State.Cycle + 1}"; break;
            case "deck": page = _deckPage; title = "骰组"; break;
            case "settlement": page = _settlementPage; title = "结算"; break;
            case "settings": page = _settingsPage; title = "设置"; break;
            case "help": page = _helpPage; title = "说明"; break;
            default: page = _battlePage; title = App.Sim?.State.Expedition?.Region.Name ?? "远征"; break;
        }
        ShowPage(page, title);
    }

    private void ShowPage(Control page, string title)
    {
        foreach (var child in _pages.GetChildren()) if (child is CanvasItem c) c.Visible = ReferenceEquals(child, page);
        _pageTitle.Text = title;
        if (!App.Settings.ReduceMotion) Reveal(page);
    }

    private static void Reveal(Control control)
    {
        control.Modulate = new Color(1,1,1,0); control.Scale = Vector2.One * .992f;
        Callable.From(() =>
        {
            if (!IsInstanceValid(control)) return;
            control.PivotOffset = control.Size * .5f;
            var tween = control.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(control, "modulate", Colors.White, .15);
            tween.TweenProperty(control, "scale", Vector2.One, .18);
        }).CallDeferred();
    }

    private void BindTown()
    {
        const string p = "Margin/Layout/Pages/Town";
        N<Button>(p+"/Actions/Start").Pressed += TownStart;
        N<Button>(p+"/Actions/Deck").Pressed += () => OpenDeck("town");
        N<Button>(p+"/Actions/Settings").Pressed += () => Act("settings");
        N<Button>(p+"/Actions/Help").Pressed += () => Act("help");
        N<Button>(p+"/Actions/Quit").Pressed += _root.Quit;
        _townBoard = N<Control>(p+"/Main/Left/BoardAspect/BoardFrame/TownBoard");
        _townBall = N<TextureRect>(p+"/Main/Left/BoardAspect/BoardFrame/TownBoard/TownBall");
        _townAimGuide = N<ColorRect>(p+"/Main/Left/BoardAspect/BoardFrame/TownBoard/AimGuide");
        _townStatus = N<Label>(p+"/Main/Left/Status");
        _townBoard.GuiInput += TownInput;
        for (int i=0;i<Catalog.Definition.Town.Rows*Catalog.Definition.Town.Columns;i++)
        {
            int tile=i; var b=N<Button>(p+"/Main/Left/BoardAspect/BoardFrame/TownBoard/Plot"+i); _townTiles.Add(b); b.Pressed += () => SelectTownTile(tile);
        }
        N<Button>(p+"/Main/Left/Dispatch/DispatchSelected").Pressed += () => LaunchTown(TargetTownAngle());
        N<Button>(p+"/Main/Left/Dispatch/DispatchAuto").Pressed += () => LaunchTown(App.TownSimulation.AutoAngle(State));
        N<Button>(p+"/Main/Inspector/Margin/Content/Build").Pressed += BuildSelectedBlueprint;
        N<Button>(p+"/Main/Inspector/Margin/Content/Upgrade").Pressed += UpgradeSelectedBuilding;
        N<Button>(p+"/Main/Inspector/Margin/Content/Move").Pressed += ToggleMove;
        N<Button>(p+"/Main/Inspector/Margin/Content/AutoDispatch").Pressed += () => { Tap(); App.MutateTown(s=>s.AutoDispatch=!s.AutoDispatch); UpdateTownInspector(); };
    }

    private void TownStart()
    {
        if (State.ActiveRunId != "") { Act("resume"); return; }
        if (State.Flight is not null) return;
        Tap(); App.EditingDeck.Clear(); App.EditingDeck.AddRange(App.Deck); App.Scene="regions"; Invalidate();
    }

    private void SelectTownTile(int tile)
    {
        if (State.Flight is not null || State.ActiveRunId != "") return;
        Tap();
        if (_movingBuilding != "" && !Catalog.IsResourceTile(tile) && State.Buildings.Values.All(x=>x.Tile!=tile))
        { string id=_movingBuilding; if(App.MutateTown(s=>App.Progression!.Relocate(s,id,tile))) _movingBuilding=""; }
        _selectedTile=tile; UpdateTownInspector();
    }

    private void TownInput(InputEvent ev)
    {
        if (State.Flight is not null || State.ActiveRunId != "") return;
        if (ev is InputEventMouseButton b && b.ButtonIndex==MouseButton.Left && b.Pressed)
        { _townAiming=true; TownAim(_townBoard.GetGlobalMousePosition()); _townBoard.AcceptEvent(); }
        else if (ev is InputEventMouseMotion && _townAiming) TownAim(_townBoard.GetGlobalMousePosition());
    }
    private void TownAim(Vector2 p)
    {
        var local=(p-_townBoard.GlobalPosition)/_townBoard.Size*new Vector2(800,480);
        _townAngle=MathEx.Clamp(Math.Atan2(Math.Min(-20,local.Y-430),local.X-400),-Math.PI+.18,-.18);
    }
    private double TargetTownAngle()
    {
        if(_selectedTile<0) return App.TownSimulation.AutoAngle(State);
        var box=App.TownSimulation.TileBounds(_selectedTile); return Math.Atan2((box.Top+box.Bottom)/2-430,(box.Left+box.Right)/2-400);
    }
    private void LaunchTown(double angle) { Tap(); App.MutateTown(s=>App.TownSimulation.Launch(s,angle,App.Deck[0])); }
    public void CancelTownAim() { _townAiming=false; _townAimGuide.Visible=false; }

    private void UpdateTown()
    {
        N<Button>("Margin/Layout/Pages/Town/Actions/Start").Text = State.ActiveRunId!="" ? "继续游戏" : "开始游戏";
        N<Button>("Margin/Layout/Pages/Town/Actions/Start").Disabled = State.Flight is not null;
        var size=_townBoard.Size;
        for(int i=0;i<_townTiles.Count;i++)
        {
            var box=App.TownSimulation.TileBounds(i); var b=_townTiles[i];
            b.Position=new Vector2((float)box.Left/800*size.X,(float)box.Top/480*size.Y);
            b.Size=new Vector2((float)(box.Right-box.Left)/800*size.X,(float)(box.Bottom-box.Top)/480*size.Y);
            var built=State.Buildings.FirstOrDefault(p=>p.Value.Tile==i);
            bool wood=Catalog.Definition.Town.WoodTiles.Contains(i),stone=Catalog.Definition.Town.StoneTiles.Contains(i);
            b.Text=wood?"木材":stone?"石材":built.Value is null?"":(Catalog.Buildings.GetValueOrDefault(built.Key)?.Name??built.Key)+"\n"+(built.Value.Constructing?"施工 "+built.Value.Work:"Lv."+built.Value.Level);
            b.TooltipText=b.Text==""?"空地":b.Text;
            b.Modulate=Color.FromHtml(i==_selectedTile?Gold:wood?"#91D6AF":stone?"#B7BDDC":built.Value?.Constructing==true?"#FFD094":"#FFFFFF");
        }
        var f=State.Flight; SetDice(_townBall,_root.Art,App.Deck[0],1); _townBall.Size=new Vector2(42,42);
        _townBall.Position=new Vector2((float)(f?.X??400)/800*size.X,(float)(f?.Y??430)/480*size.Y)-_townBall.Size/2;
        _townAimGuide.Visible=_townAiming; _townAimGuide.Position=new Vector2(size.X*.5f,size.Y*430/480); _townAimGuide.Size=new Vector2(size.X*.24f,3); _townAimGuide.Rotation=(float)_townAngle;
        _townStatus.Text=f is not null?$"派遣中 · 命中 {f.Hits} · {Math.Max(0,f.Remaining):0.0}s":_townAiming?"松手派遣":_movingBuilding!=""?"选择空地完成搬迁":"城镇就绪";
        UpdateTownInspector();
    }

    private void UpdateTownInspector()
    {
        const string p="Margin/Layout/Pages/Town/Main/Inspector/Margin/Content";
        var title=N<Label>(p+"/SelectionTitle"); var status=N<Label>(p+"/SelectionStatus"); var desc=N<Label>(p+"/SelectionDescription");
        var effect=N<Label>(p+"/SelectionEffect"); var cost=N<Label>(p+"/SelectionCost"); var picker=N<OptionButton>(p+"/BlueprintPicker");
        var build=N<Button>(p+"/Build"); var upgrade=N<Button>(p+"/Upgrade"); var move=N<Button>(p+"/Move"); var auto=N<Button>(p+"/AutoDispatch");
        _selectedBuilding=""; picker.Visible=build.Visible=true; upgrade.Visible=move.Visible=false;
        title.Text=_selectedTile<0?"选择地块":"空地"; status.Text=desc.Text=effect.Text=cost.Text="";
        var buildingAt=State.Buildings.FirstOrDefault(x=>x.Value.Tile==_selectedTile);
        if(buildingAt.Value is not null && Catalog.Buildings.TryGetValue(buildingAt.Key,out var definition))
        {
            _selectedBuilding=definition.Id; var b=buildingAt.Value; title.Text=definition.Name; status.Text=b.Constructing?$"施工 {b.Work}/{definition.Levels[b.Level].Work}":$"Lv.{b.Level}"; desc.Text=definition.Description;
            picker.Visible=build.Visible=false; move.Visible=true; move.Text=_movingBuilding==definition.Id?"取消搬迁":"搬迁";
            if(b.Level<definition.Levels.Length)
            { var next=definition.Levels[b.Level]; effect.Text=RewardText(next.Reward); cost.Text=Resources(next.Cost); upgrade.Visible=true; upgrade.Disabled=App.Progression!.BuildingBlockers(State,definition.Id,b.Tile).Count>0; }
        }
        else if(_selectedTile>=0)
        {
            bool wood=Catalog.Definition.Town.WoodTiles.Contains(_selectedTile),stone=Catalog.Definition.Town.StoneTiles.Contains(_selectedTile);
            if(wood||stone){title.Text=wood?"木材":"石材";desc.Text="资源地块";picker.Visible=build.Visible=false;}
        }
        _blueprintIds=Catalog.Definition.Buildings.Where(b=>State.Blueprints.Contains(b.Id)&&!State.Buildings.ContainsKey(b.Id)).Select(b=>b.Id).ToArray();
        int keep=Math.Max(0,picker.Selected); picker.Clear(); foreach(string id in _blueprintIds) picker.AddItem(Catalog.Buildings[id].Name); if(_blueprintIds.Length>0)picker.Selected=Math.Min(keep,_blueprintIds.Length-1);
        build.Disabled=_selectedTile<0||Catalog.IsResourceTile(_selectedTile)||_blueprintIds.Length==0||State.ActiveRunId!=""||State.Flight is not null;
        if(!build.Disabled && _blueprintIds.Length>0){var reasons=App.Progression!.BuildingBlockers(State,_blueprintIds[picker.Selected],_selectedTile);build.Disabled=reasons.Count>0;if(reasons.Count>0)cost.Text=string.Join("；",reasons);}
        auto.Visible=State.UnlockedMechanics.Contains("auto_dispatch"); auto.Text="自动派遣 "+(State.AutoDispatch?"开":"关"); auto.Disabled=State.ActiveRunId!="";
        N<Label>(p+"/LastResult").Text=State.LastResult is { } result?$"上次远征 · {result.RegionName} · {Outcome(result.Outcome)}\n{Resources(result.Resources)}":"";
    }

    private void BuildSelectedBlueprint()
    {
        var picker=N<OptionButton>("Margin/Layout/Pages/Town/Main/Inspector/Margin/Content/BlueprintPicker");
        if(_selectedTile<0||picker.Selected<0||picker.Selected>=_blueprintIds.Length)return; string id=_blueprintIds[picker.Selected]; Tap(); App.MutateTown(s=>App.Progression!.StartConstruction(s,id,_selectedTile)); UpdateTownInspector();
    }
    private void UpgradeSelectedBuilding(){if(_selectedBuilding==""||!State.Buildings.TryGetValue(_selectedBuilding,out var b))return;Tap();App.MutateTown(s=>App.Progression!.StartConstruction(s,_selectedBuilding,b.Tile));UpdateTownInspector();}
    private void ToggleMove(){if(_selectedBuilding=="")return;Tap();_movingBuilding=_movingBuilding==_selectedBuilding?"":_selectedBuilding;UpdateTownInspector();}

    private void BindExpedition()
    {
        const string p="Margin/Layout/Pages/Expedition";
        foreach(var r in Catalog.Definition.Regions)
        {
            var card=N<PanelContainer>(p+"/RegionScroll/RegionGrid/Region_"+r.Id); _regionCards[r.Id]=card; string id=r.Id;
            card.GetNode<Button>("Margin/Content/Action").Pressed += () => SelectRegion(id);
        }
        for(int i=0;i<6;i++)_deckChips.Add(N<PanelContainer>(p+"/Side/DeckStrip/Deck"+i));
        N<Button>(p+"/Side/EditDeck").Pressed += () => OpenDeck("regions");
        N<Button>(p+"/Side/Start").Pressed += StartExpedition;
        N<Button>(p+"/Side/DeepDive").Pressed += () => ShowConfirmation("进入下一层","保留当前永久成长，开启下一层区域进度。",()=>Act("nextCycle"));
        N<Button>(p+"/Side/Back").Pressed += () => Act("town");
    }
    private void SelectRegion(string id){if(!App.Progression!.IsRegionOpen(State,id))return;Tap();App.MutateTown(s=>s.SelectedRegion=id);UpdateExpedition();}
    private void OpenDeck(string returnScene){Tap();_deckReturn=returnScene;App.EditingDeck.Clear();App.EditingDeck.AddRange(App.Deck);App.Scene="deck";Invalidate();}
    private void StartExpedition(){if(State.ActiveRunId!=""||State.Flight is not null)return;Tap();if(App.SaveDeck())App.StartExpedition();Invalidate();}

    private void UpdateExpedition()
    {
        if(App.EditingDeck.Count==0){App.EditingDeck.Clear();App.EditingDeck.AddRange(App.Deck);}
        foreach(var region in Catalog.Definition.Regions)
        {
            var card=_regionCards[region.Id]; bool open=App.Progression!.IsRegionOpen(State,region.Id),selected=State.SelectedRegion==region.Id;var record=State.Record(region.Id);
            card.GetNode<Label>("Margin/Content/Name").Text=region.Name; card.GetNode<Label>("Margin/Content/Meta").Text=$"{region.Phases.Length} 阶段 · {region.TotalWaves} 波";
            card.GetNode<Label>("Margin/Content/Progress").Text=$"通关 {record.Clears} · 主骰记录 {record.ClearedWith.Count}";
            var remaining=region.BlueprintPool.Where(id=>!State.Blueprints.Contains(id)).Select(id=>Catalog.Buildings[id].Name).ToArray();
            card.GetNode<Label>("Margin/Content/Reward").Text=remaining.Length==0?"蓝图已收集":"待发现 · "+string.Join("、",remaining.Take(3))+(remaining.Length>3?"…":"");
            card.GetNode<Label>("Margin/Content/Lock").Text=open?"":string.Join("；",App.Progression.MissingRequirements(State,region.Requirements));
            var action=card.GetNode<Button>("Margin/Content/Action");action.Text=!open?"未开放":selected?"已选择":"选择";action.Disabled=!open||State.ActiveRunId!=""||State.Flight is not null;card.Modulate=selected?new Color(1,.96f,.76f,1):Colors.White;
        }
        var chosen=Catalog.Regions[State.SelectedRegion];var chosenRecord=State.Record(chosen.Id);
        N<Label>("Margin/Layout/Pages/Expedition/Side/SelectedRegion").Text=chosen.Name;
        N<Label>("Margin/Layout/Pages/Expedition/Side/SelectedMeta").Text=$"{chosen.TotalWaves} 波 · 通关 {chosenRecord.Clears} · 齿轮 {State.Gears}";
        BindDeckStrip(App.EditingDeck);
        string lead=App.EditingDeck.Count>0&&App.Data.Types.ContainsKey(App.EditingDeck[0])?App.Data.Types[App.EditingDeck[0]].Name:"未选择";
        N<Label>("Margin/Layout/Pages/Expedition/Side/DeckSummary").Text=$"{App.EditingDeck.Count}/6 · 主骰 {lead}";
        bool valid=App.Data.ValidDeck(App.EditingDeck)&&App.Progression.CanUseDeck(State,App.EditingDeck); bool canStart=valid&&State.ActiveRunId==""&&State.Flight is null&&App.Progression.IsRegionOpen(State,State.SelectedRegion);
        N<Button>("Margin/Layout/Pages/Expedition/Side/Start").Disabled=!canStart;
        N<Label>("Margin/Layout/Pages/Expedition/Side/Blocker").Text=State.ActiveRunId!=""?"已有未结束远征":State.Flight is not null?"城镇派遣尚未结束":!valid?"需要 6 种合法骰子":"";
        N<Button>("Margin/Layout/Pages/Expedition/Side/DeepDive").Disabled=!App.Progression.CanStartNextCycle(State);
    }
    private void BindDeckStrip(IReadOnlyList<string> deck)
    {
        for(int i=0;i<_deckChips.Count;i++)
        {
            var chip=_deckChips[i]; bool show=i<deck.Count&&App.Data.Types.ContainsKey(deck[i]);chip.Visible=show;if(!show)continue;string id=deck[i];SetDice(chip.GetNode<TextureRect>("Layout/Art"),_root.Art,id,1);chip.GetNode<Label>("Layout/Name").Text=App.Data.Types[id].Name;chip.GetNode<Label>("Layout/Lead").Text=i==0?"主骰":"";
        }
    }

    private void BindDeck()
    {
        const string p="Margin/Layout/Pages/Deck";
        N<Button>(p+"/Top/Back").Pressed += () => {Tap();App.Scene=_deckReturn;Invalidate();};
        N<Button>(p+"/Bottom/Save").Pressed += SaveDeckAndReturn;
        N<Button>(p+"/Bottom/Start").Pressed += StartExpedition;
        foreach(var die in App.Data.Dice)
        {
            var card=N<PanelContainer>(p+"/Scroll/Grid/Die_"+die.Id);_diceCards[die.Id]=card;string id=die.Id;
            card.GetNode<Button>("Margin/Content/Actions/Details").Pressed += () => OpenDiceCatalog(id);
            card.GetNode<Button>("Margin/Content/Actions/Toggle").Pressed += () => {App.Action("deck:"+id);UpdateDeck();};
            card.GetNode<Button>("Margin/Content/Actions/Lead").Pressed += () => {Tap();App.SetLeadDice(id);UpdateDeck();};
        }
    }
    private void SaveDeckAndReturn(){Tap();if(App.SaveDeck())App.Scene=_deckReturn;Invalidate();}
    private void UpdateDeck()
    {
        if(App.EditingDeck.Count==0){App.EditingDeck.Clear();App.EditingDeck.AddRange(App.Deck);}
        foreach(var die in App.Data.Dice)
        {
            var card=_diceCards[die.Id];bool unlocked=State.UnlockedDice.Contains(die.Id),selected=App.EditingDeck.Contains(die.Id),lead=App.EditingDeck.FirstOrDefault()==die.Id;
            var art=card.GetNode<TextureRect>("Margin/Content/Top/Art");SetDice(art,_root.Art,die.Id,1);art.Modulate=unlocked?Colors.White:new Color(.32f,.32f,.32f,1);
            card.GetNode<Label>("Margin/Content/Top/Text/Name").Text=die.Name;var rarity=card.GetNode<Label>("Margin/Content/Top/Text/Rarity");rarity.Text=DiceContent.RarityName(die.Rarity)+" · "+die.Tag;rarity.AddThemeColorOverride("font_color",Color.FromHtml(DiceContent.RarityColor(die.Rarity)));
            card.GetNode<Label>("Margin/Content/Top/Text/Description").Text=die.Description;card.GetNode<Label>("Margin/Content/Status").Text=!unlocked?"未解锁":lead?"主骰":selected?"已携带":"";
            var toggle=card.GetNode<Button>("Margin/Content/Actions/Toggle");toggle.Text=selected?"移出":"携带";toggle.Disabled=!unlocked||(!selected&&!App.CanAddMythic(die.Id));var leadButton=card.GetNode<Button>("Margin/Content/Actions/Lead");leadButton.Text=lead?"主骰":"设主骰";leadButton.Disabled=!selected||lead;
        }
        string leadName=App.EditingDeck.Count>0&&App.Data.Types.ContainsKey(App.EditingDeck[0])?App.Data.Types[App.EditingDeck[0]].Name:"未选择";N<Label>("Margin/Layout/Pages/Deck/Top/Summary").Text=$"{App.EditingDeck.Count}/6 · 主骰 {leadName}";
        bool valid=App.Data.ValidDeck(App.EditingDeck)&&App.Progression!.CanUseDeck(State,App.EditingDeck);N<Button>("Margin/Layout/Pages/Deck/Bottom/Save").Disabled=!valid;N<Button>("Margin/Layout/Pages/Deck/Bottom/Start").Disabled=!valid||State.ActiveRunId!=""||State.Flight is not null;
    }

    private void BindBattle()
    {
        const string p="Margin/Layout/Pages/Battle";
        _field=N<TextureRect>(p+"/Body/Left/FieldAspect/FieldFrame/Field");_field.Texture=_root.Battlefield.GetTexture();
        _field.GuiInput += ev=>{if(App.Scene!="play")return;if(ev is InputEventMouseButton b&&b.ButtonIndex==MouseButton.Left&&b.Pressed){var pt=FieldPoint(_field.GetGlobalMousePosition());App.OnDown(pt.X,pt.Y);_field.AcceptEvent();}};
        for(int i=0;i<16;i++)
        {
            int slot=i;var view=N<Control>(p+"/Body/Right/Margin/Content/BoardGrid/Slot"+i);_slots.Add(view);_slotArt.Add(view.GetNode<TextureRect>("Margin/Layout/Art"));_slotBranches.Add(view.GetNode<Label>("Margin/Layout/Meta/Branches"));_slotReady.Add(view.GetNode<Label>("Margin/Layout/Meta/Ready"));_slotReload.Add(view.GetNode<ProgressBar>("Margin/Layout/Reload"));_slotHigh.Add(view.GetNode<CanvasItem>("HighFrame"));_slotReadyFrame.Add(view.GetNode<CanvasItem>("ReadyFrame"));_slotSelectedFrame.Add(view.GetNode<CanvasItem>("SelectedFrame"));
            view.GuiInput += ev=>SlotInput(slot,view,ev);
        }
        _dragArt=N<TextureRect>(p+"/DragArt");
        N<Button>(p+"/Body/Right/Margin/Content/Summon").Pressed += ()=>Act("summon");
        N<Button>(p+"/Body/Right/Margin/Content/Actions/Adjudicate").Pressed += ()=>Act("orderSkip");
        N<Button>(p+"/Body/Right/Margin/Content/Actions/Pause").Pressed += ()=>Act("pause");
        _conduits=N<BattleConduits>(p+"/Conduits");
        _conduits.Initialize(App,_root.Art,slot=>_slotArt[slot].GetGlobalRect().GetCenter()-new Vector2(0,_slotArt[slot].Size.Y*.2f),()=>_field.GlobalPosition+new Vector2((216f-25)/382*_field.Size.X,(530f-132)/444*_field.Size.Y),()=>_field.Size.Y/444);
    }
    private void SlotInput(int slot,Control view,InputEvent ev)
    {
        if(App.Scene!="play")return;
        if(ev is InputEventMouseButton b && b.Pressed)
        {
            if(b.ButtonIndex==MouseButton.Right || (b.ButtonIndex==MouseButton.Left && b.DoubleClick))
            {App.CancelPointer();App.OpenDie(slot);view.AcceptEvent();Invalidate();return;}
            if(b.ButtonIndex==MouseButton.Left){var pt=BoardPoint(view.GetGlobalMousePosition(),false);App.OnDown(pt.X,pt.Y);view.AcceptEvent();}
        }
    }
    private bool SlotHasPair(Simulation sim,int index){if(sim.State.Board[index] is null)return false;for(int j=0;j<sim.State.Board.Length;j++)if(j!=index&&sim.CanMerge(index,j))return true;return false;}
    private void UpdateBattle()
    {
        if(App.Sim is null)return;var sim=App.Sim;var state=sim.State;var e=state.Expedition;if(e is null)return;const string p="Margin/Layout/Pages/Battle";int phase=e.Region.PhaseIndex(e.LocalWave(state.Wave));
        string wave=e.LegacyRules?$"第 {state.Wave} 波":$"{e.Region.Phases[phase].Name} · {e.LocalWave(state.Wave)}/{e.Region.TotalWaves}"+(e.Region.IsBossWave(e.LocalWave(state.Wave))?" · "+e.Region.Phases[phase].BossName:"");
        N<Label>(p+"/Hud/Bar/Wave").Text=wave;N<Label>(p+"/Hud/Bar/Energy").Text=$"能量 {state.Energy:0.#}";N<Label>(p+"/Hud/Bar/Enemies").Text=$"敌人 {state.Enemies.Count(x=>x.Hp>0)}";N<Label>(p+"/Hud/Bar/HealthText").Text=$"防线 {state.Health}/{App.Data.Game.Rules.MaxHealth}";N<Label>(p+"/Hud/Bar/Lead").Text="主骰 · "+App.Data.Types[e.LeadDice].Name;
        N<ProgressBar>(p+"/Body/Right/Margin/Content/Health").MaxValue=App.Data.Game.Rules.MaxHealth;N<ProgressBar>(p+"/Body/Right/Margin/Content/Health").Value=state.Health;
        N<Label>(p+"/Body/Left/FieldAspect/FieldFrame/Intermission").Text=state.NextWaveIn>=0?$"整理时间  {Math.Max(0,state.NextWaveIn):0.0}":"";
        if(_selectedBoardSlot>=0&&(_selectedBoardSlot>=state.Board.Length||state.Board[_selectedBoardSlot] is null))_selectedBoardSlot=-1;
        bool dragging=App.Scene=="play"&&App.Pointer?.Mode=="drag";
        for(int i=0;i<_slots.Count;i++)
        {
            var die=state.Board[i];bool selected=i==_selectedBoardSlot;bool validTarget=_selectedBoardSlot>=0&&i!=_selectedBoardSlot&&sim.CanMerge(_selectedBoardSlot,i);bool hasPair=_selectedBoardSlot<0&&SlotHasPair(sim,i);
            _slotSelectedFrame[i].Visible=selected;_slotReadyFrame[i].Visible=validTarget||hasPair;_slotHigh[i].Visible=die?.Pips>=3;_slotReady[i].Text=validTarget?"合成":hasPair?"◆":"";
            if(die is null){_slotArt[i].Texture=null;_slotBranches[i].Text="";_slotReload[i].Visible=false;_slots[i].TooltipText="空位 · 单击召唤到这里";_slotKeys[i]="empty";continue;}
            string key=die.Type+":"+die.Pips+":"+die.Tier3+":"+die.Tier6;bool changed=_slotKeys[i]!=""&&_slotKeys[i]!="empty"&&_slotKeys[i]!=key;_slotKeys[i]=key;SetDice(_slotArt[i],_root.Art,die.Type,die.Pips);_slotBranches[i].Text=die.Tier3==""?"":die.Tier6==""?die.Tier3:die.Tier3+" + "+die.Tier6;_slotReload[i].Visible=true;_slotReload[i].Value=1-MathEx.Clamp(die.Cooldown/sim.Stats(die).Reload,0,1);_slots[i].TooltipText=App.Data.Types[die.Type].Name+" · "+DiceContent.RarityName(App.Data.Types[die.Type].Rarity)+"\n"+sim.SkillDescription(die);
            if(changed)AnimateSlot(i,die.Pips);bool source=dragging&&App.Pointer!.Slot==i,dragMatch=dragging&&sim.CanMerge(App.Pointer!.Slot,i);_slots[i].Modulate=source?new Color(1,1,1,.32f):dragMatch?Color.FromHtml(Mint):Colors.White;
        }
        N<Label>(p+"/Body/Right/Margin/Content/BoardHeader/BoardTitle").Text=$"阵地 {sim.Count}/{App.Data.Game.Board.Slots}";N<Label>(p+"/Body/Right/Margin/Content/BoardHeader/MergeState").Text=_selectedBoardSlot>=0?"选择合成目标":sim.HasPair()?"有可合成":"";
        var summon=N<Button>(p+"/Body/Right/Margin/Content/Summon");summon.Text=$"召唤 · {sim.SummonCost:0} 能量";summon.Disabled=App.Scene!="play"||sim.Count>=App.Data.Game.Board.Slots||state.Energy+1e-6<sim.SummonCost;
        var adjudicate=N<Button>(p+"/Body/Right/Margin/Content/Actions/Adjudicate");adjudicate.Visible=state.Deck.Any(id=>App.Data.Types[id].Traits.GetValueOrDefault("orderLaw")>0);adjudicate.Disabled=App.Scene!="play"||!sim.CanSkipOrder;N<Button>(p+"/Body/Right/Margin/Content/Actions/Pause").Disabled=App.Scene!="play";
        N<Label>(p+"/Body/Right/Margin/Content/ExpansionStatus").Text=sim.GlobalExpansionStatus();
        N<Label>(p+"/Body/Left/Hint").Text=App.Scene!="play"?"":App.Pointer?.Mode=="aim"?"松手发射 · 移出战场取消":_selectedBoardSlot>=0?"再点亮起的骰子完成合成 · 双击或右键查看详情":sim.HasPair()?"亮框表示可合成 · 单击一颗开始":sim.Count==App.Data.Game.Board.Slots?"阵地已满 · 合成会同时降低下一次召唤费用":"按住战场瞄准，松开发射";
        _dragArt.Visible=dragging;if(dragging&&state.Board[App.Pointer!.Slot] is { } d){SetDice(_dragArt,_root.Art,d.Type,d.Pips);_dragArt.GlobalPosition=GetGlobalMousePosition()-_dragArt.Size/2;}
    }
    private void AnimateSlot(int i,int pips)
    {
        if(App.Settings.ReduceMotion)return;var art=_slotArt[i];art.PivotOffset=art.Size/2;art.Scale=Vector2.One*.82f;art.Modulate=new Color(1,1,1,.65f);var t=art.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);t.TweenProperty(art,"scale",Vector2.One*(pips>=6?1.08f:pips>=3?1.04f:1f),pips>=6?.42:pips>=3?.32:.22);t.TweenProperty(art,"modulate",Colors.White,.2);
    }
    private void SelectOrMerge(int slot)
    {
        if(App.Sim is null||slot<0||slot>=App.Sim.State.Board.Length||App.Sim.State.Board[slot] is null)return;
        if(_selectedBoardSlot<0){_selectedBoardSlot=slot;return;}if(_selectedBoardSlot==slot){_selectedBoardSlot=-1;return;}
        if(App.Sim.CanMerge(_selectedBoardSlot,slot)){int source=_selectedBoardSlot;_selectedBoardSlot=-1;App.MergeSlots(source,slot);Invalidate();return;}_selectedBoardSlot=slot;
    }
    private PointD FieldPoint(Vector2 position){if(!_field.GetGlobalRect().HasPoint(position)||_field.Size.X<=0||_field.Size.Y<=0)return new(-1000,-1000);var q=(position-_field.GlobalPosition)/_field.Size;return new(25+q.X*382,132+q.Y*444);}
    private int BoardSlotAt(Vector2 position){for(int i=0;i<_slots.Count;i++)if(_slots[i].GetGlobalRect().HasPoint(position))return i;return -1;}
    private PointD BoardPoint(Vector2 position,bool moving){int i=BoardSlotAt(position);if(i>=0){var center=App.Data.SlotPosition(i);var local=(position-_slots[i].GlobalPosition)/_slots[i].Size;return new(center.X+(local.X-.5)*75,center.Y+(local.Y-.5)*80);}if(moving&&App.Pointer is { } pointer){var original=App.Data.SlotPosition(pointer.Slot);return new(original.X+1000,original.Y+1000);}return new(-1000,-1000);}
    public void MovePointer(Vector2 position){if(_townAiming)TownAim(position);if(App.Pointer is not { } p)return;var logical=p.Mode=="aim"?FieldPoint(position):BoardPoint(position,true);App.OnMove(logical.X,logical.Y);if(_dragArt.Visible)_dragArt.GlobalPosition=position-_dragArt.Size/2;}
    public void ReleasePointer(Vector2 position)
    {
        if(_townAiming){bool inside=_townBoard.GetGlobalRect().HasPoint(position);_townAiming=false;_townAimGuide.Visible=false;if(inside)LaunchTown(_townAngle);}
        if(App.Pointer is not { } pointer)return;string mode=pointer.Mode;int source=pointer.Slot;int clicked=BoardSlotAt(position);var logical=mode=="aim"?FieldPoint(position):BoardPoint(position,false);App.OnUp(logical.X,logical.Y);if(mode=="diepress"&&clicked==source&&App.Scene=="play")SelectOrMerge(source);
    }

    private void BindSettlement(){const string p="Margin/Layout/Pages/Settlement/Panel/Margin/Content/Actions";N<Button>(p+"/Return").Pressed+=()=>Act("town");N<Button>(p+"/Endless").Pressed+=()=>Act("endless");N<Button>(p+"/Retry").Pressed+=()=>Act("retrySettlement");}
    private void UpdateSettlement()
    {
        var run=App.Sim?.State;var e=run?.Expedition;if(run is null||e is null)return;const string p="Margin/Layout/Pages/Settlement/Panel/Margin/Content";N<Label>(p+"/Result").Text=Outcome(e.Outcome)+" · "+e.Region.Name;N<Label>(p+"/Reason").Text=e.EndReason;N<Label>(p+"/Stats").Text=$"波次 {run.Wave} · 击破 {run.Kills-e.StartKills} · {TimeText(run.Time-e.StartTime)}";
        if(!App.SettlementSaved){N<Label>(p+"/Rewards").Text="结算尚未写入存档";N<Label>(p+"/Unlocks").Text="";}else if(State.LastResult is { } receipt){N<Label>(p+"/Rewards").Text=receipt.Resources.Count==0?"本次未获得资源":Resources(receipt.Resources);var lines=new List<string>();if(receipt.NewGear)lines.Add("区域齿轮 +1");if(receipt.FirstClear)lines.Add("首次通关");lines.AddRange(receipt.NewBlueprints.Select(id=>"蓝图 · "+(Catalog.Buildings.GetValueOrDefault(id)?.Name??id)));lines.AddRange(receipt.NewDice.Select(id=>"骰子 · "+App.Data.Types[id].Name));lines.AddRange(receipt.NewMechanics.Select(id=>"机制 · "+Catalog.Mechanics[id].Name));lines.AddRange(receipt.NewRegions.Select(id=>"区域 · "+Catalog.Regions[id].Name));N<Label>(p+"/Unlocks").Text=string.Join("\n",lines);}
        N<Button>(p+"/Actions/Return").Disabled=!App.SettlementSaved;var endless=N<Button>(p+"/Actions/Endless");endless.Visible=e.Outcome=="victory"&&e.CanContinueEndless;endless.Disabled=!App.SettlementSaved;N<Button>(p+"/Actions/Retry").Visible=!App.SettlementSaved;
    }

    private void BindHelp()=>N<Button>("Margin/Layout/Pages/Help/Back").Pressed+=()=>Act("closeHelp");

    private void BindSettings()
    {
        const string p="Margin/Layout/Pages/Settings";var mode=N<OptionButton>(p+"/Columns/Display/WindowMode");mode.AddItem("窗口");mode.AddItem("无边框全屏");mode.AddItem("全屏");var res=N<OptionButton>(p+"/Columns/Display/Resolution");foreach(var r in _resolutions)res.AddItem($"{r.X} × {r.Y}");var fps=N<OptionButton>(p+"/Columns/Display/FpsLimit");foreach(int f in _fpsOptions)fps.AddItem(f==0?"不限":f.ToString());var msaa=N<OptionButton>(p+"/Columns/Display/Msaa");foreach(string s in new[]{"关闭","2×","4×","8×"})msaa.AddItem(s);
        mode.ItemSelected+=i=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.WindowMode=new[]{"windowed","borderless","fullscreen"}[(int)i];};res.ItemSelected+=i=>{if(!_settingsSync&&_settingsDraft is not null){_settingsDraft.Width=_resolutions[(int)i].X;_settingsDraft.Height=_resolutions[(int)i].Y;}};fps.ItemSelected+=i=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.MaxFps=_fpsOptions[(int)i];};msaa.ItemSelected+=i=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.Msaa=(int)i;};
        N<CheckButton>(p+"/Columns/Display/VSync").Toggled+=v=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.VSync=v;};N<CheckButton>(p+"/Columns/Display/ShowFps").Toggled+=v=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.ShowFps=v;};N<HSlider>(p+"/Columns/Display/UiScale").ValueChanged+=v=>{if(!_settingsSync&&_settingsDraft is not null){_settingsDraft.UiScale=v;N<Label>(p+"/Columns/Display/UiScaleLabel").Text=$"界面字号 {v:P0}";}};
        N<HSlider>(p+"/Columns/Audio/Master").ValueChanged+=v=>SetVolumeDraft("master",v);N<HSlider>(p+"/Columns/Audio/Sfx").ValueChanged+=v=>SetVolumeDraft("sfx",v);N<HSlider>(p+"/Columns/Audio/Music").ValueChanged+=v=>SetVolumeDraft("music",v);N<CheckButton>(p+"/Columns/Audio/ScreenShake").Toggled+=v=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.ScreenShake=v;};N<CheckButton>(p+"/Columns/Audio/FlashEffects").Toggled+=v=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.FlashEffects=v;};N<CheckButton>(p+"/Columns/Audio/ShowAim").Toggled+=v=>{if(!_settingsSync&&_settingsDraft is not null)_settingsDraft.ShowAim=v;};
        N<Button>(p+"/Columns/Audio/SoundToggle").Pressed+=()=>{App.Action("sound");UpdateSettings(false);};N<Button>(p+"/Columns/Audio/MusicToggle").Pressed+=()=>{App.Action("music");UpdateSettings(false);};N<Button>(p+"/Columns/Audio/ReduceMotion").Pressed+=()=>{App.Action("motion");UpdateSettings(false);};
        foreach(var q in new[]{("summon","SummonKey"),("pause","PauseKey"),("mute","MuteKey"),("fullscreen","FullscreenKey")}){string id=q.Item1;N<Button>(p+"/Columns/Controls/"+q.Item2).Pressed+=()=>{_bindingAction=id;N<Label>(p+"/Columns/Controls/BindingHint").Text="请按一个新按键；Esc 取消。";};}
        N<Button>(p+"/Columns/Controls/Backup").Pressed+=_root.BackupSave;N<Button>(p+"/Columns/Controls/OpenFolder").Pressed+=_root.OpenSaveFolder;N<Button>(p+"/Columns/Controls/Reset").Pressed+=()=>ShowConfirmation("重置永久进度","原文件会先备份。远征、城镇、区域与解锁随后重置。",_root.ResetProgress);
        N<Button>(p+"/Bottom/Cancel").Pressed+=()=>Act("closeSettings");N<Button>(p+"/Bottom/Defaults").Pressed+=()=>{_settingsDraft=new DesktopPreferences();SyncSettingsControls();};N<Button>(p+"/Bottom/Apply").Pressed+=()=>{if(_settingsDraft is not null)_root.PreviewPreferences(_settingsDraft);};
    }
    private void SetVolumeDraft(string kind,double v){if(_settingsSync||_settingsDraft is null)return;if(kind=="master")_settingsDraft.MasterVolume=v;else if(kind=="sfx")_settingsDraft.SfxVolume=v;else _settingsDraft.MusicVolume=v;const string p="Margin/Layout/Pages/Settings/Columns/Audio/";N<Label>(p+(kind=="master"?"MasterLabel":kind=="sfx"?"SfxLabel":"MusicLabel")).Text=(kind=="master"?"总音量 ":kind=="sfx"?"音效 ":"音乐 ")+v.ToString("P0");}
    private void UpdateSettings(bool resetDraft){if(resetDraft||_settingsDraft is null)_settingsDraft=CampaignCatalog.Copy(App.Preferences);SyncSettingsControls();const string p="Margin/Layout/Pages/Settings/Columns/Audio/";N<Button>(p+"SoundToggle").Text="声音 "+(App.Settings.Sound?"开":"关");N<Button>(p+"MusicToggle").Text="音乐 "+(App.Settings.Music?"开":"关");N<Button>(p+"ReduceMotion").Text="减弱动态 "+(App.Settings.ReduceMotion?"开":"关");}
    private void SyncSettingsControls()
    {
        if(_settingsDraft is null)return;_settingsSync=true;const string p="Margin/Layout/Pages/Settings";var d=_settingsDraft;N<OptionButton>(p+"/Columns/Display/WindowMode").Selected=d.WindowMode switch{"borderless"=>1,"fullscreen"=>2,_=>0};int ri=Array.FindIndex(_resolutions,r=>r.X==d.Width&&r.Y==d.Height);N<OptionButton>(p+"/Columns/Display/Resolution").Selected=Math.Max(0,ri);N<CheckButton>(p+"/Columns/Display/VSync").ButtonPressed=d.VSync;int fi=Array.IndexOf(_fpsOptions,d.MaxFps);N<OptionButton>(p+"/Columns/Display/FpsLimit").Selected=Math.Max(0,fi);N<OptionButton>(p+"/Columns/Display/Msaa").Selected=Math.Clamp(d.Msaa,0,3);N<HSlider>(p+"/Columns/Display/UiScale").Value=d.UiScale;N<Label>(p+"/Columns/Display/UiScaleLabel").Text=$"界面字号 {d.UiScale:P0}";N<CheckButton>(p+"/Columns/Display/ShowFps").ButtonPressed=d.ShowFps;
        N<HSlider>(p+"/Columns/Audio/Master").Value=d.MasterVolume;N<Label>(p+"/Columns/Audio/MasterLabel").Text=$"总音量 {d.MasterVolume:P0}";N<HSlider>(p+"/Columns/Audio/Sfx").Value=d.SfxVolume;N<Label>(p+"/Columns/Audio/SfxLabel").Text=$"音效 {d.SfxVolume:P0}";N<HSlider>(p+"/Columns/Audio/Music").Value=d.MusicVolume;N<Label>(p+"/Columns/Audio/MusicLabel").Text=$"音乐 {d.MusicVolume:P0}";N<CheckButton>(p+"/Columns/Audio/ScreenShake").ButtonPressed=d.ScreenShake;N<CheckButton>(p+"/Columns/Audio/FlashEffects").ButtonPressed=d.FlashEffects;N<CheckButton>(p+"/Columns/Audio/ShowAim").ButtonPressed=d.ShowAim;
        N<Button>(p+"/Columns/Controls/SummonKey").Text="召唤："+d.Bindings["summon"];N<Button>(p+"/Columns/Controls/PauseKey").Text="暂停："+d.Bindings["pause"];N<Button>(p+"/Columns/Controls/MuteKey").Text="声音："+d.Bindings["mute"];N<Button>(p+"/Columns/Controls/FullscreenKey").Text="全屏："+d.Bindings["fullscreen"];_settingsSync=false;
    }
    public bool HandleKeyBinding(InputEventKey key)
    {
        if(_bindingAction==""||_settingsDraft is null||App.Scene!="settings")return false;var code=key.PhysicalKeycode==Key.None?key.Keycode:key.PhysicalKeycode;var hint=N<Label>("Margin/Layout/Pages/Settings/Columns/Controls/BindingHint");if(code==Key.Escape){_bindingAction="";hint.Text="已取消按键修改。";return true;}string value=code.ToString();if(key.AltPressed||key.CtrlPressed||key.MetaPressed||code is Key.None or Key.Shift or Key.Ctrl or Key.Alt or Key.Meta||_settingsDraft.Bindings.Any(p=>p.Key!=_bindingAction&&p.Value.Equals(value,StringComparison.OrdinalIgnoreCase))){hint.Text="请选择未占用的单个按键。";return true;}_settingsDraft.Bindings[_bindingAction]=value;_bindingAction="";hint.Text="已修改，应用后生效。";SyncSettingsControls();return true;
    }

    private void BindOverlay()
    {
        N<Button>("Overlay/Center/PausePanel/Margin/Content/Continue").Pressed+=()=>Act("continue");N<Button>("Overlay/Center/PausePanel/Margin/Content/Settings").Pressed+=()=>Act("settings");N<Button>("Overlay/Center/PausePanel/Margin/Content/Town").Pressed+=()=>Act("town");N<Button>("Overlay/Center/PausePanel/Margin/Content/Abandon").Pressed+=()=>ShowConfirmation("撤回远征","不计通关；已经获得的资源与蓝图保留。",()=>Act("abandon"));N<Button>("Overlay/Center/PausePanel/Margin/Content/Help").Pressed+=()=>Act("help");
        for(int i=0;i<3;i++){int index=i;N<Button>($"Overlay/Center/UpgradePanel/Margin/Content/Choices/Choice{i}/Margin/Content/Action").Pressed+=()=>ChooseUpgrade(index);}N<Button>("Overlay/Center/UpgradePanel/Margin/Content/Town").Pressed+=()=>Act("town");
        for(int i=0;i<2;i++){int index=i;N<Button>($"Overlay/Center/DiceSkillPanel/Margin/Content/Choices/Choice{i}/Margin/Content/Action").Pressed+=()=>ChooseSkill(index);}
        N<Button>("Overlay/Center/DiePanel/Margin/Content/Back").Pressed+=()=>Act("closeDie");N<Button>("Overlay/Center/DiePanel/Margin/Content/Recycle").Pressed+=RecycleSelected;
        N<Button>("Overlay/Center/CatalogPanel/Margin/Content/Close").Pressed+=CloseCatalog;
        N<Button>("Overlay/Center/ConfirmPanel/Margin/Content/Actions/Cancel").Pressed+=()=>CloseConfirmation(false);N<Button>("Overlay/Center/ConfirmPanel/Margin/Content/Actions/Confirm").Pressed+=()=>CloseConfirmation(true);
        N<Button>("Overlay/Center/LoadErrorPanel/Margin/Content/OpenFolder").Pressed+=_root.OpenSaveFolder;N<Button>("Overlay/Center/LoadErrorPanel/Margin/Content/Backup").Pressed+=_root.BackupSave;N<Button>("Overlay/Center/LoadErrorPanel/Margin/Content/Reset").Pressed+=()=>ShowConfirmation("重置进度","原文件会先备份，再建立新进度。",_root.ResetProgress);N<Button>("Overlay/Center/LoadErrorPanel/Margin/Content/Quit").Pressed+=_root.Quit;
    }
    private void HideOverlayPanels(){foreach(var p in _overlayPanels)p.Visible=false;}
    private void ShowOverlayPanel(Control panel){HideOverlayPanels();_overlay.Visible=true;panel.Visible=true;if(!App.Settings.ReduceMotion)Reveal(panel);}
    private void HideOverlay(){HideOverlayPanels();_overlay.Visible=false;}
    private void RefreshOverlay()
    {
        if(App.LoadProblem!=""){ShowLoadError();return;}if(_confirmOpen){ShowOverlayPanel(_overlayPanels[5]);return;}if(_catalogOpen){ShowOverlayPanel(_overlayPanels[4]);return;}
        switch(App.Scene){case"paused":ShowPause();break;case"upgrade":ShowUpgrade();break;case"diceSkill":ShowDiceSkill();break;case"die":ShowDie();break;default:HideOverlay();break;}
    }
    private void ShowPause()=>ShowOverlayPanel(_overlayPanels[0]);
    private void ShowUpgrade(){if(App.Sim is null)return;ShowOverlayPanel(_overlayPanels[1]);for(int i=0;i<3;i++){var card=N<PanelContainer>($"Overlay/Center/UpgradePanel/Margin/Content/Choices/Choice{i}");bool show=i<App.Sim.State.Offers.Count;card.Visible=show;if(!show)continue;string id=App.Sim.State.Offers[i];var u=App.Data.UpgradeTypes[id];card.GetNode<Label>("Margin/Content/Key").Text=u.Tag;card.GetNode<Label>("Margin/Content/Name").Text=u.Name;card.GetNode<Label>("Margin/Content/Description").Text=u.Description;card.GetNode<Label>("Margin/Content/Preview").Text=$"Lv {App.Sim.State.Upgrades.GetValueOrDefault(id)}/{u.Max}";card.GetNode<Button>("Margin/Content/Action").Text="选择";}}
    private void ChooseUpgrade(int index){if(App.Sim is null||index<0||index>=App.Sim.State.Offers.Count)return;Act("upgrade:"+App.Sim.State.Offers[index]);}
    private void ShowDiceSkill(){if(App.Sim?.CurrentSkillChoice is not { } q)return;ShowOverlayPanel(_overlayPanels[2]);var definition=App.Data.Types[q.DiceType];N<Label>("Overlay/Center/DiceSkillPanel/Margin/Content/Title").Text=definition.Name+" · "+q.ResultPips+"点";N<Label>("Overlay/Center/DiceSkillPanel/Margin/Content/Tier").Text=q.Tier==3?"A / B":"C / D";SetDice(N<TextureRect>("Overlay/Center/DiceSkillPanel/Margin/Content/Art"),_root.Art,q.DiceType,q.ResultPips);var opts=App.Sim.SkillOptions;for(int i=0;i<2;i++){var card=N<PanelContainer>($"Overlay/Center/DiceSkillPanel/Margin/Content/Choices/Choice{i}");var o=opts[i];var preview=App.Sim.PreviewSkill(q.ChoiceId,o.Key);card.GetNode<Label>("Margin/Content/Key").Text=o.Key;card.GetNode<Label>("Margin/Content/Name").Text=o.Name;card.GetNode<Label>("Margin/Content/Description").Text=o.Description;card.GetNode<Label>("Margin/Content/Preview").Text=$"齐射 {preview.Volley:0.##} · {preview.Count} 发 · {preview.Reload:0.00}s";card.GetNode<Button>("Margin/Content/Action").Text="选择 "+o.Key;}}
    private void ChooseSkill(int index){if(App.Sim?.CurrentSkillChoice is not { } q||index<0||index>=App.Sim.SkillOptions.Count)return;Act($"diceSkill:{q.ChoiceId}:{App.Sim.SkillOptions[index].Key}");}
    private void ShowDie(){if(App.Sim is null||App.SelectedSlot<0||App.SelectedSlot>=App.Sim.State.Board.Length||App.Sim.State.Board[App.SelectedSlot] is not { } die){App.Scene="play";return;}ShowOverlayPanel(_overlayPanels[3]);var def=App.Data.Types[die.Type];var stats=App.Sim.Stats(die);N<Label>("Overlay/Center/DiePanel/Margin/Content/Title").Text=def.Name+" · "+die.Pips+"点";var rarity=N<Label>("Overlay/Center/DiePanel/Margin/Content/Rarity");rarity.Text=DiceContent.RarityName(def.Rarity);rarity.AddThemeColorOverride("font_color",Color.FromHtml(DiceContent.RarityColor(def.Rarity)));SetDice(N<TextureRect>("Overlay/Center/DiePanel/Margin/Content/Art"),_root.Art,die.Type,die.Pips);N<Label>("Overlay/Center/DiePanel/Margin/Content/Stats").Text=$"齐射 {Palette.Compact(stats.Volley)} · {stats.Count} 发 · 装填 {stats.Reload:0.00}s";N<Label>("Overlay/Center/DiePanel/Margin/Content/Skill").Text=App.Sim.SkillDescription(die);N<Label>("Overlay/Center/DiePanel/Margin/Content/Status").Text=App.Sim.ContentStatus(die);var recycle=N<Button>("Overlay/Center/DiePanel/Margin/Content/Recycle");recycle.Text=App.Sim.CanReincarnate(die)?"主动转世":"回收 · +"+App.Sim.RecycleValue(die)+" 能量";}
    private void RecycleSelected(){if(App.Sim is null||App.SelectedSlot<0||App.SelectedSlot>=App.Sim.State.Board.Length||App.Sim.State.Board[App.SelectedSlot] is not { } die)return;if(App.Sim.CanReincarnate(die))ShowConfirmation("主动转世","这颗六点轮回会重建为低点轮回，并失去本次强化选择。",()=>Act("recycle:"+App.SelectedSlot));else Act("recycle:"+App.SelectedSlot);}

    public Control ShowDiceCatalog(string id){OpenDiceCatalog(id);return _overlayPanels[4];}
    private void OpenDiceCatalog(string id)
    {
        if(!App.Data.Types.TryGetValue(id,out var die))return;_catalogOpen=true;var set=App.Data.Skills[id];N<Label>("Overlay/Center/CatalogPanel/Margin/Content/Title").Text=die.Name;var meta=N<Label>("Overlay/Center/CatalogPanel/Margin/Content/Meta");meta.Text=DiceContent.RarityName(die.Rarity)+" · "+die.Tag;meta.AddThemeColorOverride("font_color",Color.FromHtml(DiceContent.RarityColor(die.Rarity)));N<Label>("Overlay/Center/CatalogPanel/Margin/Content/Description").Text=die.Description;var skills=set.Level3.Concat(set.Level6).ToDictionary(x=>x.Key);foreach(string k in new[]{"A","B","C","D"}){var s=skills[k];N<Label>("Overlay/Center/CatalogPanel/Margin/Content/"+k).Text=(k is "A" or "B"?"3点 ":"6点 ")+k+" · "+s.Name+"\n"+s.Description;}ShowOverlayPanel(_overlayPanels[4]);
    }
    public void CloseCatalog(){_catalogOpen=false;RefreshOverlay();}

    public void ShowConfirmation(string title,string text,Action yes,Action? no=null,string confirmText="确认",string cancelText="取消")
    {
        _confirmOpen=true;_confirmYes=yes;_confirmNo=no;N<Label>("Overlay/Center/ConfirmPanel/Margin/Content/Title").Text=title;N<Label>("Overlay/Center/ConfirmPanel/Margin/Content/Text").Text=text;N<Button>("Overlay/Center/ConfirmPanel/Margin/Content/Actions/Confirm").Text=confirmText;N<Button>("Overlay/Center/ConfirmPanel/Margin/Content/Actions/Cancel").Text=cancelText;ShowOverlayPanel(_overlayPanels[5]);App.CancelPointer();
    }
    public void UpdateConfirmationText(string text){if(_confirmOpen)N<Label>("Overlay/Center/ConfirmPanel/Margin/Content/Text").Text=text;}
    public bool ConfirmationOpen=>_confirmOpen;
    public void DismissConfirmation()
    {
        _confirmOpen=false; _confirmYes=_confirmNo=null; RefreshOverlay();
    }
    private void CloseConfirmation(bool accepted){var yes=_confirmYes;var no=_confirmNo;_confirmOpen=false;_confirmYes=_confirmNo=null;if(accepted)yes?.Invoke();else no?.Invoke();RefreshOverlay();}
    private void ShowLoadError(){N<Label>("Overlay/Center/LoadErrorPanel/Margin/Content/Text").Text=App.LoadProblem;ShowOverlayPanel(_overlayPanels[6]);}

    private void Act(string id){App.Audio.Unlock();App.Action(id);Invalidate();}
    private void Tap(){App.Audio.Unlock();App.Audio.Play("tap");}

    public void Back()
    {
        if(_confirmOpen){CloseConfirmation(false);return;}if(_catalogOpen){CloseCatalog();return;}
        switch(App.Scene){case"play":Act("pause");break;case"paused":Act("continue");break;case"settings":Act("closeSettings");break;case"deck":App.Scene=_deckReturn;Invalidate();break;case"regions":Act("town");break;case"help":Act("closeHelp");break;case"die":Act("closeDie");break;case"settlement":if(App.SettlementSaved)Act("town");break;case"town":ShowConfirmation("退出游戏","当前进度会保存。",_root.Quit,null,"退出","取消");break;}
    }

    public void ApplyUiScale(double scale)=>ScaleTypography(this,scale);

    public void AssertLayout()
    {
        if(Size.X<=0||Size.Y<=0)throw new InvalidOperationException("Campaign UI root is empty.");
        if(App.Scene is "play" or "paused" or "upgrade" or "diceSkill" or "die")
        {
            if(_field.Size.X<300||_field.Size.Y<300||_slots.Count!=App.Data.Game.Board.Slots)throw new InvalidOperationException("Battle layout has no usable field or authored slots.");
            var bounds=GetViewportRect().Grow(2);foreach(var slot in _slots)if(!bounds.Encloses(slot.GetGlobalRect()))throw new InvalidOperationException("A battle slot lies outside the visible viewport: "+slot.Name);
        }
    }
}
