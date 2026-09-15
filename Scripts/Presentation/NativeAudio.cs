using Godot;
using DiceGame.App;

namespace DiceGame.Presentation;

/// <summary>Native float32 audio, no browser context, no compressed replacement samples.</summary>
public partial class NativeAudio : AudioStreamPlayer, ISoundOutput
{
    private sealed class Voice(float[] samples,float gain,bool music)
    {public float[] Samples=samples;public int Cursor;public float Gain=gain;public bool IsMusic=music;}
    private readonly Dictionary<(string,int),float[]> _cache=[];
    private readonly Dictionary<string,double> _last=[];
    private readonly List<Voice> _voices=[];
    private static readonly int[] MusicSequence=[0,2,1,4,0,3,2,5,1,4,2,6,0,2,3,4];
    private AudioStreamGeneratorPlayback? _generator;
    private bool _enabled=true,_unlocked,_suspended;
    private double _musicClock;private int _musicStep;
    public bool Enabled {get=>_enabled;set{_enabled=value;VolumeDb=value?0:-80;}}
    public bool Music {get;set;}
    public double MasterGain {get;set;}=1;
    public double SfxGain {get;set;}=1;
    public double MusicGain {get;set;}=1;
    public override void _Ready()
    {
        Stream=new AudioStreamGenerator {MixRate=22050,BufferLength=.06f};
        VolumeDb=_enabled?0:-80;Bus="Master";
    }
    public void Unlock()
    {
        _unlocked=true;_suspended=false;StreamPaused=false;
        if(!Playing){_generator?.Dispose();_generator=null;base.Play();_generator=GetStreamPlayback() as AudioStreamGeneratorPlayback;}
    }
    public void Play(string kind,int variant=0)
    {
        if(!_enabled || !_unlocked)return;
        double now=Time.GetTicksUsec()/1_000_000.0;
        double gap=kind switch {"hit"=>.04,"launch"=>.055,"kill"=>.045,"explode"=>.10,_=>0};
        double prior=_last.GetValueOrDefault(kind,-100);if(prior==0)prior=-100;
        if(gap>0 && now-prior<gap)return;_last[kind]=now;
        if(_voices.Count>22)return;
        variant=((variant%7)+7)%7;var key=(kind,variant);
        if(!_cache.TryGetValue(key,out var data)){data=SoundSynth.Synthesize(kind,variant);_cache[key]=data;}
        _voices.Add(new Voice(data,kind=="music" ? .28f:1f,kind=="music"));
    }
    public void Tick(double dt,bool active)
    {
        if(!active || !Music || !_enabled || !_unlocked || _suspended)return;
        _musicClock+=dt;
        if(_musicClock>=.36){_musicClock-=.36;Play("music",MusicSequence[_musicStep++%MusicSequence.Length]);}
    }
    /// <summary>Called once per render frame, including paused UI; only focus loss suspends the audio clock.</summary>
    public void Pump()
    {
        if(_generator is null || !_unlocked || _suspended)return;
        int available=_generator.GetFramesAvailable();
        if(available<=0)return;
        // Refill only the consumed section; fixed 60ms buffer bounds audio work and latency.
        for(int frame=0;frame<available;frame++)
        {
            double sample=0;
            foreach(var voice in _voices)if(voice.Cursor<voice.Samples.Length)sample+=voice.Samples[voice.Cursor++]*voice.Gain*(voice.IsMusic?MusicGain:SfxGain);
            float mixed=(float)(Math.Clamp(sample*.46*MasterGain,-1,1));_generator.PushFrame(new Vector2(mixed,mixed));
        }
        _voices.RemoveAll(v=>v.Cursor>=v.Samples.Length);
    }
    public void Suspend(){_suspended=true;StreamPaused=true;}
    public void Shutdown(){Stop();Stream=null;_voices.Clear();_cache.Clear();_generator?.Dispose();_generator=null;}
    public override void _ExitTree()=>Shutdown();
}
