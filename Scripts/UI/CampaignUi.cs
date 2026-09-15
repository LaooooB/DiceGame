using Godot;
using DiceGame.App;
using DiceGame.Core;
using DiceGame.Presentation;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

/// <summary>Native Control presentation only. All rewards, gates and writes go through GameApp/Progression.</summary>
public partial class CampaignUi : Control
{
    private GameRoot _root = null!;
    private GameApp App => _root.App;
    private CampaignCatalog Catalog => App.Catalog!;
    private CampaignState State => App.Campaign!;
    private VBoxContainer _content = null!;
    private Label _wallet = null!, _title = null!, _crumb = null!, _toast = null!, _fps = null!;
    private string _builtScene = "";
    private bool _dirty = true;
    private string _townPresentationKey = "";
    private ColorRect? _townAimGuide;
    private readonly List<Action> _live = [];
    private TextureRect? _field, _dragArt;
    private BattleConduits? _conduits;
    private readonly List<Control> _slots = [];
    private readonly List<TextureRect> _slotArt = [];
    private readonly List<Label> _slotNames = [];
    private readonly List<Label> _slotBranches = [];
    private readonly List<ProgressBar> _reloads = [];
    private readonly List<string> _slotKeys = [];
    private Control? _townBoard;
    private TextureRect? _townBall;
    private Label? _townStatus;
    private bool _townAiming;
    private double _townAngle = -Math.PI / 2;
    private int _selectedTile = -1;
    private string _movingBuilding = "";
    private readonly List<Button> _townTiles = [];
    private ConfirmationDialog? _confirm;
    private DesktopPreferences? _settingsDraft;
    private string _bindingAction = "";
    private Label? _bindingLabel;

