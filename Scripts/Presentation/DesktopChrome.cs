using Godot;
using DiceGame.Core;
using P=DiceGame.Presentation.Palette;

namespace DiceGame.Presentation;

/// <summary>The existing browser desktop surround, expressed as native 2D drawing. Never scales simulation coordinates.</summary>
public sealed class DesktopChrome
{
    public Vector2 Size=new(1200,960);
    public Vector2 GamePosition;
    public double GameScale=1;
    public void Layout(Vector2 size)
    {
        Size=size;GameScale=Math.Min(1,Math.Min((size.Y-36)/864.0,(size.X-64)/753.0));GameScale=Math.Max(.1,GameScale);
        double startX=(size.X-753*GameScale)/2;GamePosition=new Vector2((float)(startX+321*GameScale),(float)((size.Y-864*GameScale)/2));
    }
    public void Paint(NativeCanvas c)
    {
        c.Rect(0,0,Size.X,Size.Y,"#080F19");
        // The two desktop ambient ellipses do not participate in game clipping or game coordinates.
        c.RadialRect(0,0,Size.X,Size.Y,Size.X*.30,Size.Y*.30,0,Size.X*.60,[(0,"#18313280"),(.87,"#18313200"),(1,"#18313200")]);
        c.RadialRect(0,0,Size.X,Size.Y,Size.X*.82,Size.Y*.78,0,Size.X*.56,[(0,"#31244470"),(.90,"#31244400"),(1,"#31244400")]);
        for(int y=12;y<Size.Y;y+=24)for(int x=12;x<Size.X;x+=24)c.Circle(x,y,.7,"#68829822");
        // Soft frame surround. Original game image itself is drawn in a separate TextureRect.
        for(int i=30;i>=1;i--){double offset=i*1.8;c.Box(GamePosition.X-offset,GamePosition.Y+15-offset,432*GameScale+offset*2,864*GameScale+offset*2,25*GameScale+offset,"#00000003");}
        c.Box(GamePosition.X,GamePosition.Y,432*GameScale,864*GameScale,25*GameScale,"#0A1220","#334657");
        c.Save();c.Translate(GamePosition.X-321*GameScale,Size.Y/2-322*GameScale);c.Scale(GameScale,GameScale);
        c.Track("DICE / RICOCHET",0,8,10,P.Mint,4,"left");
        c.Text("把运气，",0,65,44,P.Text,780);c.Text("合成",0,122,44,P.Text,780);c.Text("火力。",94,122,44,P.Mint,780);
        c.Text("一条回廊，八格骰子。",0,183,13,"#93A7BA");c.Text("给弹丸一个角度，",0,209,13,"#93A7BA");c.Text("让连锁替你完成剩下的事。",0,235,13,"#93A7BA");
        (string title,string a,string b)[] rules=[
            ("按住瞄准，松手齐射","在战场内按住鼠标左键。","瞄好反弹路线，再松手发射。"),
            ("同种同点，拖动合成","随机进化为卡组内更高点骰子，","立即触发一次强化齐射。"),
            ("火力密度，还是单发威力？","阵地只留八格。","保留、合成、回收，由你决定。")];
        for(int i=0;i<rules.Length;i++)
        {
            int y=294+i*92;c.Circle(12.5,y,12.5,null,"#2A404B");c.Text((i+1).ToString("D2"),12.5,y,11,"#5B7C86",500,"center");
            c.Text(rules[i].title,42,y-3,12,"#D7E6ED",600);c.Text(rules[i].a,42,y+23,11,"#72899D");c.Text(rules[i].b,42,y+43,11,"#72899D");
        }
        c.Line(0,564,245,564,"#1E3441");c.Text("无广告 · 无付费 · 本地存档",0,591,10,"#577082");
        c.Text("SPACE 召唤  /  ESC 暂停  /  M 音效",0,613,10,"#577082");c.Text("F11 全屏  /  右键取消瞄准",0,635,10,"#577082");c.Restore();
    }
}
