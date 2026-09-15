using System.Globalization;
using System.Numerics;

namespace DiceGame.Core;

public static class MathEx
{
    public static double Clamp(double x, double a, double b) => Math.Max(a, Math.Min(b, x));
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Dist2(double ax, double ay, double bx, double by) => (ax-bx)*(ax-bx)+(ay-by)*(ay-by);
    public static bool Finite(double v, double lo = double.NegativeInfinity, double hi = double.PositiveInfinity) => double.IsFinite(v) && v >= lo && v <= hi;
    // JS Math.round is not C#'s default banker's rounding; all gameplay rounded quantities are nonnegative.
    public static double JsRound(double v)
    {
        if(!double.IsFinite(v))return v;
        double lower=Math.Floor(v);return v-lower<0.5?lower:lower+1;
    }
    /// <summary>ECMAScript toFixed rounding on the exact IEEE754 value, rather than .NET midpoint-to-even formatting.</summary>
    public static string JsFixed(double value,int places)
    {
        if(!double.IsFinite(value) || places<0 || places>20)throw new ArgumentOutOfRangeException(nameof(value));
        bool negative=value<0;ulong bits=(ulong)BitConverter.DoubleToInt64Bits(Math.Abs(value));
        int exponent=(int)((bits>>52)&0x7FF);ulong mantissa=bits&0xFFFFFFFFFFFFFUL;
        if(exponent!=0)mantissa|=1UL<<52;
        int power=exponent==0?-1074:exponent-1023-52;
        BigInteger numerator=new BigInteger(mantissa)*BigInteger.Pow(10,places),denominator=BigInteger.One;
        if(power>=0)numerator<<=power;else denominator<<=-power;
        BigInteger rounded=BigInteger.DivRem(numerator,denominator,out var remainder);if(remainder*2>=denominator)rounded++;
        string digits=rounded.ToString(CultureInfo.InvariantCulture).PadLeft(places+1,'0');
        if(places>0)digits=digits.Insert(digits.Length-places,".");return (negative?"-":"")+digits;
    }
}
public sealed class SeededRandom
{
    public uint State { get; set; }
    public SeededRandom(uint seed) { State = seed == 0 ? 0x91a45be3u : seed; }
    public double Next()
    {
        uint x = State;
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        State = x;
        return x / 4294967296.0;
    }
    public int Int(int n) => (int)Math.Floor(Next() * n);
    public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];
    public List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var a = items.ToList();
        for (int i = a.Count - 1; i > 0; i--) { int j = Int(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }
}
public readonly record struct BoundsD(double Left, double Right, double Top, double Bottom);
public sealed class SweepHit
{
    public double T, Nx, Ny, Penetration;
    public bool Wall;
    public EnemyState? Enemy;
}
public static class Collision
{
    /// <summary>Continuous point vs expanded box. Preserves source tie-breaking and penetration handling.</summary>
    public static SweepHit? SweepAabb(double x, double y, double dx, double dy, BoundsD b)
    {
        if (x > b.Left && x < b.Right && y > b.Top && y < b.Bottom)
        {
            (double d, double nx, double ny)[] edges = [(x-b.Left,-1,0),(b.Right-x,1,0),(y-b.Top,0,-1),(b.Bottom-y,0,1)];
            var q = edges.OrderBy(e => e.d).First();
            return new SweepHit { T=0, Nx=q.nx, Ny=q.ny, Penetration=q.d };
        }
        double nearX=double.NegativeInfinity, farX=double.PositiveInfinity, nearY=double.NegativeInfinity, farY=double.PositiveInfinity;
        if (Math.Abs(dx)<1e-10) { if(x<b.Left || x>b.Right) return null; }
        else { nearX=(b.Left-x)/dx; farX=(b.Right-x)/dx; if(nearX>farX) (nearX,farX)=(farX,nearX); }
        if (Math.Abs(dy)<1e-10) { if(y<b.Top || y>b.Bottom) return null; }
        else { nearY=(b.Top-y)/dy; farY=(b.Bottom-y)/dy; if(nearY>farY) (nearY,farY)=(farY,nearY); }
        double t=Math.Max(nearX,nearY), far=Math.Min(farX,farY);
        if(t<0 || t>1 || t>far || far<0) return null;
        return nearX>nearY ? new SweepHit {T=t,Nx=dx>0?-1:1} : new SweepHit {T=t,Ny=dy>0?-1:1};
    }
}
public sealed class SpatialGrid(double cellSize=58)
{
    private readonly Dictionary<(int x,int y),List<EnemyState>> _bins = [];
    private readonly double _size=cellSize;
    public void Rebuild(IEnumerable<EnemyState> enemies)
    {
        _bins.Clear();
        foreach(var e in enemies)
        {
            if(e.Dead) continue;
            int x0=(int)Math.Floor((e.X-e.W/2)/_size), x1=(int)Math.Floor((e.X+e.W/2)/_size);
            int y0=(int)Math.Floor((e.Y-e.H/2)/_size), y1=(int)Math.Floor((e.Y+e.H/2)/_size);
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                if(!_bins.TryGetValue((x,y),out var bin)) _bins[(x,y)]=bin=[];
                bin.Add(e);
            }
        }
    }
    public List<EnemyState> Query(double x0,double y0,double x1,double y1)
    {
        var result=new List<EnemyState>(); var seen=new HashSet<long>();
        for(int y=(int)Math.Floor(y0/_size);y<=(int)Math.Floor(y1/_size);y++)
        for(int x=(int)Math.Floor(x0/_size);x<=(int)Math.Floor(x1/_size);x++)
        {
            if(!_bins.TryGetValue((x,y),out var bin)) continue;
            foreach(var e in bin) if(!e.Dead && seen.Add(e.Id)) result.Add(e);
        }
        return result;
    }
}
