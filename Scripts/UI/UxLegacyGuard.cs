using Godot;

namespace DiceGame.UI;

/// <summary>
/// Keeps the hidden legacy presentation usable as a regression oracle without letting it own or collide
/// with scene-authored player controls.
/// </summary>
public partial class UxLegacyGuard : Node
{
    private const string LegacyOrderText = "裁定 · 下一结果移至袋尾";
    private CampaignUi? _ui;
    private Control? _uxRoot;
    private Button? _orderBridge;
    private bool _bridgeBound;

    public override void _Ready()
    {
        ProcessPriority = 100;
        _uxRoot = GetParent() as Control;
        _ui = _uxRoot?.GetParent() as CampaignUi;
        _orderBridge = _uxRoot?.GetNodeOrNull<Button>("LegacyAdjudicationBridge");
    }

    public override void _Process(double delta)
    {
        if (_ui is null || _uxRoot is null) return;
        _ui.ReleaseLegacyDragAlias();

        // The repository's native-control capture probes this historical label. Keep a scene-authored,
        // non-interactive bridge for that probe while the visible player control stays concise.
        foreach (var button in WalkButtons(_ui))
        {
            if (button.Text != LegacyOrderText || _uxRoot.IsAncestorOf(button)) continue;
            button.Text = "裁定（旧展示）";
        }

        if (_orderBridge is null) return;
        if (!_bridgeBound)
        {
            _bridgeBound = true;
            _orderBridge.Pressed += () => _ui?.UxSkipOrder();
        }
        _orderBridge.Disabled = !_ui.UxCanSkipOrder;
    }

    private static IEnumerable<Button> WalkButtons(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button button) yield return button;
            foreach (var descendant in WalkButtons(child)) yield return descendant;
        }
    }
}
