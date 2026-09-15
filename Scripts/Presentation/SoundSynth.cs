namespace DiceGame.Presentation;

/// <summary>Sample-for-sample formula port of audio.js; all oscillators accumulate in double and output float32.</summary>
public static class SoundSynth
{
    private static readonly double[] Notes=[261.63,329.63,392,523.25,659.25,783.99,1046.5];
    public static readonly string[] Kinds=["tap","launch","hit","kill","explode","merge","summon","upgrade","breach","clear","music","error"];
    public static float[] Synthesize(string kind,int variant=0,int sampleRate=22050)
    {
        if(sampleRate<8000 || sampleRate>192000)throw new ArgumentOutOfRangeException(nameof(sampleRate));
        variant=((variant%7)+7)%7;
        double duration=kind switch {"tap"=>.085,"launch"=>.12,"hit"=>.07,"kill"=>.19,"explode"=>.28,"merge"=>.58,"summon"=>.30,"upgrade"=>.65,"breach"=>.40,"clear"=>.62,"music"=>.90,"error"=>.15,_=>.12};
        var output=new float[(int)Math.Ceiling(sampleRate*duration)];uint seed=(uint)(194783+variant*197);double phase=0;
        int[] sequence=kind=="merge"?[0,2,3,5,6]:kind=="summon"?[0,2,4]:[0,1,2,4,6];
        for(int i=0;i<output.Length;i++)
        {
            double t=(double)i/sampleRate,progress=t/duration,v=0;
            seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;
            double noise=seed/2147483648.0-1,fade=Math.Pow(1-progress,2),attack=Math.Min(1,t*250);
            switch(kind)
            {
                case "hit":v=(Math.Sin(t*Math.PI*2*Notes[variant]*2)*.48+noise*.08)*Math.Exp(-t*60);break;
                case "launch":phase+=2*Math.PI*(850-520*progress)/sampleRate;v=(Math.Sin(phase)*.28+noise*.065)*fade;break;
                case "kill":v=(Math.Sin(t*2*Math.PI*Notes[variant])+.22*Math.Sin(t*2*Math.PI*Notes[variant]*2))*Math.Exp(-t*19)*.48+noise*.12*Math.Exp(-t*40);break;
                case "explode":phase+=2*Math.PI*(115-75*progress)/sampleRate;v=(Math.Sin(phase)*.52+noise*.30)*Math.Exp(-t*13);break;
                case "merge":case "summon":case "upgrade":case "clear":
                    double step=duration*.68/sequence.Length;
                    for(int k=0;k<sequence.Length;k++){double nt=t-k*step;if(nt>=0)v+=Math.Sin(nt*2*Math.PI*Notes[sequence[k]])*Math.Exp(-nt*13)*.26;}
                    break;
                case "breach":phase+=2*Math.PI*(155-85*progress)/sampleRate;v=(Math.Sin(phase)+noise*.2)*fade*.52;break;
                case "music":double f=Notes[variant]/2;v=(Math.Sin(t*2*Math.PI*f)+.15*Math.Sin(t*2*Math.PI*f*2))*Math.Exp(-t*5)*.22;break;
                case "error":v=Math.Sin(t*2*Math.PI*(progress<.45?240:180))*fade*.30;break;
                default:v=Math.Sin(t*2*Math.PI*(900-300*progress))*fade*.28;break;
            }
            output[i]=(float)Math.Max(-.96,Math.Min(.96,v*attack));
        }
        return output;
    }
}
