using Godot;
using DiceGame.Core;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

/// <summary>
/// Scene-authored player-facing UI. The original native presentation stays in the project as a fallback/capture oracle;
/// this layer only binds existing campaign/simulation state and actions and never owns gameplay state.
/// </summary>
public partial class CampaignUi
{
    private Control? _uxRoot;
    private MarginContainer? _uxShell;
    private Control? _uxOverlayHost;
    private string _uxScene = "";
    private string _uxRegionsKey = "", _uxDeckKey = "", _uxDecisionKey = "";
    private readonly List<Button> _uxTownTiles = [];
    private TextureRect? _uxTownBall;
    private ColorRect? _uxTownGuide;
    private readonly List<Control> _uxSlots = [];
    private readonly List<TextureRect> _uxSlotArt = [];
    private readonly List<Label> _uxSlotNames = [];
    private readonly List<Label> _uxSlotBranches = [];
    private readonly List<ProgressBar> _uxReloads = [];
    private TextureRect? _uxDragArt;
    private string[] _uxBlueprintIds = [];
    private string _uxSelectedBuilding = "";
    private string _uxDeckReturn = "regions";
    private bool _uxSettingsSync, _uxConfirmActive;
    private readonly List<Vector2I> _uxResolutions = [new(1280,720), new(1600,900), new(1920,1080), new(2560,1440), new(3840,2160)];
    private readonly int[] _uxFps = [0,30,60,90,120,144,165,240];

    public override void _Process(double delta)
    {
        if (_content is null || App.Campaign is null) return;
        if (_uxRoot is null || !IsInstanceValid(_uxRoot)) UxInitialize();
        if (_uxRoot is null || _uxShell is null) return;

        UxMotion.Reduced = App.Settings.ReduceMotion;
        UxHeader();

        if (_uxConfirmActive && (_confirm is null || !IsInstanceValid(_confirm)))
        {
            _uxConfirmActive = false;
            UxRebuildOverlay();
        }

        if (App.LoadProblem != "")
        {
            if (_uxScene != "loadError")
            {
                _uxScene = "loadError";
                UxHidePages();
                UxBuildLoadError();
            }
            return;
        }

        if (_uxScene != App.Scene)
        {
            _uxScene = App.Scene;
            _uxRegionsKey = _uxDeckKey = _uxDecisionKey = "";
            UxEnterScene(App.Scene);
        }

        switch (App.Scene)
        {
            case "town": UxBindTownRuntime(); UxUpdateTownInspector(); break;
            case "regions": UxUpdateRegions(); break;
            case "deck": UxUpdateDeck(); break;
            case "play": case "paused": case "upgrade": case "diceSkill": case "die":
                UxBindBattleRuntime(); UxUpdateBattle(); UxRefreshDecisionOverlay(); break;
            case "settlement": UxUpdateSettlement(); break;
            case "settings": UxUpdateSettings(); break;
        }
    }

    private void UxInitialize()
    {
        _uxRoot = GD.Load<PackedScene>("res://Scenes/UI/MatureUiRoot.tscn").Instantiate<Control>();
        AddChild(_uxRoot);
        _uxShell = _uxRoot.GetNode<MarginContainer>("MatureShell");
        _uxOverlayHost = _uxShell.GetNode<Control>("OverlayHost");
        UiKit.ScaleTypography(_uxShell, App.Preferences.UiScale);

        if (_content.GetParent()?.GetParent() is CanvasItem legacyShell) legacyShell.Visible = false;

        UxBindStaticButtons();
        UxCreateTownBoard();
        UxCreateBattleBoard();
        UxBindSettingsControls();
        UxReveal(_uxShell, false);
    }

