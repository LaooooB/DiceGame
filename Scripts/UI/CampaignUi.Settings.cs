using Godot;
using DiceGame.Core;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

public partial class CampaignUi
{
    private void BuildSettings()
    {
        _title.Text = "设置"; _settingsDraft = CampaignCatalog.Copy(App.Preferences); var p = _settingsDraft;
        var row = Row(_content, true); var display = Scroll(row); var audio = Scroll(row); var controls = Scroll(row);
        display.SizeFlagsStretchRatio = 1; audio.SizeFlagsStretchRatio = 1; controls.SizeFlagsStretchRatio = 1;
        Label(display, "显示", 26, Mint);
        Choice(display, "窗口模式", new[] { "窗口", "无边框全屏", "全屏" }, p.WindowMode switch { "borderless" => 1, "fullscreen" => 2, _ => 0 }, index => p.WindowMode = new[] { "windowed", "borderless", "fullscreen" }[index]);
        var resolutions = new List<Vector2I> { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
        var saved = new Vector2I(p.Width, p.Height); if (!resolutions.Contains(saved)) resolutions.Add(saved);
        Choice(display, "窗口分辨率", resolutions.Select(x => x.X + " × " + x.Y).ToArray(), resolutions.IndexOf(saved), i => { p.Width = resolutions[i].X; p.Height = resolutions[i].Y; });
        Check(display, "垂直同步", p.VSync, value => p.VSync = value);
        int[] fps = [0, 30, 60, 90, 120, 144, 165, 240]; int[] options = fps.Contains(p.MaxFps) ? fps : fps.Append(p.MaxFps).ToArray();
        Choice(display, "帧率上限", options.Select(x => x == 0 ? "不限" : x.ToString()).ToArray(), Array.IndexOf(options, p.MaxFps), i => p.MaxFps = options[i]);
        if(RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility") Label(display,"兼容渲染：战场以 2× 逻辑分辨率绘制；此后端不支持 2D MSAA。",17,Muted);
        else Choice(display, "2D 抗锯齿", ["关闭", "2×", "4×", "8×"], p.Msaa, i => p.Msaa = i);
        Slider(display, "界面字号缩放", p.UiScale, .8, 1.3, .05, value => p.UiScale = value, true);
        Check(display, "显示帧率", p.ShowFps, value => p.ShowFps = value);
        Label(display, "全屏使用显示器原生尺寸；分辨率选项用于窗口模式。战斗逻辑速度不随帧率或窗口尺寸变化。", 17, Muted);
        Label(audio, "声音与视觉舒适度", 26, Mint);
        Slider(audio, "总音量", p.MasterVolume, 0, 1, .01, v => p.MasterVolume = v);
        Slider(audio, "音效音量", p.SfxVolume, 0, 1, .01, v => p.SfxVolume = v);
        Slider(audio, "音乐音量", p.MusicVolume, 0, 1, .01, v => p.MusicVolume = v);
        var music = Button(audio,"",()=>App.Action("music")); var sound = Button(audio,"",()=>App.Action("sound"));
        _live.Add(()=>music.Text="音乐："+(App.Settings.Music?"开":"关")); _live.Add(()=>sound.Text="声音总开关："+(App.Settings.Sound?"开":"关"));
        Check(audio, "屏幕震动", p.ScreenShake, v => p.ScreenShake = v);
        Check(audio, "受伤闪光", p.FlashEffects, v => p.FlashEffects = v);
        Check(audio, "显示瞄准辅助线", p.ShowAim, v => p.ShowAim = v);
        var motion = Button(audio,"",()=>App.Action("motion")); _live.Add(()=>motion.Text="减弱动态效果："+(App.Settings.ReduceMotion?"开":"关"));
        Label(audio,"声音开关、音乐开关和减弱动态立即保存；其他项目在点击应用后保存。",17,Muted);
        Label(controls, "键盘与存档", 26, Mint);
        foreach (var (id, name) in new[] { ("summon", "召唤"), ("pause", "暂停 / 继续"), ("mute", "声音开关"), ("fullscreen", "切换全屏") })
        {
            string action = id; var button = Button(controls, name + "：" + p.Bindings[id], () => { _bindingAction = action; if (_bindingLabel is not null) _bindingLabel.Text = "请按一个新按键；Esc 取消。"; });
            _live.Add(() => button.Text = name + "：" + p.Bindings[action]);
        }
        _bindingLabel = Label(controls, "左键长按瞄准 / 松开发射，右键取消；Esc 始终可以返回或暂停。", 18, Muted);
        Label(controls, "按键不能重复。鼠标操作保持原规则，不增加自动发射。", 17, Muted);
        Button(controls, "备份当前存档", _root.BackupSave); Button(controls, "打开存档目录", _root.OpenSaveFolder);
        Button(controls, "备份并重置永久进度", () => Confirm("这会结束当前远征并重置城镇、区域、骰子解锁。原文件先备份到 backups 目录。确认重置？", _root.ResetProgress));
        var bottom = Row(_content);
        Button(bottom, "取消未应用的设置并返回", () => Act("closeSettings"));
        Button(bottom, "恢复默认设置", () => _root.PreviewPreferences(new DesktopPreferences()));
        Button(bottom, "应用并保存", () => { if (_settingsDraft is not null) _root.PreviewPreferences(_settingsDraft); });
    }
    private static void Choice(Node parent, string name, string[] values, int selected, Action<int> change)
    {
        Label(parent, name, 19, Muted); var option = Add(parent, new OptionButton { CustomMinimumSize = new Vector2(250, 46) });
        foreach (string value in values) option.AddItem(value); option.Selected = selected; option.ItemSelected += index => change((int)index);
    }
    private static void Check(Node parent, string name, bool value, Action<bool> change)
    { var check = Add(parent, new CheckButton { Text = name, ButtonPressed = value, CustomMinimumSize = new Vector2(250, 42) }); check.Toggled += value => change(value); }
    private static void Slider(Node parent, string name, double value, double min, double max, double step, Action<double> change, bool multiplier = false)
    {
        var label = Label(parent, name + $"：{value:P0}", 19, Muted);
        var slider = Add(parent, new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(250, 35) });
        slider.ValueChanged += v => { change(v); label.Text = name + $"：{v:P0}"; };
    }
    public bool HandleKeyBinding(InputEventKey key)
    {
        if (_bindingAction == "" || _settingsDraft is null || App.Scene != "settings") return false;
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        if (code == Key.Escape) { _bindingAction = ""; if (_bindingLabel is not null) _bindingLabel.Text = "已取消按键修改。"; return true; }
        string value = code.ToString();
        if (key.AltPressed || key.CtrlPressed || key.MetaPressed || code is Key.None or Key.Shift or Key.Ctrl or Key.Alt or Key.Meta || _settingsDraft.Bindings.Any(p => p.Key != _bindingAction && p.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
        { if (_bindingLabel is not null) _bindingLabel.Text = "请选择未占用的单个按键，不使用系统组合键。"; return true; }
        _settingsDraft.Bindings[_bindingAction] = value; _bindingAction = ""; if (_bindingLabel is not null) _bindingLabel.Text = "已修改，点击应用并保存后生效。"; return true;
    }
}
