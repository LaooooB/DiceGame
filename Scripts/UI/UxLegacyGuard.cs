using Godot;

namespace DiceGame.UI;

/// <summary>
/// The old hidden presentation still runs as a regression oracle. Clear only its transient drag alias
/// after the new UI has finished binding so a later legacy rebuild never frees scene-owned UX nodes.
/// </summary>
public partial class UxLegacyGuard : Node
{
    private CampaignUi? _ui;

    public override void _Ready()
    {
        ProcessPriority = 100;
        _ui = GetParent()?.GetParent() as CampaignUi;
    }

    public override void _Process(double delta) => _ui?.ReleaseLegacyDragAlias();
}