    private T Ux<T>(string path) where T : Node => _uxShell!.GetNode<T>(path);
    private static T UxPrefab<T>(string path) where T : Node => GD.Load<PackedScene>(path).Instantiate<T>();
    private static void UxClear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }

    private Label UxLabel(Node parent, string text, int size = 20, string color = Ink)
    {
        var label = UxPrefab<Label>("res://Scenes/UI/GameLabel.tscn");
        parent.AddChild(label); label.Text = text; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", Color.FromHtml(color));
        return label;
    }

    private Button UxButton(Node parent, string text, Action action, bool primary = false, bool disabled = false)
    {
        var button = UxPrefab<Button>(primary ? "res://Scenes/UI/PrimaryButton.tscn" : "res://Scenes/UI/GameButton.tscn");
        parent.AddChild(button); button.Text = text; button.Disabled = disabled; button.Pressed += action; return button;
    }

    private void UxReveal(Control control, bool overlay)
    {
        if (UxMotion.Reduced) { control.Modulate = Colors.White; control.Scale = Vector2.One; return; }
        control.Modulate = new Color(1,1,1,0); control.Scale = Vector2.One * (overlay ? .965f : .988f);
        Callable.From(() =>
        {
            if (!IsInstanceValid(control)) return;
            control.PivotOffset = control.Size * .5f;
            var tween = control.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(control, "modulate", Colors.White, overlay ? .18 : .15);
            tween.TweenProperty(control, "scale", Vector2.One, overlay ? .20 : .17);
        }).CallDeferred();
    }

    private void UxTap()
    {
        App.Audio.Unlock();
        App.Audio.Play("tap");
    }

    private void UxHeader()
    {
        Ux<Label>("Layout/Header/Bar/Wallet").Text = Resources(State.Resources);
        var fps = Ux<Label>("Layout/Header/Bar/Fps"); fps.Visible = App.Preferences.ShowFps; fps.Text = Engine.GetFramesPerSecond() + " FPS";
        Ux<Label>("Layout/Toast").Text = App.Toast?.Text ?? "";
    }

    private void UxBindStaticButtons()
    {
        Ux<Button>("Layout/Pages/Town/Actions/Start").Pressed += UxTownStart;
        Ux<Button>("Layout/Pages/Town/Actions/Deck").Pressed += () => UxOpenDeck("town");
        Ux<Button>("Layout/Pages/Town/Actions/Settings").Pressed += () => Act("settings");
        Ux<Button>("Layout/Pages/Town/Actions/Help").Pressed += () => Act("help");
        Ux<Button>("Layout/Pages/Town/Actions/Quit").Pressed += _root.Quit;
        Ux<Button>("Layout/Pages/Town/Main/Left/Dispatch/DispatchSelected").Pressed += () => LaunchTown(TargetTownAngle());
        Ux<Button>("Layout/Pages/Town/Main/Left/Dispatch/DispatchAuto").Pressed += () => LaunchTown(App.TownSimulation.AutoAngle(State));
        Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Build").Pressed += UxBuildSelectedBlueprint;
        Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Upgrade").Pressed += UxUpgradeSelectedBuilding;
        Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Move").Pressed += UxToggleMove;
        Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/AutoDispatch").Pressed += () => { UxTap(); App.MutateTown(s => s.AutoDispatch = !s.AutoDispatch); Invalidate(); };

        Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/EditDeck").Pressed += () => UxOpenDeck("regions");
        Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/Start").Pressed += UxStartExpedition;
        Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/DeepDive").Pressed += () => UxConfirm("进入下一层", "保留当前永久成长，开启下一层区域进度。", () => Act("nextCycle"));
        Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/Back").Pressed += () => Act("town");

        Ux<Button>("Layout/Pages/Deck/Top/Back").Pressed += () => { UxTap(); App.Scene = _uxDeckReturn; };
        Ux<Button>("Layout/Pages/Deck/Bottom/Save").Pressed += () => { UxTap(); App.SaveDeck(); };
        Ux<Button>("Layout/Pages/Deck/Bottom/Start").Pressed += UxStartExpedition;

        Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Summon").Pressed += () => Act("summon");
        Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Actions/Adjudicate").Pressed += () => Act("orderSkip");
        Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Actions/Pause").Pressed += () => Act("pause");

        Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Return").Pressed += () => Act("town");
        Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Endless").Pressed += () => Act("endless");
        Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Retry").Pressed += () => Act("retrySettlement");

        Ux<Button>("Layout/Pages/Help/Back").Pressed += () => Act("closeHelp");
    }

    private void UxHidePages()
    {
        foreach (var child in Ux<Control>("Layout/Pages").GetChildren()) if (child is CanvasItem canvas) canvas.Visible = false;
        UxClearOverlay();
    }

    private Control UxShowPage(string name, string title)
    {
        UxHidePages();
        Ux<Label>("Layout/Header/Bar/Page").Text = title;
        var page = Ux<Control>("Layout/Pages/" + name); page.Visible = true; UxReveal(page, false); return page;
    }

    private void UxEnterScene(string scene)
    {
        switch (scene)
        {
            case "town":
                UxShowPage("Town", $"城镇 · 深潜 {State.Cycle + 1}");
                UxBindTownRuntime(); UxUpdateTownInspector();
                break;
            case "regions":
                if (App.EditingDeck.Count == 0) { App.EditingDeck.Clear(); App.EditingDeck.AddRange(App.Deck); }
                UxShowPage("Regions", $"出战 · 深潜 {State.Cycle + 1}"); UxUpdateRegions(true);
                break;
            case "deck":
                if (App.EditingDeck.Count == 0) { App.EditingDeck.Clear(); App.EditingDeck.AddRange(App.Deck); }
                UxShowPage("Deck", "骰组"); UxUpdateDeck(true);
                break;
            case "play":
                UxShowPage("Battle", App.Sim?.State.Expedition?.Region.Name ?? "远征"); UxBindBattleRuntime(); UxUpdateBattle();
                break;
            case "paused":
                UxShowPage("Battle", (App.Sim?.State.Expedition?.Region.Name ?? "远征") + " · 暂停"); UxBindBattleRuntime(); UxUpdateBattle(); UxBuildPauseOverlay();
                break;
            case "upgrade":
                UxShowPage("Battle", App.Sim?.State.Expedition?.Region.Name ?? "远征"); UxBindBattleRuntime(); UxUpdateBattle(); UxBuildUpgradeOverlay();
                break;
            case "diceSkill":
                UxShowPage("Battle", App.Sim?.State.Expedition?.Region.Name ?? "远征"); UxBindBattleRuntime(); UxUpdateBattle(); UxBuildDiceSkillOverlay();
                break;
            case "die":
                UxShowPage("Battle", App.Sim?.State.Expedition?.Region.Name ?? "远征"); UxBindBattleRuntime(); UxUpdateBattle(); UxBuildDieOverlay();
                break;
            case "settlement":
                UxShowPage("Settlement", "结算"); UxUpdateSettlement();
                break;
            case "settings":
                UxShowPage("Settings", "设置"); UxUpdateSettings(true);
                break;
            case "help":
                UxShowPage("Help", "说明");
                break;
            default:
                UxShowPage("Town", "城镇");
                break;
        }
    }

    private void UxTownStart()
    {
        if (State.ActiveRunId != "") { Act("resume"); return; }
        if (State.Flight is not null) return;
        UxTap();
        App.EditingDeck.Clear(); App.EditingDeck.AddRange(App.Deck);
        App.Scene = "regions";
    }

    private void UxOpenDeck(string returnScene)
    {
        UxTap(); _uxDeckReturn = returnScene;
        App.EditingDeck.Clear(); App.EditingDeck.AddRange(App.Deck);
        App.Scene = "deck";
    }

    private void UxCreateTownBoard()
    {
        var board = Ux<Control>("Layout/Pages/Town/Main/Left/BoardAspect/BoardFrame/TownBoard");
        board.GuiInput += TownInput;
        var t = Catalog.Definition.Town;
        for (int index = 0; index < t.Rows * t.Columns; index++)
        {
            int tile = index;
            var b = UxPrefab<Button>("res://Scenes/UI/GameButton.tscn"); board.AddChild(b); b.Name = "Plot" + tile;
            b.CustomMinimumSize = Vector2.Zero; b.ClipText = true; b.AddThemeFontSizeOverride("font_size", 14);
            b.Pressed += () => UxSelectTownTile(tile); _uxTownTiles.Add(b);
        }
        _uxTownGuide = UxPrefab<ColorRect>("res://Scenes/UI/TownAimGuide.tscn"); board.AddChild(_uxTownGuide);
        _uxTownBall = UxPrefab<TextureRect>("res://Scenes/UI/DiceView.tscn"); board.AddChild(_uxTownBall);
        _uxTownBall.Size = new Vector2(30,30); _uxTownBall.ZIndex = 9; SetDice(_uxTownBall, _root.Art, App.Deck[0], 1);
    }

    private void UxSelectTownTile(int tile)
    {
        if (State.Flight is not null || State.ActiveRunId != "") return;
        UxTap();
        if (_movingBuilding != "" && !Catalog.IsResourceTile(tile) && State.Buildings.Values.All(x => x.Tile != tile))
        {
            string id = _movingBuilding;
            if (App.MutateTown(s => App.Progression!.Relocate(s, id, tile))) _movingBuilding = "";
        }
        _selectedTile = tile; Invalidate(); UxUpdateTownInspector();
    }

    private void UxBindTownRuntime()
    {
        if (_uxTownBall is null || _uxTownGuide is null) return;
        _townBoard = Ux<Control>("Layout/Pages/Town/Main/Left/BoardAspect/BoardFrame/TownBoard");
        _townBall = _uxTownBall; _townAimGuide = _uxTownGuide; _townStatus = Ux<Label>("Layout/Pages/Town/Main/Left/Status");
        _townTiles.Clear(); _townTiles.AddRange(_uxTownTiles);
        UpdateTown();
        if (_townStatus is not null)
        {
            var f = State.Flight;
            _townStatus.Text = f is not null ? $"派遣中 · 命中 {f.Hits} · {Math.Max(0,f.Remaining):0.0}s" : _townAiming ? "松手派遣" : _movingBuilding != "" ? "选择空地完成搬迁" : "城镇就绪";
        }
        var start = Ux<Button>("Layout/Pages/Town/Actions/Start"); start.Text = State.ActiveRunId != "" ? "继续游戏" : "开始游戏"; start.Disabled = State.Flight is not null;
    }

    private void UxUpdateTownInspector()
    {
        var title = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/SelectionTitle");
        var status = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/SelectionStatus");
        var desc = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/SelectionDescription");
        var effect = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/SelectionEffect");
        var cost = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/SelectionCost");
        var picker = Ux<OptionButton>("Layout/Pages/Town/Main/Inspector/Margin/Content/BlueprintPicker");
        var build = Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Build");
        var upgrade = Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Upgrade");
        var move = Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/Move");
        var auto = Ux<Button>("Layout/Pages/Town/Main/Inspector/Margin/Content/AutoDispatch");
        _uxSelectedBuilding = "";

        picker.Visible = build.Visible = true; upgrade.Visible = move.Visible = false;
        title.Text = _selectedTile < 0 ? "选择地块" : "空地"; status.Text = ""; desc.Text = ""; effect.Text = ""; cost.Text = "";

        var buildingAt = State.Buildings.FirstOrDefault(p => p.Value.Tile == _selectedTile);
        if (buildingAt.Value is not null && Catalog.Buildings.TryGetValue(buildingAt.Key, out var selected))
        {
            _uxSelectedBuilding = selected.Id; var b = buildingAt.Value;
            title.Text = selected.Name; status.Text = b.Constructing ? $"施工 {b.Work}/{selected.Levels[b.Level].Work}" : $"Lv.{b.Level}"; desc.Text = selected.Description;
            picker.Visible = build.Visible = false; move.Visible = true; move.Text = _movingBuilding == selected.Id ? "取消搬迁" : "搬迁";
            if (b.Level < selected.Levels.Length)
            {
                var next = selected.Levels[b.Level]; effect.Text = RewardText(next.Reward); cost.Text = Resources(next.Cost);
                upgrade.Visible = true; upgrade.Disabled = App.Progression!.BuildingBlockers(State, selected.Id, b.Tile).Count > 0;
            }
        }
        else if (_selectedTile >= 0)
        {
            bool wood = Catalog.Definition.Town.WoodTiles.Contains(_selectedTile), stone = Catalog.Definition.Town.StoneTiles.Contains(_selectedTile);
            if (wood || stone) { title.Text = wood ? "木材" : "石材"; desc.Text = "资源地块"; picker.Visible = build.Visible = false; }
        }

        _uxBlueprintIds = Catalog.Definition.Buildings.Where(b => State.Blueprints.Contains(b.Id) && !State.Buildings.ContainsKey(b.Id)).Select(b => b.Id).ToArray();
        int keep = Math.Max(0, picker.Selected); picker.Clear();
        foreach (string id in _uxBlueprintIds) picker.AddItem(Catalog.Buildings[id].Name);
        if (_uxBlueprintIds.Length > 0) picker.Selected = Math.Min(keep, _uxBlueprintIds.Length - 1);
        build.Disabled = _selectedTile < 0 || Catalog.IsResourceTile(_selectedTile) || _uxBlueprintIds.Length == 0 || State.ActiveRunId != "" || State.Flight is not null;
        if (!build.Disabled && _uxBlueprintIds.Length > 0)
        {
            var reason = App.Progression!.BuildingBlockers(State, _uxBlueprintIds[picker.Selected], _selectedTile);
            build.Disabled = reason.Count > 0; if (reason.Count > 0) cost.Text = string.Join("；", reason);
        }

        auto.Visible = State.UnlockedMechanics.Contains("auto_dispatch"); auto.Text = "自动派遣 " + (State.AutoDispatch ? "开" : "关");
        auto.Disabled = State.ActiveRunId != "";

        var last = Ux<Label>("Layout/Pages/Town/Main/Inspector/Margin/Content/LastResult");
        last.Text = State.LastResult is { } result ? $"上次远征 · {result.RegionName} · {Outcome(result.Outcome)}\n{Resources(result.Resources)}" : "";
    }

    private void UxBuildSelectedBlueprint()
    {
        var picker = Ux<OptionButton>("Layout/Pages/Town/Main/Inspector/Margin/Content/BlueprintPicker");
        if (_selectedTile < 0 || picker.Selected < 0 || picker.Selected >= _uxBlueprintIds.Length) return;
        string id = _uxBlueprintIds[picker.Selected]; UxTap();
        App.MutateTown(s => App.Progression!.StartConstruction(s, id, _selectedTile)); Invalidate(); UxUpdateTownInspector();
    }

    private void UxUpgradeSelectedBuilding()
    {
        if (_uxSelectedBuilding == "" || !State.Buildings.TryGetValue(_uxSelectedBuilding, out var b)) return;
        UxTap(); App.MutateTown(s => App.Progression!.StartConstruction(s, _uxSelectedBuilding, b.Tile)); Invalidate(); UxUpdateTownInspector();
    }

    private void UxToggleMove()
    {
        if (_uxSelectedBuilding == "") return; UxTap();
        _movingBuilding = _movingBuilding == _uxSelectedBuilding ? "" : _uxSelectedBuilding; UxUpdateTownInspector();
    }

    private void UxUpdateRegions(bool force = false)
    {
        string recordKey = string.Join("|", Catalog.Definition.Regions.Select(r => r.Id + ":" + State.Record(r.Id).Clears + ":" + State.Record(r.Id).ClearedWith.Count));
        string key = State.SelectedRegion + "|" + State.Cycle + "|" + State.Gears + "|" + State.ActiveRunId + "|" + (State.Flight is not null) + "|" + string.Join(",", App.EditingDeck) + "|" + State.Blueprints.Count + "|" + recordKey;
        if (!force && key == _uxRegionsKey) return; _uxRegionsKey = key;

        var grid = Ux<GridContainer>("Layout/Pages/Regions/Main/RegionScroll/RegionGrid"); UxClear(grid);
        foreach (var region in Catalog.Definition.Regions)
        {
            bool open = App.Progression!.IsRegionOpen(State, region.Id), selected = State.SelectedRegion == region.Id; var record = State.Record(region.Id);
            var card = UxPrefab<PanelContainer>("res://Scenes/UI/RegionCard.tscn"); grid.AddChild(card);
            card.GetNode<Label>("Margin/Content/Name").Text = region.Name;
            card.GetNode<Label>("Margin/Content/Meta").Text = $"{region.Phases.Length} 阶段 · {region.TotalWaves} 波";
            card.GetNode<Label>("Margin/Content/Progress").Text = $"通关 {record.Clears} · 主骰记录 {record.ClearedWith.Count}";
            var remaining = region.BlueprintPool.Where(id => !State.Blueprints.Contains(id)).Select(id => Catalog.Buildings[id].Name).ToArray();
            card.GetNode<Label>("Margin/Content/Reward").Text = remaining.Length == 0 ? "蓝图已收集" : "待发现 · " + string.Join("、", remaining.Take(3)) + (remaining.Length > 3 ? "…" : "");
            card.GetNode<Label>("Margin/Content/Lock").Text = open ? "" : string.Join("；", App.Progression.MissingRequirements(State, region.Requirements));
            var action = card.GetNode<Button>("Margin/Content/Action"); action.Text = !open ? "未开放" : selected ? "已选择" : "选择"; action.Disabled = !open || State.ActiveRunId != "" || State.Flight is not null;
            string regionId = region.Id; action.Pressed += () => { UxTap(); if (App.MutateTown(s => s.SelectedRegion = regionId)) { _uxRegionsKey = ""; UxUpdateRegions(true); } };
            if (selected) card.Modulate = new Color(1f, .96f, .76f, 1f);
        }

        var selectedRegion = Catalog.Regions[State.SelectedRegion]; var selectedRecord = State.Record(selectedRegion.Id);
        Ux<Label>("Layout/Pages/Regions/Main/Side/Margin/Content/SelectedRegion").Text = selectedRegion.Name;
        Ux<Label>("Layout/Pages/Regions/Main/Side/Margin/Content/SelectedMeta").Text = $"{selectedRegion.TotalWaves} 波 · 通关 {selectedRecord.Clears} · 齿轮 {State.Gears}";
        UxBuildDeckStrip(Ux<GridContainer>("Layout/Pages/Regions/Main/Side/Margin/Content/DeckStrip"), App.EditingDeck);
        string lead = App.EditingDeck.Count > 0 && App.Data.Types.ContainsKey(App.EditingDeck[0]) ? App.Data.Types[App.EditingDeck[0]].Name : "未选择";
        Ux<Label>("Layout/Pages/Regions/Main/Side/Margin/Content/DeckSummary").Text = $"{App.EditingDeck.Count}/6 · 主骰 {lead}";

        bool validDeck = App.Data.ValidDeck(App.EditingDeck) && App.Progression!.CanUseDeck(State, App.EditingDeck);
        bool canStart = validDeck && State.ActiveRunId == "" && State.Flight is null && App.Progression.IsRegionOpen(State, State.SelectedRegion);
        var start = Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/Start"); start.Disabled = !canStart;
        Ux<Label>("Layout/Pages/Regions/Main/Side/Margin/Content/Blocker").Text = State.ActiveRunId != "" ? "已有未结束远征" : State.Flight is not null ? "城镇派遣尚未结束" : !validDeck ? "需要 6 种合法骰子" : "";
        var deep = Ux<Button>("Layout/Pages/Regions/Main/Side/Margin/Content/DeepDive"); deep.Disabled = !App.Progression.CanStartNextCycle(State);
    }

    private void UxBuildDeckStrip(GridContainer parent, IReadOnlyList<string> deck)
    {
        UxClear(parent);
        for (int i = 0; i < deck.Count; i++)
        {
            string id = deck[i]; if (!App.Data.Types.ContainsKey(id)) continue;
            var chip = UxPrefab<PanelContainer>("res://Scenes/UI/DeckChip.tscn"); parent.AddChild(chip);
            SetDice(chip.GetNode<TextureRect>("Layout/Art"), _root.Art, id, 1);
            chip.GetNode<Label>("Layout/Name").Text = App.Data.Types[id].Name;
            chip.GetNode<Label>("Layout/Lead").Text = i == 0 ? "主骰" : "";
        }
    }

    private void UxStartExpedition()
    {
        if (State.ActiveRunId != "" || State.Flight is not null) return;
        UxTap();
        if (App.SaveDeck()) App.StartExpedition();
    }

    private void UxUpdateDeck(bool force = false)
    {
        string key = string.Join(",", App.EditingDeck) + "|" + string.Join(",", State.UnlockedDice.OrderBy(x => x));
        if (!force && key == _uxDeckKey) return; _uxDeckKey = key;
        var grid = Ux<GridContainer>("Layout/Pages/Deck/Scroll/Grid"); UxClear(grid);
        foreach (var die in App.Data.Dice.OrderBy(d => Array.IndexOf(DiceContent.Rarities, d.Rarity)))
        {
            bool unlocked = State.UnlockedDice.Contains(die.Id), selected = App.EditingDeck.Contains(die.Id), lead = App.EditingDeck.FirstOrDefault() == die.Id;
            var card = UxPrefab<PanelContainer>("res://Scenes/UI/DiceCard.tscn"); grid.AddChild(card);
            SetDice(card.GetNode<TextureRect>("Margin/Content/Top/Art"), _root.Art, die.Id, 1);
            card.GetNode<TextureRect>("Margin/Content/Top/Art").Modulate = unlocked ? Colors.White : new Color(.35f,.35f,.35f,1);
            card.GetNode<Label>("Margin/Content/Top/Text/Name").Text = die.Name;
            var rarity = card.GetNode<Label>("Margin/Content/Top/Text/Rarity"); rarity.Text = DiceContent.RarityName(die.Rarity) + " · " + die.Tag; rarity.AddThemeColorOverride("font_color", Color.FromHtml(DiceContent.RarityColor(die.Rarity)));
            card.GetNode<Label>("Margin/Content/Top/Text/Description").Text = die.Description;
            card.GetNode<Label>("Margin/Content/Status").Text = !unlocked ? "未解锁" : lead ? "主骰" : selected ? "已携带" : "";
            string id = die.Id;
            card.GetNode<Button>("Margin/Content/Actions/Details").Pressed += () => UxShowDiceCatalog(id);
            var toggle = card.GetNode<Button>("Margin/Content/Actions/Toggle"); toggle.Text = selected ? "移出" : "携带"; toggle.Disabled = !unlocked || (!selected && !App.CanAddMythic(id));
            toggle.Pressed += () => { App.Action("deck:" + id); _uxDeckKey = ""; UxUpdateDeck(true); };
            var leadButton = card.GetNode<Button>("Margin/Content/Actions/Lead"); leadButton.Text = lead ? "主骰" : "设主骰"; leadButton.Disabled = !selected || lead;
            leadButton.Pressed += () => { UxTap(); App.SetLeadDice(id); _uxDeckKey = ""; UxUpdateDeck(true); };
        }
        string leadName = App.EditingDeck.Count > 0 && App.Data.Types.ContainsKey(App.EditingDeck[0]) ? App.Data.Types[App.EditingDeck[0]].Name : "未选择";
        Ux<Label>("Layout/Pages/Deck/Top/Summary").Text = $"{App.EditingDeck.Count}/6 · 主骰 {leadName}";
        bool valid = App.Data.ValidDeck(App.EditingDeck) && App.Progression!.CanUseDeck(State, App.EditingDeck);
        Ux<Button>("Layout/Pages/Deck/Bottom/Save").Disabled = !valid;
        Ux<Button>("Layout/Pages/Deck/Bottom/Start").Disabled = !valid || State.ActiveRunId != "" || State.Flight is not null;
    }

    private void UxShowDiceCatalog(string id)
    {
        UxClearOverlay();
        var content = UxDecisionContent(); var die = App.Data.Types[id]; var set = App.Data.Skills[id];
        UxLabel(content, die.Name, 32); UxLabel(content, DiceContent.RarityName(die.Rarity) + " · " + die.Tag, 17, DiceContent.RarityColor(die.Rarity));
        UxLabel(content, die.Description, 19, Muted);
        foreach (var s in set.Level3.Concat(set.Level6)) UxLabel(content, (s.Key is "A" or "B" ? "3点 " : "6点 ") + s.Key + " · " + s.Name + "\n" + s.Description, 18, s.Key is "A" or "C" ? Mint : Gold);
        UxButton(content, "关闭", UxClearOverlay);
    }

    private void UxCreateBattleBoard()
    {
        var field = Ux<TextureRect>("Layout/Pages/Battle/Left/FieldAspect/FieldFrame/Field"); field.Texture = _root.Battlefield.GetTexture();
        field.GuiInput += ev =>
        {
            if (App.Scene != "play") return;
            if (ev is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left && b.Pressed)
            { var p = FieldPoint(field.GetGlobalMousePosition()); App.OnDown(p.X, p.Y); field.AcceptEvent(); }
        };
        var grid = Ux<GridContainer>("Layout/Pages/Battle/Right/Margin/Content/BoardGrid");
        for (int i = 0; i < App.Data.Game.Board.Slots; i++)
        {
            int slot = i; var view = UxPrefab<PanelContainer>("res://Scenes/UI/MatureDiceSlot.tscn"); grid.AddChild(view); view.Name = "Slot" + i;
            _uxSlots.Add(view); _uxSlotArt.Add(view.GetNode<TextureRect>("Layout/Art")); _uxSlotNames.Add(view.GetNode<Label>("Layout/Name")); _uxSlotBranches.Add(view.GetNode<Label>("Layout/Branches")); _uxReloads.Add(view.GetNode<ProgressBar>("Layout/Reload"));
            view.GuiInput += ev =>
            {
                if (App.Scene != "play") return;
                if (ev is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left && b.Pressed)
                { var p = BoardPoint(view.GetGlobalMousePosition(), false); App.OnDown(p.X, p.Y); view.AcceptEvent(); }
            };
        }
        _uxDragArt = UxPrefab<TextureRect>("res://Scenes/UI/DiceView.tscn"); _uxRoot!.AddChild(_uxDragArt); _uxDragArt.Size = new Vector2(104,104); _uxDragArt.ZIndex = 160; _uxDragArt.Visible = false;
    }

    private void UxBindBattleRuntime()
    {
        _field = Ux<TextureRect>("Layout/Pages/Battle/Left/FieldAspect/FieldFrame/Field");
        _slots.Clear(); _slotArt.Clear(); _slotNames.Clear(); _slotBranches.Clear(); _reloads.Clear(); _slotKeys.Clear();
        for (int i = 0; i < _uxSlots.Count; i++)
        {
            _slots.Add(_uxSlots[i]); _slotArt.Add(_uxSlotArt[i]); _slotNames.Add(_uxSlotNames[i]); _slotBranches.Add(_uxSlotBranches[i]); _reloads.Add(_uxReloads[i]); _slotKeys.Add("");
        }
        _battleStatus = Ux<Label>("Layout/Pages/Battle/Left/Top/Status"); _battleStats = Ux<Label>("Layout/Pages/Battle/Left/Top/Stats");
        _progress = Ux<ProgressBar>("Layout/Pages/Battle/Left/Progress"); _health = Ux<ProgressBar>("Layout/Pages/Battle/Right/Margin/Content/Health");
        _battleHint = Ux<Label>("Layout/Pages/Battle/Left/Hint"); _expansionStatus = Ux<Label>("Layout/Pages/Battle/Right/Margin/Content/ExpansionStatus");
        if (_dragArt is not null && _dragArt != _uxDragArt) _dragArt.Visible = false; _dragArt = _uxDragArt;
        if (_conduits is CanvasItem effects) effects.ZIndex = 70;
    }

    private void UxUpdateBattle()
    {
        if (App.Sim is null || _uxSlots.Count != App.Data.Game.Board.Slots) return;
        var sim = App.Sim; var state = sim.State; var e = state.Expedition; if (e is null) return;
        int phase = e.Region.PhaseIndex(e.LocalWave(state.Wave));
        Ux<Label>("Layout/Pages/Battle/Left/Top/Status").Text = e.LegacyRules ? $"第 {state.Wave} 波" : $"{e.Region.Phases[phase].Name} · {e.LocalWave(state.Wave)}/{e.Region.TotalWaves}" + (e.Region.IsBossWave(e.LocalWave(state.Wave)) ? " · " + e.Region.Phases[phase].BossName : "");
        Ux<ProgressBar>("Layout/Pages/Battle/Left/Progress").Value = (double)(e.LocalWave(state.Wave) - 1) / e.Region.TotalWaves;
        Ux<Label>("Layout/Pages/Battle/Left/Top/Stats").Text = $"能量 {state.Energy:0}   敌人 {state.Enemies.Count(x=>x.Hp>0)}   击破 {state.Kills}";
        var health = Ux<ProgressBar>("Layout/Pages/Battle/Right/Margin/Content/Health"); health.MaxValue = App.Data.Game.Rules.MaxHealth; health.Value = state.Health;
        Ux<Label>("Layout/Pages/Battle/Right/Margin/Content/HealthRow/HealthLabel").Text = $"防线 {state.Health}/{App.Data.Game.Rules.MaxHealth}";
        Ux<Label>("Layout/Pages/Battle/Right/Margin/Content/HealthRow/Lead").Text = "主骰 · " + App.Data.Types[e.LeadDice].Name;

        bool dragging = App.Scene == "play" && App.Pointer?.Mode == "drag";
        for (int i = 0; i < _uxSlots.Count; i++)
        {
            var die = state.Board[i];
            if (die is null)
            {
                _uxSlotArt[i].Texture = null; _uxSlotNames[i].Text = "+ 空位"; _uxSlotBranches[i].Text = ""; _uxReloads[i].Visible = false; _uxSlots[i].TooltipText = "单击召唤到此位置";
            }
            else
            {
                SetDice(_uxSlotArt[i], _root.Art, die.Type, die.Pips); _uxSlotNames[i].Text = App.Data.Types[die.Type].Name + " · " + die.Pips;
                _uxSlotBranches[i].Text = die.Tier3 == "" ? "" : die.Tier6 == "" ? die.Tier3 : die.Tier3 + " + " + die.Tier6;
                _uxReloads[i].Visible = true; _uxReloads[i].Value = 1 - MathEx.Clamp(die.Cooldown / sim.Stats(die).Reload, 0, 1);
                _uxSlots[i].TooltipText = DiceContent.RarityName(App.Data.Types[die.Type].Rarity) + " · " + App.Data.Types[die.Type].Description + "\n" + sim.SkillDescription(die);
            }
            bool source = dragging && App.Pointer!.Slot == i, match = dragging && sim.CanMerge(App.Pointer!.Slot, i);
            _uxSlots[i].Modulate = source ? new Color(1,1,1,.25f) : match ? Color.FromHtml(Mint) : Colors.White;
            var pulse = App.Effects.Pulses.Find(p => p.Slot == i); double scale = 1;
            if (pulse is not null && !App.Settings.ReduceMotion) { double p = 1 - pulse.Life / pulse.Max; scale += Math.Sin(p * Math.PI * 2.3) * .1 * (1-p); }
            _uxSlotArt[i].PivotOffset = _uxSlotArt[i].Size / 2; _uxSlotArt[i].Scale = Vector2.One * (float)scale;
        }

        var summon = Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Summon"); summon.Text = "召唤 · " + App.Data.Game.Rules.SummonCost + " 能量";
        summon.Disabled = App.Scene != "play" || sim.Count >= App.Data.Game.Board.Slots || state.Energy < App.Data.Game.Rules.SummonCost;
        var adjudicate = Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Actions/Adjudicate"); adjudicate.Visible = state.Deck.Any(id => App.Data.Types[id].Traits.GetValueOrDefault("orderLaw") > 0); adjudicate.Disabled = App.Scene != "play" || !sim.CanSkipOrder;
        Ux<Button>("Layout/Pages/Battle/Right/Margin/Content/Actions/Pause").Disabled = App.Scene != "play";
        Ux<Label>("Layout/Pages/Battle/Right/Margin/Content/ExpansionStatus").Text = sim.GlobalExpansionStatus();
        Ux<Label>("Layout/Pages/Battle/Left/Hint").Text = App.Scene != "play" ? "" : App.Pointer?.Mode == "aim" ? "松手发射 · 移出战场取消" : sim.HasPair() ? "有可合成骰子" : sim.Count == App.Data.Game.Board.Slots ? "阵地已满" : "按住战场瞄准，松开发射";

        if (_uxDragArt is not null)
        {
            _uxDragArt.Visible = dragging;
            if (dragging && state.Board[App.Pointer!.Slot] is { } d)
            { SetDice(_uxDragArt, _root.Art, d.Type, d.Pips); _uxDragArt.GlobalPosition = GetGlobalMousePosition() - _uxDragArt.Size / 2; }
        }
    }

    private VBoxContainer UxDecisionContent()
    {
        var overlay = UxPrefab<Control>("res://Scenes/UI/DecisionOverlay.tscn"); _uxOverlayHost!.AddChild(overlay); UxReveal(overlay, true);
        return overlay.GetNode<VBoxContainer>("Center/Panel/Margin/Content");
    }

    private void UxClearOverlay()
    {
        if (_uxOverlayHost is not null) UxClear(_uxOverlayHost);
        _uxDecisionKey = "";
    }

    private void UxRefreshDecisionOverlay()
    {
        if (_uxConfirmActive) return;
        if (App.Scene == "diceSkill" && App.Sim?.CurrentSkillChoice is { } q)
        {
            string key = "skill:" + q.ChoiceId + ":" + q.Tier + ":" + q.ResultPips;
            if (key != _uxDecisionKey) { _uxDecisionKey = key; UxBuildDiceSkillOverlay(); }
        }
        else if (App.Scene == "upgrade" && App.Sim is not null)
        {
            string key = "upgrade:" + string.Join(",", App.Sim.State.Offers);
            if (key != _uxDecisionKey) { _uxDecisionKey = key; UxBuildUpgradeOverlay(); }
        }
    }

    private void UxBuildPauseOverlay()
    {
        UxClearOverlay(); _uxDecisionKey = "pause"; var content = UxDecisionContent();
        UxLabel(content, "暂停", 34); UxButton(content, "继续", () => Act("continue"), true);
        UxButton(content, "设置", () => Act("settings")); UxButton(content, "返回城镇", () => Act("town"));
        UxButton(content, "撤回远征", () => UxConfirm("撤回远征", "不计通关；已经获得的资源与蓝图保留。", () => Act("abandon")));
        UxButton(content, "说明", () => Act("help"));
    }

    private void UxBuildUpgradeOverlay()
    {
        if (App.Sim is null) return; UxClearOverlay(); _uxDecisionKey = "upgrade:" + string.Join(",", App.Sim.State.Offers);
        var content = UxDecisionContent(); UxLabel(content, "强化", 34);
        var grid = UxPrefab<GridContainer>("res://Scenes/UI/ChoiceGrid.tscn"); grid.Columns = Math.Max(1, Math.Min(3, App.Sim.State.Offers.Count)); content.AddChild(grid);
        foreach (string id in App.Sim.State.Offers)
        {
            var u = App.Data.UpgradeTypes[id]; var card = UxPrefab<PanelContainer>("res://Scenes/UI/SkillChoiceCard.tscn"); grid.AddChild(card);
            card.GetNode<Label>("Margin/Content/Key").Text = u.Tag; card.GetNode<Label>("Margin/Content/Name").Text = u.Name; card.GetNode<Label>("Margin/Content/Description").Text = u.Description;
            card.GetNode<Label>("Margin/Content/Preview").Text = $"Lv {App.Sim.State.Upgrades.GetValueOrDefault(id)}/{u.Max}";
            card.GetNode<Button>("Margin/Content/Action").Pressed += () => Act("upgrade:" + id);
        }
        UxButton(content, "返回城镇", () => Act("town"));
    }

    private void UxBuildDiceSkillOverlay()
    {
        if (App.Sim?.CurrentSkillChoice is not { } q) return; var sim = App.Sim; UxClearOverlay(); _uxDecisionKey = "skill:" + q.ChoiceId + ":" + q.Tier + ":" + q.ResultPips;
        var content = UxDecisionContent(); var definition = App.Data.Types[q.DiceType];
        UxLabel(content, definition.Name + " · " + q.ResultPips + "点", 34);
        UxLabel(content, q.Tier == 3 ? "A / B" : "C / D", 18, Gold);
        var art = UxPrefab<TextureRect>("res://Scenes/UI/DiceView.tscn"); content.AddChild(art); art.CustomMinimumSize = new Vector2(118,118); SetDice(art, _root.Art, q.DiceType, q.ResultPips);
        var grid = UxPrefab<GridContainer>("res://Scenes/UI/ChoiceGrid.tscn"); grid.Columns = 2; content.AddChild(grid);
        foreach (var option in sim.SkillOptions)
        {
            string choice = option.Key; long token = q.ChoiceId; var preview = sim.PreviewSkill(token, choice);
            var card = UxPrefab<PanelContainer>("res://Scenes/UI/SkillChoiceCard.tscn"); grid.AddChild(card);
            card.GetNode<Label>("Margin/Content/Key").Text = choice; card.GetNode<Label>("Margin/Content/Name").Text = option.Name; card.GetNode<Label>("Margin/Content/Description").Text = option.Description;
            card.GetNode<Label>("Margin/Content/Preview").Text = $"弹丸伤害 {preview.Volley:0.##} · {preview.Count} 发 · {preview.Reload:0.00}s";
            var action = card.GetNode<Button>("Margin/Content/Action"); action.Text = "选择 " + choice; action.Pressed += () => Act($"diceSkill:{token}:{choice}");
        }
    }

    private void UxBuildDieOverlay()
    {
        if (App.Sim is null || App.SelectedSlot < 0 || App.Sim.State.Board[App.SelectedSlot] is not { } die) return;
        UxClearOverlay(); _uxDecisionKey = "die:" + die.Id + ":" + die.Pips + ":" + die.Tier3 + ":" + die.Tier6;
        var content = UxDecisionContent(); var definition = App.Data.Types[die.Type]; var stats = App.Sim.Stats(die);
        UxLabel(content, definition.Name + " · " + die.Pips + "点", 34); UxLabel(content, DiceContent.RarityName(definition.Rarity), 17, DiceContent.RarityColor(definition.Rarity));
        var art = UxPrefab<TextureRect>("res://Scenes/UI/DiceView.tscn"); content.AddChild(art); art.CustomMinimumSize = new Vector2(150,150); SetDice(art, _root.Art, die.Type, die.Pips);
        UxLabel(content, $"弹丸伤害 {Palette.Compact(stats.Volley)} · {stats.Count} 发 · 装填 {stats.Reload:0.00}s", 21, Mint);
        string skill = App.Sim.SkillDescription(die); if (skill != "") UxLabel(content, skill, 19, Gold);
        string contentStatus = App.Sim.ContentStatus(die); if (contentStatus != "") UxLabel(content, contentStatus, 18, Mint);
        UxButton(content, "返回战斗", () => Act("closeDie"), true);
        if (App.Sim.CanReincarnate(die)) UxButton(content, "主动转世", () => UxConfirm("主动转世", "这颗六点轮回会重建为低点轮回，并失去本次强化选择。", () => Act("recycle:" + App.SelectedSlot)));
        else UxButton(content, "回收 · +" + App.Sim.RecycleValue(die) + " 能量", () => Act("recycle:" + App.SelectedSlot));
    }

    private void UxRebuildOverlay()
    {
        if (App.LoadProblem != "") { UxBuildLoadError(); return; }
        switch (App.Scene)
        {
            case "paused": UxBuildPauseOverlay(); break;
            case "upgrade": UxBuildUpgradeOverlay(); break;
            case "diceSkill": UxBuildDiceSkillOverlay(); break;
            case "die": UxBuildDieOverlay(); break;
            default: UxClearOverlay(); break;
        }
    }

    private void UxConfirm(string title, string text, Action yes)
    {
        UxClearOverlay(); _uxConfirmActive = true;
        if (_confirm is not null && IsInstanceValid(_confirm)) { _confirm.QueueFree(); _confirm = null; }
        _confirm = new ConfirmationDialog { Visible = false }; AddChild(_confirm);
        var content = UxDecisionContent(); UxLabel(content, title, 32); UxLabel(content, text, 19, Muted);
        var row = UxPrefab<HBoxContainer>("res://Scenes/UI/LayoutRow.tscn"); content.AddChild(row);
        UxButton(row, "取消", () => UxCloseConfirm(false)); UxButton(row, "确认", () => { UxCloseConfirm(false); yes(); }, true);
    }

    private void UxCloseConfirm(bool restore)
    {
        _uxConfirmActive = false;
        if (_confirm is not null && IsInstanceValid(_confirm)) { _confirm.Hide(); _confirm.QueueFree(); _confirm = null; }
        UxClearOverlay(); if (restore) UxRebuildOverlay();
    }

    private void UxBuildLoadError()
    {
        UxClearOverlay(); Ux<Label>("Layout/Header/Bar/Page").Text = "存档保护"; var content = UxDecisionContent();
        UxLabel(content, "存档保护", 34); UxLabel(content, App.LoadProblem, 19, Danger);
        UxButton(content, "打开存档目录", _root.OpenSaveFolder); UxButton(content, "导出备份", _root.BackupSave);
        UxButton(content, "备份并重置", () => UxConfirm("重置进度", "原文件会先备份，再建立新进度。", _root.ResetProgress));
        UxButton(content, "退出", _root.Quit);
    }

    private void UxUpdateSettlement()
    {
        var run = App.Sim?.State; var e = run?.Expedition; if (run is null || e is null) return;
        Ux<Label>("Layout/Pages/Settlement/Panel/Margin/Content/Result").Text = Outcome(e.Outcome) + " · " + e.Region.Name;
        Ux<Label>("Layout/Pages/Settlement/Panel/Margin/Content/Reason").Text = e.EndReason;
        Ux<Label>("Layout/Pages/Settlement/Panel/Margin/Content/Stats").Text = $"波次 {run.Wave} · 击破 {run.Kills - e.StartKills} · {TimeText(run.Time - e.StartTime)}";
        var rewards = Ux<Label>("Layout/Pages/Settlement/Panel/Margin/Content/Rewards"); var unlocks = Ux<Label>("Layout/Pages/Settlement/Panel/Margin/Content/Unlocks");
        if (!App.SettlementSaved)
        {
            rewards.Text = "结算尚未写入存档"; unlocks.Text = "";
        }
        else if (State.LastResult is { } receipt)
        {
            rewards.Text = receipt.Resources.Count == 0 ? "本次未获得资源" : Resources(receipt.Resources);
            var lines = new List<string>(); if (receipt.NewGear) lines.Add("区域齿轮 +1"); if (receipt.FirstClear) lines.Add("首次通关");
            lines.AddRange(receipt.NewBlueprints.Select(id => "蓝图 · " + (Catalog.Buildings.GetValueOrDefault(id)?.Name ?? id)));
            lines.AddRange(receipt.NewDice.Select(id => "骰子 · " + App.Data.Types[id].Name)); lines.AddRange(receipt.NewMechanics.Select(id => "机制 · " + Catalog.Mechanics[id].Name)); lines.AddRange(receipt.NewRegions.Select(id => "区域 · " + Catalog.Regions[id].Name));
            unlocks.Text = string.Join("\n", lines);
        }
        var ret = Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Return"); ret.Disabled = !App.SettlementSaved;
        var endless = Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Endless"); endless.Visible = e.Outcome == "victory" && e.CanContinueEndless; endless.Disabled = !App.SettlementSaved;
        Ux<Button>("Layout/Pages/Settlement/Panel/Margin/Content/Actions/Retry").Visible = !App.SettlementSaved;
    }

    private void UxBindSettingsControls()
    {
        var windowMode = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/WindowMode"); windowMode.AddItem("窗口"); windowMode.AddItem("无边框全屏"); windowMode.AddItem("全屏");
        var fps = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/FpsLimit"); foreach (int value in _uxFps) fps.AddItem(value == 0 ? "不限" : value.ToString());
        var msaa = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/Msaa"); foreach (string value in new[] { "关闭", "2×", "4×", "8×" }) msaa.AddItem(value);

        windowMode.ItemSelected += i => { if (_uxSettingsSync || _settingsDraft is null) return; _settingsDraft.WindowMode = new[] { "windowed", "borderless", "fullscreen" }[(int)i]; };
        Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/Resolution").ItemSelected += i => { if (_uxSettingsSync || _settingsDraft is null || i >= _uxResolutions.Count) return; _settingsDraft.Width = _uxResolutions[(int)i].X; _settingsDraft.Height = _uxResolutions[(int)i].Y; };
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Display/Content/Vsync").Toggled += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.VSync = v; };
        fps.ItemSelected += i => { if (!_uxSettingsSync && _settingsDraft is not null && i < _uxFps.Length) _settingsDraft.MaxFps = _uxFps[(int)i]; };
        msaa.ItemSelected += i => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.Msaa = (int)i; };
        Ux<HSlider>("Layout/Pages/Settings/Columns/Display/Content/UiScale").ValueChanged += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.UiScale = v; };
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Display/Content/ShowFps").Toggled += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.ShowFps = v; };
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/Master").ValueChanged += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.MasterVolume = v; };
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/Sfx").ValueChanged += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.SfxVolume = v; };
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/MusicVolume").ValueChanged += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.MusicVolume = v; };
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/ScreenShake").Toggled += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.ScreenShake = v; };
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/Flash").Toggled += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.FlashEffects = v; };
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/ShowAim").Toggled += v => { if (!_uxSettingsSync && _settingsDraft is not null) _settingsDraft.ShowAim = v; };
        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/SoundToggle").Pressed += () => App.Action("sound");
        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/MusicToggle").Pressed += () => App.Action("music");
        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/MotionToggle").Pressed += () => App.Action("motion");

        BindSettingButton("BindSummon", "summon"); BindSettingButton("BindPause", "pause"); BindSettingButton("BindMute", "mute"); BindSettingButton("BindFullscreen", "fullscreen");
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/Backup").Pressed += _root.BackupSave;
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/Folder").Pressed += _root.OpenSaveFolder;
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/Reset").Pressed += () => UxConfirm("重置进度", "原存档会先备份。随后重置城镇、区域与解锁。", _root.ResetProgress);
        Ux<Button>("Layout/Pages/Settings/Bottom/Cancel").Pressed += () => Act("closeSettings");
        Ux<Button>("Layout/Pages/Settings/Bottom/Defaults").Pressed += () => { _settingsDraft = new DesktopPreferences(); UxUpdateSettings(true); };
        Ux<Button>("Layout/Pages/Settings/Bottom/Apply").Pressed += () => { if (_settingsDraft is not null) _root.PreviewPreferences(_settingsDraft); };
    }

    private void BindSettingButton(string node, string action)
    {
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/" + node).Pressed += () =>
        {
            _bindingAction = action; _bindingLabel = Ux<Label>("Layout/Pages/Settings/Columns/Controls/Content/BindingStatus"); _bindingLabel.Text = "按下新按键 · Esc 取消";
        };
    }

    private void UxUpdateSettings(bool force = false)
    {
        _settingsDraft ??= CampaignCatalog.Copy(App.Preferences); var p = _settingsDraft; _uxSettingsSync = true;
        var window = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/WindowMode"); window.Selected = p.WindowMode switch { "borderless" => 1, "fullscreen" => 2, _ => 0 };
        var saved = new Vector2I(p.Width, p.Height); if (!_uxResolutions.Contains(saved)) _uxResolutions.Add(saved);
        var resolutions = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/Resolution"); resolutions.Clear(); foreach (var r in _uxResolutions) resolutions.AddItem(r.X + " × " + r.Y); resolutions.Selected = _uxResolutions.IndexOf(saved);
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Display/Content/Vsync").ButtonPressed = p.VSync;
        var fps = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/FpsLimit"); int fpsIndex = Array.IndexOf(_uxFps, p.MaxFps); fps.Selected = Math.Max(0, fpsIndex);
        var msaa = Ux<OptionButton>("Layout/Pages/Settings/Columns/Display/Content/Msaa"); msaa.Selected = Math.Clamp(p.Msaa,0,3); msaa.Disabled = RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility";
        Ux<HSlider>("Layout/Pages/Settings/Columns/Display/Content/UiScale").Value = p.UiScale; Ux<Label>("Layout/Pages/Settings/Columns/Display/Content/UiScaleLabel").Text = $"界面字号 · {p.UiScale:P0}";
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Display/Content/ShowFps").ButtonPressed = p.ShowFps;
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/Master").Value = p.MasterVolume; Ux<Label>("Layout/Pages/Settings/Columns/Audio/Content/MasterLabel").Text = $"总音量 · {p.MasterVolume:P0}";
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/Sfx").Value = p.SfxVolume; Ux<Label>("Layout/Pages/Settings/Columns/Audio/Content/SfxLabel").Text = $"音效音量 · {p.SfxVolume:P0}";
        Ux<HSlider>("Layout/Pages/Settings/Columns/Audio/Content/MusicVolume").Value = p.MusicVolume; Ux<Label>("Layout/Pages/Settings/Columns/Audio/Content/MusicLabel").Text = $"音乐音量 · {p.MusicVolume:P0}";
        Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/ScreenShake").ButtonPressed = p.ScreenShake; Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/Flash").ButtonPressed = p.FlashEffects; Ux<CheckButton>("Layout/Pages/Settings/Columns/Audio/Content/ShowAim").ButtonPressed = p.ShowAim;
        _uxSettingsSync = false;

        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/SoundToggle").Text = "声音 · " + (App.Settings.Sound ? "开" : "关");
        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/MusicToggle").Text = "音乐 · " + (App.Settings.Music ? "开" : "关");
        Ux<Button>("Layout/Pages/Settings/Columns/Audio/Content/MotionToggle").Text = "减弱动态效果 · " + (App.Settings.ReduceMotion ? "开" : "关");
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/BindSummon").Text = "召唤 · " + p.Bindings["summon"];
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/BindPause").Text = "暂停 · " + p.Bindings["pause"];
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/BindMute").Text = "声音开关 · " + p.Bindings["mute"];
        Ux<Button>("Layout/Pages/Settings/Columns/Controls/Content/BindFullscreen").Text = "切换全屏 · " + p.Bindings["fullscreen"];
        _bindingLabel = Ux<Label>("Layout/Pages/Settings/Columns/Controls/Content/BindingStatus");
        if (_bindingAction == "") _bindingLabel.Text = "Esc 返回 · 右键取消当前指针操作";
    }
}
