using System.Globalization;
using DiceGame.Core;

namespace DiceGame.Presentation;

public static class Palette
{
    public const string Bg="#0A1220", Panel="#121F30", Line="#26364A", Muted="#8394AC", Text="#EDF5F7",
        Mint="#72EAC8", Gold="#F8DE87", Orange="#FFAD76", Pink="#FF97B8";
    public static string Compact(double n)
    {
        if(n>=1e9) return MathEx.JsFixed(n/1e9,1)+"B";
        if(n>=1e6) return MathEx.JsFixed(n/1e6,1)+"M";
        if(n>=1e4) return MathEx.JsFixed(n/1e3,1)+"k";
        return Math.Ceiling(n).ToString(CultureInfo.InvariantCulture);
    }
}
public sealed class Particle
{
    public double X,Y,Vx,Vy,Life,Max,Size,Angle;public string Color="";public int Shape;
}
public sealed class Ring {public double X,Y,Radius,Life,Max;public string Color="";}
public sealed class Floater {public string Label="",Color="";public double X,Y,Size,Life,Max;}
public sealed class ElectricArc {public List<PointD> Points=[];public string Color="";public double Life,Max;}
public sealed class Conduit {public int Slot;public string Color="";public double Life,Max;public bool Surge;}
public sealed class DiePulse {public int Slot,Pips;public string Color="";public double Life,Max;public bool Merge;}
public sealed class WaveBanner {public string Label="",Sub="",Color="";public double Life,Max;}

