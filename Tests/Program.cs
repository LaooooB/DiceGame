using System.Text.Json;
using DiceGame.App;
using DiceGame.Core;
using DiceGame.Presentation;

namespace DiceGame.Tests;

internal static class Program
{
    private static readonly string Root=AppContext.BaseDirectory;
    private static readonly GameData Data=GameData.FromDirectory(Path.Combine(Root,"LegacyBalanceData"));
    private static readonly List<object> Results=[];
    private static int _passed,_failed;
    private static void Main()
    {
        Run("format: ECMAScript rounding and compact UI labels",()=>
        {
            Assert(MathEx.JsRound(.49999999999999994)==0,"Round just below half");
            Assert(MathEx.JsRound(.5)==1 && MathEx.JsRound(2.5)==3,"JS rounding is not banker's rounding");
            Assert(MathEx.JsFixed(1.25,1)=="1.3","Exact midpoint toFixed");
            Assert(MathEx.JsFixed(2.55,1)=="2.5","Binary floating-point toFixed");
            Assert(Palette.Compact(10250)=="10.3k","Compact formatting drift");
        });
        Run("reference: 17 simulation replays / 179 checkpoints",SimulationGolden);
        Run("reference: 72 dice stat profiles",StatsGolden);
        Run("reference: 4 x 64 xorshift32 samples",RandomGolden);
        Run("reference: swept collision edge cases",CollisionGolden);
        Run("reference: aiming preview trajectories",TraceGolden);
        Run("reference: full particle / arc / ring event lifecycle",EffectsGolden);
        Run("reference: 84 original float32 sound profiles",AudioGolden);
        Run("input: hold never auto-fires; release fires once",()=>
        {
            var a=NewApp();a.OnDown(240,250);for(int i=0;i<120;i++)a.Tick(1.0/120);Assert(a.Sim!.State.ManualVolleys==0,"Auto-fire during hold");
            a.OnUp(240,250);Assert(a.Sim.State.ManualVolleys==1,"Missing release volley");a.OnUp(240,250);Assert(a.Sim.State.ManualVolleys==1,"Double release");
        });
        Run("input: drag below battlefield cancels volley",()=>{var a=NewApp();a.OnDown(230,240);a.OnMove(230,650);a.OnUp(230,650);Assert(a.Sim!.State.ManualVolleys==0,"Cancelled shot fired");});
        Run("input: release outside canvas cancels volley",()=>{var a=NewApp();a.OnDown(230,240);a.OnUp(600,240);Assert(a.Sim!.State.ManualVolleys==0,"Off-canvas shot fired");});
        Run("input: focus loss cancels and pauses",()=>
        {var a=NewApp();a.OnDown(230,240);a.OnFocusLost();a.OnUp(230,240);Assert(a.Scene=="paused" && a.Pointer is null && a.Sim!.State.ManualVolleys==0,"Focus loss not safe");double time=a.Sim!.State.Time;a.Tick(.1);Assert(a.Sim.State.Time==time,"Paused simulation advanced");});
        Run("input: same-type same-pip drag merges and surges",()=>
        {var a=NewApp();a.OnDown(75,638);a.OnMove(169,638);a.OnUp(169,638);Assert(a.Sim!.Count==2 && a.Sim.State.Board[1]!.Pips==2 && a.Sim.State.PendingShots.Count==2,"Merge not atomic");});
        Run("input: different types reject merging",()=>
        {var a=NewApp();a.OnDown(75,638);a.OnMove(263,638);a.OnUp(263,638);Assert(a.Sim!.Count==3 && a.Sim.State.Merges==0,"Different types merged");});
        Run("input: click opens die panel; recycling frees slot",()=>
        {var a=NewApp();double energy=a.Sim!.State.Energy;a.OnDown(75,638);a.OnUp(75,638);Assert(a.Scene=="die","Missing die panel");a.Action("recycle:0");Assert(a.Sim.Count==2 && a.Sim.State.Energy==energy+8 && a.Scene=="play","Recycle failed");});
        Run("input: empty slots summon into selected slot",()=>
        {var a=NewApp();a.OnDown(357,731);a.OnUp(357,731);Assert(a.Sim!.State.Board[7] is not null && a.Sim.Count==4,"Summon destination changed");});
        Run("input: empty deck cannot save",()=>
        {var a=NewApp();a.Action("home");a.Action("editDeck");foreach(var type in Data.Dice)a.Action("deck:"+type.Id);a.Action("deckSave");Assert(a.Scene=="deck" && a.Deck.Count==6,"Empty deck saved");});
        Run("rules: full board rejects summon without cost",()=>
        {var s=new Simulation(Data,seed:2);s.State.Energy=1000;while(s.Count<8)s.Summon();double energy=s.State.Energy;Assert(!s.Summon().Ok && s.State.Energy==energy,"Full summon consumed energy");});
        Run("rules: six-pip pair cannot merge",()=>
        {var s=new Simulation(Data,seed:2);s.State.Board[0]!.Pips=6;s.State.Board[1]!.Pips=6;Assert(!s.CanMerge(0,1) && !s.Merge(0,1).Ok,"Pips overflow");});
        Run("rules: source snapshots survive recycle and upgrade",()=>
        {var s=new Simulation(Data,seed:2);s.Fire(-1.5);double damage=s.State.PendingShots[0].Snapshot.Stats.Damage;s.Recycle(0);s.State.Upgrades["power"]=9;Assert(s.State.PendingShots[0].Snapshot.Stats.Damage==damage,"Snapshot mutated");});
        Run("rules: no retroactive mutation of exported saves",()=>
        {var s=new Simulation(Data,seed:2);var saved=s.ExportSave();double hp=saved.Enemies[0].Hp;s.State.Enemies[0].Hp=1;s.State.Board[0]!.Pips=4;Assert(saved.Enemies[0].Hp==hp && saved.Board[0]!.Pips==1,"Save shared mutable objects");});
        Run("rules: invalid dt rejected",()=>
        {var s=new Simulation(Data);foreach(double dt in new[]{double.NaN,double.PositiveInfinity,-.01,1.0})Throws(()=>s.Step(dt));});
        Run("save: encoded run resumes deterministically",()=>
        {var s=new Simulation(Data,seed:114);s.Fire(-1.15);for(int i=0;i<50;i++)s.Step(1.0/120);var encoded=SaveCodec.Encode(new SaveEnvelope {Deck=s.State.Deck,Run=s.ExportSave()});var restored=Simulation.Restore(Data,SaveCodec.Decode(Data,encoded).Run!);for(int i=0;i<180;i++){s.Step(1.0/120);restored.Step(1.0/120);}Compare(J(s.ExportSave()),J(restored.ExportSave()),"restore");});
        Run("save: unsupported schema is rejected",()=>{var s=new Simulation(Data).ExportSave();s.Schema=99;Throws(()=>Simulation.Restore(Data,s));});
        Run("save: oversized / malformed payload is rejected",()=>{Throws(()=>SaveCodec.Decode(Data,"{"));Throws(()=>SaveCodec.Decode(Data,new string('x',SaveCodec.MaxBytes+1)));});
        Run("effects: paused updates retain particles, advance menu clock only",()=>
        {var f=new EffectSystem(Data);f.OnEvent(new CombatEvent {Type="hit",X=100,Y=200,Color="#72EAC8",Amount=20});double x=f.Particles[0].X,life=f.Particles[0].Life;f.Update(.1,false);Assert(f.T==.1 && f.FxTime==0 && f.Particles[0].X==x && f.Particles[0].Life==life,"Paused effects moved");});
        Run("effects: particles are bounded",()=>
        {var f=new EffectSystem(Data);for(int i=0;i<100;i++)f.AddParticle(1,2,"#72EAC8",40);Assert(f.Particles.Count==Data.Game.Limits.Particles,"Particle cap not enforced");});
        string result=JsonSerializer.Serialize(new {passed=_passed,failed=_failed,engine="Pure .NET 8 core; this does not execute Godot rendering",results=Results},new JsonSerializerOptions {WriteIndented=true});
        string path=Path.Combine(Directory.GetCurrentDirectory(),"core-test-results.json");File.WriteAllText(path,result);Console.WriteLine($"\n{_passed} passed, {_failed} failed. {path}");Environment.ExitCode=_failed>0?1:0;
    }
    private static void Run(string name,Action test)
    {
        try{test();_passed++;Results.Add(new{name,status="passed"});Console.WriteLine("PASS "+name);}
        catch(Exception e){_failed++;Results.Add(new{name,status="failed",error=e.ToString()});Console.Error.WriteLine("FAIL "+name+"\n"+e);}
    }
    private static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Throws(Action test){bool threw=false;try{test();}catch(Exception){threw=true;}Assert(threw,"Expected an exception");}
    private static JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"Fixtures",name)));
    private static JsonElement J(object? value)=>JsonSerializer.SerializeToElement(value,GameData.JsonOptions);
    // Expected-only properties deliberately allow explicitly serialized C# defaults absent in the original JS objects.
    private static void Compare(JsonElement expected,JsonElement actual,string path,double absolute=1e-8,double relative=1e-10)
    {
        if(expected.ValueKind==JsonValueKind.Number)
        {
            Assert(actual.ValueKind==JsonValueKind.Number,path+": expected number");double a=expected.GetDouble(),b=actual.GetDouble();
            Assert(double.IsFinite(b) && Math.Abs(a-b)<=absolute+relative*Math.Abs(a),$"{path}: expected {a:R}, actual {b:R}");return;
        }
        Assert(expected.ValueKind==actual.ValueKind,$"{path}: kind {expected.ValueKind} != {actual.ValueKind}");
        switch(expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach(var prop in expected.EnumerateObject()){Assert(actual.TryGetProperty(prop.Name,out var value),path+" missing "+prop.Name);Compare(prop.Value,value,path+"."+prop.Name,absolute,relative);}break;
            case JsonValueKind.Array:
                Assert(expected.GetArrayLength()==actual.GetArrayLength(),$"{path}: length {expected.GetArrayLength()} != {actual.GetArrayLength()}");
                for(int i=0;i<expected.GetArrayLength();i++)Compare(expected[i],actual[i],path+"["+i+"]",absolute,relative);break;
            case JsonValueKind.String:Assert(expected.GetString()==actual.GetString(),path+": string differs");break;
            case JsonValueKind.True:case JsonValueKind.False:Assert(expected.GetBoolean()==actual.GetBoolean(),path+": boolean differs");break;
        }
    }
    private static void SimulationGolden()
    {
        using var file=Read("simulation_cases.json");
        foreach(var c in file.RootElement.EnumerateArray())
        {
            string name=c.GetProperty("name").GetString()!;var deck=c.GetProperty("deck").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var sim=c.GetProperty("initial").ValueKind==JsonValueKind.Null?new Simulation(Data,deck,c.GetProperty("seed").GetUInt32()):Simulation.Restore(Data,JsonSerializer.Deserialize<RunState>(c.GetProperty("initial").GetRawText(),GameData.JsonOptions)!);
            Compare(c.GetProperty("expectedInitial"),J(sim.ExportSave()),name+".initial");sim.TakeEvents();int index=0;
            foreach(var step in c.GetProperty("steps").EnumerateArray())
            {
                string op=step.GetProperty("op").GetString()!;var args=step.GetProperty("args");object? result=null;
                switch(op)
                {
                    case "step":for(int i=0;i<args[0].GetInt32();i++)sim.Step(args.GetArrayLength()>1?args[1].GetDouble():1.0/120);break;
                    case "restore":sim=Simulation.Restore(Data,sim.ExportSave());break;
                    case "fire":result=sim.Fire(args[0].GetDouble());break;
                    case "merge":result=sim.Merge(args[0].GetInt32(),args[1].GetInt32());break;
                    case "move":result=sim.Move(args[0].GetInt32(),args[1].GetInt32());break;
                    case "summon":result=sim.Summon();break;
                    case "recycle":result=sim.Recycle(args[0].GetInt32());break;
                    case "offerUpgrades":sim.OfferUpgrades();break;
                    case "chooseUpgrade":result=sim.ChooseUpgrade(args[0].GetString()!);break;
                    default:throw new InvalidDataException("Unknown fixture operation "+op);
                }
                string label=name+".step["+(index++)+"]("+op+")";Compare(step.GetProperty("result"),J(result),label+".result");Compare(step.GetProperty("events"),J(sim.TakeEvents()),label+".events");Compare(step.GetProperty("expected"),J(sim.ExportSave()),label+".state");
            }
        }
    }
    private static void StatsGolden()
    {
        using var file=Read("stats.json");foreach(var c in file.RootElement.EnumerateArray())
        {var sim=new Simulation(Data,seed:9137);sim.State.Upgrades=JsonSerializer.Deserialize<Dictionary<string,int>>(c.GetProperty("upgrades").GetRawText())!;var die=new DieState {Type=c.GetProperty("type").GetString()!,Pips=c.GetProperty("pips").GetInt32()};Compare(c.GetProperty("expected"),J(sim.Stats(die)),die.Type+die.Pips);}
    }
    private static void RandomGolden()
    {
        using var file=Read("rng.json");foreach(var c in file.RootElement.EnumerateArray())
        {var rng=new SeededRandom(c.GetProperty("seed").GetUInt32());foreach(var n in c.GetProperty("values").EnumerateArray())Assert(rng.Next()==n.GetDouble(),"RNG sequence differs");Assert(rng.State==c.GetProperty("state").GetUInt32(),"RNG end state differs");}
    }
    private static void CollisionGolden()
    {
        using var file=Read("collisions.json");foreach(var c in file.RootElement.EnumerateArray())
        {var b=c.GetProperty("box");var box=new BoundsD(b.GetProperty("left").GetDouble(),b.GetProperty("right").GetDouble(),b.GetProperty("top").GetDouble(),b.GetProperty("bottom").GetDouble());var hit=Collision.SweepAabb(c.GetProperty("x").GetDouble(),c.GetProperty("y").GetDouble(),c.GetProperty("dx").GetDouble(),c.GetProperty("dy").GetDouble(),box);Compare(c.GetProperty("expected"),J(hit),"sweep");}
    }
    private static void TraceGolden()
    {using var file=Read("traces.json");foreach(var c in file.RootElement.EnumerateArray()){var sim=new Simulation(Data,seed:24137);Compare(c.GetProperty("expected"),J(sim.TraceAim(c.GetProperty("angle").GetDouble())),"trace");}}
    private static object FxState(EffectSystem f)=>new {t=f.T,fxTime=f.FxTime,shake=f.Shake,flash=f.Flash,particles=f.Particles,rings=f.Rings,floaters=f.Floaters,arcs=f.Arcs,conduits=f.Conduits,pulses=f.Pulses,banner=f.Banner,rng=f.Random.State};
    private static void EffectsGolden()
    {
        using var file=Read("effects.json");foreach(var c in file.RootElement.EnumerateArray())
        {
            var f=new EffectSystem(Data) {ReduceMotion=c.GetProperty("reduced").GetBoolean()};int index=0;
            foreach(var s in c.GetProperty("steps").EnumerateArray())
            {if(s.TryGetProperty("event",out var e))f.OnEvent(JsonSerializer.Deserialize<CombatEvent>(e.GetRawText(),GameData.JsonOptions)!);else f.Update(s.GetProperty("dt").GetDouble(),s.GetProperty("active").GetBoolean());Compare(s.GetProperty("expected"),J(FxState(f)),"fx["+(index++)+"]");}
        }
    }
    private static void AudioGolden()
    {
        using var file=Read("audio_index.json");byte[] raw=File.ReadAllBytes(Path.Combine(Root,"Fixtures","audio_reference.f32"));
        foreach(var c in file.RootElement.EnumerateArray())
        {
            string kind=c.GetProperty("kind").GetString()!;int variant=c.GetProperty("variant").GetInt32(),offset=c.GetProperty("offset").GetInt32();var samples=SoundSynth.Synthesize(kind,variant,c.GetProperty("sampleRate").GetInt32());Assert(samples.Length==c.GetProperty("count").GetInt32(),"Audio duration differs");
            double error=0;for(int i=0;i<samples.Length;i++){float expected=BitConverter.ToSingle(raw,(offset+i)*4);error=Math.Max(error,Math.Abs(samples[i]-expected));}
            Assert(error<=2e-7,$"{kind}:{variant} sample error {error:R}");
        }
    }
    private static GameApp NewApp(){var a=new GameApp(Data,new MemoryStorage(),new SilentAudio());a.StartNew(123);return a;}
    private sealed class MemoryStorage : IDesktopStorage
    {private string? _save;public string? Read()=>_save;public void Write(string json)=>_save=json;public uint NewSeed()=>123;}
    private sealed class SilentAudio : ISoundOutput
    {public bool Enabled{get;set;}public bool Music{get;set;}public void Unlock(){}public void Play(string kind,int variant=0){}public void Tick(double dt,bool active){}public void Suspend(){}}
}
