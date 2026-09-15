using Godot;
using DiceGame.App;
using DiceGame.Presentation;

namespace DiceGame.UI;

/// <summary>Original board-to-launcher charge effect, projected between the new native UI coordinates.</summary>
public partial class BattleConduits : Control
{
    private GameApp _app = null!;
    private NativeArt _art = null!;
    private Func<int, Vector2> _source = null!;
    private Func<Vector2> _destination = null!;
    private Func<float> _scale = null!;
    public void Initialize(GameApp app, NativeArt art, Func<int, Vector2> source, Func<Vector2> destination, Func<float> scale)
    { _app=app; _art=art; _source=source; _destination=destination; _scale=scale; MouseFilter=MouseFilterEnum.Ignore; ZIndex=9; }
    public override void _Draw()
    {
        if(_app?.Scene!="play") return;
        float scale=_scale();
        foreach(var q in _app.Effects.Conduits)
        {
            double p=1-q.Life/q.Max; var from=_source(q.Slot)-GlobalPosition; var target=_destination()-GlobalPosition;
            var at=new Vector2(Mathf.Lerp(from.X,target.X,(float)p),Mathf.Lerp(from.Y,target.Y,(float)(p*p)));
            var color=Color.FromHtml(q.Color); color.A=(float)((1-p)*.8);
            DrawLine(from,at,color,1.4f*scale,true);
            float size=28*scale; DrawTextureRect(_art.Glow(q.Color),new Rect2(at-Vector2.One*size/2,Vector2.One*size),false,new Color(1,1,1,color.A));
            DrawCircle(at,2.5f*scale,color);
        }
    }
}
