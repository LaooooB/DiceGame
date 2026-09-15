using DiceGame.Core;

namespace DiceGame.Presentation;

public sealed partial class NativeRenderer
{
    private void DrawExpansionFields(NativeCanvas c)
    {
        var a=D.Game.Arena;c.Save();
        foreach(var f in State.Expansion.Fields)
        {
            c.Circle(f.X,f.Y,f.Radius,"#12101E88",f.Color+"66",1);
            c.Circle(f.X,f.Y,f.Radius*.45,"#080A14AA",f.Color+"99",1.2);
            double angle=F.T*1.4;
            for(int i=0;i<4;i++)
            {
                double t=angle+i*Math.PI/2;
                c.Line(f.X+Math.Cos(t)*f.Radius*.7,f.Y+Math.Sin(t)*f.Radius*.7,f.X+Math.Cos(t+.45)*f.Radius*.3,f.Y+Math.Sin(t+.45)*f.Radius*.3,f.Color+"66",1.4);
            }
        }
        foreach(var r in State.Expansion.Rifts)
        {
            double start=r.Position-r.Width,end=r.Position+r.Width;
            if(r.Wall==2)
            {c.Line(Math.Max(a.Left,start),a.Top+2,Math.Min(a.Right,end),a.Top+2,r.Color,4,true);c.Line(Math.Max(a.Left,start),a.Bottom-2,Math.Min(a.Right,end),a.Bottom-2,r.Color+"66",2);}
            else
            {
                double x=r.Wall==0?a.Left+2:a.Right-2,exit=r.Wall==0?a.Right-2:a.Left+2;
                c.Line(x,Math.Max(a.Top,start),x,Math.Min(a.Bottom,end),r.Color,4,true);c.Line(exit,Math.Max(a.Top,start),exit,Math.Min(a.Bottom,end),r.Color+"66",2);
            }
        }
        c.Restore();
    }
    private void DrawExpansionMarkers(NativeCanvas c,EnemyState e)
    {
        if(e.Dead)return;var badges=new List<(string Text,string Color)>();
        if(e.Conditions.TryGetValue("sunder",out var armor) && armor.Until>State.Time)badges.Add(("破"+armor.Stacks,"#AFDAE9"));
        if(e.Conditions.TryGetValue("mass",out var mass) && mass.Until>State.Time)badges.Add(("重"+mass.Stacks,"#B2A8E5"));
        if(e.Souls.Any(s=>s.Value>State.Time))badges.Add(("魂","#C0ABEC"));
        if(e.Debt is not null)badges.Add(("因","#FFB2CF"));
        for(int i=0;i<badges.Count;i++)c.Text(badges[i].Text,e.X-e.W/2+i*16,e.Y+e.H/2+9,8,badges[i].Color,600);
    }
    private void DrawExpansionProjectile(NativeCanvas c,ProjectileState p)
    {
        string color=p.Stats.Color;
        if(p.Stats.Auxiliary==AuxiliaryKind.Swarm)
        {c.Line(p.X-5,p.Y-3,p.X+5,p.Y+3,color,1);c.Line(p.X-5,p.Y+3,p.X+5,p.Y-3,color,1);}
        if(p.Stats.Auxiliary==AuxiliaryKind.Soul)c.Circle(p.X,p.Y,5,null,color,1);
        if(p.PhaseCount>0 || p.RiftUsed)c.Circle(p.X,p.Y,6,null,color+"88",1);
    }
    private void DrawExpansionShield(NativeCanvas c)
    {
        int shield=State.Expansion.Shield;if(shield<=0)return;
        c.Line(36,514,396,514,"#8FCFEE",2.5);
        for(int i=0;i<shield;i++)c.Box(190+i*10,503,7,7,2,"#8FCFEE");
    }
}
