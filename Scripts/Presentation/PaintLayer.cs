using Godot;

namespace DiceGame.Presentation;

public partial class PaintLayer : Node2D
{
    public Action<NativeCanvas>? Paint;
    private NativeCanvas? _canvas;
    public void Initialize(NativeArt art,Action<NativeCanvas> paint)
    {
        _canvas=new NativeCanvas(this,art);Paint=paint;TextureFilter=TextureFilterEnum.Linear;QueueRedraw();
    }
    public override void _Draw() {if(_canvas is null)return;_canvas.Begin();Paint?.Invoke(_canvas);}
}
