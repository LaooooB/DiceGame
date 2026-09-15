using System.Globalization;
using System.Text;
using DiceGame.Core;
using Godot;

namespace DiceGame.Presentation;

/// <summary>
/// Godot CanvasItem drawing, not an embedded browser. Text remains editable native text.
/// Static dice/icon/glow pixels are exported losslessly from the original renderer.
/// Gradients are cached native ImageTextures; moving particles, lines and UI are drawn by Godot.
/// </summary>
public sealed class NativeArt : IDisposable
{
    private readonly Dictionary<int,Texture2D> _dice=[];
    private readonly Dictionary<string,Texture2D> _glows=[];
    private readonly Dictionary<string,Texture2D> _gradients=[];
    private readonly Dictionary<int,SystemFont> _fonts=[];
    public readonly Texture2D Background;
    public readonly Texture2D Icons;
    public static readonly int[] Sizes=[22,41,46,49,53,60,64,92,125];
    public static readonly string[] TypeRows=["pulse","blast","arc","frost","split","bank"];
    public static readonly string[] IconKinds=["arc","energy","blast","frost","split","bank","pulse","shield","pause","play","sound","muted","close","back","check","plus","aim","merge","reload","info"];
    public NativeArt()
    {
        Background=GD.Load<Texture2D>("res://Assets/Reference/background.png");
        Icons=GD.Load<Texture2D>("res://Assets/Reference/icons.png");
        foreach(int s in Sizes) _dice[s]=GD.Load<Texture2D>($"res://Assets/Reference/dice_{s}.png");
        foreach(string col in new[]{"72EAC8","FFAD76","CBA7FF","87D6FF","F8DE87","FF97B8"})
            _glows["#"+col]=GD.Load<Texture2D>("res://Assets/Reference/glow_"+col+".png");
    }
    public Texture2D Dice(int size)=>_dice[size];
    public SystemFont Font(int weight)
    {
        if(_fonts.TryGetValue(weight,out var font)) return font;
        string os=OS.GetName();
        string[] names=os=="Windows"?["Segoe UI","Microsoft YaHei","Arial"]:
            os=="macOS"?[".AppleSystemUIFont","PingFang SC","Helvetica Neue","Arial"]:
            ["DejaVu Sans","Noto Sans CJK SC","Noto Sans","sans-serif"];
        font=new SystemFont {FontNames=names,FontWeight=weight,AllowSystemFallback=true,Oversampling=2.0f};
        _fonts[weight]=font;return font;
    }
    public Texture2D Glow(string color)
    {
        if(_glows.TryGetValue(color,out var texture)) return texture;
        var c=NativeCanvas.Parse(color);using var image=Image.CreateEmpty(48,48,false,Image.Format.Rgba8);
        for(int y=0;y<48;y++)for(int x=0;x<48;x++)
        {
            double r=Math.Sqrt((x+0.5-24)*(x+0.5-24)+(y+0.5-24)*(y+0.5-24))/24;
            double alpha=r<0.14?MathEx.Lerp(1,0.8,r/0.14):r<0.42?MathEx.Lerp(0.8,0.2,(r-0.14)/0.28):MathEx.Lerp(0.2,0,(r-0.42)/0.58);
            image.SetPixel(x,y,new Color(c.R,c.G,c.B,(float)MathEx.Clamp(alpha,0,1)));
        }
        texture=ImageTexture.CreateFromImage(image);_glows[color]=texture;return texture;
    }
    public Texture2D Gradient(string key,string svg)
    {
        if(_gradients.TryGetValue(key,out var texture)) return texture;
        using var image=new Image();var error=image.LoadSvgFromString(svg,2.0f);
        if(error!=Error.Ok) throw new InvalidOperationException($"Gradient rasterization failed: {error}");
        texture=ImageTexture.CreateFromImage(image);_gradients[key]=texture;return texture;
    }
    public void Dispose()
    {
        foreach(var f in _fonts.Values) f.Dispose();
        foreach(var t in _gradients.Values) t.Dispose();
        // GD.Load resources are engine-owned/shared. Do not invalidate another node's atlas.
        _fonts.Clear();_gradients.Clear();
    }
}

