using Godot;
using DiceGame.Core;
using DiceGame.Presentation;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

public partial class CampaignUi
{
    private Label? _battleStatus, _battleStats, _battleHint;
    private ProgressBar? _health, _progress;
    private void BuildBattle()
    {
        var sim = App.Sim!; var e = sim.State.Expedition!;
        _title.Text = e.Region.Name + (e.Endless ? " · 无尽挑战" : " · 区域远征");
        var row = Row(_content, true); var left = Column(row); left.CustomMinimumSize = new Vector2(220, 0); left.SizeFlagsHorizontal = SizeFlags.Fill;
        Label(left, "远征进度", 24, Mint); _battleStatus = Label(left, "", 21);
        _progress = Add(left, new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12), MouseFilter = MouseFilterEnum.Ignore });
        _battleStats = Label(left, "", 21);
        _health = Add(left, new ProgressBar { MaxValue = App.Data.Game.Rules.MaxHealth, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 24), MouseFilter = MouseFilterEnum.Ignore });
        Label(left, "主骰：" + App.Data.Types[e.LeadDice].Name, 21, Gold);
        Button(left, "暂停", () => Act("pause")); Button(left, "设置", () => Act("settings")); Button(left, "玩法说明", () => Act("help"));
        var center = Add(row, new AspectRatioContainer { Ratio = 382f / 444, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 400) });
        _field = Add(center, new TextureRect { Texture = _root.Battlefield.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Cross }, "BattlefieldInput");
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RoundedTexture.gdshader") };
        material.SetShaderParameter("logical_size", new Vector2(382, 444)); material.SetShaderParameter("corner_radius", 10); _field.Material = material;
        _field.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left && b.Pressed)
            { var p = FieldPoint(_field.GetGlobalMousePosition()); App.OnDown(p.X, p.Y); _field.AcceptEvent(); }
        };
        // Keep the full four-row board visible; large-font hints may scroll instead of expanding the whole window off-screen.
        var rightScroll=Add(row,new ScrollContainer {CustomMinimumSize=new Vector2(686,0),SizeFlagsHorizontal=SizeFlags.Fill,
            SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled});
        var right = Column(rightScroll); right.CustomMinimumSize = new Vector2(664, 0); right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Label(right, $"骰子阵地 · {App.Data.Game.Board.Slots} 个席位", 26, Mint);
        right.AddThemeConstantOverride("separation",8);
        Label(right, "同种类 + 同点数才能合成。拖动到空位可移动。", 18, Muted);
        var board = Add(right, new GridContainer { Name = "DiceBoard", Columns = App.Data.Game.Board.Columns });
        board.AddThemeConstantOverride("h_separation",8); board.AddThemeConstantOverride("v_separation",8);
        var slotScene = GD.Load<PackedScene>("res://Scenes/UI/DiceSlot.tscn");
        for (int i = 0; i < App.Data.Game.Board.Slots; i++)
        {
            int slot = i; var view = slotScene.Instantiate<PanelContainer>(); Add(board, view, "Slot" + i);
            view.MouseDefaultCursorShape = CursorShape.PointingHand;
            _slots.Add(view); _slotArt.Add(view.GetNode<TextureRect>("Layout/Art")); _slotNames.Add(view.GetNode<Label>("Layout/Name")); _slotBranches.Add(view.GetNode<Label>("Layout/Branches")); _reloads.Add(view.GetNode<ProgressBar>("Layout/Reload")); _slotKeys.Add("");
            view.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left && b.Pressed)
                { var p = BoardPoint(view.GetGlobalMousePosition(), false); App.OnDown(p.X, p.Y); view.AcceptEvent(); }
            };
        }
        var summon = Button(right, "召唤骰子 · " + App.Data.Game.Rules.SummonCost + " 能量", () => Act("summon"));
        _live.Add(() => summon.Disabled = App.Scene != "play" || App.Sim is null || App.Sim.Count >= App.Data.Game.Board.Slots || App.Sim.State.Energy < App.Data.Game.Rules.SummonCost);
        _battleHint = Label(right, "长按战场瞄准，松手发射。", 21, Gold);
        Label(right, "三级选 A/B；六级重选 A/B，再选 C/D。点击骰子可查看或回收。", 18, Muted);
        var loot = Label(right, "", 18, Gold);
        _live.Add(() => { if (App.Sim?.State.Expedition is { } ex) loot.Text = Resources(ex.Loot) + "\n蓝图 " + ex.FoundBlueprints.Count; });
        _conduits=Add(this,new BattleConduits(),"BoardChargeEffects");
        _conduits.Initialize(App,_root.Art,
            slot=>_slotArt[slot].GetGlobalRect().GetCenter()-new Vector2(0,_slotArt[slot].Size.Y*.25f),
            ()=>_field.GlobalPosition+new Vector2((216f-25)/382*_field.Size.X,(530f-132)/444*_field.Size.Y),
            ()=>_field.Size.Y/444);
        _conduits.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _dragArt = Dice(this, _root.Art, App.Deck[0], 1, 0); _dragArt.Size = new Vector2(104, 104); _dragArt.ZIndex = 10; _dragArt.Visible = false;
    }
    private void UpdateBoard()
    {
        if (App.Scene != "play" || App.Sim is null || _slots.Count != App.Data.Game.Board.Slots) return;
        var sim = App.Sim; var state = sim.State; var e = state.Expedition!; int phase = e.Region.PhaseIndex(e.LocalWave(state.Wave));
        if (_battleStatus is not null) _battleStatus.Text = e.LegacyRules ? $"兼容旧无尽存档\n第 {state.Wave} 波" : $"{e.Region.Phases[phase].Name}\n第 {e.LocalWave(state.Wave)}/{e.Region.TotalWaves} 波" + (e.Region.IsBossWave(e.LocalWave(state.Wave)) ? "\n" + e.Region.Phases[phase].BossName : "");
        if (_progress is not null) _progress.Value = (double)(e.LocalWave(state.Wave) - 1) / e.Region.TotalWaves;
        if (_battleStats is not null) _battleStats.Text = $"用时 {TimeText(state.Time)}\n能量 {state.Energy:0}\n防线 {state.Health}/{App.Data.Game.Rules.MaxHealth}\n击破 {state.Kills}\n场上敌人 {state.Enemies.Count(x => x.Hp > 0)}";
        if (_health is not null) _health.Value = state.Health;
        bool dragging = App.Pointer?.Mode == "drag";
        for (int i = 0; i < _slots.Count; i++)
        {
            var die = state.Board[i]; string key = die is null ? "empty" : die.Type + ":" + die.Pips + ":" + die.Tier3 + ":" + die.Tier6;
            if (_slotKeys[i] != key)
            {
                _slotKeys[i] = key;
                if (die is null) { _slotArt[i].Texture = null; _slotNames[i].Text = "+ 空位"; _slotBranches[i].Text = ""; _slots[i].TooltipText = "单击召唤到此位置"; }
                else { SetDice(_slotArt[i], _root.Art, die.Type, die.Pips); _slotNames[i].Text = App.Data.Types[die.Type].Name + " · " + die.Pips; _slotBranches[i].Text = die.Tier3=="" ? "3/6 强化" : die.Tier6=="" ? die.Tier3+" 分支" : die.Tier3+" + "+die.Tier6;
                    _slots[i].TooltipText = DiceContent.RarityName(App.Data.Types[die.Type].Rarity)+" · "+App.Data.Types[die.Type].Description+"\n"+sim.SkillDescription(die); }
            }
            _reloads[i].Visible = die is not null;
            if (die is not null) _reloads[i].Value = 1 - MathEx.Clamp(die.Cooldown / sim.Stats(die).Reload, 0, 1);
            bool source = dragging && App.Pointer!.Slot == i, match = dragging && sim.CanMerge(App.Pointer!.Slot, i);
            _slots[i].Modulate = source ? new Color(1, 1, 1, .27f) : match ? Color.FromHtml(Mint) : Colors.White;
            var pulse = App.Effects.Pulses.Find(p => p.Slot == i);
            double scale = 1;
            if (pulse is not null && !App.Settings.ReduceMotion) { double p = 1 - pulse.Life / pulse.Max; scale += Math.Sin(p * Math.PI * 2.3) * .1 * (1 - p); }
            _slotArt[i].PivotOffset = _slotArt[i].Size / 2; _slotArt[i].Scale = Vector2.One * (float)scale;
        }
        if (_dragArt is not null)
        {
            _dragArt.Visible = dragging;
            if (dragging && state.Board[App.Pointer!.Slot] is { } d)
            { string key = d.Type + ":" + d.Pips; if (_dragArt.GetMeta("dice_key", "").AsString() != key) { SetDice(_dragArt, _root.Art, d.Type, d.Pips); _dragArt.SetMeta("dice_key", key); } _dragArt.GlobalPosition = GetGlobalMousePosition() - _dragArt.Size / 2; }
        }
        if (_battleHint is not null) _battleHint.Text = App.Pointer?.Mode == "aim" ? "松手齐射；移出战场再松手可取消。" : sim.HasPair() ? "有可合成的骰子。现在升点，还是先保留攻击频率？" : sim.Count==App.Data.Game.Board.Slots ? "阵地已满且无可合成项：点击低点骰子回收腾位。" : "长按战场瞄准，松手发射。";
    }
    private PointD FieldPoint(Vector2 position)
    {
        if (_field is null || !_field.GetGlobalRect().HasPoint(position) || _field.Size.X <= 0 || _field.Size.Y <= 0) return new PointD(-1000, -1000);
        var p = (position - _field.GlobalPosition) / _field.Size;
        return new PointD(25 + p.X * 382, 132 + p.Y * 444);
    }
    private PointD BoardPoint(Vector2 position, bool moving)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].GetGlobalRect().HasPoint(position)) continue;
            var center = App.Data.SlotPosition(i); var local = (position - _slots[i].GlobalPosition) / _slots[i].Size;
            return new PointD(center.X + (local.X - .5) * 75, center.Y + (local.Y - .5) * 80);
        }
        if (moving && App.Pointer is { } pointer)
        {
            var original = App.Data.SlotPosition(pointer.Slot);
            // Out-of-board coordinates must not accidentally identify a valid drop slot.
            return new PointD(original.X + 1000, original.Y + 1000);
        }
        return new PointD(-1000, -1000);
    }
    public void MovePointer(Vector2 position)
    {
        if (_townAiming) TownAim(position);
        if (App.Pointer is not { } p) return;
        var logical = p.Mode == "aim" ? FieldPoint(position) : BoardPoint(position, true); App.OnMove(logical.X, logical.Y);
    }
    public void ReleasePointer(Vector2 position)
    {
        if (_townAiming)
        { bool inside = _townBoard is not null && _townBoard.GetGlobalRect().HasPoint(position); _townAiming = false; if (inside) LaunchTown(_townAngle); }
        if (App.Pointer is not { } p) return;
        var logical = p.Mode == "aim" ? FieldPoint(position) : BoardPoint(position, false); App.OnUp(logical.X, logical.Y);
    }
}
