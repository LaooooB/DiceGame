using Godot;

namespace DiceGame.UI;

/// <summary>Small, non-blocking feedback for scene-authored buttons.</summary>
public partial class AnimatedButton : Button
{
    private Tween? _motion;

    public override void _Ready()
    {
        MouseEntered += () => Animate(1.018f, 1f);
        MouseExited += () => Animate(1f, 1f);
        FocusEntered += () => Animate(1.012f, 1f);
        FocusExited += () => Animate(1f, 1f);
        ButtonDown += () => Animate(.975f, .94f);
        ButtonUp += () => Animate(HasFocus() ? 1.012f : 1f, 1f);
        Resized += CenterPivot;
        CenterPivot();
    }

    private void CenterPivot() => PivotOffset = Size * .5f;

    private void Animate(float scale, float alpha)
    {
        if (_motion is not null && _motion.IsValid()) _motion.Kill();
        if (UiKit.ReduceMotion)
        {
            Scale = Vector2.One;
            Modulate = new Color(1, 1, 1, alpha);
            return;
        }
        _motion = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(this, "scale", Vector2.One * scale, .09);
        _motion.TweenProperty(this, "modulate", new Color(1, 1, 1, alpha), .07);
    }
}