/// <summary>Same event-to-effect mapping and seeded particle equations as renderer.js. No engine dependency.</summary>
public sealed class EffectSystem(GameData data)
{
    public readonly List<Particle> Particles=[];
    public readonly List<Ring> Rings=[];
    public readonly List<Floater> Floaters=[];
    public readonly List<ElectricArc> Arcs=[];
    public readonly List<Conduit> Conduits=[];
    public readonly List<DiePulse> Pulses=[];
    public WaveBanner? Banner;
    public double T,FxTime,Shake,Flash;
    public bool ReduceMotion;
    public readonly SeededRandom Random=new(924153);
    private readonly Dictionary<string,double> _hitGate=[];
    public void Clear()
    {
        Particles.Clear();Rings.Clear();Floaters.Clear();Arcs.Clear();Conduits.Clear();Pulses.Clear();Banner=null;Shake=0;Flash=0;
    }
    public void AddParticle(double x,double y,string color,int count=10,double power=100)
    {
        count=(int)Math.Floor(count*(ReduceMotion?0.45:1));
        for(int i=0;i<count;i++)
        {
            if(Particles.Count>=data.Game.Limits.Particles) Particles.RemoveAt(0);
            double angle=Random.Next()*Math.PI*2,speed=(0.35+Random.Next()*0.8)*power,life=0.3+Random.Next()*0.38;
            Particles.Add(new Particle {X=x,Y=y,Vx=Math.Cos(angle)*speed,Vy=Math.Sin(angle)*speed,Life=life,Max=life,
                Color=color,Size=1.8+Random.Next()*3,Angle=angle,Shape=i%3});
        }
    }
    public void AddRing(double x,double y,double radius,string color,double life=0.4)
    {
        if(Rings.Count>=data.Game.Limits.Rings) Rings.RemoveAt(0);
        Rings.Add(new Ring {X=x,Y=y,Radius=radius,Color=color,Life=life,Max=life});
    }
    public void AddFloater(string label,double x,double y,string color,double size=14,double life=0.7)
    {
        if(Floaters.Count>=data.Game.Limits.Floaters) Floaters.RemoveAt(0);
        Floaters.Add(new Floater {Label=label,X=x,Y=y,Color=color,Size=size,Life=life,Max=life});
    }
    public void OnEvent(CombatEvent e)
    {
        switch(e.Type)
        {
            case "hit":
                AddParticle(e.X,e.Y,e.Color,3,48);
                string key=Math.Floor(e.X/16)+":"+Math.Floor(e.Y/16);
                double previous=_hitGate.GetValueOrDefault(key,-100);if(previous==0) previous=-100;
                if(FxTime-previous>0.11)
                {
                    _hitGate[key]=FxTime;
                    AddFloater(Palette.Compact(e.Amount),e.X+(Random.Next()-0.5)*15,e.Y-12,e.Color,e.Amount>50?16:12,0.5);
                }
                break;
            case "kill":
                AddParticle(e.X,e.Y,e.Color,e.Kind=="boss"?38:14,e.Kind=="boss"?230:115);AddRing(e.X,e.Y,e.W*0.85,e.Color,0.34);
                if(e.Combo%3==0) AddFloater("+"+e.Reward+" 能量",e.X,e.Y+10,Palette.Gold,11,0.9);
                if(e.Combo>=5) Shake=Math.Max(Shake,1);
                if(e.Kind=="boss") {Shake=4;Banner=new WaveBanner {Label="首领击破",Sub="BOSS ELIMINATED",Life=2,Max=2,Color=Palette.Gold};}
                break;
            case "explosion":
                AddRing(e.X,e.Y,e.Radius,e.Color,0.38);AddRing(e.X,e.Y,e.Radius*0.6,"#FFF7E9",0.23);
                AddParticle(e.X,e.Y,e.Color,e.Volatile?20:9,e.Radius*2.4);Shake=Math.Max(Shake,e.Volatile?3:1.3);break;
            case "arc":
                if(Arcs.Count>=data.Game.Limits.Arcs) Arcs.RemoveAt(0);
                var points=new List<PointD>{new(e.X,e.Y)};
                for(int i=1;i<5;i++) points.Add(new PointD(MathEx.Lerp(e.X,e.Tx,i/5.0)+(Random.Next()-0.5)*16,MathEx.Lerp(e.Y,e.Ty,i/5.0)+(Random.Next()-0.5)*16));
                points.Add(new PointD(e.Tx,e.Ty));Arcs.Add(new ElectricArc {Points=points,Color=e.Color,Life=0.19,Max=0.19});break;
            case "wall": AddParticle(e.X,e.Y,e.Color,e.Boost?6:2,55);AddRing(e.X,e.Y,e.Boost?20:11,e.Color,0.19);break;
            case "split": AddRing(e.X,e.Y,26,e.Color,0.25);AddParticle(e.X,e.Y,e.Color,8,85);break;
            case "launch": AddRing(e.X,e.Y,e.Surge?19:11,e.Color,0.12);break;
            case "conduit": Conduits.Add(new Conduit {Slot=e.Slot,Color=e.Color,Life=0.30,Max=0.30,Surge=e.Surge});break;
            case "summon": Pulses.Add(new DiePulse {Slot=e.Slot,Color=data.Types[e.Die!.Type].Color,Life=0.65,Max=0.65});break;
            case "merge":
                Pulses.Add(new DiePulse {Slot=e.B,Color=data.Types[e.Die!.Type].Color,Life=0.90,Max=0.90,Merge=true,Pips=e.Die.Pips});Shake=2.8;break;
            case "breach": AddRing(e.X,e.Y,62,Palette.Pink,0.5);AddParticle(e.X,e.Y,Palette.Pink,18,150);Flash=0.18;Shake=4;break;
            case "wave":
                double duration=e.Boss?1.9:1.25;
                Banner=new WaveBanner {Label=e.Boss?"首领压境":"第 "+e.Wave.ToString("D2")+" 波",Sub=e.Boss?"BREAK THE WARDEN":"ENDLESS CORRIDOR",Life=duration,Max=duration,Color=e.Boss?Palette.Pink:Palette.Mint};break;
            case "clear": Banner=new WaveBanner {Label="漂亮，清场。",Sub="+"+e.Reward+" 能量  /  下一波准备",Life=1.2,Max=1.2,Color=Palette.Gold};break;
            case "upgraded": Flash=0.07;break;
        }
    }
    public void Update(double dt,bool active=true)
    {
        T+=dt;if(!active) return;FxTime+=dt;
        foreach(var p in Particles)
        {
            p.Life-=dt;p.X+=p.Vx*dt;p.Y+=p.Vy*dt;p.Vx*=Math.Exp(-dt*2.3);p.Vy+=80*dt;p.Angle+=dt*3;
        }
        Particles.RemoveAll(p=>p.Life<=0);
        foreach(var x in Rings)x.Life-=dt;foreach(var x in Floaters)x.Life-=dt;foreach(var x in Arcs)x.Life-=dt;
        foreach(var x in Conduits)x.Life-=dt;foreach(var x in Pulses)x.Life-=dt;
        Rings.RemoveAll(x=>x.Life<=0);Floaters.RemoveAll(x=>x.Life<=0);Arcs.RemoveAll(x=>x.Life<=0);Conduits.RemoveAll(x=>x.Life<=0);Pulses.RemoveAll(x=>x.Life<=0);
        if(Banner is not null) {Banner.Life-=dt;if(Banner.Life<=0) Banner=null;}
        Shake=Math.Max(0,Shake-dt*12);Flash=Math.Max(0,Flash-dt);if(_hitGate.Count>200) _hitGate.Clear();
    }
}