public sealed class NativeCanvas(CanvasItem item,NativeArt art)
{
    private Transform2D _transform=Transform2D.Identity;
    private readonly Stack<(Transform2D transform,double alpha)> _stack=[];
    public double Alpha { get; set; }=1;
    public NativeArt Art=>art;
    private static Vector2 V(double x,double y)=>new((float)x,(float)y);
    public void Begin() {_stack.Clear();_transform=Transform2D.Identity;Alpha=1;Apply();}
    private void Apply()=>item.DrawSetTransformMatrix(_transform);
    public void Save()=>_stack.Push((_transform,Alpha));
    public void Restore() {(_transform,Alpha)=_stack.Pop();Apply();}
    public void Translate(double x,double y) {_transform*=new Transform2D(0,V(x,y));Apply();}
    public void Rotate(double angle) {_transform*=new Transform2D((float)angle,Vector2.Zero);Apply();}
    public void Scale(double x,double y) {_transform*=new Transform2D(V(x,0),V(0,y),Vector2.Zero);Apply();}
    public static Color Parse(string value)
    {
        if(value.StartsWith("rgba",StringComparison.Ordinal))
        {
            var p=value[(value.IndexOf('(')+1)..value.IndexOf(')')].Split(',');
            return new Color(float.Parse(p[0],CultureInfo.InvariantCulture)/255,float.Parse(p[1],CultureInfo.InvariantCulture)/255,
                float.Parse(p[2],CultureInfo.InvariantCulture)/255,float.Parse(p[3],CultureInfo.InvariantCulture));
        }
        return Color.FromHtml(value);
    }
    private Color Tint(string value) {var c=Parse(value);return new Color(c.R,c.G,c.B,c.A*(float)MathEx.Clamp(Alpha,0,1));}
    public void Rect(double x,double y,double w,double h,string color)
    {if(w>0 && h>0 && Alpha>0)item.DrawRect(new Rect2(V(x,y),V(w,h)),Tint(color));}
    public void Line(double x,double y,double tx,double ty,string color,double width=1,bool roundCaps=false)
    {
        if(width<=0 || Alpha<=0) return;
        item.DrawLine(V(x,y),V(tx,ty),Tint(color),(float)width,true);
        if(roundCaps) {Circle(x,y,width/2,color);Circle(tx,ty,width/2,color);}
    }
    public void Polyline(IEnumerable<PointD> points,string color,double width=1,bool roundCaps=false)
    {
        var p=points.Select(v=>V(v.X,v.Y)).ToArray();if(p.Length<2 || Alpha<=0)return;
        item.DrawPolyline(p,Tint(color),(float)width,true);
        if(roundCaps){Circle(p[0].X,p[0].Y,width/2,color);Circle(p[^1].X,p[^1].Y,width/2,color);}
    }
    public void Polygon(PointD[] points,string color)
    {if(points.Length>=3 && Alpha>0)item.DrawColoredPolygon(points.Select(p=>V(p.X,p.Y)).ToArray(),Tint(color));}
    public void Circle(double x,double y,double radius,string? fill=null,string? stroke=null,double width=1)
    {
        if(radius<=0 || Alpha<=0)return;
        if(fill is not null)item.DrawCircle(V(x,y),(float)radius,Tint(fill));
        if(stroke is not null && width>0)item.DrawArc(V(x,y),(float)radius,0,Mathf.Tau,Math.Max(24,(int)(radius*2)),Tint(stroke),(float)width,true);
    }
    public void Box(double x,double y,double w,double h,double radius,string? fill=null,string? stroke=null,double width=1)
    {
        if(w<=0 || h<=0 || Alpha<=0)return;
        double r=Math.Max(0,Math.Min(radius,Math.Min(w/2,h/2)));
        if(r<0.01)
        {
            if(fill is not null)Rect(x,y,w,h,fill);
            if(stroke is not null)item.DrawRect(new Rect2(V(x,y),V(w,h)),Tint(stroke),false,(float)width);return;
        }
        var points=new List<Vector2>(68);int n=Math.Max(6,Math.Min(24,(int)Math.Ceiling(r*1.5)));
        (double cx,double cy,double start)[] corners=[(x+w-r,y+r,-Math.PI/2),(x+w-r,y+h-r,0),(x+r,y+h-r,Math.PI/2),(x+r,y+r,Math.PI)];
        foreach(var (cx,cy,start) in corners)for(int i=0;i<=n;i++)
        {double a=start+i*(Math.PI/2)/n;points.Add(V(cx+Math.Cos(a)*r,cy+Math.Sin(a)*r));}
        if(fill is not null)item.DrawColoredPolygon(points.ToArray(),Tint(fill));
        if(stroke is not null){points.Add(points[0]);item.DrawPolyline(points.ToArray(),Tint(stroke),(float)width,true);}
    }
    private static string F(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
    private static string Stops((double pos,string color)[] stops)
    {
        var s=new StringBuilder();
        foreach(var (pos,color) in stops){var c=Parse(color);s.Append($"<stop offset='{F(pos)}' stop-color='#{c.ToHtml(false)}' stop-opacity='{F(c.A)}'/>");}
        return s.ToString();
    }
    public void GradientBox(double x,double y,double w,double h,double radius,(double pos,string color)[] stops,
        double x0=0,double y0=0,double? x1=null,double? y1=null,string? stroke=null,double width=1)
    {
        if(w<=0 || h<=0 || Alpha<=0)return;
        double r=Math.Max(0,Math.Min(radius,Math.Min(w/2,h/2))),pad=2;
        double endX=x1??w,endY=y1??h;
        string definition=$"<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='{F(x0+pad)}' y1='{F(y0+pad)}' x2='{F(endX+pad)}' y2='{F(endY+pad)}'>{Stops(stops)}</linearGradient>";
        string svg=$"<svg xmlns='http://www.w3.org/2000/svg' width='{F(w+pad*2)}' height='{F(h+pad*2)}'><defs>{definition}</defs><rect x='{F(pad)}' y='{F(pad)}' width='{F(w)}' height='{F(h)}' rx='{F(r)}' fill='url(#g)'/></svg>";
        var texture=art.Gradient(svg,svg);item.DrawTextureRect(texture,new Rect2(V(x-pad,y-pad),V(w+pad*2,h+pad*2)),false,new Color(1,1,1,(float)Alpha));
        if(stroke is not null)Box(x,y,w,h,r,null,stroke,width);
    }
    public void RadialRect(double x,double y,double w,double h,double cx,double cy,double innerRadius,double outerRadius,(double pos,string color)[] stops)
    {
        if(w<=0 || h<=0 || Alpha<=0)return;
        var mapped=stops.Select(s=>((innerRadius+s.pos*(outerRadius-innerRadius))/outerRadius,s.color)).ToArray();
        string svg=$"<svg xmlns='http://www.w3.org/2000/svg' width='{F(w)}' height='{F(h)}'><defs><radialGradient id='r' gradientUnits='userSpaceOnUse' cx='{F(cx-x)}' cy='{F(cy-y)}' r='{F(outerRadius)}'>{Stops(mapped)}</radialGradient></defs><rect width='{F(w)}' height='{F(h)}' fill='url(#r)'/></svg>";
        var texture=art.Gradient(svg,svg);item.DrawTextureRect(texture,new Rect2(V(x,y),V(w,h)),false,new Color(1,1,1,(float)Alpha));
    }
    public void Image(Texture2D texture,double x,double y,double w,double h,string color="#FFFFFF")
    {if(Alpha>0)item.DrawTextureRect(texture,new Rect2(V(x,y),V(w,h)),false,Tint(color));}
    public void Glow(string color,double x,double y,double size)=>Image(art.Glow(color),x-size/2,y-size/2,size,size);
    public void Die(string type,int pips,double x,double y,double size,double angle=0,double alpha=1,int referenceSize=0)
    {
        int row=Array.IndexOf(NativeArt.TypeRows,type);if(row<0)return;
        int s=referenceSize>0?referenceSize:NativeArt.Sizes.OrderBy(n=>Math.Abs(n-size)).First();
        double cell=s+16,scale=size/s;
        Save();Alpha*=alpha;Translate(x,y);Rotate(angle);Scale(scale,scale);
        item.DrawTextureRectRegion(art.Dice(s),new Rect2(V(-cell/2,-cell/2),V(cell,cell)),
            new Rect2(V((Math.Clamp(pips,1,6)-1)*cell*3,row*cell*3),V(cell*3,cell*3)),new Color(1,1,1,(float)Alpha));
        Restore();
    }
    public void Icon(string kind,double x,double y,double size,string color=Palette.Text)
    {
        int i=Array.IndexOf(NativeArt.IconKinds,kind);if(i<0)return;
        double cell=32*size/24;
        item.DrawTextureRectRegion(art.Icons,new Rect2(V(x-cell/2,y-cell/2),V(cell,cell)),new Rect2(V(i%5*96,i/5*96),V(96,96)),Tint(color));
    }
    public double Measure(string text,double size=14,int weight=500)=>art.Font(weight).GetStringSize(text,HorizontalAlignment.Left,-1,(int)Math.Round(size)).X;
    public void Text(object? value,double x,double y,double size=14,string color=Palette.Text,int weight=500,string align="left")
    {
        if(Alpha<=0)return;
        string text=value is IFormattable fmt?fmt.ToString(null,CultureInfo.InvariantCulture):value?.ToString()??"";
        var font=art.Font(weight);int fontSize=(int)Math.Round(size);double width=Measure(text,size,weight);
        if(align=="center")x-=width/2;else if(align=="right")x-=width;
        double baseline=y+(font.GetAscent(fontSize)-font.GetDescent(fontSize))/2;
        item.DrawString(font,V(x,baseline),text,HorizontalAlignment.Left,-1,fontSize,Tint(color));
    }
    public void Track(string value,double x,double y,double size=12,string color=Palette.Muted,double spacing=3,string align="center")
    {
        double width=value.Sum(ch=>Measure(ch.ToString(),size,600))+spacing*(value.Length-1);
        double xx=align=="center"?x-width/2:x;
        foreach(char ch in value){string c=ch.ToString();Text(c,xx,y,size,color,600);xx+=Measure(c,size,600)+spacing;}
    }
    public double Wrap(string value,double x,double y,double width,double size=12,string color=Palette.Muted,double lineHeight=19,int maxLines=5)
    {
        var rows=new List<string>();string row="";
        foreach(char ch in value)
        {
            if(ch=='\n'){rows.Add(row);row="";continue;}
            if(Measure(row+ch,size)>width && row.Length>0){rows.Add(row);row=ch.ToString();}else row+=ch;
        }
        if(row.Length>0)rows.Add(row);
        int n=Math.Min(rows.Count,maxLines);for(int i=0;i<n;i++)Text(rows[i],x,y+i*lineHeight,size,color);return n*lineHeight;
    }
    public void DashedBox(double x,double y,double w,double h,double r,string color,double width,double on,double off)
    {
        var pts=new List<PointD>();r=Math.Min(r,Math.Min(w,h)/2);
        (double cx,double cy,double start)[] corners=[(x+w-r,y+r,-Math.PI/2),(x+w-r,y+h-r,0),(x+r,y+h-r,Math.PI/2),(x+r,y+r,Math.PI)];
        foreach(var (cx,cy,start) in corners)for(int i=0;i<=12;i++){double a=start+i*Math.PI/24;pts.Add(new PointD(cx+Math.Cos(a)*r,cy+Math.Sin(a)*r));}
        pts.Add(pts[0]);double phase=0;
        for(int i=1;i<pts.Count;i++){var a=pts[i-1];var b=pts[i];DashedLine(a.X,a.Y,b.X,b.Y,color,width,on,off,phase);phase+=Math.Sqrt(MathEx.Dist2(a.X,a.Y,b.X,b.Y));}
    }
    public void DashedLine(double x,double y,double tx,double ty,string color,double width,double on,double off,double offset=0)
    {
        double dx=tx-x,dy=ty-y,length=Math.Sqrt(dx*dx+dy*dy);if(length<=0.001)return;
        double cycle=on+off,start=-((offset%cycle+cycle)%cycle);
        for(double d=start;d<length;d+=cycle)
        {double a=Math.Max(0,d),b=Math.Min(length,d+on);if(b>a)Line(x+dx*a/length,y+dy*a/length,x+dx*b/length,y+dy*b/length,color,width);}
    }
}
