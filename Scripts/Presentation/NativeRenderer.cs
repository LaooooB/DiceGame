using System.Globalization;
using DiceGame.App;
using DiceGame.Core;
using P=DiceGame.Presentation.Palette;

namespace DiceGame.Presentation;

/// <summary>Native CanvasItem port of renderer.js. Coordinates are reference-canvas units, not window pixels.</summary>
public sealed class NativeRenderer(GameApp app)
{
    private GameData D=>app.Data;
    private EffectSystem F=>app.Effects;
    private Simulation S=>app.Sim!;
    private RunState State=>S.State;
    public bool ShowsBattle=>app.Sim is not null && app.Scene!="menu" && app.Scene!="deck" && !(app.Scene=="help" && app.HelpReturn=="menu");
    private static string N(double value,int places)=>MathEx.JsFixed(value,places);
    public void Base(NativeCanvas c)
    {
        c.Image(c.Art.Background,0,0,432,864);
        if(app.Scene=="deck") Deck(c);
        else if(!ShowsBattle) Menu(c);
        else {Header(c);c.Box(16,122,400,427,19,"#07101C","#28394B");c.Box(21,127,390,417,15,null,"#162D3A");}
    }
    public void FieldBelow(NativeCanvas c)
    {
        if(!ShowsBattle)return;Shake(c);
        c.GradientBox(28,136,376,410,0,[(0,"#0D1725"),(1,"#0C1B29")],0,0,0,398);
        for(int y=137;y<535;y+=25)c.Line(28,y,404,y,"rgba(102,141,164,.055)");
        for(int x=28;x<=404;x+=25)c.Line(x,136,x,535,"rgba(102,141,164,.055)");
        for(int i=0;i<22;i++)c.Circle(42+(i*71)%352,146+((i*53+F.T*(2+i%3))%370),i%3==0?1.1:0.7,"rgba(123,197,200,.16)");
        c.Rect(27,136,3,399,"#203948");c.Rect(402,136,3,399,"#203948");
        (double,string)[] wall=[(0,"rgba(114,234,200,.13)"),(.6,"rgba(114,234,200,.5)"),(1,"rgba(114,234,200,.85)")];
        c.GradientBox(28,136,1.5,399,0,wall,0,0,0,399);c.GradientBox(402.5,136,1.5,399,0,wall,0,0,0,399);
        for(int y=153;y<510;y+=34){c.Line(30,y,34,y,"#496E78");c.Line(398,y,402,y,"#496E78");}
        foreach(var e in State.Enemies)Enemy(c,e);
        if(app.Pointer?.Mode=="aim" && (!app.NativeUi || app.Preferences.ShowAim))Aim(c);
    }
    private void Shake(NativeCanvas c)
    {if(F.Shake>0 && !F.ReduceMotion && (!app.NativeUi || app.Preferences.ScreenShake))c.Translate(Math.Sin(F.T*71)*F.Shake,Math.Cos(F.T*83)*F.Shake*.6);}
    /// <summary>This entire layer uses Godot's additive CanvasItemMaterial, matching Canvas 'lighter'.</summary>
    public void Projectiles(NativeCanvas c)
    {
        if(!ShowsBattle)return;Shake(c);
        foreach(var p in State.Projectiles)
        {
            string color=p.Stats.Color;
            if(p.Trail.Count>1){c.Alpha=p.Child ? .32:.50;c.Polyline(p.Trail.Append(new PointD(p.X,p.Y)),color,p.Child?1.5:2.6,true);}
            c.Alpha=p.Child ? .65:1;double size=p.Surge?34:p.Child?18:25;c.Glow(color,p.X,p.Y,size);
            c.Circle(p.X,p.Y,p.Child?2:3.5,color);c.Circle(p.X,p.Y,p.Child?1:1.8,"#FFFFFF");
            if(p.Stats.Effect=="bank" && p.WallPower>1)c.Circle(p.X,p.Y,6,null,color);
        }
        c.Alpha=1;
    }
    public void FieldAbove(NativeCanvas c)
    {
        if(!ShowsBattle)return;Shake(c);DrawEffects(c);
        string shield=State.Health<=4?P.Pink:P.Mint;
        c.GradientBox(31,487,370,35,0,[(0,shield+"00"),(1,shield+"15")],0,0,0,34);
        c.Line(34,519,398,519,shield+"77",1.2);for(int x=42;x<400;x+=24)c.Line(x,526,x+7,526,shield+"44");
        if(State.Combo>=3 && State.ComboTime>0)
        {
            c.Save();c.Alpha=Math.Min(1,State.ComboTime);c.Track("CHAIN",55,449,8,P.Gold,2,"left");
            c.Text(State.Combo.ToString("D2"),54,479,38,P.Gold,800);c.Text("连锁",55,506,11,P.Gold,600);c.Restore();
        }
        if(F.Banner is { } b)
        {
            c.Save();c.Alpha=Math.Max(0,Math.Min(1,Math.Min((b.Max-b.Life)*8,b.Life*5)));
            c.GradientBox(64,287,304,74,0,[(0,"#0A142000"),(.25,"#0A1420E8"),(.75,"#0A1420E8"),(1,"#0A142000")],0,0,304,0);
            c.Text(b.Label,216,311,26,b.Color,750,"center");c.Track(b.Sub,216,342,9,P.Muted,1);c.Restore();
        }
        if(F.Flash>0 && !F.ReduceMotion && (!app.NativeUi || app.Preferences.FlashEffects))c.Rect(28,136,376,400,"rgba(255,125,159,"+N(F.Flash*.8,6)+")");
    }
    public void BattleLauncher(NativeCanvas c) { if(ShowsBattle) Launcher(c); }
    public void Foreground(NativeCanvas c)
    {
        if(!ShowsBattle)return;Launcher(c);bool aiming=app.Pointer?.Mode=="aim",cancel=aiming && app.Pointer!.Y>575;
        c.Text(aiming?(cancel?"松手取消 · 移回战场继续瞄准":"松手齐射  /  移到骰子区取消"):(S.Count>0?"按住战场瞄准  ·  松手齐射":"召唤骰子，然后按住战场瞄准"),216,568,11,aiming?(cancel?P.Pink:P.Mint):P.Muted,aiming?600:550,"center");
        Board(c);
        foreach(var q in F.Conduits)
        {
            double p=1-q.Life/q.Max;var from=D.SlotPosition(q.Slot);double x=MathEx.Lerp(from.X,216,p),y=MathEx.Lerp(from.Y-25,530,p*p);
            c.Save();c.Alpha=(1-p)*.8;c.Line(from.X,from.Y-25,x,y,q.Color,1.4);c.Glow(q.Color,x,y,28);c.Circle(x,y,2.5,q.Color);c.Restore();
        }
    }
    public void Overlay(NativeCanvas c)
    {
        switch(app.Scene)
        {
            case "paused":Pause(c);break;case "upgrade":Upgrade(c);break;case "over":GameOver(c);break;
            case "die":DieInfo(c);break;case "help":Help(c);break;case "confirmNew":ConfirmNew(c);break;
        }
        if(app.Toast is {Life:>0} toast)
        {
            c.Save();c.Alpha=Math.Min(1,toast.Life*4);double w=Math.Min(380,c.Measure(toast.Text,12,600)+36);
            c.Box((432-w)/2,555,w,33,12,"#213448","#537387");c.Text(toast.Text,216,571.5,12,"#F1FCFF",600,"center");c.Restore();
        }
    }
    private void Button(NativeCanvas c,string id,double x,double y,double w,double h,string label,string style="ghost",double fontSize=14,
        bool disabled=false,double radius=12,string? icon=null,bool iconOnly=false,double iconSize=20,string color=P.Text)
    {
        double yy=y+(app.Pointer?.Button==id?1.5:0);c.Save();if(disabled)c.Alpha*=.5;
        if(style=="primary")
        {c.Box(x,yy+4,w,h,14,"#245F57");c.GradientBox(x,yy,w,h,14,[(0,"#9EF7DB"),(1,"#5ECBAE")],stroke:"#BCFFE8",width:.8);}
        else c.Box(x,yy,w,h,radius,style=="danger"?"#442635":style=="ghost"?"rgba(22,36,53,.6)":"#1A2A3D",style=="danger"?"#83445B":P.Line);
        if(icon is not null)c.Icon(icon,x+(iconOnly?w/2:24),yy+h/2,iconSize,style=="primary"?"#102C2A":color);
        if(label.Length>0)c.Text(label,x+w/2,yy+h/2,fontSize,style=="primary"?"#102C2A":color,650,"center");c.Restore();
    }
    private void Menu(NativeCanvas c)
    {
        double t=F.T;c.Track("A LITTLE CHAOS. A LOT OF IMPACT.",216,40,9,P.Muted,1.5);
        Button(c,"sound",369,64,34,34,"",icon:app.Settings.Sound?"sound":"muted",iconOnly:true,iconSize:17);
        c.Box(29,71,117,25,12,"#1D302F","#365750",.8);c.Circle(42,83.5,2.5,P.Mint);c.Text("鼠标 · 无尽弹射",53,83.5,10,P.Mint,600);
        c.Text("骰子回响",216,156,52,P.Text,800,"center");c.Track("D I C E   R I C O C H E T",216,205,12,P.Mint,1.2);
        c.Text("合出火力，让每一次碰撞都有回响。",216,236,12,P.Muted,500,"center");
        c.RadialRect(24,260,384,257,216,381,16,169,[(0,"rgba(75,188,165,.15)"),(1,"rgba(40,80,100,0)")]);
        PointD[] path=[new(58,420),new(372,292),new(372,444),new(68,321)];double dashOffset=0;
        for(int i=1;i<path.Length;i++){var a=path[i-1];var b=path[i];c.DashedLine(a.X,a.Y,b.X,b.Y,"#33535C",1,3,8,dashOffset);dashOffset+=Math.Sqrt(MathEx.Dist2(a.X,a.Y,b.X,b.Y));}
        double flow=(t*.24)%1;int j=Math.Min(2,(int)Math.Floor(flow*3));double f=flow*3-j;
        double px=MathEx.Lerp(path[j].X,path[j+1].X,f),py=MathEx.Lerp(path[j].Y,path[j+1].Y,f);c.Glow(P.Gold,px,py,36);c.Circle(px,py,3.8,"#FFFFFF");
        c.Save();c.Translate(216,470);c.Scale(1,.25);c.Circle(0,0,118,null,"#315B5D",1.8);c.Circle(0,0,84,null,"rgba(114,234,200,.18)",2);c.Restore();
        c.Die("arc",2,95,390+Math.Sin(t*1.2)*5,53,-.25);c.Die("blast",4,330,318+Math.Cos(t)*6,64,.23);c.Die("pulse",5,215,371+Math.Sin(t*1.4)*6,125,-.13);
        c.Circle(342,441,4,P.Gold);c.Circle(84,290,2.5,P.Mint);c.Circle(153,299,2,"#9EAED0");
        for(int i=0;i<6;i++){double a=t*.14+i*Math.PI/3;c.Circle(216+Math.Cos(a)*145,376+Math.Sin(a)*94,1.6,"#547F84");}
        (string icon,string label)[] steps=[("aim","按住瞄准"),("play","松手齐射"),("merge","同种同点合成")];
        for(int i=0;i<3;i++){int x=81+i*135;c.Icon(steps[i].icon,x,520,20,i==1?P.Gold:P.Mint);c.Text(steps[i].label,x,548,12,P.Text,550,"center");}
        c.Line(144,511,144,548,P.Line);c.Line(280,511,280,548,P.Line);c.Text("本局卡组",32,597,13,P.Text,650);c.Text(app.Deck.Count+" / 6",102,597,11,P.Muted);
        Button(c,"editDeck",331,581,69,29,"配置",fontSize:11);int n=app.Deck.Count;double step=Math.Min(64,365.0/n),start=216-(n-1)*step/2;
        for(int i=0;i<n;i++){string id=app.Deck[i];double x=start+i*step;c.Die(id,1,x,645,41);c.Text(D.Types[id].Name,x,681,10,P.Muted,600,"center");}
        if(app.ResumeData is { } saved)
        {Button(c,"resume",32,715,368,55,"继续战斗 · 第 "+saved.Wave+" 波","primary");Button(c,"new",32,785,236,39,"重新开始",fontSize:12);Button(c,"help",280,785,120,39,"玩法说明",fontSize:12);}
        else {Button(c,"start",32,723,368,59,"进入回廊","primary",18);c.Icon("play",355,752,19,"#123A32");Button(c,"help",145,805,142,30,"玩法说明",fontSize:11);}
        if(app.Meta.BestWave>0)c.Text("最佳纪录  /  第 "+app.Meta.BestWave+" 波",216,705,10,P.Gold,550,"center");
    }
    private void Header(NativeCanvas c)
    {
        var s=State;c.Die("pulse",4,34,33,22,-.13);c.Text("骰子回响",54,31,17,P.Text,750);c.Track("DICE RICOCHET",55,51,8,P.Muted,1.6,"left");
        Button(c,"sound",327,20,35,35,"",icon:app.Settings.Sound?"sound":"muted",iconOnly:true,iconSize:17);Button(c,"pause",373,20,35,35,"",icon:"pause",iconOnly:true,iconSize:17);
        c.Text(s.Wave.ToString("D2"),28,89,32,P.Text,750);c.Text("WAVE",81,79,9,P.Muted,700);c.Text(s.Wave%5==0?"首领波次":"无尽回廊",81,97,10,s.Wave%5==0?P.Pink:P.Muted,550);
        c.Icon("shield",180,87,21,s.Health<=4?P.Pink:P.Mint);c.Text(s.Health,199,87,22,s.Health<=4?P.Pink:P.Text,700);c.Text("/ "+D.Game.Rules.MaxHealth,228,91,11,P.Muted);
        for(int i=0;i<D.Game.Rules.MaxHealth;i++)c.Box(170+i*7,108,4.5,2.5,1,i<s.Health?(s.Health<=4?P.Pink:P.Mint):"#2B394A");
        c.Icon("energy",313,84,20,P.Gold);c.Text(Math.Floor(s.Energy),398,83,26,P.Gold,700,"right");c.Text("召唤能量",398,105,10,P.Muted,500,"right");
    }
    private void Enemy(NativeCanvas c,EnemyState e)
    {
        var a=D.Game.Arena;if(e.Dead || e.Y+e.H/2<a.Top-4 || e.Y-e.H/2>a.Bottom)return;
        bool iced=State.Time<e.SlowUntil;string color=iced?"#87D6FF":e.Kind=="volatile"?P.Orange:e.Kind=="boss"?P.Pink:e.Kind=="armored"?"#C6B4EE":"#E5969F";
        double x=e.X-e.W/2,y=e.Y-e.H/2;c.Box(x,y+4,e.W,e.H,7,"rgba(0,0,0,.35)");
        c.GradientBox(x,y,e.W,e.H,7,[(0,color+"36"),(1,color+"10")],stroke:color+"AC",width:1.3);
        c.Line(x+8,y+3,x+e.W-8,y+3,color+"5A");if(e.Kind=="armored")c.Box(x+3,y+3,e.W-6,e.H-6,5,null,color+"45");
        if(e.Kind=="boss"){c.Track("WARDEN",e.X,y+12,7,color,1.5);c.Icon("shield",x+12,e.Y+5,10,color);c.Text(P.Compact(Math.Max(0,e.Hp)),e.X+5,e.Y+6,23,P.Text,750,"center");}
        else{if(e.Kind=="volatile")c.Icon("blast",x+7,y+7,7,color);else if(iced)c.Icon("frost",x+7,y+7,7,color);c.Text(P.Compact(Math.Max(0,e.Hp)),e.X,e.Y+1,e.Hp>=1000?13:18,P.Text,650,"center");}
        c.Box(x+7,y+e.H-6,e.W-14,2,1,"#263345");c.Box(x+7,y+e.H-6,(e.W-14)*MathEx.Clamp(e.Hp/e.MaxHp,0,1),2,1,color);
        if(e.Flash>0){c.Save();c.Alpha=e.Flash*.35;c.Box(x,y,e.W,e.H,7,"#FFFFFF");c.Restore();}
        if(iced){c.Save();c.Alpha=.5;c.Icon("frost",x+e.W-5,y+3,10,"#C5EDFF");c.Restore();}
    }
    private void Launcher(NativeCanvas c)
    {
        double a=app.Pointer?.Mode=="aim"?app.AimAngle:State.LastAim;int ready=S.ReadyCount;
        c.Circle(216,533,19,"#101D2B","#305C62",1.3);c.Circle(216,533,15,null,ready>0?P.Mint+"AA":"#526576",1.8);
        c.Save();c.Translate(216,533);c.Rotate(a+Math.PI/2);c.Box(-5,-22,10,22,4,"#45666B","#85C9B7");c.Box(-3,-24,6,7,2,P.Mint);c.Restore();
        c.Circle(216,533,8,"#1D4544","#8CCBBB");c.Circle(216,533,3,P.Mint);c.Text("就绪",51,543,9,P.Muted);c.Text(ready+" / "+S.Count,79,543,11,ready>0?P.Mint:P.Muted,650);
        c.Text("得分 "+P.Compact(State.Score),382,543,10,P.Muted,500,"right");
    }
    private void Aim(NativeCanvas c)
    {
        if(app.Pointer is not { } pointer || pointer.Y>575)return;var points=S.TraceAim(app.AimAngle);c.Save();
        for(int i=1;i<points.Count;i++)
        {c.Alpha=Math.Max(.15,.72-i*.12);var a=points[i-1];var b=points[i];c.DashedLine(a.X,a.Y,b.X,b.Y,P.Mint,i==1?1.7:1.2,4,7,-F.T*30);if(b.Enemy is not null)c.Circle(b.X,b.Y,7,null,P.Gold,1.5);else c.Circle(b.X,b.Y,3,P.Mint);}
        c.Alpha=.65;c.Icon("aim",MathEx.Clamp(pointer.X,39,393),MathEx.Clamp(pointer.Y,149,506),23,P.Mint);c.Restore();
    }
    private void DrawEffects(NativeCanvas c)
    {
        c.Save();
        foreach(var r in F.Rings){double p=1-r.Life/r.Max;c.Alpha=(1-p)*.7;c.Circle(r.X,r.Y,Math.Max(1,r.Radius*(.12+p*.88)),null,r.Color,Math.Max(.5,(1-p)*2.5));}
        foreach(var a in F.Arcs){c.Alpha=Math.Min(1,a.Life*10);c.Polyline(a.Points,a.Color,3);c.Polyline(a.Points,"#F4EEFF");}
        foreach(var p in F.Particles)
        {
            c.Alpha=Math.Min(1,p.Life/p.Max*1.4);c.Save();c.Translate(p.X,p.Y);c.Rotate(p.Angle);
            if(p.Shape==0)c.Rect(-p.Size/2,-p.Size/2,p.Size,p.Size,p.Color);
            else if(p.Shape==1)c.Polygon([new(0,-p.Size),new(p.Size*.6,p.Size*.5),new(-p.Size*.6,p.Size*.5)],p.Color);
            else c.Rect(-p.Size,-.6,p.Size*2,1.2,p.Color);c.Restore();
        }
        foreach(var f in F.Floaters){double p=1-f.Life/f.Max;c.Alpha=Math.Min(1,f.Life*4);c.Text(f.Label,f.X,f.Y-p*28,f.Size,f.Color,700,"center");}c.Restore();
    }
    private void Board(NativeCanvas c)
    {
        bool dragging=app.Pointer?.Mode=="drag";int from=dragging?app.Pointer!.Slot:-1;
        c.Text("骰子阵地",30,590,13,P.Text,650);c.Text(S.Count+" / 8",107,590,11,P.Muted,600);bool pair=S.HasPair();
        c.Text(pair?"有可合成的骰子":"同种类 + 同点数 才可合成",402,590,10,pair?P.Mint:P.Muted,550,"right");
        for(int i=0;i<D.Game.Board.Slots;i++)
        {
            var pos=D.SlotPosition(i);var d=State.Board[i];bool match=dragging && S.CanMerge(from,i),fade=dragging && from==i;
            c.Save();if(fade)c.Alpha=.27;c.Box(pos.X-41,pos.Y-42,82,86,13,d is not null?"#152235":"#101B2B",match?P.Mint:"#2A3B50",match?1.8:1);
            if(d is not null)
            {
                if(match){c.Save();c.Alpha=.10+Math.Sin(F.T*8)*.04;c.Box(pos.X-40,pos.Y-41,80,84,12,P.Mint);c.Restore();}
                double scale=1;var pulse=F.Pulses.Find(p=>p.Slot==i);if(pulse is not null){double progress=1-pulse.Life/pulse.Max;scale=1+Math.Sin(progress*Math.PI*2.3)*.10*(1-progress);}
                c.Die(d.Type,d.Pips,pos.X,pos.Y-8,49*scale,referenceSize:49);var type=D.Types[d.Type];c.Text(type.Name+" · "+d.Pips,pos.X,pos.Y+29,10,type.Color,600,"center");
                double loaded=1-MathEx.Clamp(d.Cooldown/S.Stats(d).Reload,0,1);c.Box(pos.X-27,pos.Y+38,54,2,1,"#2B3D50");c.Box(pos.X-27,pos.Y+38,54*loaded,2,1,type.Color);
                c.Circle(pos.X+31,pos.Y-32,2.2,loaded>=.999?P.Mint:"#586278");c.Icon(type.Icon,pos.X-31,pos.Y-32,8,type.Color);
            }
            else
            {c.DashedBox(pos.X-24,pos.Y-28,48,47,9,"#2C4155",1,2,4);c.Icon("plus",pos.X,pos.Y-6,19,"#3C566C");c.Text("空位",pos.X,pos.Y+28,10,"#567087",500,"center");}
            c.Restore();
        }
        foreach(var p in F.Pulses)
        {
            var pos=D.SlotPosition(p.Slot);double progress=1-p.Life/p.Max;c.Save();c.Alpha=Math.Max(0,1-progress);
            c.Circle(pos.X,pos.Y-8,28+progress*32,null,p.Color,2);if(p.Merge)c.Text("合成！"+p.Pips+" 点",pos.X,pos.Y-51-progress*16,12,p.Color,750,"center");c.Restore();
        }
        c.Text("单击查看 · 拖动合成 · 不同类型不能合成",216,789,10,P.Muted,500,"center");bool full=S.Count==D.Game.Board.Slots;
        Button(c,"summon",28,807,270,42,"","primary",disabled:State.Energy<D.Game.Rules.SummonCost || full);
        c.Icon("plus",50,828,15,"#18433A");c.Text(full?"阵地已满":"召唤骰子",72,828,15,"#123A32",700);c.Icon("energy",246,828,16,"#1B594A");c.Text(D.Game.Rules.SummonCost,279,828,16,"#123A32",750,"right");
        Button(c,"help",310,807,94,42,"说明",fontSize:12);
        if(dragging && State.Board[from] is { } dragged){var p=app.Pointer!;c.Save();c.Alpha=.93;c.Die(dragged.Type,dragged.Pips,p.X,p.Y-18,60,-.06);c.Restore();}
    }
    private static void Dim(NativeCanvas c)=>c.Rect(0,0,432,864,"rgba(3,9,16,.82)");
    private static void Modal(NativeCanvas c,double x,double y,double w,double h)
    {c.Box(x,y+7,w,h,24,"rgba(0,0,0,.25)");c.GradientBox(x,y,w,h,24,[(0,"#1B2B3E"),(1,"#111D2E")],stroke:"#344C60");}
    private void Pause(NativeCanvas c)
    {
        Dim(c);Modal(c,30,201,372,478);c.Track("TAKE A BREATH",216,238,9,P.Mint,2);c.Text("战斗暂停",216,279,29,P.Text,750,"center");c.Text("进度已自动保存",216,312,12,P.Muted,500,"center");
        Button(c,"continue",57,345,318,51,"继续战斗","primary",16);Button(c,"sound",57,413,152,45,"音效  "+(app.Settings.Sound?"开":"关"),fontSize:12);
        Button(c,"music",223,413,152,45,"配乐  "+(app.Settings.Music?"开":"关"),fontSize:12);Button(c,"motion",57,474,318,43,"减弱闪光与震动  "+(app.Settings.ReduceMotion?"开":"关"),fontSize:12);
        Button(c,"home",57,546,318,46,"保存并返回主界面",fontSize:13);c.Text("按 Esc 也可以继续",216,636,10,P.Muted,500,"center");
    }
    private void Upgrade(NativeCanvas c)
    {
        Dim(c);c.Track("CHOOSE YOUR ECHO",216,157,10,P.Mint,2.8);c.Text("回响强化",216,202,34,P.Text,750,"center");c.Text("选择一项，本局所有同类骰子持续受益。",216,238,12,P.Muted,500,"center");
        for(int i=0;i<State.Offers.Count;i++)
        {
            string id=State.Offers[i];var u=D.UpgradeTypes[id];int y=278+i*127;Button(c,"upgrade:"+id,27,y,378,111,"",radius:17);
            c.GradientBox(28,y+1,375,109,16,[(0,u.Color+"13"),(1,u.Color+"00")],0,-1,107,109);c.Box(45,y+29,48,48,13,u.Color+"16",u.Color+"4A");
            c.Icon(u.Icon,69,y+53,26,u.Color);c.Text(u.Name,110,y+29,18,P.Text,700);c.Text(u.Tag,110,y+54,10,u.Color,600);c.Wrap(u.Description,110,y+78,268,11,P.Muted,17,2);c.Icon("plus",377,y+30,14,u.Color);
        }
        c.Text("战斗已暂停 · 选好再继续",216,698,11,P.Muted,500,"center");int owned=State.Upgrades.Values.Where(v=>v>0).Sum();if(owned>0)c.Text("已获得 "+owned+" 次强化",216,724,10,P.Gold,600,"center");
    }
    private void GameOver(NativeCanvas c)
    {
        Dim(c);Modal(c,27,161,378,534);c.Track("THE CORRIDOR REMEMBERS",216,200,9,P.Pink,1.9);c.Text("防线失守",216,244,33,P.Text,750,"center");
        c.Text("第 "+State.Wave+" 波",216,302,43,P.Gold,750,"center");c.Text(State.Wave>=app.Meta.BestWave?"这次回响，已记入你的最佳纪录。":"再调整一次火力，下一局走得更远。",216,350,11,P.Muted,500,"center");
        (string value,string label)[] cols=[(P.Compact(State.Score),"得分"),(State.Kills.ToString(),"击破"),(State.BestCombo.ToString(),"最高连锁")];
        for(int i=0;i<3;i++){c.Text(cols[i].value,92+i*124,420,23,P.Text,700,"center");c.Text(cols[i].label,92+i*124,450,11,P.Muted,500,"center");}
        Button(c,"restart",54,508,324,54,"再来一局","primary",17);Button(c,"home",54,580,324,45,"返回主界面",fontSize:13);
    }
    private void DieInfo(NativeCanvas c)
    {
        if(app.SelectedSlot<0 || State.Board[app.SelectedSlot] is not { } d)return;
        var type=D.Types[d.Type];var stats=S.Stats(d);var level=D.Game.Levels[d.Pips-1];Dim(c);Modal(c,28,139,376,604);
        Button(c,"closeDie",354,154,33,33,"",icon:"close",iconOnly:true,iconSize:15);c.Track(type.Tag,216,177,10,type.Color,2);c.Die(d.Type,d.Pips,216,255,92,-.06);c.Text(type.Name+"骰子 · "+d.Pips+" 点",216,329,25,P.Text,700,"center");
        (string value,string label)[] values=[(P.Compact(stats.Volley),"单次齐射伤害"),(N(stats.Reload,2)+"s","装填时间"),(d.Pips.ToString(),"每轮弹丸")];
        for(int i=0;i<3;i++){c.Text(values[i].value,94+i*122,390,24,type.Color,750,"center");c.Text(values[i].label,94+i*122,420,10,P.Muted,500,"center");}
        c.Wrap(type.Description,54,468,324,13,P.Text,22,3);c.Text(d.Pips<6?"同种同点合成 → 卡组内随机 "+(d.Pips+1)+" 点骰子":"已达六点上限，继续保留火力或回收。",216,553,11,P.Muted,550,"center");
        Button(c,"closeDie",54,585,324,48,"返回战斗","primary",15);Button(c,"recycle:"+app.SelectedSlot,54,651,324,43,"回收这颗骰子 · +"+level.Recycle+" 能量",fontSize:12,color:P.Gold);
        c.Text("回收只移除骰子，不清除已发射弹丸。",216,720,10,P.Muted,500,"center");
    }
    private void Deck(NativeCanvas c)
    {
        Button(c,"deckBack",26,30,40,38,"",icon:"back",iconOnly:true);c.Text("配置卡组",216,48,25,P.Text,750,"center");
        c.Text("携带 1–6 种骰子，召唤与合成均从中随机。",216,103,12,P.Muted,500,"center");c.Text("类型越少越容易配对；类型越多，战斗效果越丰富。",216,127,10,P.Muted,500,"center");
        for(int i=0;i<D.Dice.Length;i++)
        {
            var type=D.Dice[i];int x=26+i%2*194,y=162+i/2*192;bool selected=app.EditingDeck.Contains(type.Id);Button(c,"deck:"+type.Id,x,y,182,177,"",radius:17);
            c.Box(x,y,182,177,17,selected?type.Color+"0A":null,selected?type.Color+"9A":"#2A3D50");
            if(selected){c.Circle(x+160,y+20,9,type.Color);c.Icon("check",x+160,y+20,12,"#16382F");}else c.Circle(x+160,y+20,9,null,"#42596C");
            c.Die(type.Id,1,x+38,y+47,46,-.04);c.Text(type.Name,x+77,y+37,18,P.Text,700);c.Text(type.Tag,x+77,y+61,10,type.Color,600);c.Wrap(type.Description,x+16,y+102,150,11,P.Muted,18,4);
        }
        c.Text("当前每种出现概率："+(app.EditingDeck.Count>0?N(100.0/app.EditingDeck.Count,1)+"%":"请选择至少一种"),216,761,11,P.Muted,500,"center");
        Button(c,"deckSave",27,795,378,51,"保存卡组  "+app.EditingDeck.Count+" / 6","primary",15,disabled:app.EditingDeck.Count==0);
    }
    private void Help(NativeCanvas c)
    {
        Dim(c);Modal(c,24,99,384,671);Button(c,"closeHelp",355,116,33,33,"",icon:"close",iconOnly:true,iconSize:14);c.Text("一分钟，学会回响",216,159,25,P.Text,700,"center");
        (string icon,string title,string body)[] rows=[
            ("aim","按住战场，移动瞄准","松手让所有已装填的骰子齐射。按住时不会自动开火；移到骰子区或按右键可取消。"),
            ("merge","同种类、同点数才可合成","把两颗相同骰子拖到一起，随机得到卡组内更高一点的骰子，并立刻打出强化齐射。"),
            ("reload","数量与威力，需要取舍","保留更多骰子，火力更密；合成后单次齐射更强、装填稍慢，同时腾出一个格子。"),
            ("energy","满格也不会卡死","阵地最多八格。单击骰子可看数值或回收。击破敌人与自然恢复都会补充召唤能量。")];
        for(int i=0;i<rows.Length;i++){int y=225+i*111;c.Icon(rows[i].icon,55,y+2,24,i%2==1?P.Gold:P.Mint);c.Text(rows[i].title,82,y,15,P.Text,650);c.Wrap(rows[i].body,82,y+29,293,12,P.Muted,20,3);}
        Button(c,"closeHelp",51,698,330,48,"开始回响","primary",15);
    }
    private void ConfirmNew(NativeCanvas c)
    {
        Dim(c);Modal(c,30,273,372,294);c.Text("开始新的回响？",216,324,25,P.Text,700,"center");c.Text("当前保存的战斗将被替换，最佳纪录保留。",216,369,11,P.Muted,500,"center");
        Button(c,"confirmStart",57,415,318,47,"开始新的一局","primary");Button(c,"cancelNew",57,481,318,42,"保留进度");
    }
}