    public void Initialize(GameRoot root)
    {
        _root = root; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        Add(this, new ColorRect { Color = Color.FromHtml("#091422"), MouseFilter = MouseFilterEnum.Ignore }, "Background").SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backdrop = Add(this, new TextureRect { Texture = root.Art.Background, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(1, 1, 1, .16f), MouseFilter = MouseFilterEnum.Ignore }, "OriginalAmbientArt");
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shell = GD.Load<PackedScene>("res://Scenes/UI/DesktopShell.tscn").Instantiate<MarginContainer>(); AddChild(shell);
        _content = shell.GetNode<VBoxContainer>("Layout/Content"); _title = shell.GetNode<Label>("Layout/Header/Title"); _wallet = shell.GetNode<Label>("Layout/Header/Wallet");
        _crumb = shell.GetNode<Label>("Layout/Breadcrumb"); _toast = shell.GetNode<Label>("Layout/Toast"); _fps = shell.GetNode<Label>("Layout/Fps");
        _toast.AddThemeColorOverride("font_color", Color.FromHtml(Gold));
        Refresh();
    }
    public void Invalidate() => _dirty = true;
    private void Act(string id) { App.Audio.Unlock(); App.Action(id); Invalidate(); }
    private void Do(Action action) { App.Audio.Unlock(); action(); Invalidate(); }
    public void Refresh()
    {
        if (_content is null) return;
        if(App.Scene == "town" && App.Campaign is not null)
        { string key = string.Join("|",State.Buildings.Select(p=>p.Key+":"+p.Value.Level+":"+p.Value.Work+":"+p.Value.Constructing))+":"+(State.Flight is not null); if(key!=_townPresentationKey){_townPresentationKey=key;_dirty=true;} }
        if (_dirty || _builtScene != App.Scene) { Build(); ScaleTypography(this,App.Preferences.UiScale); }
        _wallet.Text = App.Campaign is null ? "" : Resources(State.Resources);
        _toast.Text = App.Toast?.Text ?? ""; _fps.Visible = App.Preferences.ShowFps; _fps.Text = Engine.GetFramesPerSecond() + " FPS";
        foreach (var update in _live) update();
        UpdateBoard(); UpdateTown(); _conduits?.QueueRedraw();
    }
    private string Resources(IEnumerable<KeyValuePair<string, long>> values) => string.Join("    ", values.Where(p => p.Value != 0).Select(p => (Catalog.Resources.GetValueOrDefault(p.Key)?.Name ?? p.Key) + " " + Palette.Compact(p.Value)));
    private string RewardText(RewardDefinition reward)
    {
        var text = new List<string>(); if (reward.Resources.Count > 0) text.Add(Resources(reward.Resources));
        text.AddRange(reward.Dice.Select(id => "骰子：" + App.Data.Types[id].Name));
        text.AddRange(reward.Mechanics.Select(id => "机制：" + Catalog.Mechanics[id].Name));
        text.AddRange(reward.Blueprints.Select(id => "蓝图：" + Catalog.Buildings[id].Name));
        var b = reward.Bonuses;
        foreach(var (type,amount) in b.DiceDamagePercent) text.Add($"{App.Data.Types[type].Name}伤害 +{amount:P0}");
        if (b.DamagePercent > 0) text.Add($"永久伤害 +{b.DamagePercent:P0}");
        if (b.ReloadPercent > 0) text.Add($"装填缩短 +{b.ReloadPercent:P0}");
        if (b.StartEnergy > 0) text.Add($"初始能量 +{b.StartEnergy:0.#}");
        if (b.PassiveEnergy > 0) text.Add($"每秒能量 +{b.PassiveEnergy:0.##}");
        return string.Join("；", text);
    }
    private void Build()
    {
        App.CancelPointer(); CancelTownAim(); _dirty = false; _builtScene = App.Scene; _live.Clear();
        if(_dragArt is not null) { RemoveChild(_dragArt); _dragArt.QueueFree(); }
        if(_conduits is not null) {RemoveChild(_conduits);_conduits.QueueFree();_conduits=null;}
        _field = null; _dragArt = null; _slots.Clear(); _slotArt.Clear(); _slotNames.Clear(); _slotBranches.Clear(); _reloads.Clear(); _slotKeys.Clear();
        _townBoard = null; _townBall = null; _townTiles.Clear(); _townStatus = null; _townAimGuide = null;
        Clear(_content); _bindingAction = ""; _bindingLabel = null;
        _title.Text = "骰子回响";
        _crumb.Text = "城镇准备  →  选择骰子与区域  →  局内成长  →  胜利或失败  →  回城建设、解锁";
        if (App.LoadProblem != "") { BuildLoadError(); return; }
        switch (App.Scene)
        {
            case "town": BuildTown(); break;
            case "regions": BuildRegions(); break;
            case "deck": BuildDeck(); break;
            case "play": BuildBattle(); break;
            case "paused": BuildPause(); break;
            case "upgrade": BuildUpgrade(); break;
            case "diceSkill": BuildDiceSkill(); break;
            case "die": BuildDie(); break;
            case "settlement": BuildSettlement(); break;
            case "settings": BuildSettings(); break;
            case "help": BuildHelp(); break;
            default: App.Scene = "town"; Invalidate(); break;
        }
    }
    private void BuildLoadError()
    {
        var card = Card(_content, "存档保护", App.LoadProblem);
        Label(card, "当前没有创建新档覆盖旧档。打开目录后可以保留或替换损坏文件；只有明确选择重置才会重新开始。", 20, Gold);
        Button(card, "打开存档目录", _root.OpenSaveFolder); Button(card, "导出原始存档备份", _root.BackupSave);
        Button(card, "备份并重置进度", () => Confirm("将先保留原始文件备份，然后建立新进度。确认重置？", _root.ResetProgress));
        Button(card, "退出", _root.Quit);
    }
    private void Navigation(Node parent, bool regions = true)
    {
        var row = Row(parent);
        if (regions) Button(row, "选择区域", () => Act("regions"));
        Button(row, "设置", () => Act("settings")); Button(row, "玩法说明", () => Act("help"));
        Button(row, "退出游戏", _root.Quit);
    }
    private void BuildTown()
    {
        _title.Text = $"城镇 · 第 {State.Cycle + 1} 层深潜";
        var row = Row(_content, true); var left = Column(row, true); left.SizeFlagsStretchRatio = 1.8f;
        var right = Scroll(row); right.CustomMinimumSize = new Vector2(460, 0);
        Label(left, "把骰子弹向资源或工地。命中工地推进建设，建筑完工后解锁下一局的选择。", 20, Muted);
        var aspect = Add(left, new AspectRatioContainer { Ratio = 800f / 480, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill });
        _townBoard = Add(aspect, new Control { MouseFilter = MouseFilterEnum.Stop }, "TownBoard");
        _townBoard.GuiInput += TownInput;
        var t = Catalog.Definition.Town;
        for (int index = 0; index < t.Rows * t.Columns; index++)
        {
            int tile = index;
            var b = Add(_townBoard, new Button { Text = "空地", ClipText = true }, "Plot" + tile); _townTiles.Add(b);
            b.AddThemeFontSizeOverride("font_size", 15);
            b.Pressed += () =>
            {
                if (State.Flight is not null || State.ActiveRunId != "") return;
                if (_movingBuilding != "" && !Catalog.IsResourceTile(tile) && State.Buildings.Values.All(x => x.Tile != tile))
                { string id = _movingBuilding; if (App.MutateTown(s => App.Progression!.Relocate(s, id, tile))) _movingBuilding = ""; }
                _selectedTile = tile; Invalidate();
            };
        }
        _townAimGuide = Add(_townBoard,new ColorRect { Color=Color.FromHtml(Mint), MouseFilter=MouseFilterEnum.Ignore, Visible=false },"DispatchAimGuide");
        _townBall = Dice(_townBoard, _root.Art, App.Deck[0], 1, 0); _townBall.Size = new Vector2(30, 30); _townBall.ZIndex = 2;
        _townStatus = Label(left, "", 20, Mint);
        var dispatch = Row(left);
        Button(dispatch, "向选中地块派遣 · 1 补给", () => LaunchTown(TargetTownAngle()));
        Button(dispatch, "自动选工地派遣 · 1 补给", () => LaunchTown(App.TownSimulation.AutoAngle(State)));
        var hint = Label(left, "长按城镇空白处调整方向，松手派遣。单击空地放置建筑；单击建筑查看升级。", 18, Muted);
        _live.Add(() => hint.Text = _movingBuilding == "" ? "长按城镇空白处瞄准，松手派遣；单击地块选择建设位置。" : "正在搬迁：请选择一块空地。再点击建筑的“取消搬迁”可取消。");
        if (State.ActiveRunId != "")
        {
            Label(right, "有一场未结束的远征。城镇建设暂不可用；继续远征或撤回结算。", 20, Gold);
            Button(right, "继续上次远征", () => Act("resume")); Button(right, "撤回并结算已获得物资", () => Confirm("本次不记通关，但保留已获得的资源和蓝图。确认撤回？", () => Act("abandon")));
        }
        else if (State.Flight is null) Label(right, "下一步：完成工地，或挑选区域带回更多蓝图。", 20, Mint);
        if (State.LastResult is { } result)
        {
            var recent = Card(right, "上次远征", result.RegionName + " · " + Outcome(result.Outcome));
            Label(recent, Resources(result.Resources), 18, Gold);
            foreach (string id in result.NewBlueprints.Where(Catalog.Buildings.ContainsKey)) Label(recent, "获得蓝图：" + Catalog.Buildings[id].Name, 18, Mint);
        }
        var buildingAt = State.Buildings.FirstOrDefault(p => p.Value.Tile == _selectedTile);
        if (buildingAt.Value is not null && Catalog.Buildings.TryGetValue(buildingAt.Key, out var selected))
        {
            var b = buildingAt.Value; var box = Card(right, selected.Name, selected.Description);
            Label(box, b.Constructing ? $"建设中：{b.Work}/{selected.Levels[b.Level].Work} 次有效施工" : $"已建成 Lv.{b.Level}", 20, Mint);
            if (b.Level < selected.Levels.Length)
            {
                var next = selected.Levels[b.Level]; Label(box, "下级效果：" + RewardText(next.Reward), 18);
                Label(box, "成本：" + Resources(next.Cost), 18, Gold);
                Button(box, "开始升级", () => Do(() => App.MutateTown(s => App.Progression!.StartConstruction(s, selected.Id, b.Tile))), App.Progression!.BuildingBlockers(State, selected.Id, b.Tile).Count > 0);
            }
            Button(box, _movingBuilding == selected.Id ? "取消搬迁" : "搬迁到另一块空地", () => { _movingBuilding = _movingBuilding == selected.Id ? "" : selected.Id; Invalidate(); }, State.Flight is not null || State.ActiveRunId != "");
        }
        Label(right, "可建设蓝图", 24);
        if (_selectedTile < 0) Label(right, "先在左侧选择一块空地。", 18, Muted);
        foreach (var b in Catalog.Definition.Buildings.Where(b => State.Blueprints.Contains(b.Id) && !State.Buildings.ContainsKey(b.Id)))
        {
            var box = Card(right, b.Name, b.Description); Label(box, RewardText(b.Levels[0].Reward), 18, Mint); Label(box, Resources(b.Levels[0].Cost) + $" · 施工 {b.Levels[0].Work}", 18, Gold);
            var reason = App.Progression!.BuildingBlockers(State, b.Id, _selectedTile); if (_selectedTile < 0) reason.Add("请选择空地。");
            if (reason.Count > 0) Label(box, string.Join("；", reason), 16, Muted);
            var build = Button(box, "放置工地", () => Do(() => App.MutateTown(s => App.Progression!.StartConstruction(s, b.Id, _selectedTile))));
            _live.Add(() => build.Disabled = _selectedTile < 0 || App.Progression!.BuildingBlockers(State, b.Id, _selectedTile).Count > 0);
        }
        if (State.UnlockedMechanics.Contains("auto_dispatch"))
            Button(right, "自动派遣：" + (State.AutoDispatch ? "开" : "关"), () => Do(() => App.MutateTown(s => s.AutoDispatch = !s.AutoDispatch)));
        Navigation(_content);
    }
    private double TargetTownAngle()
    {
        if (_selectedTile < 0) return App.TownSimulation.AutoAngle(State);
        var box = App.TownSimulation.TileBounds(_selectedTile);
        return Math.Atan2((box.Top + box.Bottom) / 2 - 430, (box.Left + box.Right) / 2 - 400);
    }
    private void LaunchTown(double angle) => Do(() => App.MutateTown(s => App.TownSimulation.Launch(s, angle, App.Deck[0])));
    private void TownInput(InputEvent ev)
    {
        if (_townBoard is null || State.Flight is not null || State.ActiveRunId != "") return;
        if (ev is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left && b.Pressed)
        { _townAiming = true; TownAim(_townBoard.GetGlobalMousePosition()); }
        else if (ev is InputEventMouseMotion && _townAiming) TownAim(_townBoard.GetGlobalMousePosition());
    }
    private void TownAim(Vector2 p)
    {
        if (_townBoard is null) return;
        var local = (p - _townBoard.GlobalPosition) / _townBoard.Size * new Vector2(800, 480);
        _townAngle = MathEx.Clamp(Math.Atan2(Math.Min(-20, local.Y - 430), local.X - 400), -Math.PI + .18, -.18);
    }
    public void CancelTownAim() => _townAiming = false;
    private void UpdateTown()
    {
        if (_townBoard is null || _townBall is null || App.Scene != "town") return;
        var size = _townBoard.Size;
        for (int i = 0; i < _townTiles.Count; i++)
        {
            var box = App.TownSimulation.TileBounds(i); var b = _townTiles[i];
            b.Position = new Vector2((float)box.Left / 800 * size.X, (float)box.Top / 480 * size.Y);
            b.Size = new Vector2((float)(box.Right - box.Left) / 800 * size.X, (float)(box.Bottom - box.Top) / 480 * size.Y);
            var built = State.Buildings.FirstOrDefault(p => p.Value.Tile == i);
            bool wood = Catalog.Definition.Town.WoodTiles.Contains(i), stone = Catalog.Definition.Town.StoneTiles.Contains(i);
            b.Text = wood ? "木材" : stone ? "石材" : built.Value is null ? "空地" : (Catalog.Buildings.GetValueOrDefault(built.Key)?.Name ?? built.Key) + "\n" + (built.Value.Constructing ? "施工 " + built.Value.Work : "Lv." + built.Value.Level);
            b.TooltipText = b.Text;
            b.Modulate = Color.FromHtml(i == _selectedTile ? Gold : wood ? "#91D6AF" : stone ? "#B7BDDC" : built.Value?.Constructing == true ? "#FFD094" : "#FFFFFF");
        }
        if(_townAimGuide is not null) { _townAimGuide.Visible = _townAiming; _townAimGuide.Position = new Vector2(size.X*.5f,size.Y*430/480); _townAimGuide.Size = new Vector2(size.X*.20f,2); _townAimGuide.Rotation = (float)_townAngle; }
        var f = State.Flight; _townBall.Position = new Vector2((float)(f?.X ?? 400) / 800 * size.X, (float)(f?.Y ?? 430) / 480 * size.Y) - _townBall.Size / 2;
        if (_townStatus is not null) _townStatus.Text = f is not null ? $"派遣中 · 已命中 {f.Hits} 次 · 剩余 {Math.Max(0, f.Remaining):0.0} 秒" : _townAiming ? $"松手派遣 · 方向 {_townAngle * 180 / Math.PI:0}°" : "城镇就绪 · 选择地块，或开始下一场远征";
    }
    private void BuildRegions()
    {
        _title.Text = $"区域电梯 · 第 {State.Cycle + 1} 层 · 齿轮 {State.Gears}";
        Label(_content, "每次远征独立挑战一个区域。用不同主骰通关同一区域，分别获得一次齿轮；重复使用同一主骰仍获得资源。", 20, Muted);
        var scroll = Scroll(_content); var grid = Add(scroll, new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (var region in Catalog.Definition.Regions)
        {
            bool open = App.Progression!.IsRegionOpen(State, region.Id); var record = State.Record(region.Id);
            var box = Card(grid, region.Name, region.Description); box.CustomMinimumSize = new Vector2(310, 0);
            Label(box, $"{region.Phases.Length} 个阶段 · {region.TotalWaves} 波 · {region.Phases.Length} 个头目", 18, Muted);
            Label(box, $"不同主骰：{record.ClearedWith.Count}  ·  通关 {record.Clears} 次", 18, Mint);
            Label(box, "通关主骰：" + (record.ClearedWith.Count == 0 ? "无" : string.Join("、", record.ClearedWith.Select(id => App.Data.Types.GetValueOrDefault(id)?.Name ?? id))), 16, Muted);
            var remaining = region.BlueprintPool.Where(id => !State.Blueprints.Contains(id)).ToArray();
            Label(box, "待发现蓝图：" + (remaining.Length == 0 ? "已收集" : string.Join("、", remaining.Select(id => Catalog.Buildings[id].Name))), 17, Gold);
            if (!open) Label(box, string.Join("\n", App.Progression.MissingRequirements(State, region.Requirements)), 17, Muted);
            Button(box, open ? "选择骰子并出发" : "未开放", () => Do(() => App.SelectRegion(region.Id)), !open || State.ActiveRunId != "" || State.Flight is not null);
        }
        if (State.ActiveRunId != "") Button(_content, "当前远征未结束 · 返回城镇继续或结算", () => Act("town"));
        var bottom = Row(_content); Button(bottom, "返回城镇", () => Act("town"));
        Button(bottom, "进入下一层深潜", () => Confirm("保留骰子、机制、建筑和资源；开始更难的新一层区域进度。确认进入？", () => Act("nextCycle")), !App.Progression!.CanStartNextCycle(State));
        if (!App.Progression.CanStartNextCycle(State)) Label(_content, "深潜条件：" + string.Join("；", App.Progression.MissingRequirements(State, Catalog.Definition.NextCycleRequirements)), 16, Muted);
    }
    private void BuildDeck()
    {
        _title.Text = "配置骰子 · " + Catalog.Regions[State.SelectedRegion].Name;
        Label(_content, "必须携带 6 种不同骰子。首位是主骰，决定通关记录归属；召唤和合成从六种骰子中等概率随机。", 20, Muted);
        var grid = Add(Scroll(_content), new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (var die in App.Data.Dice)
        {
            bool unlocked = State.UnlockedDice.Contains(die.Id), selected = App.EditingDeck.Contains(die.Id);
            var box = Card(grid, die.Name + (unlocked ? selected ? " · 已携带" : "" : " · 未解锁"), die.Description); box.CustomMinimumSize = new Vector2(420, 0);
            var art = Dice(box, _root.Art, die.Id, 1, 112); art.Modulate = unlocked ? Colors.White : new Color(.4f, .4f, .4f);
            if (!unlocked)
            {
                var source = Catalog.Buildings.Values.Where(b => b.Levels.Any(l => l.Reward.Dice.Contains(die.Id))).Select(b => b.Name);
                Label(box, "解锁来源：" + string.Join("、", source), 18, Gold);
            }
            Button(box, selected ? "移出卡组" : "加入卡组", () => Act("deck:" + die.Id), !unlocked);
            Button(box, App.EditingDeck.FirstOrDefault() == die.Id ? "本次主骰" : "设为主骰", () => Do(() => App.SetLeadDice(die.Id)), !selected || App.EditingDeck.FirstOrDefault() == die.Id);
        }
        string lead = App.EditingDeck.Count == 0 ? "未选择" : App.Data.Types[App.EditingDeck[0]].Name;
        Label(_content, $"已携带 {App.EditingDeck.Count}/6 · 主骰 {lead}" + (App.EditingDeck.Count > 0 ? $" · 每种骰子出现概率 {100.0 / App.EditingDeck.Count:0.#}%" : ""), 22, Mint);
        var bottom = Row(_content); Button(bottom, "返回区域", () => Act("deckBack"));
        Button(bottom, "保存卡组", () => Act("deckSave"), !App.Data.ValidDeck(App.EditingDeck));
        Button(bottom, "开始远征", () => Do(() => { if (App.SaveDeck()) App.StartExpedition(); }), !App.Data.ValidDeck(App.EditingDeck) || State.ActiveRunId != "" || State.Flight is not null);
    }
    private static string Outcome(string outcome) => outcome switch { "victory" => "区域通关", "defeat" => "远征失败", "abandoned" => "主动撤回", _ => outcome };
    private void BuildSettlement()
    {
        var run = App.Sim?.State; var e = run?.Expedition; if (run is null || e is null) { App.Scene = "town"; Invalidate(); return; }
        _title.Text = Outcome(e.Outcome) + " · " + e.Region.Name;
        var body = Scroll(_content); var stats = Card(body, "本次远征", e.EndReason);
        Label(stats, $"波次 {run.Wave} · 击破 {run.Kills - e.StartKills} · 用时 {TimeText(run.Time - e.StartTime)}", 25);
        if (!App.SettlementSaved)
        {
            Label(stats, "奖励尚未写入存档。不要把这个界面当作领取成功；重试保存后才能回城或继续无尽。", 22, Gold);
            Button(stats, "重新保存并确认结算", () => Act("retrySettlement"));
        }
        else if (State.LastResult is { } receipt)
        {
            Label(stats, "已到账：" + (receipt.Resources.Count == 0 ? "本次未获得资源" : Resources(receipt.Resources)), 24, Gold);
            Label(stats, receipt.NewGear ? "新主骰通关：区域齿轮 +1" : e.Outcome == "victory" ? "同一主骰重复通关：保留资源奖励，不重复发放齿轮。" : "未通关：已获得的资源与蓝图保留。", 20, Mint);
            if (receipt.FirstClear) Label(stats, "首次通关奖励已领取。", 20, Mint);
            foreach (string id in receipt.NewBlueprints) Label(stats, "新蓝图：" + (Catalog.Buildings.GetValueOrDefault(id)?.Name ?? id), 22, Gold);
            foreach (string id in receipt.NewDice) Label(stats, "解锁骰子：" + App.Data.Types[id].Name, 22, Mint);
            foreach (string id in receipt.NewMechanics) Label(stats, "解锁机制：" + Catalog.Mechanics[id].Name, 22, Mint);
            foreach (string id in receipt.NewRegions) Label(stats, "区域开放：" + Catalog.Regions[id].Name, 22, Mint);
            Label(stats, receipt.NewBlueprints.Count > 0 ? "下一步：回城把新蓝图建出来，再用新的骰子或机制出发。" : "下一步：回城建设，或更换主骰补齐区域齿轮。", 21);
        }
        var buttons = Row(_content); Button(buttons, "返回城镇 · 建设与解锁", () => Act("town"), !App.SettlementSaved);
        if (e.Outcome == "victory" && e.CanContinueEndless) Button(buttons, "保留本局搭配 · 继续无尽", () => Act("endless"), !App.SettlementSaved);
    }
    private static string TimeText(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
    private void BuildPause()
    {
        _title.Text = "远征暂停"; var box = Card(_content, "当前战斗已经暂停", "返回城镇时保留战斗快照；结算前不能开始另一场远征。");
        Button(box, "继续远征", () => Act("continue")); Button(box, "设置", () => Act("settings")); Button(box, "保存并返回城镇", () => Act("town"));
        Button(box, "撤回并结算", () => Confirm("撤回不算通关，已获得资源和蓝图保留。确认撤回？", () => Act("abandon")));
        Button(box, "玩法说明", () => Act("help"));
    }
    private void BuildUpgrade()
    {
        _title.Text = "选择本局强化"; Label(_content, "战斗已暂停。强化只在当前远征生效；继续无尽会保留当前搭配。", 21, Muted);
        var grid = Add(Scroll(_content), new GridContainer { Columns = Math.Max(1, Math.Min(3, App.Sim!.State.Offers.Count)), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (string id in App.Sim.State.Offers)
        { var u = App.Data.UpgradeTypes[id]; var box = Card(grid, u.Name, u.Tag); box.CustomMinimumSize = new Vector2(380, 280); Label(box, u.Description, 23); Label(box, $"当前层数 {App.Sim.State.Upgrades.GetValueOrDefault(id)}/{u.Max}", 20, Mint); Button(box, "选择强化", () => Act("upgrade:" + id)); }
        Button(_content, "保存并返回城镇", () => Act("town"));
    }
    private void BuildDie()
    {
        if (App.Sim is null || App.SelectedSlot < 0 || App.Sim.State.Board[App.SelectedSlot] is not { } die) { App.Scene = "play"; Invalidate(); return; }
        var definition = App.Data.Types[die.Type]; var stats = App.Sim.Stats(die); var box = Card(_content, definition.Name + $" · {die.Pips} 点", definition.Description);
        Dice(box, _root.Art, die.Type, die.Pips, 180);
        Label(box, $"齐射伤害 {Palette.Compact(stats.Volley)} · 装填 {stats.Reload:0.00} 秒 · 每轮 {stats.Count} 颗弹丸", 25, Mint);
        Label(box, die.Pips < 6 ? "同种同点才能合成。合成会减少当前攻击席位，并生成卡组内随机种类的更高点数骰子。" : "已到六点上限。保留火力，或回收腾出空位。", 21);
        Label(box, App.Sim.SkillDescription(die), 21, Gold);
        if (die.Pips is 3 or 4) Label(box, "下一次合成会继承落点骰子的 A/B 分支编号，但技能按随机结果种类切换。", 19, Muted);
        if (die.Pips == 5) Label(box, "合成六级后：按最终种类重新选择 A/B，再选择 C/D。", 21, Mint);
        Button(box, "返回战斗", () => Act("closeDie"));
        Button(box, "回收此骰子 · +" + App.Data.Game.Levels[die.Pips - 1].Recycle + " 能量", () => Act("recycle:" + App.SelectedSlot));
    }
    private void BuildHelp()
    {
        _title.Text = "玩法说明"; var box = Scroll(_content);
        Label(box, "城镇与区域", 27, Mint);
        Label(box, "从区域中带回金币、建材、蓝图与补给。蓝图需要放置工地，再用城镇弹射推进施工。建成后立即获得骰子、机制或永久加成；下一次远征应用这些变化。", 23);
        Label(box, "每场远征只进入一个区域，经过普通波、小头目和最终头目。头目逃出防线算失败，不能靠拖时间跳过。胜利、失败与撤回均回城结算；失败不清空已经获得的物资。", 23);
        Label(box, "骰子战斗", 27, Mint);
        Label(box, "长按战场瞄准，松手发射；移出战场再松手取消。卡组必须携带六种不同骰子，棋盘有 24 个席位。拖动可移动骰子；只有同种、同点骰子才能合成。合成产生的类型从本局卡组随机选取，点数上升一级。", 23);
        Label(box, "点击骰子查看数值或回收。合成会减少当前火力席位，所以高点数并不总比保留更多攻击频率更合适。", 23);
        Label(box, "三级与六级分支", 27, Mint);
        Label(box, "先随机确定合成结果，再选择这颗新骰子的技能。三级从 A/B 中选一项；四、五级沿用落点骰子的分支编号，技能按新类型切换；六级清除继承分支，重新选 A/B，再选 C/D。选择只影响该颗骰子，敌人和弹丸在选择期间完全暂停。", 23);
        Label(box, "主骰、齿轮与深潜", 27, Mint);
        Label(box, "卡组首位是主骰，仅决定这次通关记录归属。每个区域、每层深潜、每种主骰首次通关各给一份齿轮。同一主骰反复通关仍有普通奖励，但不重复给齿轮。区域界面显示下一片区域缺少什么。", 23);
        Label(box, "解锁无尽后，区域胜利可以先确认正常奖励，再保留当前搭配继续挑战。无尽段只结算新增收获，不重复领取此前奖励。深潜会保留永久成长，并开启下一层独立区域记录。", 23);
        Button(_content, "返回", () => Act("closeHelp"));
    }
    private void Confirm(string text, Action yes)
    {
        if (_confirm is not null && IsInstanceValid(_confirm)) return;
        App.CancelPointer(); _confirm = new ConfirmationDialog { Title = "确认操作", DialogText = text, OkButtonText = "确认", CancelButtonText = "取消" };
        AddChild(_confirm);
        void Close() { if (_confirm is not null) { _confirm.Hide(); _confirm.QueueFree(); _confirm = null; } }
        _confirm.Confirmed += () => { Close(); yes(); Invalidate(); }; _confirm.Canceled += Close; _confirm.PopupCentered(new Vector2I(640, 250));
    }
    public void Back()
    {
        if (_confirm is not null) { _confirm.Hide(); _confirm.QueueFree(); _confirm = null; return; }
        switch (App.Scene)
        {
            case "play": Act("pause"); break; case "paused": Act("continue"); break;
            case "settings": Act("closeSettings"); break; case "deck": Act("deckBack"); break;
            case "regions": Act("town"); break; case "help": Act("closeHelp"); break; case "die": Act("closeDie"); break;
            case "settlement": if (App.SettlementSaved) Act("town"); break;
            case "town": Confirm("退出游戏？当前进度会保存。", _root.Quit); break;
        }
    }
    public void AssertLayout()
    {
        if (_content.Size.X <= 0 || _content.Size.Y <= 0) throw new InvalidOperationException("Native content area is empty.");
        if (App.Scene == "play")
        {
            if (_field is null || _slots.Count != App.Data.Game.Board.Slots || _field.Size.X < 200 || _field.Size.Y < 200)
                throw new InvalidOperationException("Battle layout has no usable field / configured slots.");
            var bounds=GetViewportRect().Grow(1);
            var shell=GetChildren().OfType<MarginContainer>().Single();
            if(!bounds.Encloses(shell.GetGlobalRect())) throw new InvalidOperationException("Battle shell exceeds viewport at the current font scale: "+shell.GetGlobalRect()+" vs "+bounds+" content="+_content.GetGlobalRect());
            foreach(var slot in _slots)
                if(!bounds.Encloses(slot.GetGlobalRect())) throw new InvalidOperationException("A dice slot lies outside the visible viewport: "+slot.Name);
        }
    }
}
